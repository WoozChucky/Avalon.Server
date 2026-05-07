using System;
using System.Collections.Generic;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Units;

namespace Avalon.World.Combat;

public sealed class Encounter : IEncounter
{
    private readonly CombatConfig _config;
    private readonly HashSet<IUnit> _hostiles = new();
    private readonly HashSet<IUnit> _players  = new();
    private readonly Dictionary<IUnit, Dictionary<IUnit, float>> _threat = new();

    public Encounter(CombatConfig config)
    {
        _config        = config;
        Id             = Guid.NewGuid();
        SpawnedAt      = DateTime.UtcNow;
        LastDamageTime = DateTime.UtcNow;
    }

    public Guid     Id             { get; }
    public DateTime SpawnedAt      { get; }
    public DateTime LastDamageTime { get; private set; }

    public IReadOnlyCollection<IUnit> Hostiles => _hostiles;
    public IReadOnlyCollection<IUnit> Players  => _players;

    public bool ShouldEnd { get; private set; }

    public void AddHostile(IUnit hostile)
    {
        if (!_hostiles.Add(hostile))
            return;
        var list = new Dictionary<IUnit, float>();
        foreach (var p in _players)
            list[p] = _config.InitialThreatSeed;
        _threat[hostile] = list;
    }

    public void AddPlayer(IUnit player)
    {
        if (!_players.Add(player))
            return;
        foreach (var h in _hostiles)
            _threat[h][player] = _config.InitialThreatSeed;
    }

    public void RemovePlayer(IUnit player)
    {
        _players.Remove(player);
        foreach (var threatList in _threat.Values)
            threatList.Remove(player);
    }

    public IReadOnlyDictionary<IUnit, float> GetThreatList(IUnit hostile)
        => _threat.TryGetValue(hostile, out var list)
            ? list
            : new Dictionary<IUnit, float>();

    public IUnit? GetTopThreat(IUnit hostile)
    {
        if (!_threat.TryGetValue(hostile, out var list) || list.Count == 0)
            return null;
        IUnit? top = null;
        var    max = float.MinValue;
        foreach (var (u, t) in list)
        {
            if (t > max)
            {
                max = t;
                top = u;
            }
        }
        return top;
    }

    public void AddThreat(IUnit hostile, IUnit attacker, float amount)
    {
        if (!_threat.TryGetValue(hostile, out var list))
            return;
        list.TryGetValue(attacker, out var cur);
        list[attacker] = cur + amount;
        LastDamageTime = DateTime.UtcNow;
    }

    public void OnParticipantDied(IUnit unit)
    {
        if (_hostiles.Remove(unit))
            _threat.Remove(unit);
        // Player death keeps participant entry (downed); explicit removal via RemovePlayer when player exits instance.
    }

    public void Update(TimeSpan deltaTime)
    {
        // Decay + end-check filled in Phase F. Stub for now.
    }
}
