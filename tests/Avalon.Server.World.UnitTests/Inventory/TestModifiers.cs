using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>Item instance modifiers for the economy and save tests (#1179).</summary>
internal static class TestModifiers
{
    /// <summary>A modifier with a new id on <paramref name="item" />, its values given as (index, stat, value, high end).</summary>
    public static ItemInstanceModifier Modifier(ItemInstanceId item, ushort modifierId, byte ordinal,
        params (byte Index, StatType Stat, int Value, int? Value2)[] values)
    {
        var id = Guid.CreateVersion7();
        return new ItemInstanceModifier
        {
            Id = id,
            ItemInstanceId = item,
            ModifierId = modifierId,
            Ordinal = ordinal,
            Values = [.. values.Select(v => new ItemInstanceModifierValue
                { ItemInstanceModifierId = id, Index = v.Index, Stat = v.Stat, Value = v.Value, Value2 = v.Value2 })],
        };
    }

    /// <summary>Each modifier as its id, catalog id, ordinal and values, in the order given: equal exactly when they are.</summary>
    public static string Describe(params IReadOnlyList<ItemInstanceModifier> modifiers) =>
        string.Join(" | ", modifiers.Select(m => $"{m.Id} {Rolled(m)}"));

    /// <summary>What <see cref="Describe" /> says, without the ids: what a trade record keeps of each modifier.</summary>
    public static string DescribeRolls(IReadOnlyList<ItemInstanceModifier> modifiers) => string.Join(" | ", modifiers.Select(Rolled));

    /// <summary>A trade record's copy of an item's modifiers, in ordinal order, as the overload above words the item's own.</summary>
    public static string DescribeRolls(IEnumerable<TradeRecordItemModifier> modifiers) => string.Join(" | ", modifiers
        .OrderBy(m => m.Ordinal)
        .Select(m => Line(m.ModifierId, m.Ordinal, m.Values.OrderBy(v => v.Index).Select(v => (v.Index, v.Stat, v.Value, v.Value2)))));

    private static string Rolled(ItemInstanceModifier m) =>
        Line(m.ModifierId, m.Ordinal, m.Values.Select(v => (v.Index, v.Stat, v.Value, v.Value2)));

    private static string Line(ushort modifierId, byte ordinal, IEnumerable<(byte Index, StatType Stat, int Value, int? Value2)> values) =>
        $"{modifierId}@{ordinal}: " + string.Join(", ", values.Select(v => $"{v.Index} {v.Stat} {v.Value}..{v.Value2}"));
}
