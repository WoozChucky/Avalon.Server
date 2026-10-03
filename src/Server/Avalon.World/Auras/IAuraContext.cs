using Avalon.Common;
using Avalon.Common.ValueObjects;

namespace Avalon.World.Auras;

/// <summary>
/// The curated view an AuraScript gets of one aura on one unit, for one hook call. Reads, and four acts that go through
/// the services that own them. Tick thread only. World-side, not the modding API.
/// </summary>
public interface IAuraContext
{
    AuraId AuraId { get; }
    string AuraName { get; }

    /// <summary>The unit holding the aura.</summary>
    ObjectGuid Target { get; }
    string TargetName { get; }
    uint TargetHealth { get; }
    uint TargetMaxHealth { get; }
    bool TargetIsCharacter { get; }

    /// <summary>Who applied it; raw 0 when nobody did, or nobody is known.</summary>
    ObjectGuid Caster { get; }

    /// <summary>The caster is alive in the target's instance now: only then does the aura's damage credit it.</summary>
    bool CasterPresent { get; }

    uint Stacks { get; }
    TimeSpan Remaining { get; }
    TimeSpan Duration { get; }

    /// <summary>Ends this aura, with reason Script, once the hook returns.</summary>
    void Remove();

    /// <summary>
    /// A hit on the target from this aura, as one of its damage ticks: crit and armour roll with its snapshot, never a
    /// dodge or a block; the caster is credited while present. Answers the damage dealt; 0 on a target that ignores hits.
    /// It goes through the combat service's periodic path with no hostility check, from any aura, a helpful one
    /// included: aura scripts are trusted, so a script author must make sure the target is one it means to hurt.
    /// </summary>
    uint Damage(uint amount);

    /// <summary>A fixed heal of the target (no scaling, no crit), never past its maximum. Answers the health restored.</summary>
    uint Heal(uint amount);

    /// <summary>A system line to the target, when it is a character with a connection.</summary>
    void Tell(string line);
}
