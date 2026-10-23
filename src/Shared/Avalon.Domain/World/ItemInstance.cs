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

    /// <summary>
    /// What this copy rolled (#1179): its rarity, item level and modifiers, stored as JSON in one column. Every
    /// equipment instance has rolls; null for a consumable or a quest item.
    /// </summary>
    public ItemRolls? Rolls { get; set; }

    public DateTime UpdatedAt { get; set; }
}
