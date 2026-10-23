using System.Globalization;
using Avalon.Api.Contract;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Infrastructure.Scripts;

namespace Avalon.Api.Worlds.Templates;

/// <summary>Field errors, keyed by the camelCase name of the field (as the request body spells it).</summary>
public sealed class TemplateErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool Any => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out List<string>? list))
            _errors[field] = list = [];
        if (!list.Contains(message, StringComparer.Ordinal))
            list.Add(message);
    }

    public IDictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal);
}

/// <summary>
/// One rule set per template kind: the rules a save must pass so that the world's own loaders accept the row
/// afterwards. The field rules run on the request, the "world rules" on the row as it would be stored (they cover
/// the columns an edit cannot reach too, so an already-bad row is refused rather than re-saved). Rules that the
/// shared <c>Avalon.Combat</c> loaders own are called, not copied: <see cref="AbilityRules.Problem"/>,
/// <see cref="AuraRules.Problem"/>, <see cref="AuraRules.LinkProblem"/>, <see cref="AuraRules.DamageTypeProblem"/>,
/// <see cref="AuraRules.SourceProblem"/> and <see cref="CreatureTemplateRules.Validate"/>. The world loads items with no
/// validation of its own, so items get sanity rules, plus the ItemTemplates table's checks as request fields (a weapon's
/// attack time, #1178; a template's rarity and base kind, #1179), so a bad value is a 400 rather than a database error.
/// </summary>
public static class TemplateValidation
{
    /// <summary>The lowest stat modifier the AuraStatModifiers table takes, exclusive (CK_AuraStatModifiers_Value).</summary>
    private const float AuraModifierFloor = -1_000_000f;

    public static TemplateErrors Item(UpdateItemTemplateRequest r)
    {
        TemplateErrors e = new();
        Name(e, r.Name);
        Defined(e, "class", r.Class);
        Defined(e, "subClass", r.SubClass);
        // CK_ItemTemplates_Rarity (#1179): Magic and Rare are rolled on a copy, never a template's own.
        if (r.Rarity is not (Contract.ItemRarity.Common or Contract.ItemRarity.Unique))
            e.Add("rarity", "A template is Common or Unique; Magic and Rare are rolled on a copy.");
        if (r.Slot is { } slot) Defined(e, "slot", slot);
        if (r.BaseKind is { } baseKind)
        {
            Defined(e, "baseKind", baseKind);
            if (r.Slot is null)
            {
                e.Add("baseKind", "A base kind needs the slot it is worn in.");
            }
            else if (Enum.IsDefined(baseKind)
                     && ItemBaseKindSlots.SlotOf((Domain.World.ItemBaseKind)baseKind) is var wornIn
                     && wornIn != (Domain.World.ItemSlotType)r.Slot.Value)
            {
                // A base kind and its slot agree (#1179): what rolls on a base is read by its kind.
                e.Add("baseKind", $"A {baseKind} base is worn in the {wornIn} slot, not {r.Slot.Value}.");
            }
        }

        // ItemInstances.ItemLevel (#1179, smallint, CK_ItemInstances_ItemLevel): a copy made from the template takes its
        // required level as its item level, so a level above the column's range could never be stored.
        if (r.RequiredLevel > short.MaxValue)
            e.Add("requiredLevel", $"Required level must be at most {short.MaxValue}: a copy's item level is taken from it.");

        Flags(e, "flags", r.Flags);
        if (r.MaxStackSize < 1) e.Add("maxStackSize", "Max stack size must be at least 1.");
        else if (r.Slot is not null && r.MaxStackSize > 1) e.Add("maxStackSize", "Equipment never stacks: its max stack size is 1.");
        Classes(e, r.AllowedClasses);

        Damage(e, 1, r.DamageMin1, r.DamageMax1, r.DamageType1);
        Damage(e, 2, r.DamageMin2, r.DamageMax2, r.DamageType2);
        // CK_ItemTemplates_AttackTimeMs: a weapon's pace is above 0; an item that is not a weapon has none.
        if (r.AttackTimeMs == 0) e.Add("attackTimeMs", "Attack time must be above 0, or null for an item that is not a weapon.");

        (Contract.StatType? Type, uint? Value)[] stats =
        [
            (r.StatType1, r.StatValue1), (r.StatType2, r.StatValue2), (r.StatType3, r.StatValue3),
            (r.StatType4, r.StatValue4), (r.StatType5, r.StatValue5), (r.StatType6, r.StatValue6),
            (r.StatType7, r.StatValue7), (r.StatType8, r.StatValue8), (r.StatType9, r.StatValue9),
            (r.StatType10, r.StatValue10),
        ];
        for (int i = 0; i < stats.Length; i++)
        {
            int n = i + 1;
            if (stats[i].Type is { } type) Defined(e, $"statType{n}", type);
            if (stats[i].Type is not null && stats[i].Value is null)
                e.Add($"statValue{n}", $"Stat {n} has a type but no value.");
            if (stats[i].Type is null && stats[i].Value is not null)
                e.Add($"statType{n}", $"Stat {n} has a value but no type.");
        }

        return e;
    }

