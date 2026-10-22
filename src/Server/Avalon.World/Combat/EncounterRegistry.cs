using System.Diagnostics;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;

namespace Avalon.World.Combat;

public sealed class EncounterRegistry : IEncounterRegistry
{
    private readonly CombatConfig _config;
    private readonly TimeProvider? _time;
    private readonly List<Encounter> _active = new();

    /// <param name="time">The instance's clock, handed to every encounter it creates (#614).</param>
    public EncounterRegistry(CombatConfig config, TimeProvider? time = null)
    {
        _config = config;
        _time = time;
    }

    public IReadOnlyCollection<IEncounter> Active => _active;

    /// <summary>Replaces <paramref name="into" />'s contents with the active encounters, allocating nothing once it has grown.</summary>
    internal void CopyActiveTo(List<Encounter> into)
    {
        into.Clear();
        into.AddRange(_active);
    }

    public IEncounter? FindEncounterContaining(IUnit unit)
    {
        foreach (Encounter enc in _active)
        {
            if (enc.Hostiles.Contains(unit) || enc.Players.Contains(unit))
                return enc;
        }
        return null;
    }

    /// <summary>The first active encounter holding both units, each on its own side: an encounter the pair already shares (#1165).</summary>
    internal Encounter? FindEncounterContainingBoth(IUnit first, IUnit second)
    {
        foreach (Encounter enc in _active)
        {
            if (Holds(enc, first) && Holds(enc, second))
                return enc;
        }
        return null;
    }

    /// <summary>The first active encounter holding <paramref name="unit" /> with fewer than <paramref name="hostileCap" /> hostiles (#1165).</summary>
    internal Encounter? FindEncounterWithRoom(IUnit unit, int hostileCap)
    {
        foreach (Encounter enc in _active)
        {
            if (enc.Hostiles.Count < hostileCap && Holds(enc, unit))
                return enc;
        }
        return null;
    }

    /// <summary>
    /// The first active encounter holding <paramref name="unit" /> on its own side (#1168): for a creature, the fight it is
    /// a hostile in.
    /// </summary>
    internal Encounter? FindEncounterHolding(IUnit unit)
    {
        foreach (Encounter enc in _active)
        {
            if (Holds(enc, unit))
                return enc;
        }
        return null;
    }

    /// <summary>
    /// Makes one fight of two active encounters (#1168): the one created first takes in the other
    /// (<see cref="Encounter.Absorb" />), which leaves the active encounters. The older one survives, so the fight keeps
    /// when it began and its place ahead of the encounters opened after it. Answers the survivor.
    /// </summary>
    internal Encounter Merge(Encounter first, Encounter second)
    {
        int firstAt = _active.IndexOf(first);
        int secondAt = _active.IndexOf(second);
        Debug.Assert(firstAt >= 0 && secondAt >= 0 && firstAt != secondAt, "Only two different active encounters merge");

        (Encounter survivor, int absorbedAt) = firstAt < secondAt ? (first, secondAt) : (second, firstAt);
        survivor.Absorb(_active[absorbedAt]);
        _active.RemoveAt(absorbedAt);
        return survivor;
    }

    /// <summary>
    /// Takes <paramref name="player" /> out of every active encounter it is in, and off every threat list there (#1168). A
    /// player can be in several: one it joined past the merge cap, and its own.
    /// </summary>
    internal void RemovePlayerEverywhere(IUnit player)
    {
        foreach (Encounter enc in _active)
        {
            if (enc.Players.Contains(player))
                enc.RemovePlayer(player);
        }
    }

    /// <summary>
    /// Whether <paramref name="enc" /> holds <paramref name="unit" /> on the side combat puts it on: a creature as a hostile,
    /// anything else as a player, as <c>CombatService</c> adds it. A creature only on the players' side (a healer) has no
    /// threat list there, so it does not count as held.
    /// </summary>
    internal static bool Holds(Encounter enc, IUnit unit) =>
        unit is ICreature ? enc.Hostiles.Contains(unit) : enc.Players.Contains(unit);

    public IEncounter CreateEncounter()
    {
        var enc = new Encounter(_config, _time);
        _active.Add(enc);
        return enc;
    }

    public void Dispose(IEncounter encounter)
    {
        if (encounter is Encounter e)
            _active.Remove(e);
    }
}
