using Avalon.Common.GameAuth;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.World.Entities;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.World.UnitTests.Persistence;

internal static class AdmittedCharacter
{
    public static async Task BindAsync(IDbContextFactory<CharacterDbContext> database, CharacterEntity character)
    {
        var fences = new GameplayFenceRepository(database);
        var accountId = character.Data!.AccountId;
        var head = await fences.FindAsync(accountId, CancellationToken.None);
        var authority = new GameplayWriteAuthority(accountId, head?.GameSessionId ?? Guid.NewGuid(), head?.FencingToken ?? 1);
        if (head is null)
        {
            Assert.True(await fences.AdvanceAsync(authority, false, DateTime.UtcNow.AddSeconds(44), CancellationToken.None));
            Assert.True(await fences.ActivateAsync(authority, DateTime.UtcNow.AddSeconds(44), CancellationToken.None));
        }
        character.BindGameplayAuthority(authority);
    }
}