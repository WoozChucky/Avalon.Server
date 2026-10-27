using System.Globalization;
using Avalon.Api.Contract;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.Api.Worlds.Templates;

/// <summary>
/// One editable field of a template: how to read it from the row (for the audit diff) and how to write the request
/// value onto it. One table per kind serves both, so the diff and the apply cannot disagree about which fields exist.
/// The names are the request body camelCase names, which is also how the audit and the errors name a field.
/// </summary>
internal sealed record TemplateField<TRow, TRequest>(string Name, Func<TRow, object?> Get, Action<TRow, TRequest> Set);

internal static class TemplateFields
{
    // The request enums are the contract ones, the row enums are the domain ones; the numbers are the same, as in the read DTOs.
    private static T Conv<T>(Enum from, T type) where T : struct, Enum => (T)Enum.ToObject(typeof(T), from);

    private static T? ConvN<T>(Enum? from, T? type) where T : struct, Enum =>
        from is null ? null : (T)Enum.ToObject(typeof(T), from);

    // An optional name left blank is no name: stored as null, as the world reads a blank script or group anyway.
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static TemplateField<ItemTemplate, UpdateItemTemplateRequest> I(
        string name, Func<ItemTemplate, object?> get, Action<ItemTemplate, UpdateItemTemplateRequest> set) => new(name, get, set);

    private static TemplateField<AbilityTemplate, UpdateAbilityTemplateRequest> A(
        string name, Func<AbilityTemplate, object?> get, Action<AbilityTemplate, UpdateAbilityTemplateRequest> set) => new(name, get, set);

    private static TemplateField<CreatureTemplate, UpdateCreatureTemplateRequest> C(
        string name, Func<CreatureTemplate, object?> get, Action<CreatureTemplate, UpdateCreatureTemplateRequest> set) => new(name, get, set);

    private static TemplateField<AuraTemplate, UpdateAuraTemplateRequest> U(
        string name, Func<AuraTemplate, object?> get, Action<AuraTemplate, UpdateAuraTemplateRequest> set) => new(name, get, set);

    public static readonly IReadOnlyList<TemplateField<AbilityTemplate, UpdateAbilityTemplateRequest>> Ability =
    [
        A("name", e => e.Name, (e, r) => e.Name = r.Name),
        A("castTime", e => e.CastTime, (e, r) => e.CastTime = r.CastTime),
        A("cooldown", e => e.Cooldown, (e, r) => e.Cooldown = r.Cooldown),
        A("cost", e => e.Cost, (e, r) => e.Cost = r.Cost),
        A("scriptName", e => e.ScriptName, (e, r) => e.ScriptName = r.ScriptName),
        A("range", e => e.Range, (e, r) => e.Range = Conv(r.Range, e.Range)),
        A("effects", e => e.Effects, (e, r) => e.Effects = Conv(r.Effects, e.Effects)),
        A("maxTargets", e => e.MaxTargets, (e, r) => e.MaxTargets = r.MaxTargets),
        A("auraId", e => e.AuraId?.Value, (e, r) => e.AuraId = r.AuraId is { } aura ? new AuraId(aura) : null),
        A("damageType", e => e.DamageType, (e, r) => e.DamageType = ConvN(r.DamageType, e.DamageType)),
        A("tags", e => e.Tags, (e, r) => e.Tags = Conv(r.Tags, e.Tags)),
        A("damageEffectiveness", e => e.DamageEffectiveness, (e, r) => e.DamageEffectiveness = r.DamageEffectiveness),
        A("spellBaseMin", e => e.SpellBaseMin, (e, r) => e.SpellBaseMin = r.SpellBaseMin),
        A("spellBaseMax", e => e.SpellBaseMax, (e, r) => e.SpellBaseMax = r.SpellBaseMax),
        A("spellBasePerLevelPct", e => e.SpellBasePerLevelPct, (e, r) => e.SpellBasePerLevelPct = r.SpellBasePerLevelPct),
        A("convertToType", e => e.ConvertToType, (e, r) => e.ConvertToType = ConvN(r.ConvertToType, e.ConvertToType)),
        A("convertPct", e => e.ConvertPct, (e, r) => e.ConvertPct = r.ConvertPct),
        A("addedType", e => e.AddedType, (e, r) => e.AddedType = ConvN(r.AddedType, e.AddedType)),
        A("addedMin", e => e.AddedMin, (e, r) => e.AddedMin = r.AddedMin),
        A("addedMax", e => e.AddedMax, (e, r) => e.AddedMax = r.AddedMax),
    ];

