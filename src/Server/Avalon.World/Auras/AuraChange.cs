using Avalon.Common.ValueObjects;

namespace Avalon.World.Auras;

/// <summary>
/// One change a unit's watchers are owed, as it stood when it happened. Removed carries no time left.
/// <paramref name="Control" /> is the aura's crowd control as the wire carries it
/// (<see cref="ActiveAura.WireControl" />).
/// </summary>
public readonly record struct AuraChange(
    AuraId AuraId, uint Key, ulong CasterGuid, uint Stacks, uint RemainingMs, uint DurationMs, AuraChangeKind Kind,
    uint Control);
