using Avalon.Api.Contract;
using Avalon.Api.Contract.Mappers;
using Avalon.Api.Hosting.Exceptions;
using Avalon.Api.Worlds.Exceptions;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;

namespace Avalon.Api.Worlds.Services;

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

    /// <summary>The character's auras as its world last saved them, by slot; empty for a character with none.</summary>
    Task<CharacterAurasDto> GetAurasAsync(CharacterId id, CancellationToken cancellationToken = default);
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
    private readonly ICharacterAuraRepository _auraRepository;
    private readonly ICombatDataRepository _combatDataRepository;

    public CharacterService(
        ICharacterRepository characterRepository,
        ICharacterInventoryRepository inventoryRepository,
        IItemInstanceRepository itemInstanceRepository,
        ICharacterAbilityRepository characterAbilityRepository,
        IAbilityTemplateRepository abilityTemplateRepository,
        IItemTemplateRepository itemTemplateRepository,
        ICharacterStatsRepository statsRepository,
        ICharacterQuestRepository questRepository,
        ICharacterAuraRepository auraRepository,
        ICombatDataRepository combatDataRepository)
    {
        _characterRepository = characterRepository;
        _inventoryRepository = inventoryRepository;
        _itemInstanceRepository = itemInstanceRepository;
        _characterAbilityRepository = characterAbilityRepository;
        _abilityTemplateRepository = abilityTemplateRepository;
        _itemTemplateRepository = itemTemplateRepository;
        _statsRepository = statsRepository;
        _questRepository = questRepository;
        _auraRepository = auraRepository;
        _combatDataRepository = combatDataRepository;
    }

    public Task<Character?> GetCharacterByIdAsync(CharacterId id, CancellationToken cancellationToken = default) =>
        _characterRepository.FindByIdAsync(id, track: false, cancellationToken);

    public Task<PagedResult<Character>> PaginateAsync(CharacterPaginateFilters filters, CancellationToken cancellationToken = default) =>
        _characterRepository.PaginateAsync(filters, track: false, cancellationToken);

    public async Task UpdateCosmeticAsync(Character character, string? newName, CancellationToken cancellationToken = default)
    {
        if (newName is not null)
            await RenameAsync(character, newName, cancellationToken);
    }

    public async Task UpdateAnyAsync(Character character, CharacterPatchDto dto, CancellationToken cancellationToken = default)
    {
        // The rename first: refused (rule, taken, online), the request changes nothing.
        if (dto.Name is not null)
            await RenameAsync(character, dto.Name, cancellationToken);

        if (dto.Level.HasValue) character.Level = dto.Level.Value;
        if (dto.Experience.HasValue) character.Experience = dto.Experience.Value;
        if (dto.Health.HasValue) character.Health = dto.Health.Value;
        if (dto.Power1.HasValue) character.Power1 = dto.Power1.Value;
        if (dto.Power2.HasValue) character.Power2 = dto.Power2.Value;

        // A tracked update never writes the name (it is insert-only to the change tracker): the rename above did.
        await _characterRepository.UpdateAsync(character, cancellationToken);
    }

    private const string NameTaken = "Name already taken";

    /// <summary>
    /// Renames the character under the rule a create follows (#757): 3 to 12 ASCII letters, stored first letter
    /// upper-case and the rest lower-case, refused as taken when another character holds the name in any case, and
    /// refused with 409 while the character is online (owner decision). A name that would not change writes nothing.
    /// The write is <see cref="ICharacterRepository.TryRenameAsync" />, one conditional statement that writes only
    /// while the row is offline, so a login cannot land between the online check and the write; a rename that loses
    /// the race to the unique index on NameKey is answered as taken.
    /// </summary>
    private async Task RenameAsync(Character character, string name, CancellationToken cancellationToken)
    {
        if (!CharacterName.IsValid(name))
            throw new BusinessException(CharacterName.Requirement);

        string display = CharacterName.Display(name);
        if (string.Equals(display, character.Name, StringComparison.Ordinal))
            return;

        Character? existing = await _characterRepository.FindByNameAsync(name, cancellationToken);
        if (existing is not null && existing.Id != character.Id)
            throw new BusinessException(NameTaken);

        switch (await _characterRepository.TryRenameAsync(character.Id, display, cancellationToken))
        {
            case CharacterRename.Renamed:
                character.Name = display;
                return;
            case CharacterRename.Online:
                throw new CharacterOnlineException();
            case CharacterRename.NameTaken:
                throw new BusinessException(NameTaken);
            default:
                throw new CharacterNotFoundException();
        }
    }

    public async Task<CharacterInventoryDto?> GetInventoryAsync(CharacterId id, CancellationToken cancellationToken = default)
    {
        Character? character = await _characterRepository.FindByIdAsync(id, track: false, cancellationToken);
        if (character is null) return null;

        IReadOnlyCollection<CharacterInventory> inventoryRows = await _inventoryRepository.GetByCharacterIdAsync(id, cancellationToken);
        IReadOnlyList<ItemInstance> instances = await _itemInstanceRepository.GetByCharacterIdAsync(id, cancellationToken);

        // Instances live in the Character database and templates in the World database, so the
        // template is looked up by id rather than joined.
        IReadOnlyList<ItemTemplate> templates = await _itemTemplateRepository.GetByIdsAsync(
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
        instanceById.TryGetValue(row.ItemId, out ItemInstance? instance);
        ItemTemplate? template = instance is not null && templateById.TryGetValue(instance.TemplateId, out ItemTemplate? found)
            ? found
            : null;

        return new CharacterInventoryItemDto
        {
            ItemId = row.ItemId.Value,
            Container = (Avalon.Api.Contract.InventoryType)row.Container,
            Slot = row.Slot,
            Count = instance?.Count ?? 0,
            Durability = instance?.Durability ?? 0,
            Rarity = (Avalon.Api.Contract.ItemRarity)(instance?.Rarity ?? default),
            ItemLevel = instance?.ItemLevel ?? 0,
            Modifiers = instance is null ? [] : ModifiersOf(instance),
            Template = template is null ? null : new CharacterInventoryItemTemplateDto
            {
                Id = template.Id.Value,
                Name = template.Name ?? string.Empty,
                Rarity = (Avalon.Api.Contract.ItemRarity)template.Rarity,
                DisplayId = template.DisplayId,
                SlotType = (Avalon.Api.Contract.ItemSlotType?)template.Slot,
                RequiredLevel = template.RequiredLevel ?? 0,
            },
        };
    }

    /// <summary>
    /// The copy's modifiers in the order the item shows them, by ordinal, each with its values in the order of the
    /// catalog's stat lines: the read loads the rows in no particular order.
    /// </summary>
    private static List<CharacterInventoryModifierDto> ModifiersOf(ItemInstance instance) =>
        instance.Modifiers
            .OrderBy(m => m.Ordinal)
            .Select(m => new CharacterInventoryModifierDto
            {
                ModifierId = m.ModifierId,
                Values = m.Values
                    .OrderBy(v => v.Index)
                    .Select(v => new CharacterInventoryModifierValueDto
                    {
                        Stat = (Avalon.Api.Contract.StatType)v.Stat,
                        Value = v.Value,
                        Value2 = v.Value2,
                    })
                    .ToList(),
            })
            .ToList();

    public async Task<CharacterStatsDto?> GetStatsAsync(CharacterId id, CancellationToken cancellationToken = default)
    {
        CharacterStats? stats = await _statsRepository.GetByCharacterIdAsync(id, cancellationToken);
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
            CritPct = stats.CritPct,
            Evasion = stats.Evasion,
            MaxWard = stats.MaxWard,
            FireResistance = stats.FireResistance,
            ColdResistance = stats.ColdResistance,
            LightningResistance = stats.LightningResistance,
            BlightResistance = stats.BlightResistance,
        };
    }

    public async Task<CharacterQuestLogDto> GetQuestLogAsync(CharacterId id, CancellationToken cancellationToken = default)
    {
        CharacterQuestRows rows = await _questRepository.GetByCharacterIdAsync(id, cancellationToken);
        return rows.ToQuestLogDto(id.Value);
    }

    public async Task<CharacterAurasDto> GetAurasAsync(CharacterId id, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CharacterAura> rows = await _auraRepository.GetByCharacterIdAsync(id, cancellationToken);

        return new CharacterAurasDto
        {
            CharacterId = id.Value,
            Auras = rows.Select(r => new CharacterAuraDto
            {
                AuraId = r.AuraId,
                CasterCharacterId = CasterCharacterIdOf(r.CasterGuid),
                SourceAbilityId = r.SourceAbilityId,
                Stacks = r.Stacks,
                RemainingMs = r.RemainingMs,
                DurationMs = r.DurationMs,
                TicksLeft = r.TicksLeft,
                AppliedAt = r.AppliedAt,
            }).ToList(),
        };
    }

    /// <summary>The character id a saved caster names; null for nobody (0) or for anything but a character.</summary>
    private static uint? CasterCharacterIdOf(ulong casterGuid)
    {
        var guid = new ObjectGuid(casterGuid);
        return guid.Type == ObjectType.Character && guid.Id != 0 ? guid.Id : null;
    }

    public async Task<CharacterAbilitiesDto?> GetAbilitiesAsync(CharacterId id, CancellationToken cancellationToken = default)
    {
        Character? character = await _characterRepository.FindByIdAsync(id, track: false, cancellationToken);
        if (character is null) return null;

        IReadOnlyCollection<CharacterAbility> abilityRows = await _characterAbilityRepository.GetCharacterAbilitiesAsync(id, cancellationToken);
        IReadOnlyList<AbilityTemplate> templates = await _abilityTemplateRepository.GetByIdsAsync(
            abilityRows.Select(s => s.AbilityId), cancellationToken);
        var templateById = templates.ToDictionary(t => t.Id);

        AttackerCombat attacker = await AttackerAsync(character, cancellationToken);

        return new CharacterAbilitiesDto
        {
            CharacterId = character.Id.Value,
            Abilities = abilityRows
                .Select(row => MapAbility(row, templateById, attacker))
                .ToList(),
        };
    }

    /// <summary>
    /// What the character attacks with, as the world server's stats refresh builds it (#1178), through the same
    /// Avalon.Combat entry points: the offence block from the saved attributes (with gear) and the worn items with their
    /// saved modifiers (#1179), the main hand's weapon (the bare hand's with none), over the world's combat formula.
    /// Without auras: a character out of the world holds none that count, and the ones it saved are stale. A character with
    /// no saved stats has no attributes.
    /// </summary>
    private async Task<AttackerCombat> AttackerAsync(Character character, CancellationToken cancellationToken)
    {
        CharacterStats? stats = await _statsRepository.GetByCharacterIdAsync(character.Id, cancellationToken);
        IReadOnlyList<WornItem> worn = await WornItemsAsync(character.Id, cancellationToken);
        CombatFormula formula = (await _combatDataRepository.GetFormulasAsync(cancellationToken))
            .FirstOrDefault(f => f.Id == CombatFormula.SingletonId)
            ?? throw new InvalidOperationException("The world has no combat formula row.");

        OffenceStats offence = CharacterOffence.Of(stats?.Strength ?? 0, stats?.Agility ?? 0, stats?.Intellect ?? 0, worn,
            formula);
        var weapon = WeaponDamage.Of(CharacterStatsCalculator.MainHandOf(worn), formula);
        return new AttackerCombat(character.Level, stats?.CritPct ?? 0f, weapon, offence);
    }

    /// <summary>
    /// Each item the character wears, as the world's stats refresh counts them: its current template and the modifiers its
    /// saved instance holds (#1179), one per worn item, so two copies of one template count twice, and none for an item in a
    /// slot its template does not fit (<see cref="EquipmentSlots.Accepts" />).
    /// </summary>
    private async Task<IReadOnlyList<WornItem>> WornItemsAsync(CharacterId id, CancellationToken cancellationToken)
    {
        var worn = (await _inventoryRepository.GetByCharacterIdAsync(id, cancellationToken))
            .Where(r => r.Container == Avalon.Common.InventoryType.Equipment)
            .ToList();
        if (worn.Count == 0) return [];

        // Loaded with their modifiers and the modifiers' values.
        var instances = (await _itemInstanceRepository.GetByCharacterIdAsync(id, cancellationToken))
            .ToDictionary(i => i.Id);
        IEnumerable<ItemTemplateId> templateIds = worn
            .Select(r => instances.GetValueOrDefault(r.ItemId)?.TemplateId)
            .OfType<ItemTemplateId>();
        var templates = (await _itemTemplateRepository.GetByIdsAsync(templateIds, cancellationToken))
            .ToDictionary(t => t.Id);

        List<WornItem> result = [];
        foreach (CharacterInventory row in worn)
        {
            if (instances.TryGetValue(row.ItemId, out ItemInstance? instance)
                && templates.TryGetValue(instance.TemplateId, out ItemTemplate? template)
                && EquipmentSlots.Accepts(row.Slot, template.Slot))
            {
                result.Add(new WornItem(template, instance.Modifiers));
            }
        }

        return result;
    }

    private static CharacterAbilityAmountDto AmountFor(AbilityTemplate t, in AttackerCombat attacker)
    {
        // The DTO states the amount alone, which no speed moves, so no pace is worked out for it.
        AbilityAmount amount = AbilityAmounts.For(attacker, AbilityMetadataMapper.From(t), default);
        return new CharacterAbilityAmountDto
        {
            Kind = (Avalon.Api.Contract.AbilityAmountKind)amount.Kind,
            Min = amount.Min,
            Max = amount.Max,
        };
    }

    private static CharacterAbilityDto MapAbility(
        CharacterAbility row,
        Dictionary<AbilityId, AbilityTemplate> templateById,
        AttackerCombat attacker)
    {
        templateById.TryGetValue(row.AbilityId, out AbilityTemplate? template);
        return new CharacterAbilityDto
        {
            AbilityId = row.AbilityId.Value,
            Amount = template is null ? null : AmountFor(template, attacker),
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
                AllowedClasses = template.AllowedClasses is null
                    ? []
                    : template.AllowedClasses.ToList(),
            },
        };
    }
}