    public static readonly IReadOnlyList<TemplateField<CreatureTemplate, UpdateCreatureTemplateRequest>> Creature =
    [
        C("name", e => e.Name, (e, r) => e.Name = r.Name),
        C("subName", e => e.SubName, (e, r) => e.SubName = r.SubName),
        C("iconName", e => e.IconName, (e, r) => e.IconName = r.IconName),
        C("minLevel", e => e.MinLevel, (e, r) => e.MinLevel = r.MinLevel),
        C("maxLevel", e => e.MaxLevel, (e, r) => e.MaxLevel = r.MaxLevel),
        C("speedWalk", e => e.SpeedWalk, (e, r) => e.SpeedWalk = r.SpeedWalk),
        C("speedRun", e => e.SpeedRun, (e, r) => e.SpeedRun = r.SpeedRun),
        C("speedSwim", e => e.SpeedSwim, (e, r) => e.SpeedSwim = r.SpeedSwim),
        C("rarity", e => e.Rarity, (e, r) => e.Rarity = Conv(r.Rarity, e.Rarity)),
        C("family", e => e.Family, (e, r) => e.Family = Conv(r.Family, e.Family)),
        C("type", e => e.Type, (e, r) => e.Type = Conv(r.Type, e.Type)),
        C("displayId", e => e.DisplayId, (e, r) => e.DisplayId = r.DisplayId),
        C("lootTableId", e => e.LootTableId?.Value, (e, r) => e.LootTableId = r.LootTableId is { } id ? new LootTableId(id) : null),
        C("minGold", e => e.MinGold, (e, r) => e.MinGold = r.MinGold),
        C("maxGold", e => e.MaxGold, (e, r) => e.MaxGold = r.MaxGold),
        C("movementType", e => e.MovementType, (e, r) => e.MovementType = r.MovementType),
        C("detectionRange", e => e.DetectionRange, (e, r) => e.DetectionRange = r.DetectionRange),
        C("movementId", e => e.MovementId, (e, r) => e.MovementId = r.MovementId),
        C("scriptName", e => e.ScriptName, (e, r) => e.ScriptName = r.ScriptName),
        C("healthModifier", e => e.HealthModifier, (e, r) => e.HealthModifier = r.HealthModifier),
        C("manaModifier", e => e.ManaModifier, (e, r) => e.ManaModifier = r.ManaModifier),
        C("armorModifier", e => e.ArmorModifier, (e, r) => e.ArmorModifier = r.ArmorModifier),
        C("experienceModifier", e => e.ExperienceModifier, (e, r) => e.ExperienceModifier = r.ExperienceModifier),
        C("regenHealth", e => e.RegenHealth, (e, r) => e.RegenHealth = r.RegenHealth),
        C("damageType", e => e.DamageType, (e, r) => e.DamageType = Conv(r.DamageType, e.DamageType)),
        C("damageModifier", e => e.DamageModifier, (e, r) => e.DamageModifier = r.DamageModifier),
        C("fireResistance", e => e.FireResistance, (e, r) => e.FireResistance = r.FireResistance),
        C("coldResistance", e => e.ColdResistance, (e, r) => e.ColdResistance = r.ColdResistance),
        C("lightningResistance", e => e.LightningResistance, (e, r) => e.LightningResistance = r.LightningResistance),
        C("blightResistance", e => e.BlightResistance, (e, r) => e.BlightResistance = r.BlightResistance),
        C("evasionModifier", e => e.EvasionModifier, (e, r) => e.EvasionModifier = r.EvasionModifier),
        C("wardModifier", e => e.WardModifier, (e, r) => e.WardModifier = r.WardModifier),
        C("baseAttackTime", e => e.BaseAttackTime, (e, r) => e.BaseAttackTime = r.BaseAttackTime),
        C("rangeAttackTime", e => e.RangeAttackTime, (e, r) => e.RangeAttackTime = r.RangeAttackTime),
        C("experience", e => e.Experience, (e, r) => e.Experience = r.Experience),
        C("bodyRemoveTimerSecs", e => e.BodyRemoveTimerSecs, (e, r) => e.BodyRemoveTimerSecs = r.BodyRemoveTimerSecs),
        C("stunDurationPct", e => e.StunDurationPct, (e, r) => e.StunDurationPct = r.StunDurationPct),
        C("rootDurationPct", e => e.RootDurationPct, (e, r) => e.RootDurationPct = r.RootDurationPct),
        C("silenceDurationPct", e => e.SilenceDurationPct, (e, r) => e.SilenceDurationPct = r.SilenceDurationPct),
        C("slowDurationPct", e => e.SlowDurationPct, (e, r) => e.SlowDurationPct = r.SlowDurationPct),
    ];

