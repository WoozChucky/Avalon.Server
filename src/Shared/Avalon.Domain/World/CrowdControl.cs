namespace Avalon.Domain.World;

/// <summary>
/// The states a harmful aura can put its holder in, as flags so one aura can grant two. Stun stops moving, turning and
/// every action but cancelling one's own buffs; Root stops moving; Silence stops every ability but the basic attack.
/// A slow is not a flag: it is any harmful aura with a negative MovementSpeed modifier (see AuraRules.IsSlow).
/// </summary>
[Flags]
public enum CrowdControl : byte
{
    None = 0,
    Stun = 1,
    Root = 2,
    Silence = 4,
}
