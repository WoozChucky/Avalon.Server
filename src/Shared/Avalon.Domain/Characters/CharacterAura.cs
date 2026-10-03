using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Characters;

/// <summary>
/// One aura a character held when it was last saved: enough to resume it as it was, its time having stood still while
/// the character was out of the world. <see cref="AuraId" /> points into the World database, with no foreign key; a row
/// whose aura is no longer loaded is dropped at select.
/// </summary>
public class CharacterAura
{
    public CharacterId CharacterId { get; set; } = default!;

    /// <summary>Its place among the character's auras, from 0; with the character, the key.</summary>
    public int Slot { get; set; }

    public uint AuraId { get; set; }

    /// <summary>Who applied it, raw; 0 for nobody or nobody known. Kept so an Independent aura stays one per caster.</summary>
    public ulong CasterGuid { get; set; }

    /// <summary>The ability that applied it, for its threat and power gain; null for none.</summary>
    public uint? SourceAbilityId { get; set; }

    public int Stacks { get; set; }

    /// <summary>Milliseconds it had left, rounded up.</summary>
    public uint RemainingMs { get; set; }

    /// <summary>Its whole duration when last applied, in milliseconds.</summary>
    public uint DurationMs { get; set; }

    /// <summary>The ticks it still owed.</summary>
    public int TicksLeft { get; set; }

    /// <summary>Its snapshot: the amount per tick and per stack, and the caster's crit chance and level.</summary>
    public float TickAmount { get; set; }

    public float CritPct { get; set; }

    public int CasterLevel { get; set; }

    /// <summary>
    /// The fraction of a point its ticks had earned but not yet dealt or healed, so its remaining ticks add up to what
    /// they would have had it never left the world.
    /// </summary>
    public double PeriodicCarry { get; set; }

    public DateTime AppliedAt { get; set; }
}
