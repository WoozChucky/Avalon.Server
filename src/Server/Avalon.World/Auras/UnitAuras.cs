using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public.Units;

namespace Avalon.World.Auras;

/// <summary>
/// The auras one unit holds, and the changes its watchers are owed this tick. Owned by the World-side CharacterEntity
/// and Creature, never by anything on the modding API. Only the aura system (and select, through Load) changes it.
/// While its unit is a member of an instance it is bound to that instance's <see cref="AuraRoster" />, which it tells
/// of every change it records (#898). Tick thread only.
/// </summary>
/// <param name="changed">Called on every change (not on Load): a character's marks its save.</param>
public sealed class UnitAuras(Action? changed = null)
{
    private readonly List<ActiveAura> _auras = [];
    private readonly List<AuraChange> _changes = [];
    private uint _nextKey = 1;
    private AuraStatTotals? _totals;
    private AuraRoster? _roster;
    private IUnit? _owner;

    public int Count => _auras.Count;

    /// <summary>The held arrival protection copy with the latest expiry, cached for combat and AI checks.</summary>
    internal ActiveAura? HeldArrivalProtection { get; private set; }

    /// <summary>
    /// The crowd control the held auras grant together (<see cref="AuraTemplate.Control" />), cached for the movement,
    /// cast and AI gates (<see cref="ControlGates" />), which read it with no scan.
    /// </summary>
    internal CrowdControl Control { get; private set; }

    /// <summary>The auras <c>Game:MaxAurasPerUnit</c> counts: all but the held arrival protection's slot.</summary>
    internal int OrdinaryCount => Count - (HeldArrivalProtection is null ? 0 : 1);

    public IReadOnlyList<ActiveAura> All => _auras;

    public bool HasChanges => _changes.Count > 0;

    /// <summary>This tick's changes, oldest first; cleared once they are sent.</summary>
    public IReadOnlyList<AuraChange> Changes => _changes;

    /// <summary>The roster of the instance its unit is a member of; null outside any instance.</summary>
    internal AuraRoster? Roster => _roster;

    /// <summary>The unit these auras belong to, while bound to a roster.</summary>
    internal IUnit? Owner => _owner;

    /// <summary>Whether the bound roster lists its unit as a holder; the roster's own to set.</summary>
    internal bool ListedAsHolder { get; set; }

    /// <summary>Whether the bound roster lists its unit as changed this tick; the roster's own to set.</summary>
    internal bool ListedAsChanged { get; set; }

    internal void Bind(AuraRoster roster, IUnit owner)
    {
        _roster = roster;
        _owner = owner;
    }

    internal void Unbind()
    {
        _roster = null;
        _owner = null;
    }

    /// <summary>What the held auras add to each stat; worked out again only after a change.</summary>
    public AuraStatTotals StatTotals =>
        _totals ??= _auras.Count == 0
            ? AuraStatTotals.Empty
            : AuraStatTotals.Of(_auras, static a => (a.Template.Modifiers, a.Stacks));

    /// <summary>The held copy of <paramref name="id" />: the one from <paramref name="caster" /> when given, else the first.</summary>
    public ActiveAura? Find(AuraId id, ObjectGuid? caster)
    {
        for (int i = 0; i < _auras.Count; i++)
        {
            ActiveAura aura = _auras[i];
            if (aura.Id.Value == id.Value && (caster is null || aura.CasterGuid == caster))
                return aura;
        }

        return null;
    }

    public bool Contains(ActiveAura aura) => _auras.Contains(aura);

    public void Add(ActiveAura aura, DateTimeOffset now)
    {
        aura.Key = _nextKey++;
        _auras.Add(aura);
        Record(aura, AuraChangeKind.Applied, now);
    }

    /// <summary>An aura held was refreshed or stacked.</summary>
    public void Changed(ActiveAura aura, AuraChangeKind kind, DateTimeOffset now) => Record(aura, kind, now);

    public bool Remove(ActiveAura aura, DateTimeOffset now)
    {
        if (!_auras.Remove(aura))
            return false;

        Record(aura, AuraChangeKind.Removed, now);
        return true;
    }

