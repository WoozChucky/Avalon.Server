using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Avalon.World.Entities;
using Avalon.World.Loot;
using Avalon.World.Public;

namespace Avalon.World.Doodads;

/// <summary>
/// <see cref="IDoodadUseContext" /> (and so <see cref="IDoodadContext" />) over one hook of one doodad's script. One
/// instance is reused by its owner for every hook it runs: <see cref="Begin" /> points it at the doodad, the hook runs,
/// <see cref="End" /> closes it, so a hook allocates no context, and a script that kept one gets an
/// <see cref="InvalidOperationException" /> from every member. Its owner reads <see cref="Changed" /> and
/// <see cref="RespawnRequested" /> before <see cref="End" />, to tell the instance and schedule the respawn. One per
/// owner and thread: the use service's on the tick, and its own for each off-tick instance build (spawn mode, where the
/// live acts do nothing and answer 0).
/// </summary>
public sealed class DoodadUseContext(TimeProvider time) : IDoodadUseContext
{
    private Doodad? _doodad;
    private ILootRandom? _random;
    private IWorldConnection? _connection;
    private CharacterEntity? _user;
    private bool _spawning;

    /// <summary>Whether the hook changed the doodad's state (SetState or Despawn), so the instance is owed DoodadChanged.</summary>
    internal bool Changed { get; private set; }

    /// <summary>The delay the hook asked for through RespawnAfter, the last one when it asked twice; null when it asked none.</summary>
    internal TimeSpan? RespawnRequested { get; private set; }

    public DoodadTemplateId TemplateId => Doodad.Template.Id;
    public string Name => Doodad.Template.Name;
    public Vector3 Position => Doodad.Position;
    public DoodadState State => Doodad.State;
    public float AreaRadius => Doodad.Template.AreaRadius;
    public uint RestorePct => Doodad.Template.RestorePct;
    public IReadOnlyList<DoodadOutcomeView> AuraOutcomes => Doodad.Template.AuraOutcomes;
    public uint OutcomeWeightTotal => Doodad.Template.OutcomeWeightTotal;

    public string UserName => User.Name;
    public bool UserInParty => User.PartyId is not null;

    private Doodad Doodad => _doodad ?? throw Closed();

    private CharacterEntity User =>
        _user ?? (_doodad is null ? throw Closed() : throw new InvalidOperationException("This doodad hook has no user."));

    public void SetState(DoodadState state)
    {
        Doodad doodad = Doodad;
        if (state is not (DoodadState.Ready or DoodadState.Used))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state,
                "A script sets a doodad Ready or Used; Despawn() takes it out of every view.");
        }

        if (doodad.State == state)
            return;

        doodad.State = state;
        Changed = true;
    }

    public void Despawn()
    {
        Doodad doodad = Doodad;
        if (_spawning)
            return;

        if (doodad.Template.Solid)
            throw new InvalidOperationException($"Doodad {doodad.Guid} is solid, and a solid doodad is never despawned.");

        if (doodad.State == DoodadState.Despawned)
            return;

        doodad.State = DoodadState.Despawned;
        Changed = true;
    }

    public void RespawnAfter(TimeSpan delay)
    {
        EnsureOpen();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(delay, TimeSpan.Zero);
        if (!_spawning)
            RespawnRequested = delay;
    }

    public long NextRandom(long maxExclusive)
    {
        EnsureOpen();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return _random!.NextInt64(0, maxExclusive);
    }

    public void RefreshVisibility()
    {
        Doodad doodad = Doodad;
        if (!_spawning)
            doodad.RefreshVisibility();
    }

    public int DropLoot()
    {
        EnsureOpen();
        return _spawning ? 0 : throw NotYet(nameof(DropLoot));
    }

    public int ApplyAuraInRadius(AuraId aura, float radius)
    {
        EnsureOpen();
        return _spawning ? 0 : throw NotYet(nameof(ApplyAuraInRadius));
    }

    public int RestoreInRadius(uint healthPct, uint powerPct, float radius)
    {
        EnsureOpen();
        return _spawning ? 0 : throw NotYet(nameof(RestoreInRadius));
    }

    public void Tell(string line)
    {
        EnsureOpen();
        if (_spawning || _connection is null)
            return;

        _connection.Send(SChatMessagePacket.System(line, time.GetUtcNow().UtcDateTime, PacketEncoder.Shared));
    }

    /// <summary>
    /// Opens the context over <paramref name="doodad" />: for a use, with its <paramref name="user" /> and the user's
    /// <paramref name="connection" />; for another tick hook, with neither; for OnSpawn, off the tick, with
    /// <paramref name="spawning" />, where every live act does nothing.
    /// </summary>
    internal void Begin(Doodad doodad, ILootRandom random, IWorldConnection? connection = null,
        CharacterEntity? user = null, bool spawning = false)
    {
        _doodad = doodad;
        _random = random;
        _connection = connection;
        _user = user;
        _spawning = spawning;
        Changed = false;
        RespawnRequested = null;
    }

    /// <summary>Closes the context: every member throws until the next <see cref="Begin" />.</summary>
    internal void End()
    {
        _doodad = null;
        _random = null;
        _connection = null;
        _user = null;
        _spawning = false;
    }

    private void EnsureOpen()
    {
        if (_doodad is null)
            throw Closed();
    }

    private static InvalidOperationException Closed() =>
        new("A doodad context is valid only during the script hook it was handed to.");

    private static NotImplementedException NotYet(string act) =>
        new($"The doodad act {act} comes with the stock doodad scripts.");
}
