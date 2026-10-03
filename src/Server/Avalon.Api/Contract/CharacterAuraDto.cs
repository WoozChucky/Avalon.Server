namespace Avalon.Api.Contract;

/// <summary>One saved aura of a character, as its world last saved it.</summary>
public sealed class CharacterAuraDto
{
    public uint AuraId { get; set; }

    /// <summary>Who applied it, raw; 0 for nobody or nobody known.</summary>
    public ulong CasterGuid { get; set; }

    /// <summary>The ability that applied it, or null.</summary>
    public uint? SourceAbilityId { get; set; }

    public int Stacks { get; set; }

    /// <summary>The milliseconds it had left when saved; nothing runs it down while the character is offline.</summary>
    public uint RemainingMs { get; set; }

    /// <summary>Its whole duration when last applied, in milliseconds.</summary>
    public uint DurationMs { get; set; }

    /// <summary>The ticks it still owed.</summary>
    public int TicksLeft { get; set; }

    public DateTime AppliedAt { get; set; }
}
