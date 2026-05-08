using System;
using System.Collections.Generic;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Combat;

public interface IEncounter
{
    Guid                              Id              { get; }
    DateTime                          SpawnedAt       { get; }
    DateTime                          LastDamageTime  { get; }
    IReadOnlyCollection<IUnit>        Hostiles        { get; }
    IReadOnlyCollection<IUnit>        Players         { get; }

    IReadOnlyDictionary<IUnit, float> GetThreatList(IUnit hostile);
    IUnit?                            GetTopThreat(IUnit hostile);
    void                              OnParticipantDied(IUnit unit);
    void                              Update(TimeSpan deltaTime);
    bool                              ShouldEnd { get; }
}
