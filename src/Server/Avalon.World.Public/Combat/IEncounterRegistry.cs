using System.Collections.Generic;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Combat;

public interface IEncounterRegistry
{
    IReadOnlyCollection<IEncounter> Active { get; }

    IEncounter? FindEncounterContaining(IUnit unit);
    IEncounter  CreateEncounter();
    void        Dispose(IEncounter encounter);
}
