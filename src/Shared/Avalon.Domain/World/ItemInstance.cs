using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

public class ItemInstance
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public ItemInstanceId Id { get; set; }
    
    [Required]
    public ItemTemplate Template { get; set; }
    public ItemTemplateId TemplateId { get; set; }
    
    public CharacterId CharacterId { get; set; }
    
    [DefaultValue(1)]
    public uint Count { get; set; }
    
    public uint Durability { get; set; }
    
    public uint Charges { get; set; }
    
    public ItemInstanceFlags Flags { get; set; }

    public DateTime UpdatedAt { get; set; }
}

[Flags]
public enum ItemInstanceFlags
{
    None = 0,
    Attuned = 1,
    Tradeable = 2,
    Broken = 4,
    Cooldown = 8,
}
