using Avalon.Api.Contract;
using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.Infrastructure.Scripts;

namespace Avalon.Api.Templates;

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
/// shared <c>Avalon.Combat</c> loaders own are called, not copied: <see cref="AbilityRules.Problem"/> and
/// <see cref="CreatureTemplateRules.Validate"/>. The world loads items with no validation of its own and the
/// ItemTemplates table has no check constraint, so items get sanity rules only.
/// </summary>
public static class TemplateValidation
{
    public static TemplateErrors Item(UpdateItemTemplateRequest r)
    {
        TemplateErrors e = new();
        Name(e, r.Name);
        Defined(e, "class", r.Class);
        Defined(e, "subClass", r.SubClass);
        Defined(e, "rarity", r.Rarity);
        if (r.Slot is { } slot) Defined(e, "slot", slot);
        Flags(e, "flags", r.Flags);
        if (r.MaxStackSize < 1) e.Add("maxStackSize", "Max stack size must be at least 1.");
        Classes(e, r.AllowedClasses);

        Damage(e, 1, r.DamageMin1, r.DamageMax1, r.DamageType1);
        Damage(e, 2, r.DamageMin2, r.DamageMax2, r.DamageType2);

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
        Classes(e, r.AllowedClasses);
        return e;
    }

    public static TemplateErrors Creature(UpdateCreatureTemplateRequest r)
    {
        TemplateErrors e = new();
        Name(e, r.Name);
        Defined(e, "rarity", r.Rarity);
        Defined(e, "family", r.Family);
        Defined(e, "type", r.Type);
        if (r.MinLevel > r.MaxLevel) e.Add("maxLevel", "Max level must not be below min level.");
        foreach ((string field, float value) in new (string, float)[]
                 {
                     ("speedWalk", r.SpeedWalk), ("speedRun", r.SpeedRun), ("speedSwim", r.SpeedSwim),
                     ("detectionRange", r.DetectionRange), ("healthModifier", r.HealthModifier),
                     ("manaModifier", r.ManaModifier), ("armorModifier", r.ArmorModifier),
                     ("experienceModifier", r.ExperienceModifier), ("damageModifier", r.DamageModifier),
                 })
        {
            if (!float.IsFinite(value) || value < 0f) e.Add(field, "Must be a finite number of 0 or more.");
        }

        if (r.MinGold < 0) e.Add("minGold", "Min gold must be 0 or more.");
        if (r.MaxGold < r.MinGold) e.Add("maxGold", "Max gold must not be below min gold.");
        if (r.RangeAttackTime < 0) e.Add("rangeAttackTime", "Must be 0 or more.");
        if (r.RespawnTimerSecs < 0) e.Add("respawnTimerSecs", "Must be 0 or more.");
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
            errors.Add(ex.Message.Contains(nameof(CreatureTemplate.BaseAttackTime), StringComparison.Ordinal)
                ? "baseAttackTime"
                : "template", $"The world would refuse this creature: {ex.Message}");
        }

        // CK_CreatureTemplates_BodyRadius_Positive.
        if (!float.IsFinite(row.BodyRadius) || row.BodyRadius <= 0f)
            errors.Add("bodyRadius", "Body radius must be a finite number above 0.");
    }

    /// <summary>
    /// A script name the world's published catalog does not list (<paramref name="known"/> is the list for this kind of
    /// template). Not checked while no catalog is published (<paramref name="catalog"/> null: no world has reported in),
    /// and an empty name is never unknown: whether a template needs a script is its own rule. A name equal to
    /// <paramref name="stored"/> is not checked: only a change of script is, so a row that already names an unlisted
    /// script can still have its other fields edited.
    /// </summary>
    public static void ScriptKnown(TemplateErrors errors, string? scriptName, string? stored, ScriptCatalogSnapshot? catalog,
        Func<ScriptCatalogSnapshot, IReadOnlyList<string>> known)
    {
        if (catalog is null || string.IsNullOrWhiteSpace(scriptName)) return;
        if (string.Equals(scriptName, stored, StringComparison.Ordinal)) return;
        if (!known(catalog).Contains(scriptName, StringComparer.Ordinal))
            errors.Add("scriptName", $"Unknown script '{scriptName}' on this world");
    }

    private static string AbilityField(string problem) => problem switch
    {
        _ when problem.Contains("must use", StringComparison.Ordinal) => "scriptName",
        _ when problem.Contains("power type", StringComparison.OrdinalIgnoreCase) => "costPowerType",
        _ when problem.StartsWith("Cost ", StringComparison.Ordinal) => "costPowerType",
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
        long all = Enum.GetValues<T>().Aggregate(0L, (acc, v) => acc | Convert.ToInt64(v));
        if ((Convert.ToInt64(value) & ~all) != 0) e.Add(field, "Contains a flag that does not exist.");
    }

    private static void Classes(TemplateErrors e, List<Avalon.World.Public.Enums.CharacterClass>? classes)
    {
        if (classes is null) return;
        foreach (var c in classes)
        {
            if (!Enum.IsDefined(c)) e.Add("allowedClasses", $"'{c}' is not a known class.");
        }

        if (classes.Distinct().Count() != classes.Count) e.Add("allowedClasses", "A class is listed more than once.");
    }

    private static void Damage(TemplateErrors e, int n, uint? min, uint? max, Contract.DamageType? type)
    {
        if (type is { } t) Defined(e, $"damageType{n}", t);
        if (min is null != max is null)
            e.Add(min is null ? $"damageMin{n}" : $"damageMax{n}", $"Damage {n} needs both a minimum and a maximum.");
        else if (min > max)
            e.Add($"damageMax{n}", $"Damage {n} maximum must not be below its minimum.");
    }
}
