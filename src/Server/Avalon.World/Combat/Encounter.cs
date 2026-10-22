using Avalon.Common.Mathematics;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Units;

namespace Avalon.World.Combat;

public sealed class Encounter : IEncounter
{
    private readonly CombatConfig _config;
    private readonly TimeProvider _time;
    private readonly HashSet<IUnit> _hostiles = new();
    private readonly HashSet<IUnit> _players = new();
    private readonly Dictionary<IUnit, Dictionary<IUnit, float>> _threat = new();

    // Reused every tick by Update: the attackers whose threat decayed to nothing. The tick allocates nothing.
    private readonly List<IUnit> _decayed = new();

    // The encounter this one was merged into, if it was (#1168).
    private Encounter? _mergedInto;

    /// <param name="time">The instance's clock, the one the rest of the world times by (#614).</param>
    public Encounter(CombatConfig config, TimeProvider? time = null)
    {
        _config = config;
        _time = time ?? TimeProvider.System;
        Id = Guid.NewGuid();
        SpawnedAt = Now;
        LastDamageTime = Now;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public Guid Id { get; }
    public DateTime SpawnedAt { get; }
    public DateTime LastDamageTime { get; private set; }

    public IReadOnlyCollection<IUnit> Hostiles => _hostiles;
    public IReadOnlyCollection<IUnit> Players => _players;

    /// <summary>
    /// The hostiles, for a <c>foreach</c> over the set's own enumerator: one over <see cref="Hostiles" /> boxes it
    /// (#883). Adding a player or threat during the walk is safe, since neither changes who the hostiles are.
    /// </summary>
    internal HostileWalk WalkHostiles() => new(_hostiles);

    internal readonly struct HostileWalk(HashSet<IUnit> hostiles)
    {
        public HashSet<IUnit>.Enumerator GetEnumerator() => hostiles.GetEnumerator();
    }

    public bool ShouldEnd { get; private set; }

    public void AddHostile(IUnit hostile)
    {
        if (!_hostiles.Add(hostile))
            return;
        var list = new Dictionary<IUnit, float>();
        foreach (IUnit p in _players)
            list[p] = _config.InitialThreatSeed;
        _threat[hostile] = list;
    }

    public void AddPlayer(IUnit player)
    {
        if (!_players.Add(player))
            return;
        foreach (IUnit h in _hostiles)
            _threat[h][player] = _config.InitialThreatSeed;
    }

    /// <summary>
    /// Makes one fight of this and <paramref name="other" /> (#1168): every hostile of <paramref name="other" /> comes
    /// over with its threat list exactly as it stands, and every player with it. Nothing is seeded (owner decision): a
    /// player new to this encounter holds no threat on its hostiles, nor its players on a hostile brought in, until a
    /// hit, a heal or a taunt adds some. The fight's last damage is the later of the two. <paramref name="other" /> is
    /// left empty and points here (<see cref="Current" />). Moves the lists themselves, so it allocates nothing unless a
    /// set here has to grow.
    /// </summary>
    internal void Absorb(Encounter other)
    {
        foreach (IUnit player in other._players)
            _players.Add(player);

        foreach ((IUnit hostile, Dictionary<IUnit, float> list) in other._threat)
        {
            if (_threat.TryGetValue(hostile, out Dictionary<IUnit, float>? own))
            {
                // A hostile in both fights, which combat no longer forms: it keeps the higher threat of each attacker.
                foreach ((IUnit attacker, float threat) in list)
                {
                    if (!own.TryGetValue(attacker, out float current) || threat > current)
                        own[attacker] = threat;
                }
                continue;
            }

            _hostiles.Add(hostile);
            _threat[hostile] = list;
        }

        if (other.LastDamageTime > LastDamageTime)
            LastDamageTime = other.LastDamageTime;

        other._hostiles.Clear();
        other._players.Clear();
        other._threat.Clear();
        other._mergedInto = this;
    }

    /// <summary>
    /// The fight this encounter now is: itself, or the encounter it was merged into (#1168), followed to the end. For a
    /// caller holding an encounter across work that may have merged it away, such as a hit's death after the hit.
    /// </summary>
    internal Encounter Current
    {
        get
        {
            Encounter current = this;
            while (current._mergedInto is { } into)
                current = into;
            return current;
        }
    }

    public void RemovePlayer(IUnit player)
    {
        _players.Remove(player);
        foreach (Dictionary<IUnit, float> threatList in _threat.Values)
            threatList.Remove(player);
    }

    public IReadOnlyDictionary<IUnit, float> GetThreatList(IUnit hostile)
        => _threat.TryGetValue(hostile, out Dictionary<IUnit, float>? list)
            ? list
            : new Dictionary<IUnit, float>();

    public IUnit? GetTopThreat(IUnit hostile)
    {
        if (!_threat.TryGetValue(hostile, out Dictionary<IUnit, float>? list) || list.Count == 0)
            return null;
        IUnit? top = null;
        float max = float.MinValue;
        foreach ((IUnit? u, float t) in list)
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
        if (!_threat.TryGetValue(hostile, out Dictionary<IUnit, float>? list))
            return;
        list.TryGetValue(attacker, out float cur);
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
        foreach ((IUnit? hostile, Dictionary<IUnit, float>? threatList) in _threat)
        {
            _decayed.Clear();
            foreach ((IUnit? attacker, float threat) in threatList)
            {
                float rate = _config.DefaultDecayRatePerSecond;
                if (Vector3.Distance(attacker.Position, hostile.Position) > _config.EngagementRadius)
                    rate *= _config.OutOfRangeDecayMultiplier;
                float next = threat - rate * dt;
                if (next <= 0) _decayed.Add(attacker);
                else threatList[attacker] = next;
            }
            foreach (IUnit u in _decayed) threatList.Remove(u);
        }
        _decayed.Clear();

        bool noHostiles = _hostiles.Count == 0;
        bool pastGrace = (Now - LastDamageTime).TotalSeconds >= _config.EncounterEndGraceSeconds;
        ShouldEnd = noHostiles && pastGrace;
    }
}
