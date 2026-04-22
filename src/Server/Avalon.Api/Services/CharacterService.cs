using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;

namespace Avalon.Api.Services;

public interface ICharacterService
{
    Task<List<Character>> GetAllCharactersAsync(AccountId id, CancellationToken cancellationToken = default);
    Task<Character?> GetCharacterByIdAsync(CharacterId id, CancellationToken cancellationToken = default);
}

public class CharacterService : ICharacterService
{
    private readonly ICharacterRepository _characterRepository;

    public CharacterService(ICharacterRepository characterRepository)
    {
        _characterRepository = characterRepository;
    }

    public Task<List<Character>> GetAllCharactersAsync(AccountId id, CancellationToken cancellationToken = default) =>
        _characterRepository.FindByAccountAsync(id, cancellationToken);

    public Task<Character?> GetCharacterByIdAsync(CharacterId id, CancellationToken cancellationToken = default) =>
        _characterRepository.FindByIdAsync(id, track: false, cancellationToken);
}