    /// <summary>
    /// The moment the auras' time stopped, while their character waits to enter the world or moves between instances
    /// (<see cref="Hold" />); null once it has entered (<see cref="ResumeHeld" />), or for auras that were never held. A
    /// save taken meanwhile reads the time left at this moment, so the wait costs nothing.
    /// </summary>
    public DateTimeOffset? HeldSince { get; private set; }

    /// <summary>
    /// The auras a character had when it last left the world, at select. Nothing is owed: the client gets a list. With
    /// <paramref name="heldSince" />, their schedules are anchored there and their time stands still until
    /// <see cref="ResumeHeld" />.
    /// </summary>
    public void Load(IEnumerable<ActiveAura> restored, DateTimeOffset? heldSince = null)
    {
        _auras.Clear();
        HeldArrivalProtection = null;
        _changes.Clear();
        foreach (ActiveAura aura in restored)
        {
            aura.Key = _nextKey++;
            _auras.Add(aura);
        }

        RecomputeControl();

        HeldSince = heldSince;
        _totals = null;
        _roster?.Noted(this);
    }

    /// <summary>
    /// The character has left one instance for another: its auras' time stands still from <paramref name="now" /> until
    /// it enters the next (<see cref="ResumeHeld" />), so no tick is lost or paid in a burst for the time between. A hold
    /// already in place (a character still waiting to enter) is kept.
    /// </summary>
    public void Hold(DateTimeOffset now) => HeldSince ??= now;

    /// <summary>
    /// The character has entered its instance: the held auras' time starts again from <paramref name="now" />, each
    /// schedule (and its next roll to break, #770) moved on by the time it waited, so it keeps all the time, ticks and
    /// rolls it had. Nothing when nothing is held.
    /// </summary>
    public void ResumeHeld(DateTimeOffset now)
    {
        if (HeldSince is not { } since)
            return;

        HeldSince = null;
        TimeSpan waited = now - since;
        if (waited <= TimeSpan.Zero)
            return;

        foreach (ActiveAura aura in _auras)
        {
            aura.Schedule = aura.Schedule with { ExpiresAt = aura.Schedule.ExpiresAt + waited };
            aura.NextBreakRollAt += waited;
        }
    }

    /// <summary>Forgets this tick's changes. Its roster is not told: it calls this itself, as it empties its list.</summary>
    public void ClearChanges() => _changes.Clear();

    private void Record(ActiveAura aura, AuraChangeKind kind, DateTimeOffset now)
    {
        if (ArrivalProtection.Is(aura.Id.Value))
        {
            // Normally one Refresh buff; catalog rows may also allow Independent copies. Recompute only on changes.
            HeldArrivalProtection = null;
            foreach (ActiveAura held in _auras)
            {
                if (ArrivalProtection.Is(held.Id.Value)
                    && (HeldArrivalProtection is null
                        || held.Schedule.ExpiresAt > HeldArrivalProtection.Schedule.ExpiresAt))
                {
                    HeldArrivalProtection = held;
                }
            }
        }

        // Also when the unit was already controlled: a refresh renews the held copy onto the current template first, so
        // a reload that took the Control away arrives here as a change to an aura that no longer grants any.
        if (aura.Template.Control != CrowdControl.None || Control != CrowdControl.None)
            RecomputeControl();

        uint remaining = kind == AuraChangeKind.Removed ? 0u : RemainingMs(aura, now);
        _changes.Add(new AuraChange(aura.Id, aura.Key, aura.CasterGuid.RawValue, aura.Stacks, remaining,
            aura.DurationMs, kind, aura.WireControl));
        _totals = null;
        _roster?.Noted(this);
        changed?.Invoke();
    }

    private void RecomputeControl()
    {
        CrowdControl control = CrowdControl.None;
        foreach (ActiveAura held in _auras)
            control |= held.Template.Control;

        Control = control;
    }

    /// <summary>Milliseconds left, rounded up, so an aura with any time left never reads 0.</summary>
    public static uint RemainingMs(ActiveAura aura, DateTimeOffset now)
    {
        double ms = Math.Ceiling(aura.Schedule.Remaining(now).TotalMilliseconds);
        return ms >= uint.MaxValue ? uint.MaxValue : (uint)ms;
    }
}