    public static readonly IReadOnlyList<TemplateField<ItemTemplate, UpdateItemTemplateRequest>> Item =
    [
        I("name", e => e.Name, (e, r) => e.Name = r.Name),
        I("class", e => e.Class, (e, r) => e.Class = Conv(r.Class, e.Class)),
        I("subClass", e => e.SubClass, (e, r) => e.SubClass = Conv(r.SubClass, e.SubClass)),
        I("flags", e => e.Flags, (e, r) => e.Flags = Conv(r.Flags, e.Flags)),
        I("maxStackSize", e => e.MaxStackSize, (e, r) => e.MaxStackSize = r.MaxStackSize),
        I("displayId", e => e.DisplayId, (e, r) => e.DisplayId = r.DisplayId),
        I("rarity", e => e.Rarity, (e, r) => e.Rarity = Conv(r.Rarity, e.Rarity)),
        I("buyPrice", e => e.BuyPrice, (e, r) => e.BuyPrice = r.BuyPrice),
        I("sellPrice", e => e.SellPrice, (e, r) => e.SellPrice = r.SellPrice),
        I("slot", e => e.Slot, (e, r) => e.Slot = ConvN(r.Slot, e.Slot)),
        I("baseKind", e => e.BaseKind, (e, r) => e.BaseKind = ConvN(r.BaseKind, e.BaseKind)),
        I("allowedClasses", e => e.AllowedClasses, (e, r) => e.AllowedClasses = r.AllowedClasses.ToList()),
        I("requiredLevel", e => e.RequiredLevel, (e, r) => e.RequiredLevel = r.RequiredLevel),
        I("requiredStrength", e => e.RequiredStrength, (e, r) => e.RequiredStrength = r.RequiredStrength),
        I("requiredAgility", e => e.RequiredAgility, (e, r) => e.RequiredAgility = r.RequiredAgility),
        I("requiredIntellect", e => e.RequiredIntellect, (e, r) => e.RequiredIntellect = r.RequiredIntellect),
        I("requiresAnyAttribute", e => e.RequiresAnyAttribute, (e, r) => e.RequiresAnyAttribute = r.RequiresAnyAttribute),
        I("damageType", e => e.DamageType, (e, r) => e.DamageType = ConvN(r.DamageType, e.DamageType)),
        I("baseTemplateId", e => e.BaseTemplateId?.Value,
            (e, r) => e.BaseTemplateId = r.BaseTemplateId is { } named ? new ItemTemplateId(named) : null),
        I("baseStats", e => BaseStatText(e.BaseStats), (e, r) => MergeBaseStats(e, r.BaseStats ?? [])),
        I("statType1", e => e.StatType1, (e, r) => e.StatType1 = ConvN(r.StatType1, e.StatType1)),
        I("statValue1", e => e.StatValue1, (e, r) => e.StatValue1 = r.StatValue1),
        I("statType2", e => e.StatType2, (e, r) => e.StatType2 = ConvN(r.StatType2, e.StatType2)),
        I("statValue2", e => e.StatValue2, (e, r) => e.StatValue2 = r.StatValue2),
        I("statType3", e => e.StatType3, (e, r) => e.StatType3 = ConvN(r.StatType3, e.StatType3)),
        I("statValue3", e => e.StatValue3, (e, r) => e.StatValue3 = r.StatValue3),
        I("statType4", e => e.StatType4, (e, r) => e.StatType4 = ConvN(r.StatType4, e.StatType4)),
        I("statValue4", e => e.StatValue4, (e, r) => e.StatValue4 = r.StatValue4),
        I("statType5", e => e.StatType5, (e, r) => e.StatType5 = ConvN(r.StatType5, e.StatType5)),
        I("statValue5", e => e.StatValue5, (e, r) => e.StatValue5 = r.StatValue5),
        I("statType6", e => e.StatType6, (e, r) => e.StatType6 = ConvN(r.StatType6, e.StatType6)),
        I("statValue6", e => e.StatValue6, (e, r) => e.StatValue6 = r.StatValue6),
        I("statType7", e => e.StatType7, (e, r) => e.StatType7 = ConvN(r.StatType7, e.StatType7)),
        I("statValue7", e => e.StatValue7, (e, r) => e.StatValue7 = r.StatValue7),
        I("statType8", e => e.StatType8, (e, r) => e.StatType8 = ConvN(r.StatType8, e.StatType8)),
        I("statValue8", e => e.StatValue8, (e, r) => e.StatValue8 = r.StatValue8),
        I("statType9", e => e.StatType9, (e, r) => e.StatType9 = ConvN(r.StatType9, e.StatType9)),
        I("statValue9", e => e.StatValue9, (e, r) => e.StatValue9 = r.StatValue9),
        I("statType10", e => e.StatType10, (e, r) => e.StatType10 = ConvN(r.StatType10, e.StatType10)),
        I("statValue10", e => e.StatValue10, (e, r) => e.StatValue10 = r.StatValue10),
        I("useScript", e => e.UseScript, (e, r) => e.UseScript = NullIfBlank(r.UseScript)),
        I("useCastTimeMs", e => e.UseCastTimeMs, (e, r) => e.UseCastTimeMs = r.UseCastTimeMs),
        I("useCooldownMs", e => e.UseCooldownMs, (e, r) => e.UseCooldownMs = r.UseCooldownMs),
        I("useCooldownGroup", e => e.UseCooldownGroup, (e, r) => e.UseCooldownGroup = NullIfBlank(r.UseCooldownGroup)),
        I("useValue", e => e.UseValue, (e, r) => e.UseValue = r.UseValue),
    ];

