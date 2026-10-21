namespace Avalon.Api.Contract;

/// <summary>
/// The states a harmful aura puts its holder in (auras), as flags so one aura can grant two; None for most auras. The
/// stored numbers, so values are only ever appended, and sent as the integer mask like the other contract flags. A slow
/// is not a flag: it is a harmful aura with a negative movement speed modifier.
/// </summary>
[Flags]
public enum CrowdControl : byte
{
    None = 0,
    Stun = 1,
    Root = 2,
    Silence = 4,
}
