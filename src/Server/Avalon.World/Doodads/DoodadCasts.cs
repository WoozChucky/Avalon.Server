using Avalon.Common;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Doodads;

/// <summary>What a doodad cast answers to as it ends (DoodadUseService): whether it may complete, and its end.</summary>
public interface IDoodadCastOwner
{
    /// <summary>Asked once its time has run: false ends it interrupted instead (someone spent the doodad meanwhile).</summary>
    bool CanComplete(IDoodadHost host, in PendingDoodadUse use);

    /// <summary>The cast completed, its finish already broadcast: the use takes effect.</summary>
    void Completed(IDoodadHost host, in PendingDoodadUse use);

    /// <summary>The cast ended early, its interrupt already broadcast: nothing was spent.</summary>
    void Interrupted(IDoodadHost host, in PendingDoodadUse use);
}

/// <summary>
/// The doodad cast bars of one instance, at most one per character, on the item cast's rules: ticked by MapInstance right
/// after the item cast bars. A cast ends early, out loud, when its character moves or dies (checked each tick; damage
/// alone never ends it), leaves the instance, starts an ability cast, uses an item that passed its checks or uses another
/// doodad (those call <see cref="Interrupt" />), or, once its time has run on the container's clock, when its owner
/// refuses its completion. Only the active casts are visited, and a tick with none reads nothing; every broadcast and
/// owner call is contained, so one failing never leaves a cast stuck or stops the others. Tick thread only.
/// </summary>
public sealed class DoodadCasts(
    IDoodadHost host,
    IDoodadCastAudience audience,
    IDoodadCastOwner owner,
    Func<uint> takeCastId,
    TimeProvider time,
    ILogger logger)
{
    private readonly Dictionary<ObjectGuid, PendingDoodadUse> _casts = [];

    // This tick's casts that broke (moved, died) and that are due, kept between ticks so a tick allocates none.
    private readonly List<PendingDoodadUse> _broken = [];
    private readonly List<PendingDoodadUse> _due = [];

    /// <summary>The instance's next cast id, never 0.</summary>
    public uint TakeCastId() => takeCastId();

    public bool IsCasting(ObjectGuid character) => _casts.ContainsKey(character);

    /// <summary>Begins a cast and broadcasts its start. Throws when the character already has one: interrupt it first.</summary>
    public void Start(in PendingDoodadUse use)
    {
        if (!_casts.TryAdd(use.Character.Guid, use))
            throw new InvalidOperationException($"{use.Character.Guid} is already using a doodad; interrupt that first.");

        try
        {
            audience.BroadcastDoodadCastStart(use.Character, use.Doodad.Template.Id, use.CastTimeSeconds, use.CastId);
        }
        catch (Exception e)
        {
            Failed(e, "start", use);
        }
    }

    /// <summary>Ends the character's cast out loud and tells its owner. False when it had none.</summary>
    public bool Interrupt(ObjectGuid character)
    {
        if (!_casts.Remove(character, out PendingDoodadUse use))
            return false;

        EndInterrupted(use);
        return true;
    }

    public void Update()
    {
        if (_casts.Count == 0)
            return;

        DateTime now = time.GetUtcNow().UtcDateTime;
        _broken.Clear();
        _due.Clear();
        foreach (PendingDoodadUse use in _casts.Values)
        {
            if (use.Character.IsDead || use.Character.Position != use.StartPosition)
                _broken.Add(use);
            else if (now >= use.EndsAt)
                _due.Add(use);
        }

        foreach (PendingDoodadUse use in _broken)
        {
            if (TakeIfRunning(use))
                EndInterrupted(use);
        }

        foreach (PendingDoodadUse use in _due)
        {
            if (!TakeIfRunning(use))
                continue;

            bool canComplete;
            try
            {
                canComplete = owner.CanComplete(host, use);
            }
            catch (Exception e)
            {
                Failed(e, "completion check", use);
                canComplete = false;
            }

            if (!canComplete)
            {
                EndInterrupted(use);
                continue;
            }

            try
            {
                audience.BroadcastDoodadCastFinish(use.Character, use.Doodad.Template.Id, use.CastId);
            }
            catch (Exception e)
            {
                Failed(e, "finish", use);
            }

            try
            {
                owner.Completed(host, use);
            }
            catch (Exception e)
            {
                Failed(e, "completion", use);
            }
        }

        _broken.Clear();
        _due.Clear();
    }

    // Removes the cast when it is still the one running: an owner call earlier in this tick may have ended it, or
    // started another for the same character.
    private bool TakeIfRunning(in PendingDoodadUse use)
    {
        if (!_casts.TryGetValue(use.Character.Guid, out PendingDoodadUse running) || running.CastId != use.CastId)
            return false;

        _casts.Remove(use.Character.Guid);
        return true;
    }

    private void EndInterrupted(in PendingDoodadUse use)
    {
        try
        {
            audience.BroadcastDoodadCastInterrupted(use.Character, use.Doodad.Template.Id, use.CastId);
        }
        catch (Exception e)
        {
            Failed(e, "interrupt", use);
        }

        try
        {
            owner.Interrupted(host, use);
        }
        catch (Exception e)
        {
            Failed(e, "interruption", use);
        }
    }

    private void Failed(Exception e, string step, in PendingDoodadUse use) =>
        logger.LogError(e, "The {Step} of the doodad cast {CastId} of {Character} on doodad {Doodad} (template {Template}) threw",
            step, use.CastId, use.Character.Guid, use.Doodad.Guid, use.Doodad.Template.Id.Value);
}
