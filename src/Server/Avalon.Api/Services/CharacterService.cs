using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;

namespace Avalon.Api.Services;

public interface ICharacterService
{
    Task<List<Character>> GetAllCharactersAsync(AccountId id, CancellationToken cancellationToken = default);
    Task<Character?> GetCharacterByIdAsync(CharacterId id, CancellationToken cancellationToken = default);
    Task UpdateCosmeticAsync(Character character, string? newName, CancellationToken cancellationToken = default);
    Task UpdateAnyAsync(Character character, CharacterPatchDto dto, CancellationToken cancellationToken = default);
    Task<CharacterInventoryDto?> GetInventoryAsync(CharacterId id, CancellationToken cancellationToken = default);
    Task<PagedResult<Character>> PaginateAsync(CharacterPaginateFilters filters, CancellationToken cancellationToken = default);
}

public class CharacterService : ICharacterService
{
    private readonly ICharacterRepository _characterRepository;
    private readonly ICharacterInventoryRepository _inventoryRepository;

    public CharacterService(
        ICharacterRepository characterRepository,
        ICharacterInventoryRepository inventoryRepository)
    {
        _characterRepository = characterRepository;
        _inventoryRepository = inventoryRepository;
    }

    public Task<List<Character>> GetAllCharactersAsync(AccountId id, CancellationToken cancellationToken = default) =>
        _characterRepository.FindByAccountAsync(id, cancellationToken);

    public Task<Character?> GetCharacterByIdAsync(CharacterId id, CancellationToken cancellationToken = default) =>
        _characterRepository.FindByIdAsync(id, track: false, cancellationToken);

    public Task<PagedResult<Character>> PaginateAsync(CharacterPaginateFilters filters, CancellationToken cancellationToken = default) =>
        _characterRepository.PaginateAsync(filters, track: false, cancellationToken);

    public async Task UpdateCosmeticAsync(Character character, string? newName, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(newName) && newName != character.Name)
        {
            var existing = await _characterRepository.FindByNameAsync(newName, cancellationToken);
            if (existing is not null && existing.Id != character.Id)
                throw new BusinessException("Name already taken");

            character.Name = newName;
        }
        await _characterRepository.UpdateAsync(character, cancellationToken);
    }

    public async Task UpdateAnyAsync(Character character, CharacterPatchDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Name is not null && dto.Name != character.Name)
        {
            var existing = await _characterRepository.FindByNameAsync(dto.Name, cancellationToken);
            if (existing is not null && existing.Id != character.Id)
                throw new BusinessException("Name already taken");
            character.Name = dto.Name;
        }
        if (dto.Level.HasValue)      character.Level = dto.Level.Value;
        if (dto.Experience.HasValue) character.Experience = dto.Experience.Value;
        if (dto.Health.HasValue)     character.Health = dto.Health.Value;
        if (dto.Power1.HasValue)     character.Power1 = dto.Power1.Value;
        if (dto.Power2.HasValue)     character.Power2 = dto.Power2.Value;

        await _characterRepository.UpdateAsync(character, cancellationToken);
    }

    public async Task<CharacterInventoryDto?> GetInventoryAsync(CharacterId id, CancellationToken cancellationToken = default)
    {
        var character = await _characterRepository.FindByIdAsync(id, track: false, cancellationToken);
        if (character is null) return null;

        var items = await _inventoryRepository.GetByCharacterIdAsync(id, cancellationToken);

        return new CharacterInventoryDto
        {
            CharacterId = character.Id.Value,
            Items = items.Select(MapItem).ToList(),
        };
    }

    private static CharacterInventoryItemDto MapItem(CharacterInventory i) => new()
    {
        ItemId = i.ItemId.Value,
        Container = i.Container,
        Slot = i.Slot,
    };
}