    public static readonly IReadOnlyList<TemplateField<AuraTemplate, UpdateAuraTemplateRequest>> Aura =
    [
        U("name", e => e.Name, (e, r) => e.Name = r.Name),
        U("icon", e => e.Icon, (e, r) => e.Icon = r.Icon),
        U("kind", e => e.Kind, (e, r) => e.Kind = Conv(r.Kind, e.Kind)),
        U("durationMs", e => e.DurationMs, (e, r) => e.DurationMs = r.DurationMs),
        U("tickIntervalMs", e => e.TickIntervalMs, (e, r) => e.TickIntervalMs = r.TickIntervalMs),
        U("periodicKind", e => e.PeriodicKind, (e, r) => e.PeriodicKind = Conv(r.PeriodicKind, e.PeriodicKind)),
        U("periodicBase", e => e.PeriodicBase, (e, r) => e.PeriodicBase = r.PeriodicBase),
        U("periodicEffectiveness", e => e.PeriodicEffectiveness, (e, r) => e.PeriodicEffectiveness = r.PeriodicEffectiveness),
        U("stacking", e => e.Stacking, (e, r) => e.Stacking = Conv(r.Stacking, e.Stacking)),
        U("maxStacks", e => e.MaxStacks, (e, r) => e.MaxStacks = r.MaxStacks),
        U("control", e => e.Control, (e, r) => e.Control = Conv(r.Control, e.Control)),
        U("breakOnDamagePct", e => e.BreakOnDamagePct, (e, r) => e.BreakOnDamagePct = r.BreakOnDamagePct),
        U("breakPerSecondPct", e => e.BreakPerSecondPct, (e, r) => e.BreakPerSecondPct = r.BreakPerSecondPct),
        U("scriptName", e => e.ScriptName, (e, r) => e.ScriptName = NullIfBlank(r.ScriptName)),
        U("modifiers", e => ModifierText(e.Modifiers), (e, r) => MergeModifiers(e, r.Modifiers ?? [])),
    ];

