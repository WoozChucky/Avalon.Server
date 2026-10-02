using Avalon.Common;
using Avalon.World.Entities;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Items;

/// <summary>
/// The item cast bars of one instance (item use), at most one per character. Ticked by MapInstance on
/// every tick, after the ability cast system. A cast ends early, out loud, when its character moves or dies (checked
/// each tick; damage alone never ends it), leaves the instance, uses another item or starts an ability cast (those call
/// <see cref="Interrupt" />), or, once its time has run, when the use can no longer complete. Every broadcast and
/// callback is contained, so one failing never leaves a cast stuck or stops the others. Tick thread only. Allocates
/// nothing on a tick with no cast.
/// </summary>
public sealed class ItemUseCasts(IItemCastAudience audience, Func<uint> takeCastId, ILogger logger)
{
    private readonly Dictionary<ObjectGuid, PendingItemUse> _casts = [];
    private readonly List<PendingItemUse> _done = [];
    private readonly List<PendingItemUse> _broken = [];

    /// <summary>The instance's next cast id, never 0.</summary>
    public uint TakeCastId() => takeCastId();

    public bool IsCasting(ObjectGuid character) => _casts.ContainsKey(character);

    /// <summary>Begins a cast and broadcasts its start. Throws when the character already has one: interrupt it first.</summary>
    public void Start(PendingItemUse use)
    {
        if (!_casts.TryAdd(use.Character.Guid, use))
            throw new InvalidOperationException($"{use.Character.Guid} is already using an item; interrupt that first.");

        Contained(() => audience.BroadcastItemCastStart(use.Character, use.Item, use.CastTimeSeconds, use.CastId), "start", use);
    }

    /// <summary>Ends the character's cast out loud and runs its Interrupted callback. False when it had none.</summary>
    public bool Interrupt(ObjectGuid character) => End(character, runCallback: true);

    /// <summary>Ends the character's cast out loud without its callback (the caller answers the use itself).</summary>
    public bool Cancel(ObjectGuid character) => End(character, runCallback: false);

    public void Update(TimeSpan deltaTime)
    {
        if (_casts.Count == 0)
            return;

        _done.Clear();
        _broken.Clear();
        foreach (PendingItemUse use in _casts.Values)
        {
            if (use.Character.IsDead || use.Character.Position != use.StartPosition)
            {
                _broken.Add(use);
                continue;
            }

            use.Elapsed += (float)deltaTime.TotalSeconds;
            if (use.Elapsed >= use.CastTimeSeconds)
                _done.Add(use);
        }

        foreach (PendingItemUse use in _broken)
            End(use.Character.Guid, runCallback: true);

        foreach (PendingItemUse use in _done)
        {
            // A callback earlier in this loop may already have ended it.
            if (!_casts.TryGetValue(use.Character.Guid, out PendingItemUse? still) || !ReferenceEquals(still, use))
                continue;

            bool canComplete;
            try
            {
                canComplete = use.CanComplete();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Asking whether the item cast of {Character} can complete threw", use.Character.Guid);
                canComplete = false;
            }

            if (!canComplete)
            {
                End(use.Character.Guid, runCallback: true);
                continue;
            }

            _casts.Remove(use.Character.Guid);
            Contained(() => audience.BroadcastItemCastFinish(use.Character, use.Item, use.CastId), "finish", use);
            Contained(use.Completed, "completion", use);
        }
    }

    private bool End(ObjectGuid character, bool runCallback)
    {
        if (!_casts.Remove(character, out PendingItemUse? use))
            return false;

        Contained(() => audience.BroadcastItemCastInterrupted(use.Character, use.Item, use.CastId), "interrupt", use);
        if (runCallback)
            Contained(use.Interrupted, "interruption", use);
        return true;
    }

    private void Contained(Action action, string step, PendingItemUse use)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            logger.LogError(e, "The {Step} of the item cast {CastId} of {Character} (item template {Item}) threw",
                step, use.CastId, use.Character.Guid, use.Item.Value);
        }
    }
}
