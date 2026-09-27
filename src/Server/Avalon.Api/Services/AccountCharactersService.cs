using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Worlds;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Characters;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.Services;

/// <summary>An account's characters across every world (#523).</summary>
public interface IAccountCharactersService
{
    /// <summary>
    /// <paramref name="account"/>'s characters on every world this api is configured for, that has an
    /// auth Worlds row, and that <paramref name="caller"/> may enter. A world that is unavailable, or
    /// whose read fails, is listed in <see cref="CharacterListDto.UnavailableWorlds"/> instead; a world
    /// the caller may not enter appears nowhere.
    /// </summary>
    Task<CharacterListDto> GetAsync(AccountId account, AccountAccessLevel caller, CancellationToken cancellationToken = default);
}

public sealed class AccountCharactersService(
    IWorldRepository worlds,
    IWorldDatabases databases,
    IWorldRepositories perWorld,
    ILogger<AccountCharactersService> logger) : IAccountCharactersService
{
    public async Task<CharacterListDto> GetAsync(AccountId account, AccountAccessLevel caller,
        CancellationToken cancellationToken = default)
    {
        List<CharacterDto> characters = [];
        List<ushort> unavailable = [];

        foreach (WorldEntity world in (await worlds.FindAllAsync(track: false, cancellationToken)).OrderBy(w => w.Id.Value))
        {
            // The auth server's world rule, a mask test. A world the caller may not enter is not revealed.
            if (!AccessLevels.ForWorld(world.AccessLevelRequired).Allows(caller)) continue;
            if (!databases.TryGet(world.Id, out _)) continue;
            if (!databases.IsAvailable(world.Id))
            {
                unavailable.Add(world.Id.Value);
                continue;
            }

            try
            {
                List<Character> found = await perWorld.Characters(world.Id).FindByAccountAsync(account, cancellationToken);
                characters.AddRange(found.Select(c => c.ToDto(world.Id.Value, world.Name)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException
                                              || !cancellationToken.IsCancellationRequested)
            {
                // The type only: a driver's message can carry hosts and ports.
                logger.LogError("Reading world {WorldId}'s characters failed with {ExceptionType}; listed as unavailable",
                    world.Id.Value, exception.GetType().Name);
                unavailable.Add(world.Id.Value);
            }
        }

        return new CharacterListDto { Characters = characters, UnavailableWorlds = unavailable };
    }
}
