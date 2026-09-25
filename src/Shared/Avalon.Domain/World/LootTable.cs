using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// What a creature can drop when it dies: a list of <see cref="LootTableEntry"/>s. A creature
/// template names one through <see cref="CreatureTemplate.LootTableId"/>, and an entry can roll
/// another table, so several creatures can share a common one.
/// </summary>
/// <remarks>
/// Ids are chosen by whoever authors the data, not generated, because other rows refer to them.
/// </remarks>
public class LootTable : IDbEntity<LootTableId>
{
    public LootTableId Id { get; set; } = default!;

    /// <summary>A label for whoever authors loot. Only error messages read it at runtime.</summary>
    public string Name { get; set; } = string.Empty;

    public List<LootTableEntry> Entries { get; set; } = [];
}