    /// <summary>The base-stat ranges as the audit line shows them: stat:min-max by stat (#1179).</summary>
    private static string BaseStatText(IEnumerable<ItemTemplateBaseStat> stats) =>
        string.Join(",", stats.OrderBy(s => s.Stat).Select(s =>
            $"{s.Stat}:{s.Min.ToString(CultureInfo.InvariantCulture)}-{s.Max.ToString(CultureInfo.InvariantCulture)}"));

    /// <summary>
    /// Makes the row's base-stat ranges the request's, the whole list (#1179), by stat: a stat no longer listed is removed
    /// (EF deletes the orphan), a listed one updated in place, a new one added, so EF never tracks two rows with one key.
    /// </summary>
    private static void MergeBaseStats(ItemTemplate row, IReadOnlyList<ItemTemplateBaseStatDto> wanted)
    {
        foreach (ItemTemplateBaseStat held in row.BaseStats.ToList())
        {
            if (!wanted.Any(w => (int)w.Stat == (int)held.Stat))
                row.BaseStats.Remove(held);
        }

        foreach (ItemTemplateBaseStatDto w in wanted)
        {
            var stat = (Avalon.Domain.World.ItemBaseStat)(int)w.Stat;
            ItemTemplateBaseStat? held = row.BaseStats.Find(s => s.Stat == stat);
            if (held is null)
            {
                row.BaseStats.Add(new ItemTemplateBaseStat { TemplateId = row.Id, Stat = stat, Min = w.Min, Max = w.Max });
            }
            else
            {
                held.Min = w.Min;
                held.Max = w.Max;
            }
        }
    }

    /// <summary>The modifiers as the audit line shows them: stat:kind:value by stat.</summary>
    private static string ModifierText(IEnumerable<Avalon.Domain.World.AuraStatModifier> modifiers) =>
        string.Join(",", modifiers.OrderBy(m => m.Stat)
            .Select(m => $"{m.Stat}:{m.Kind}:{m.Value.ToString("R", CultureInfo.InvariantCulture)}"));

    /// <summary>
    /// Makes the row's modifiers the request's, by stat: a stat no longer listed is removed (EF deletes the orphan), a
    /// listed one updated in place, a new one added, so EF never tracks two rows with one key.
    /// </summary>
    private static void MergeModifiers(AuraTemplate row, IReadOnlyList<AuraStatModifierDto> wanted)
    {
        foreach (Avalon.Domain.World.AuraStatModifier held in row.Modifiers.ToList())
        {
            if (!wanted.Any(w => (int)w.Stat == (int)held.Stat))
                row.Modifiers.Remove(held);
        }

        foreach (AuraStatModifierDto w in wanted)
        {
            var stat = (Avalon.Domain.World.AuraStat)(int)w.Stat;
            var kind = (Avalon.Domain.World.AuraModifierKind)(int)w.Kind;
            Avalon.Domain.World.AuraStatModifier? held = row.Modifiers.FirstOrDefault(m => m.Stat == stat);
            if (held is null)
            {
                row.Modifiers.Add(new Avalon.Domain.World.AuraStatModifier { AuraId = row.Id, Stat = stat, Kind = kind, Value = w.Value });
            }
            else
            {
                held.Kind = kind;
                held.Value = w.Value;
            }
        }
    }
}
