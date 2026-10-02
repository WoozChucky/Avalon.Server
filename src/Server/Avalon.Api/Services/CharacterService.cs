using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Exceptions;
using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Api.Services;

public interface ICharacterService
{
    Task<Character?> GetCharacterByIdAsync(CharacterId id, CancellationToken cancellationToken = default);
    Task UpdateCosmeticAsync(Character character, string? newName, CancellationToken cancellationToken = default);
    Task UpdateAnyAsync(Character character, CharacterPatchDto dto, CancellationToken cancellationToken = default);
    Task<CharacterInventoryDto?> GetInventoryAsync(CharacterId id, CancellationToken cancellationToken = default);
    Task<CharacterAbilitiesDto?> GetAbilitiesAsync(CharacterId id, CancellationToken cancellationToken = default);
    Task<CharacterStatsDto?> GetStatsAsync(CharacterId id, CancellationToken cancellationToken = default);

    /// <summary>The character's quest log as its world last saved it (#714); empty lists for a character with none.</summary>
    Task<CharacterQuestLogDto> GetQuestLogAsync(CharacterId id, CancellationToken cancellationToken = default);
    Task<PagedResult<Character>> PaginateAsync(CharacterPaginateFilters filters, CancellationToken cancellationToken = default);
}

public class CharacterService : ICharacterService
{
    private readonly ICharacterRepository _characterRepository;
    private readonly ICharacterInventoryRepository _inventoryRepository;
    private readonly IItemInstanceRepository _itemInstanceRepository;
    private readonly ICharacterAbilityRepository _characterAbilityRepository;
    private readonly IAbilityTemplateRepository _abilityTemplateRepository;
    private readonly IItemTemplateRepository _itemTemplateRepository;
    private readonly ICharacterStatsRepository _statsRepository;
    private readonly ICharacterQuestRepository _questRepository;

    public CharacterService(
        ICharacterRepository characterRepository,
        ICharacterInventoryRepository inventoryRepository,
        IItemInstanceRepository itemInstanceRepository,
        ICharacterAbilityRepository characterAbilityRepository,
        IAbilityTemplateRepository abilityTemplateRepository,
        IItemTemplateRepository itemTemplateRepository,
        ICharacterStatsRepository statsRepository,
        ICharacterQuestRepository questRepository)
    {
        _characterRepository = characterRepository;
        _inventoryRepository = inventoryRepository;
        _itemInstanceRepository = itemInstanceRepository;
        _characterAbilityRepository = characterAbilityRepository;
        _abilityTemplateRepository = abilityTemplateRepository;
        _itemTemplateRepository = itemTemplateRepository;
        _statsRepository = statsRepository;
        _questRepository = questRepository;
    }

    public Task<Character?> GetCharacterByIdAsync(CharacterId id, CancellationToken cancellationToken = default) =>
        _characterRepository.FindByIdAsync(id, track: false, cancellationToken);

    public Task<PagedResult<Character>> PaginateAsync(CharacterPaginateFilters filters, CancellationToken cancellationToken = default) =>
        _characterRepository.PaginateAsync(filters, track: false, cancellationToken);

    public async Task UpdateCosmeticAsync(Character character, string? newName, CancellationToken cancellationToken = default)
    {
        bool renamed = newName is not null && await RenameAsync(character, newName, cancellationToken);
        await SaveAsync(character, renamed, cancellationToken);
    }

    public async Task UpdateAnyAsync(Character character, CharacterPatchDto dto, CancellationToken cancellationToken = default)
    {
        bool renamed = dto.Name is not null && await RenameAsync(character, dto.Name, cancellationToken);
        if (dto.Level.HasValue)      character.Level = dto.Level.Value;
        if (dto.Experience.HasValue) character.Experience = dto.Experience.Value;
        if (dto.Health.HasValue)     character.Health = dto.Health.Value;
        if (dto.Power1.HasValue)     character.Power1 = dto.Power1.Value;
        if (dto.Power2.HasValue)     character.Power2 = dto.Power2.Value;

        await SaveAsync(character, renamed, cancellationToken);
    }

    private const string NameTaken = "Name already taken";

