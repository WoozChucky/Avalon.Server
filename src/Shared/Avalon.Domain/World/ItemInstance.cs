using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common;
using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// One item a character owns. Persisted in the Character database beside the CharacterInventory
/// row that places it. <see cref="TemplateId" /> names a row in the World database, so there is no
/// navigation to the template and no foreign key: a join across two databases is not possible.
/// </summary>
public class ItemInstance : IDbEntity<ItemInstanceId>
{
    /// <summary>Allocated by the world server (IItemIdAllocator), never by EF or the database.</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public ItemInstanceId Id { get; set; }

    public ItemTemplateId TemplateId { get; set; }

    public CharacterId CharacterId { get; set; }

    [DefaultValue(1)]
    public uint Count { get; set; }

    public uint Durability { get; set; }

    public uint Charges { get; set; }

    public ItemInstanceFlags Flags { get; set; }

    /// <summary>This copy's rarity: a template's own (Common or Unique) for an item made from it outright.</summary>
    public ItemRarity Rarity { get; set; }

    /// <summary>This copy's item level, at least 1: the template's required level for an item made from it outright.</summary>
    public ushort ItemLevel { get; set; }

    /// <summary>
    /// A rare copy's two words, given once at its roll (#1179) and kept, so a change to the word lists never renames an item
    /// a player holds; null on every other copy (CK_ItemInstances_RareName).
    /// </summary>
    public string? RareName { get; set; }

    /// <summary>
    /// Whether the copy is identified (#1179): a magic, rare or unique copy that drops or comes from a quest arrives
    /// unidentified, and a common copy always is one (CK_ItemInstances_Identified). Every path that makes a copy states it.
    /// </summary>
    public required bool Identified { get; set; }

    /// <summary>
    /// The hidden running total of every imprint's instability roll (#1180), 0 on a copy never imprinted. World-side only:
    /// never sent to a client, shown over REST, logged or exported. At <see cref="ItemInstability.BrickAt" /> the copy
    /// bricks, so it never passes 199 (CK_ItemInstances_Instability holds it at 0 or more).
    /// </summary>
    public ushort Instability { get; set; }

    /// <summary>
    /// Whether the copy bricked (#1180): visible, and never modified or attuned again. True exactly when
    /// <see cref="Instability" /> is at least <see cref="ItemInstability.BrickAt" /> (CK_ItemInstances_Tainted).
    /// </summary>
    public bool Tainted { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// What the read fills (<c>GetByCharacterIdAsync</c>). A save never writes them through here: they go as their own
    /// rows (<c>ItemModifierWrite</c>), so an item row handed to a save carries none.
    /// </summary>
    public List<ItemInstanceModifier> Modifiers { get; set; } = [];

    /// <summary>
    /// The base stats this copy rolled when it was made (#1179), one row per stat: filled by the read and by the creation
    /// graph, as <see cref="Modifiers" /> is, and saved, like them, as their own rows (<c>ItemModifierWrite.BaseStats</c>).
    /// </summary>
    public List<ItemInstanceBaseStat> BaseStats { get; set; } = [];
}