    public static TemplateErrors Ability(UpdateAbilityTemplateRequest r)
    {
        TemplateErrors e = new();
        Name(e, r.Name);
        if (string.IsNullOrWhiteSpace(r.ScriptName)) e.Add("scriptName", "Script name is required.");
        Defined(e, "range", r.Range);
        Flags(e, "effects", r.Effects);
        Defined(e, "costPowerType", r.CostPowerType);
        // CK_AbilityTemplates_CostPowerType: only a cost of 0 may name no pool.
        if (r.Cost > 0 && r.CostPowerType == Contract.PowerType.None)
            e.Add("costPowerType", "A cost needs the pool it is spent from (Mana, Fury or Energy).");
        if (r.DamageType is { } damageType) Defined(e, "damageType", damageType);
        Flags(e, "tags", r.Tags);
        if (r.ConvertToType is { } convertTo) Defined(e, "convertToType", convertTo);
        if (r.AddedType is { } addedType) Defined(e, "addedType", addedType);
        Classes(e, r.AllowedClasses);
        return e;
    }

    public static TemplateErrors Aura(UpdateAuraTemplateRequest r)
    {
        TemplateErrors e = new();
        Name(e, r.Name);
        if (string.IsNullOrWhiteSpace(r.Icon)) e.Add("icon", "Icon is required.");
        Defined(e, "kind", r.Kind);
        Defined(e, "periodicKind", r.PeriodicKind);
        Defined(e, "stacking", r.Stacking);
        Flags(e, "control", r.Control);
        foreach (AuraStatModifierDto m in r.Modifiers ?? [])
        {
            Defined(e, "modifiers", m.Stat);
            Defined(e, "modifiers", m.Kind);
            // CK_AuraStatModifiers_Value: a floor the world's rules do not have.
            if (m.Value <= AuraModifierFloor)
                e.Add("modifiers", $"{m.Stat} modifier must be above {AuraModifierFloor.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (r.Modifiers is { } list && list.Select(m => m.Stat).Distinct().Count() != list.Count)
            e.Add("modifiers", "A stat is modified more than once.");
        return e;
    }

    public static TemplateErrors Creature(UpdateCreatureTemplateRequest r)
    {
        TemplateErrors e = new();
        Name(e, r.Name);
        Defined(e, "rarity", r.Rarity);
        Defined(e, "family", r.Family);
        Defined(e, "type", r.Type);
        Defined(e, "damageType", r.DamageType);
        if (r.MinLevel > r.MaxLevel) e.Add("maxLevel", "Max level must not be below min level.");
        foreach ((string field, float value) in new (string, float)[]
                 {
                     ("speedWalk", r.SpeedWalk), ("speedRun", r.SpeedRun), ("speedSwim", r.SpeedSwim),
                     ("detectionRange", r.DetectionRange), ("healthModifier", r.HealthModifier),
                     ("manaModifier", r.ManaModifier), ("armorModifier", r.ArmorModifier),
                     ("experienceModifier", r.ExperienceModifier), ("damageModifier", r.DamageModifier),
                     ("evasionModifier", r.EvasionModifier), ("wardModifier", r.WardModifier),
                 })
        {
            if (!float.IsFinite(value) || value < 0f) e.Add(field, "Must be a finite number of 0 or more.");
        }

        // #1178: a resistance is a percentage either way, negative taking more (CreatureTemplateRules, the check constraints).
        foreach ((string field, float value) in new (string, float)[]
                 {
                     ("fireResistance", r.FireResistance), ("coldResistance", r.ColdResistance),
                     ("lightningResistance", r.LightningResistance), ("blightResistance", r.BlightResistance),
                 })
        {
            if (!float.IsFinite(value) || value < -100f || value > 100f) e.Add(field, "Must be a number from -100 to 100.");
        }

        if (r.MinGold < 0) e.Add("minGold", "Min gold must be 0 or more.");
        if (r.MaxGold < r.MinGold) e.Add("maxGold", "Max gold must not be below min gold.");
        if (r.RangeAttackTime < 0) e.Add("rangeAttackTime", "Must be 0 or more.");
        if (r.BodyRemoveTimerSecs < 0) e.Add("bodyRemoveTimerSecs", "Must be 0 or more.");
        // BaseAttackTime is the world loader's rule: CreatureTemplateRules, run by CreatureWorldRules.
        return e;
    }

    /// <summary>
    /// What the world's ability loader and the AbilityTemplates check constraints say of the row as it would be
    /// stored. Keyed by the editable field the problem is about; a problem in a column an edit cannot reach is keyed
    /// <c>template</c>.
    /// </summary>
    public static void AbilityWorldRules(TemplateErrors errors, AbilityTemplate row)
    {
        if (AbilityRules.Problem(row) is not { } problem) return;
        errors.Add(AbilityField(problem), $"The world would refuse this ability: {problem}.");
    }

    /// <summary>
    /// What the world's Creatures reload and the CreatureTemplates check constraints say of the row as it would be
    /// stored: <see cref="CreatureTemplateRules.Validate"/> on the row, and the body radius check.
    /// </summary>
    public static void CreatureWorldRules(TemplateErrors errors, CreatureTemplate row)
    {
        try
        {
            CreatureTemplateRules.Validate([row]);
        }
        catch (InvalidDataException ex)
        {
            errors.Add(CreatureField(ex.Message), $"The world would refuse this creature: {ex.Message}");
        }

        // CK_CreatureTemplates_BodyRadius_Positive.
        if (!float.IsFinite(row.BodyRadius) || row.BodyRadius <= 0f)
            errors.Add("bodyRadius", "Body radius must be a finite number above 0.");
    }

    /// <summary>
    /// What the world's Auras reload and the AuraTemplates check constraints say of the row as it would be stored:
    /// <see cref="AuraRules.Problem"/>, keyed by the field the problem is about.
    /// </summary>
    public static void AuraWorldRules(TemplateErrors errors, AuraTemplate row)
    {
        if (AuraRules.Problem(row) is not { } problem) return;
        errors.Add(AuraField(problem), $"The world would refuse this aura: {problem}.");
    }

    /// <summary>
    /// A script name the world's published catalog does not list (<paramref name="known"/> is the list for this kind of
    /// template). Not checked while no catalog is published (<paramref name="catalog"/> null: no world has reported in),
    /// and an empty name is never unknown: whether a template needs a script is its own rule. A name equal to
    /// <paramref name="stored"/> is not checked: only a change of script is, so a row that already names an unlisted
    /// script can still have its other fields edited. <paramref name="field"/> is the request field the error is keyed by.
    /// </summary>
    public static void ScriptKnown(TemplateErrors errors, string? scriptName, string? stored, ScriptCatalogSnapshot? catalog,
        Func<ScriptCatalogSnapshot, IReadOnlyList<string>?> known, string field = "scriptName")
    {
        if (catalog is null || string.IsNullOrWhiteSpace(scriptName)) return;
        if (string.Equals(scriptName, stored, StringComparison.Ordinal)) return;
        // A world that publishes no list of this kind (one built before item scripts) is not checked, as with no catalog.
        if (known(catalog) is not { } names) return;
        if (!names.Contains(scriptName, StringComparer.Ordinal))
            errors.Add(field, $"Unknown script '{scriptName}' on this world");
    }

    /// <summary>
    /// The field an <see cref="AbilityRules.Problem"/> refusal is about, keyed by its wording; <c>template</c> for a column
    /// an edit cannot reach.
    /// </summary>
    public static string AbilityField(string problem) => problem switch
    {
        _ when problem.Contains("must use", StringComparison.Ordinal) => "scriptName",
        _ when problem.Contains("power type", StringComparison.OrdinalIgnoreCase) => "costPowerType",
        _ when problem.StartsWith("Cost ", StringComparison.Ordinal) => "costPowerType",
        _ when problem.StartsWith("the ability does nothing", StringComparison.Ordinal) => "effects",
        _ when problem.StartsWith("unknown AddedType", StringComparison.Ordinal) => "addedType",
        _ when problem.Contains("damage type", StringComparison.Ordinal) => "damageType",
        // The offence model (#1178): AbilityRules' wording, keyed by the editable field each refusal is about.
        _ when problem.StartsWith("a class attack deals its weapon's physical damage", StringComparison.Ordinal) => "damageType",
        _ when problem.StartsWith("a basic attack is paced by its weapon", StringComparison.Ordinal) => "cooldown",
        _ when IsTagProblem(problem) => "tags",
        _ when problem.StartsWith("ConvertPct ", StringComparison.Ordinal)
               || problem.StartsWith("only an Attack converts", StringComparison.Ordinal) => "convertPct",
        _ when problem.StartsWith("a conversion names an element", StringComparison.Ordinal)
               || problem.StartsWith("only an Attack names a ConvertToType", StringComparison.Ordinal) => "convertToType",
        _ when problem.StartsWith("AddedMin ", StringComparison.Ordinal) => "addedMax",
        _ when problem.StartsWith("added damage names its type", StringComparison.Ordinal) => "addedType",
        _ when problem.StartsWith("SpellBaseMin ", StringComparison.Ordinal)
               || problem.Contains("needs a SpellBaseMax above 0", StringComparison.Ordinal) => "spellBaseMax",
        _ when problem.StartsWith("SpellBasePerLevelPct ", StringComparison.Ordinal) => "spellBasePerLevelPct",
        _ when problem.StartsWith("DamageEffectiveness ", StringComparison.Ordinal)
               || problem.Contains("needs a DamageEffectiveness above 0", StringComparison.Ordinal) => "damageEffectiveness",
        _ => "template",
    };

    /// <summary>
    /// The field an <see cref="AuraRules.SourceProblem"/> refusal is about, on an ability edit: the spell base for a Spell
    /// or a Heal with nothing for its aura to draw on, otherwise the tags the aura scales with.
    /// </summary>
    public static string AbilitySourceField(string problem) =>
        problem.Contains("needs a SpellBaseMax above 0", StringComparison.Ordinal) ? "spellBaseMax" : "tags";

    /// <summary>Whether an <see cref="AbilityRules.Problem"/> refusal is about what the ability's tags say it is.</summary>
    private static bool IsTagProblem(string problem) =>
        problem.StartsWith("Tags ", StringComparison.Ordinal)
        || problem.StartsWith("an ability is at most one of Attack, Spell and Heal", StringComparison.Ordinal)
        || problem.StartsWith("DamageOverTime is an aura's tag", StringComparison.Ordinal)
        || problem.StartsWith("only a Hostile ability is an Attack or a Spell", StringComparison.Ordinal)
        || problem.StartsWith("a Hostile ability that deals damage is an Attack or a Spell", StringComparison.Ordinal)
        || problem.StartsWith("an Ally ability that heals is a Heal", StringComparison.Ordinal)
        || problem.StartsWith("Projectile is the tag of a projectile", StringComparison.Ordinal)
        || problem.StartsWith("only an Attack is Melee", StringComparison.Ordinal);

    private static string CreatureField(string problem) => problem switch
    {
        _ when problem.Contains(nameof(CreatureTemplate.BaseAttackTime), StringComparison.Ordinal) => "baseAttackTime",
        _ when problem.Contains(nameof(CreatureTemplate.StunDurationPct), StringComparison.Ordinal) => "stunDurationPct",
        _ when problem.Contains(nameof(CreatureTemplate.RootDurationPct), StringComparison.Ordinal) => "rootDurationPct",
        _ when problem.Contains(nameof(CreatureTemplate.SilenceDurationPct), StringComparison.Ordinal) => "silenceDurationPct",
        _ when problem.Contains(nameof(CreatureTemplate.SlowDurationPct), StringComparison.Ordinal) => "slowDurationPct",
        _ when problem.Contains(nameof(CreatureTemplate.DamageType), StringComparison.Ordinal) => "damageType",
        _ => "template",
    };

    /// <summary>The field an <see cref="AuraRules.Problem"/> refusal is about, keyed by its wording.</summary>
    public static string AuraField(string problem) => problem switch
    {
        _ when problem.StartsWith("unknown kind", StringComparison.Ordinal) => "kind",
        _ when problem.StartsWith("unknown periodic kind", StringComparison.Ordinal) => "periodicKind",
        _ when problem.StartsWith("unknown stacking", StringComparison.Ordinal) => "stacking",
        _ when problem.StartsWith("unknown control", StringComparison.Ordinal) => "control",
        _ when problem.Contains("cannot stun, root or silence", StringComparison.Ordinal) => "control",
        _ when problem.StartsWith("BreakOnDamagePct", StringComparison.Ordinal) => "breakOnDamagePct",
        _ when problem.StartsWith("BreakPerSecondPct", StringComparison.Ordinal) => "breakPerSecondPct",
        _ when problem.StartsWith("TickIntervalMs", StringComparison.Ordinal) => "tickIntervalMs",
        _ when problem.Contains("TickIntervalMs above 0", StringComparison.Ordinal) => "tickIntervalMs",
        _ when problem.StartsWith("DurationMs", StringComparison.Ordinal) => "durationMs",
        _ when problem.Contains("MaxStacks", StringComparison.Ordinal) => "maxStacks",
        _ when problem.StartsWith("PeriodicBase", StringComparison.Ordinal) => "periodicBase",
        _ when problem.StartsWith("PeriodicEffectiveness", StringComparison.Ordinal) => "periodicEffectiveness",
        _ when problem.Contains("cannot heal", StringComparison.Ordinal) || problem.Contains("cannot deal", StringComparison.Ordinal) => "periodicKind",
        _ when problem.Contains("modif", StringComparison.Ordinal) || problem.StartsWith("unknown stat", StringComparison.Ordinal)
               || problem.Contains("is already a percentage", StringComparison.Ordinal) => "modifiers",
        _ => "template",
    };

    private static void Name(TemplateErrors e, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) e.Add("name", "Name is required.");
    }

    private static void Defined<T>(TemplateErrors e, string field, T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) e.Add(field, $"'{value}' is not a known value.");
    }