    /// <summary>
    /// Renames the character under the rule a create follows (#757): 3 to 12 ASCII letters, stored first letter
    /// upper-case and the rest lower-case, and refused as taken when another character holds the name in any case.
    /// True when the name changed.
    /// </summary>
    private async Task<bool> RenameAsync(Character character, string name, CancellationToken cancellationToken)
    {
        if (!CharacterName.IsValid(name))
            throw new BusinessException(CharacterName.Requirement);

        string display = CharacterName.Display(name);
        if (string.Equals(display, character.Name, StringComparison.Ordinal))
            return false;

        var existing = await _characterRepository.FindByNameAsync(name, cancellationToken);
        if (existing is not null && existing.Id != character.Id)
            throw new BusinessException(NameTaken);

        character.Name = display;
        return true;
    }

    /// <summary>
    /// Writes the row. The taken check before a rename and this write are not atomic: another character can take the
    /// name in between, and the unique index on NameKey refuses this write; the caller then gets the check's answer.
    /// Any other failure is rethrown.
    /// </summary>
    private async Task SaveAsync(Character character, bool renamed, CancellationToken cancellationToken)
    {
        try
        {
            await _characterRepository.UpdateAsync(character, cancellationToken);
        }
        catch (DbUpdateException ex) when (renamed)
        {
            var holder = await _characterRepository.FindByNameAsync(character.Name, cancellationToken);
            if (holder is not null && holder.Id != character.Id)
                throw new BusinessException(NameTaken, ex);
            throw;
        }
    }

    public async Task<CharacterInventoryDto?> GetInventoryAsync(CharacterId id, CancellationToken cancellationToken = default)
    {
        var character = await _characterRepository.FindByIdAsync(id, track: false, cancellationToken);
        if (character is null) return null;

        var inventoryRows = await _inventoryRepository.GetByCharacterIdAsync(id, cancellationToken);
        var instances = await _itemInstanceRepository.GetByCharacterIdAsync(id, cancellationToken);

        // Instances live in the Character database and templates in the World database, so the
        // template is looked up by id rather than joined.
        var templates = await _itemTemplateRepository.GetByIdsAsync(
            instances.Select(i => i.TemplateId), cancellationToken);

        var instanceById = instances.ToDictionary(i => i.Id);
        var templateById = templates.ToDictionary(t => t.Id);

        return new CharacterInventoryDto
        {
            CharacterId = character.Id.Value,
            Items = inventoryRows.Select(row => MapItem(row, instanceById, templateById)).ToList(),
        };
    }

    private static CharacterInventoryItemDto MapItem(
        CharacterInventory row,
        Dictionary<ItemInstanceId, ItemInstance> instanceById,
        Dictionary<ItemTemplateId, ItemTemplate> templateById)
    {
        instanceById.TryGetValue(row.ItemId, out var instance);
        ItemTemplate? template = instance is not null && templateById.TryGetValue(instance.TemplateId, out var found)
            ? found
            : null;

        return new CharacterInventoryItemDto
        {
            ItemId = row.ItemId.Value,
            Container = (Avalon.Api.Contract.InventoryType)row.Container,
            Slot = row.Slot,
            Count = instance?.Count ?? 0,
            Durability = instance?.Durability ?? 0,
            Template = template is null ? null : new CharacterInventoryItemTemplateDto
            {
                Id = template.Id.Value,
                Name = template.Name ?? string.Empty,
                Rarity = (Avalon.Api.Contract.ItemRarity)template.Rarity,
                DisplayId = template.DisplayId,
                SlotType = (Avalon.Api.Contract.ItemSlotType?)template.Slot,
                ItemPower = template.ItemPower ?? 0,
                RequiredLevel = template.RequiredLevel ?? 0,
            },
        };
    }

    public async Task<CharacterStatsDto?> GetStatsAsync(CharacterId id, CancellationToken cancellationToken = default)
    {
        var stats = await _statsRepository.GetByCharacterIdAsync(id, cancellationToken);
        if (stats is null) return null;

        return new CharacterStatsDto
        {
            CharacterId = stats.CharacterId.Value,
            MaxHealth = stats.MaxHealth,
            MaxPower1 = stats.MaxPower1,
            MaxPower2 = stats.MaxPower2,
            Stamina = stats.Stamina,
            Strength = stats.Strength,
            Agility = stats.Agility,
            Intellect = stats.Intellect,
            Armor = stats.Armor,
            BlockPct = stats.BlockPct,
            DodgePct = stats.DodgePct,
            CritPct = stats.CritPct,
            AttackDamage = stats.AttackDamage,
            AbilityDamage = stats.AbilityDamage,
        };
    }

