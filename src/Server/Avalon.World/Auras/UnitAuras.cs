using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.World.Auras;

/// <summary>
/// The auras one unit holds, and the changes its watchers are owed this tick. Owned by the World-side CharacterEntity
/// and Creature, never by anything on the modding API. Only the aura system (and select, through Load) changes it.
/// Tick thread only.
/// </summary>
/// <param name="changed">Called on every change (not on Load): a character's marks its save.</param>
public sealed class UnitAuras(Action? changed = null)
{
    private readonly List<ActiveAura> _auras = [];
    private readonly List<AuraChange> _changes = [];
    private uint _nextKey = 1;
    private AuraStatTotals? _totals;

    public int Count => _auras.Count;

    public IReadOnlyList<ActiveAura> All => _auras;

    public bool HasChanges => _changes.Count > 0;

    /// <summary>This tick's changes, oldest first; cleared once they are sent.</summary>
    public IReadOnlyList<AuraChange> Changes => _changes;

    /// <summary>What the held auras add to each stat; worked out again only after a change.</summary>
    public AuraStatTotals StatTotals =>
        _totals ??= _auras.Count == 0
            ? AuraStatTotals.Empty
            : AuraStatTotals.Of(_auras.Select(a => ((IReadOnlyList<AuraStatModifier>)a.Template.Modifiers, a.Stacks)));

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

    /// <summary>The auras a character had when it last left the world, at select. Nothing is owed: the client gets a list.</summary>
    public void Load(IEnumerable<ActiveAura> restored)
    {
        _auras.Clear();
        _changes.Clear();
        foreach (ActiveAura aura in restored)
        {
            aura.Key = _nextKey++;
            _auras.Add(aura);
        }

        _totals = null;
    }

    public void ClearChanges() => _changes.Clear();

    private void Record(ActiveAura aura, AuraChangeKind kind, DateTimeOffset now)
    {
        uint remaining = kind == AuraChangeKind.Removed ? 0u : RemainingMs(aura, now);
        _changes.Add(new AuraChange(aura.Id, aura.Key, aura.CasterGuid.RawValue, aura.Stacks, remaining, aura.DurationMs, kind));
        _totals = null;
        changed?.Invoke();
    }

    /// <summary>Milliseconds left, rounded up, so an aura with any time left never reads 0.</summary>
    public static uint RemainingMs(ActiveAura aura, DateTimeOffset now)
    {
        double ms = Math.Ceiling(aura.Schedule.Remaining(now).TotalMilliseconds);
        return ms >= uint.MaxValue ? uint.MaxValue : (uint)ms;
    }
}
