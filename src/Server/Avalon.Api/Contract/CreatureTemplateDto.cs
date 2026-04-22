using Avalon.World.Public.Enums;

namespace Avalon.Api.Contract;

public sealed class CreatureTemplateDto
{
    public ulong Id { get; set; }
    public string Name { get; set; } = "";
    public string SubName { get; set; } = "";
    public string IconName { get; set; } = "";
    public short MinLevel { get; set; }
    public short MaxLevel { get; set; }
    public float SpeedWalk { get; set; }
    public float SpeedRun { get; set; }
    public float SpeedSwim { get; set; }
    public short Rank { get; set; }
    public CreatureFamily Family { get; set; }
    public CreatureType Type { get; set; }
    public int LootId { get; set; }
    public int MinGold { get; set; }
    public int MaxGold { get; set; }
    public string AIName { get; set; } = "";
    public short MovementType { get; set; }
    public float DetectionRange { get; set; }
    public int MovementId { get; set; }
    public string ScriptName { get; set; } = "";
    public float HealthModifier { get; set; }
    public float ManaModifier { get; set; }
    public float ArmorModifier { get; set; }
    public float ExperienceModifier { get; set; }
    public short RegenHealth { get; set; }
    public short DmgSchool { get; set; }
    public float DamageModifier { get; set; }
    public int BaseAttackTime { get; set; }
    public int RangeAttackTime { get; set; }
    public uint Experience { get; set; }

    /// <summary>Seconds before the creature re-spawns after death.</summary>
    public int RespawnTimerSecs { get; set; }

    /// <summary>Seconds before the creature's corpse is removed.</summary>
    public int BodyRemoveTimerSecs { get; set; }
}
