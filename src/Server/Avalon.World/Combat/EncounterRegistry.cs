using System.Collections.Generic;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Units;

namespace Avalon.World.Combat;

public sealed class EncounterRegistry : IEncounterRegistry
{
    private readonly CombatConfig _config;
    private readonly List<Encounter> _active = new();

    public EncounterRegistry(CombatConfig config) => _config = config;

    public IReadOnlyCollection<IEncounter> Active => _active;

    public IEncounter? FindEncounterContaining(IUnit unit)
    {
        foreach (var enc in _active)
        {
            if (enc.Hostiles.Contains(unit) || enc.Players.Contains(unit))
                return enc;
        }
        return null;
    }

    public IEncounter CreateEncounter()
    {
        var enc = new Encounter(_config);
        _active.Add(enc);
        return enc;
    }

    public void Dispose(IEncounter encounter)
    {
        if (encounter is Encounter e)
            _active.Remove(e);
    }
}
