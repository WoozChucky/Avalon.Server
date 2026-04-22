using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;

namespace Avalon.Api.Services;

public interface ICharacterService
{
    Task<List<Character>> GetAllCharactersAsync(AccountId id, CancellationToken cancellationToken = default);
    Task<Character?> GetCharacterByIdAsync(CharacterId id, CancellationToken cancellationToken = default);
    Task UpdateCosmeticAsync(Character character, string? newName, CancellationToken cancellationToken = default);
    Task UpdateAnyAsync(Character character, CharacterPatchDto dto, CancellationToken cancellationToken = default);
    Task<CharacterInventoryDto?> GetInventoryAsync(CharacterId id, CancellationToken cancellationToken = default);
    Task<CharacterSpellsDto?> GetSpellsAsync(CharacterId id, CancellationToken cancellationToken = default);
    Task<PagedResult<Character>> PaginateAsync(CharacterPaginateFilters filters, CancellationToken cancellationToken = default);
}

public class CharacterService : ICharacterService
{
    private readonly ICharacterRepository _characterRepository;
    private readonly ICharacterInventoryRepository _inventoryRepository;
    private readonly IItemInstanceRepository _itemInstanceRepository;
    private readonly ICharacterSpellRepository _characterSpellRepository;
    private readonly ISpellTemplateRepository _spellTemplateRepository;

    public CharacterService(
        ICharacterRepository characterRepository,
        ICharacterInventoryRepository inventoryRepository,
        IItemInstanceRepository itemInstanceRepository,
        ICharacterSpellRepository characterSpellRepository,
        ISpellTemplateRepository spellTemplateRepository)
    {
        _characterRepository = characterRepository;
        _inventoryRepository = inventoryRepository;
        _itemInstanceRepository = itemInstanceRepository;
        _characterSpellRepository = characterSpellRepository;
        _spellTemplateRepository = spellTemplateRepository;
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

        var inventoryRows = await _inventoryRepository.GetByCharacterIdAsync(id, cancellationToken);
        var instances = await _itemInstanceRepository.GetByCharacterIdWithTemplateAsync(id, cancellationToken);
        var instanceById = instances.ToDictionary(i => i.Id);

        return new CharacterInventoryDto
        {
            CharacterId = character.Id.Value,
            Items = inventoryRows.Select(row => MapItem(row, instanceById)).ToList(),
        };
    }

    private static CharacterInventoryItemDto MapItem(
        CharacterInventory row,
        Dictionary<ItemInstanceId, ItemInstance> instanceById)
    {
        instanceById.TryGetValue(row.ItemId, out var instance);

        return new CharacterInventoryItemDto
        {
            ItemId = row.ItemId.Value,
            Container = (Avalon.Api.Contract.InventoryType)row.Container,
            Slot = row.Slot,
            Count = instance?.Count ?? 0,
            Durability = instance?.Durability ?? 0,
            Template = instance?.Template is null ? null : new CharacterInventoryItemTemplateDto
            {
                Id = instance.Template.Id.Value,
                Name = instance.Template.Name ?? string.Empty,
                Rarity = (Avalon.Api.Contract.ItemRarity)instance.Template.Rarity,
                DisplayId = instance.Template.DisplayId,
                SlotType = (Avalon.Api.Contract.ItemSlotType)(instance.Template.Slot ?? default),
                ItemPower = instance.Template.ItemPower ?? 0,
                RequiredLevel = instance.Template.RequiredLevel ?? 0,
            },
        };
    }

    public async Task<CharacterSpellsDto?> GetSpellsAsync(CharacterId id, CancellationToken cancellationToken = default)
    {
        var character = await _characterRepository.FindByIdAsync(id, track: false, cancellationToken);
        if (character is null) return null;

        var spellRows = await _characterSpellRepository.GetCharacterSpellsAsync(id, cancellationToken);
        var templates = await _spellTemplateRepository.GetByIdsAsync(
            spellRows.Select(s => s.SpellId), cancellationToken);
        var templateById = templates.ToDictionary(t => t.Id);

        return new CharacterSpellsDto
        {
            CharacterId = character.Id.Value,
            Spells = spellRows.Select(row => MapSpell(row, templateById)).ToList(),
        };
    }

    private static CharacterSpellDto MapSpell(
        CharacterSpell row,
        Dictionary<SpellId, SpellTemplate> templateById)
    {
        templateById.TryGetValue(row.SpellId, out var template);
        return new CharacterSpellDto
        {
            SpellId = row.SpellId.Value,
            Template = template is null ? null : new CharacterSpellTemplateDto
            {
                Id = template.Id.Value,
                Name = template.Name ?? string.Empty,
                CastTime = template.CastTime,
                Cooldown = template.Cooldown,
                Cost = template.Cost,
                Range = (Avalon.Api.Contract.SpellRange)template.Range,
                Effects = (Avalon.Api.Contract.SpellEffect)template.Effects,
                EffectValue = template.EffectValue,
                AllowedClasses = template.AllowedClasses is null
                    ? []
                    : template.AllowedClasses
                        .Select(c => (Avalon.Api.Contract.CharacterClass)c)
                        .ToList(),
            },
        };
    }
}
