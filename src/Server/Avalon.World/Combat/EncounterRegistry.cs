using Avalon.World.Public.Combat;
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

    /// <summary>The first active encounter holding both units, on either side: an encounter the pair already shares (#1165).</summary>
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

    private static bool Holds(Encounter enc, IUnit unit) => enc.Hostiles.Contains(unit) || enc.Players.Contains(unit);

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
