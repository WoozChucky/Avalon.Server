using Avalon.Api.Authentication;
using Avalon.Api.Exceptions;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;

namespace Avalon.Api.Services;

public interface ICharacterService
{
    Task<IList<Character>> GetAllCharacters(AccountId? id);
    Task<Character> GetCharacterById(CharacterId id);
}

public class CharacterService : ICharacterService
{
    private ILogger<CharacterService> _logger;
    private ICharacterRepository _characterRepository;
    private readonly IAuthContext _authContext;

    public CharacterService(ILoggerFactory loggerFactory, ICharacterRepository characterRepository, IAuthContext authContext)
    {
        _logger = loggerFactory.CreateLogger<CharacterService>();
        _characterRepository = characterRepository;
        _authContext = authContext;
    }

    public async Task<IList<Character>> GetAllCharacters(AccountId? id)
    {
        if (id == null)
        {
            id = _authContext.Account?.Id!;
        }
        else
        {
            if (_authContext.Account?.Id != id && !_authContext.Account!.AccessLevel.HasFlag(AccountAccessLevel.GameMaster))
            {
                throw new BusinessException("Unauthorized");
            }
        }
        
        return await _characterRepository.FindByAccountAsync(id);
    }

    public async Task<Character> GetCharacterById(CharacterId id)
    {
        var character = await _characterRepository.FindByIdAsync(id);
        if (character == null)
        {
            throw new BusinessException("Character not found");
        }
        
        return character;
    }
}
