using System;
using System.Collections.Generic;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Units;

namespace Avalon.World.Combat;

public sealed class Encounter : IEncounter
{
    private readonly CombatConfig _config;
    private readonly TimeProvider _time;
    private readonly HashSet<IUnit> _hostiles = new();
    private readonly HashSet<IUnit> _players  = new();
    private readonly Dictionary<IUnit, Dictionary<IUnit, float>> _threat = new();

    // Reused every tick by Update: the attackers whose threat decayed to nothing. The tick allocates nothing.
    private readonly List<IUnit> _decayed = new();

    /// <param name="time">The instance's clock, the one the rest of the world times by (#614).</param>
    public Encounter(CombatConfig config, TimeProvider? time = null)
    {
        _config        = config;
        _time          = time ?? TimeProvider.System;
        Id             = Guid.NewGuid();
        SpawnedAt      = Now;
        LastDamageTime = Now;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

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
        LastDamageTime = Now;
    }

    /// <summary>
    /// Takes <paramref name="hostile" /> out of the encounter with its threat list (#614). The players
    /// stay, with their threat on every other hostile; an encounter left with no hostile ends after its
    /// grace, as it does after a kill.
    /// </summary>
    public void RemoveHostile(IUnit hostile)
    {
        if (_hostiles.Remove(hostile))
            _threat.Remove(hostile);
    }

    public void OnParticipantDied(IUnit unit)
    {
        if (_hostiles.Remove(unit))
            _threat.Remove(unit);
        // Player death keeps participant entry (downed); explicit removal via RemovePlayer when player exits instance.
    }

    public void Update(TimeSpan deltaTime)
    {
        float dt = (float)deltaTime.TotalSeconds;
        foreach (var (hostile, threatList) in _threat)
        {
            _decayed.Clear();
            foreach (var (attacker, threat) in threatList)
            {
                float rate = _config.DefaultDecayRatePerSecond;
                if (Vector3.Distance(attacker.Position, hostile.Position) > _config.EngagementRadius)
                    rate *= _config.OutOfRangeDecayMultiplier;
                float next = threat - rate * dt;
                if (next <= 0) _decayed.Add(attacker);
                else           threatList[attacker] = next;
            }
            foreach (var u in _decayed) threatList.Remove(u);
        }
        _decayed.Clear();

        bool noHostiles = _hostiles.Count == 0;
        bool pastGrace  = (Now - LastDamageTime).TotalSeconds >= _config.EncounterEndGraceSeconds;
        ShouldEnd = noHostiles && pastGrace;
    }
}
