using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.Network.Packets.Auras;

namespace Avalon.World.Auras;

/// <summary>
/// A unit's auras as a client is told them (auras), in entries reused from one packet to the next (#980): a packet is
/// encoded before its <c>Create</c> returns, so the list and its entries are free again as soon as it is. What one call
/// returns is good until the next. One per instance, tick thread only; it allocates only as it grows to the most entries
/// one packet has held.
/// </summary>
/// <remarks>
/// Every entry handed out has each of its members set by the call that hands it out, so it says only what a new one
/// would; a member added to <see cref="AuraEntryDto" /> must be set by both <see cref="List" /> and
/// <see cref="Updates" /> (<c>AuraWireShould</c> fails until it is).
/// </remarks>
public sealed class AuraWire
{
    /// <summary>
    /// <see cref="AuraEntryDto.Control" />'s bit for a slow (#770), above the <see cref="CrowdControl" /> flags
    /// (1 stun, 2 root, 4 silence), which it carries as they are.
    /// </summary>
    public const uint SlowBit = 8;

    private readonly List<AuraEntryDto> _entries = new(8);
    private readonly List<AuraEntryDto> _made = new(8);

    /// <summary>
    /// The crowd control <paramref name="template" /> puts its holder in, as <see cref="AuraEntryDto.Control" />
    /// carries it: its flags, and <see cref="SlowBit" /> for a slow (a harmful aura that lowers movement speed, found
    /// by walking its modifiers). Cached on each aura (<see cref="ActiveAura.WireControl" />): no entry works it out.
    /// </summary>
    public static uint ControlOf(AuraTemplate template) =>
        (uint)template.Control | (AuraRules.IsSlow(template) ? SlowBit : 0u);

    /// <summary>Every aura held, with the time each has left now; no action.</summary>
    public List<AuraEntryDto> List(UnitAuras auras, DateTimeOffset now)
    {
        _entries.Clear();
        // Indexed rather than walked with foreach, which boxes an interface's enumerator (#640).
        IReadOnlyList<ActiveAura> held = auras.All;
        for (int i = 0; i < held.Count; i++)
        {
            ActiveAura aura = held[i];
            AuraEntryDto entry = Next();
            entry.AuraId = aura.Id.Value;
            entry.InstanceKey = aura.Key;
            entry.CasterGuid = aura.CasterGuid.RawValue;
            entry.Stacks = aura.Stacks;
            entry.RemainingMs = UnitAuras.RemainingMs(aura, now);
            entry.DurationMs = aura.DurationMs;
            entry.Action = default;
            entry.Control = aura.WireControl;
        }

        return _entries;
    }

    /// <summary>This tick's changes, in order, each as it stood when it happened.</summary>
    public List<AuraEntryDto> Updates(IReadOnlyList<AuraChange> changes)
    {
        _entries.Clear();
        for (int i = 0; i < changes.Count; i++)
        {
            AuraChange change = changes[i];
            AuraEntryDto entry = Next();
            entry.AuraId = change.AuraId.Value;
            entry.InstanceKey = change.Key;
            entry.CasterGuid = change.CasterGuid;
            entry.Stacks = change.Stacks;
            entry.RemainingMs = change.RemainingMs;
            entry.DurationMs = change.DurationMs;
            entry.Action = (AuraUpdateAction)change.Kind;
            entry.Control = change.Control;
        }

        return _entries;
    }

    /// <summary>The next entry of this call, added to the list: one made before if there is one free, else a new one.</summary>
    private AuraEntryDto Next()
    {
        AuraEntryDto entry;
        if (_entries.Count < _made.Count)
        {
            entry = _made[_entries.Count];
        }
        else
        {
            entry = new AuraEntryDto();
            _made.Add(entry);
        }

        _entries.Add(entry);
        return entry;
    }
}