    private static void Flags<T>(TemplateErrors e, string field, T value) where T : struct, Enum
    {
        long all = Enum.GetValues<T>().Aggregate(0L, (acc, v) => acc | Convert.ToInt64(v, CultureInfo.InvariantCulture));
        if ((Convert.ToInt64(value, CultureInfo.InvariantCulture) & ~all) != 0) e.Add(field, "Contains a flag that does not exist.");
    }

    private static void Classes(TemplateErrors e, List<CharacterClass>? classes)
    {
        if (classes is null) return;
        foreach (CharacterClass c in classes)
        {
            if (!CharacterClasses.IsPlayable(c)) e.Add("allowedClasses", $"'{c}' is not a known class.");
        }

        if (classes.Distinct().Count() != classes.Count) e.Add("allowedClasses", "A class is listed more than once.");
    }

    private static void Damage(TemplateErrors e, int n, uint? min, uint? max, Contract.DamageType? type)
    {
        if (type is { } t) Defined(e, $"damageType{n}", t);
        // A weapon's damage is always typed (#1179): its local physical scaling applies to its Physical ranges only.
        else if (min is not null || max is not null) e.Add($"damageType{n}", $"Damage {n} needs a damage type.");
        if (min is null != max is null)
            e.Add(min is null ? $"damageMin{n}" : $"damageMax{n}", $"Damage {n} needs both a minimum and a maximum.");
        else if (min > max)
            e.Add($"damageMax{n}", $"Damage {n} maximum must not be below its minimum.");
    }
}
