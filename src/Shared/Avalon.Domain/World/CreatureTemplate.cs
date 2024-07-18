using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

public class CreatureTemplate : IDbEntity<CreatureTemplateId>, ICreatureMetadata
{
    [Key]
    [Required]
    public CreatureTemplateId Id { get; set; }
    
    public string Name { get; set; } = string.Empty;
    
    public string SubName { get; set; } = string.Empty;
    
    public string IconName { get; set; } = string.Empty;

    public short MinLevel { get; set; }

    public short MaxLevel { get; set; }

    public float SpeedWalk { get; set; }

    public float SpeedRun { get; set; }

    public float SpeedSwim { get; set; }

    public short Rank { get; set; }

    public CreatureFamily Family { get; set; }

    public CreatureType Type { get; set; }

    public short Exp { get; set; }

    public int LootId { get; set; }

    public int MinGold { get; set; }

    public int MaxGold { get; set; }

    public string AIName { get; set; } = string.Empty;

    public short MovementType { get; set; }

    public float DetectionRange { get; set; }

    public int MovementId { get; set; }

    public string ScriptName { get; set; } = string.Empty;

    public float HealthModifier { get; set; }

    public float ManaModifier { get; set; }

    public float ArmorModifier { get; set; }

    public float ExperienceModifier { get; set; }

    public short RegenHealth { get; set; }

    public short DmgSchool { get; set; }

    public float DamageModifier { get; set; }

    public int BaseAttackTime { get; set; }

    public int RangeAttackTime { get; set; }
    
    [NotMapped]
    public Vector3 StartPosition { get; set; }
}
