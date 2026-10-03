using System.Collections.Concurrent;
using System.Reflection;
using Avalon.Domain.World;

namespace Avalon.Balance.Core;

/// <summary>The seed rows the simulator uses, mutable so overrides can change them before validation.</summary>
public sealed class SeedTables
{
    public required List<AbilityTemplate> AbilityTemplates { get; init; }
    public required List<ClassLevelStat> ClassLevelStats { get; init; }
    public required List<ClassStatFactors> ClassStatFactors { get; init; }
    public required List<CombatFormula> CombatFormulas { get; init; }
    public required List<CreatureBaseStat> CreatureBaseStats { get; init; }
    public required List<CreatureRarityModifier> CreatureRarityModifiers { get; init; }
    public required List<CreatureTemplate> CreatureTemplates { get; init; }
    public required List<ItemTemplate> ItemTemplates { get; init; }
    public required List<CharacterCreateInfo> CharacterCreateInfos { get; init; }
    public required List<CharacterLevelExperience> CharacterLevelExperiences { get; init; }
    public required List<VendorStock> VendorStocks { get; init; }
    public required List<AuraTemplate> AuraTemplates { get; init; }
    public required List<AuraStatModifier> AuraStatModifiers { get; init; }

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Columns = new();

    /// <summary>
    /// A deep copy of the row lists: each row is a new object carrying the same writable public properties (the ones
    /// SeedReader fills), so an override on the copy never reaches this instance. Columns are copied by value; a
    /// collection-valued column is shared, which is safe because overrides only write scalars.
    /// </summary>
    public SeedTables Clone() => new()
    {
        AbilityTemplates = CloneRows(AbilityTemplates),
        ClassLevelStats = CloneRows(ClassLevelStats),
        ClassStatFactors = CloneRows(ClassStatFactors),
        CombatFormulas = CloneRows(CombatFormulas),
        CreatureBaseStats = CloneRows(CreatureBaseStats),
        CreatureRarityModifiers = CloneRows(CreatureRarityModifiers),
        CreatureTemplates = CloneRows(CreatureTemplates),
        ItemTemplates = CloneRows(ItemTemplates),
        CharacterCreateInfos = CloneRows(CharacterCreateInfos),
        CharacterLevelExperiences = CloneRows(CharacterLevelExperiences),
        VendorStocks = CloneRows(VendorStocks),
        AuraTemplates = CloneRows(AuraTemplates),
        AuraStatModifiers = CloneRows(AuraStatModifiers),
    };

    private static List<T> CloneRows<T>(List<T> rows) where T : class, new()
    {
        PropertyInfo[] columns = Columns.GetOrAdd(typeof(T), type => type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
            .ToArray());

        var copy = new List<T>(rows.Count);
        foreach (T row in rows)
        {
            var clone = new T();
            foreach (PropertyInfo column in columns)
                column.SetValue(clone, column.GetValue(row));
            copy.Add(clone);
        }

        return copy;
    }
}