    public async Task<CharacterQuestLogDto> GetQuestLogAsync(CharacterId id, CancellationToken cancellationToken = default)
    {
        CharacterQuestRows rows = await _questRepository.GetByCharacterIdAsync(id, cancellationToken);
        return rows.ToQuestLogDto(id.Value);
    }

    public async Task<CharacterAbilitiesDto?> GetAbilitiesAsync(CharacterId id, CancellationToken cancellationToken = default)
    {
        var character = await _characterRepository.FindByIdAsync(id, track: false, cancellationToken);
        if (character is null) return null;

        var abilityRows = await _characterAbilityRepository.GetCharacterAbilitiesAsync(id, cancellationToken);
        var templates = await _abilityTemplateRepository.GetByIdsAsync(
            abilityRows.Select(s => s.AbilityId), cancellationToken);
        var templateById = templates.ToDictionary(t => t.Id);

        var stats = await _statsRepository.GetByCharacterIdAsync(id, cancellationToken);
        var (weaponMin, weaponMax) = await MainHandRangeAsync(id, cancellationToken);

        return new CharacterAbilitiesDto
        {
            CharacterId = character.Id.Value,
            Abilities = abilityRows
                .Select(row => MapAbility(row, templateById, stats, weaponMin, weaponMax))
                .ToList(),
        };
    }

    /// <summary>The worn main hand's first damage range, as the world server's stats read it (#506); none is 0-0.</summary>
    private async Task<(uint Min, uint Max)> MainHandRangeAsync(CharacterId id, CancellationToken cancellationToken)
    {
        var worn = (await _inventoryRepository.GetByCharacterIdAsync(id, cancellationToken))
            .Where(r => r.Container == Avalon.World.Public.Enums.InventoryType.Equipment)
            .Select(r => r.ItemId)
            .ToHashSet();
        if (worn.Count == 0) return (0, 0);

        var instances = (await _itemInstanceRepository.GetByCharacterIdAsync(id, cancellationToken))
            .Where(i => worn.Contains(i.Id))
            .ToList();
        var templates = await _itemTemplateRepository.GetByIdsAsync(instances.Select(i => i.TemplateId), cancellationToken);

        ItemTemplate? weapon = templates.FirstOrDefault(t => t.Slot == Avalon.Domain.World.ItemSlotType.MainHand);
        uint max = weapon?.DamageMax1 ?? 0;
        return (Math.Min(weapon?.DamageMin1 ?? 0, max), max);
    }

    private static CharacterAbilityAmountDto AmountFor(AbilityTemplate t, CharacterStats? stats, uint weaponMin, uint weaponMax)
    {
        var kind = AbilityAmountMath.KindOf(t.ScriptName, t.Affects);
        var (min, max) = AbilityAmountMath.Range(kind, t.EffectValue, t.ScalingStat, t.ScalingCoefficient,
            t.BaseDamageCoefficient, stats?.AttackDamage ?? 0, stats?.AbilityDamage ?? 0, weaponMin, weaponMax);
        return new CharacterAbilityAmountDto { Kind = (Avalon.Api.Contract.AbilityAmountKind)kind, Min = min, Max = max };
    }

    private static CharacterAbilityDto MapAbility(
        CharacterAbility row,
        Dictionary<AbilityId, AbilityTemplate> templateById,
        CharacterStats? stats, uint weaponMin, uint weaponMax)
    {
        templateById.TryGetValue(row.AbilityId, out var template);
        return new CharacterAbilityDto
        {
            AbilityId = row.AbilityId.Value,
            Amount = template is null ? null : AmountFor(template, stats, weaponMin, weaponMax),
            Template = template is null ? null : new CharacterAbilityTemplateDto
            {
                Id = template.Id.Value,
                Name = template.Name ?? string.Empty,
                CastTime = template.CastTime,
                Cooldown = template.Cooldown,
                Cost = template.Cost,
                CostPowerType = (Avalon.Api.Contract.PowerType)template.CostPowerType,
                Range = (Avalon.Api.Contract.SpellRange)template.Range,
                Effects = (Avalon.Api.Contract.SpellEffect)template.Effects,
                EffectValue = template.EffectValue,
                AllowedClasses = template.AllowedClasses is null
                    ? []
                    : template.AllowedClasses.ToList(),
            },
        };
    }
}
