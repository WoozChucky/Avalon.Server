using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Loot;
using Avalon.World.Public;
using Avalon.World.Quests;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Doodads;

/// <summary>
/// One CMSG_INTERACT on a doodad, on the tick (InteractHandler dispatches by the target guid's type), and the end of the
/// cast bar it may start. The checks (<see cref="Check" />), in order: the user is dead; the doodad is not here or is
/// despawned; it is scenery; it is out of the template's range (a position that is not a number is out of it); the user
/// may not see it (its gate and its script's CanSee, asked afresh); its usability gate refuses the user; a once-per-
/// instance doodad is not ready; the user is casting an ability; its script's CanUse refuses. Every refusal is a silent
/// drop, a Debug line, and allocates nothing. A use that passed them ends the user's item cast and doodad cast, then
/// completes at once (no interaction time) or starts a cast bar on the container's clock (<see cref="DoodadCasts" />),
/// whose end runs the checks again, the ability cast aside, so the second of two casts on a spent doodad ends refused.
/// Completion runs the script's OnUse, then the framework's part: the quest credit, spending a once-per-instance doodad
/// (Used, or Despawned with OnDespawn, by its spent look) and its respawn. A doodad that goes Despawned on the tick runs
/// OnDespawn, whoever despawned it, the framework or a script. Every hook is contained: a throw is logged at Error, at most
/// once per script and hook per ThrottledErrorLog.Interval (<see cref="DoodadScripts.Failed" />); a CanUse that throws
/// refuses, and an OnUse that throws leaves the doodad ready and credits nothing, while what it did before the throw
/// stays. Tick thread only; a DI singleton, with the one hook context every hook it runs is handed.
/// </summary>
/// <param name="world">Its reference data's quest catalog, which the gates are asked against.</param>
/// <param name="time">The clock the script's lines are stamped with; casts and respawns run on the instance's.</param>
/// <param name="quests">Credits the use-doodad objective; none credits nothing.</param>
/// <param name="random">The randomness a script draws from; the shared generator when none is given.</param>
public sealed partial class DoodadUseService(
    DoodadScripts scripts,
    DoodadVisibility visibility,
    IWorld world,
    TimeProvider time,
    ILogger<DoodadUseService> logger,
    QuestService? quests = null,
    ILootRandom? random = null) : IDoodadCastOwner
{
    private const string CanUseHook = nameof(DoodadScript.CanUse);
    private const string OnUseHook = nameof(DoodadScript.OnUse);
    private const string OnDespawnHook = nameof(DoodadScript.OnDespawn);
    private const string OnRespawnHook = nameof(DoodadScript.OnRespawn);

    private readonly DoodadUseContext _context = new(time, logger);
    private readonly ILootRandom _random = random ?? new LootRandom(Random.Shared);

    // A quest credit that threw, logged at most once per ThrottledErrorLog.Interval; the line says the doodad was still
    // spent, since only the credit is lost. Tick thread only.
    private readonly ThrottledErrorLog _creditFailures =
        new(logger, time, "The quest credit for a doodad use (the doodad was still spent)");

    /// <summary>The user's CMSG_INTERACT on the doodad <paramref name="doodadGuid" /> of its instance <paramref name="host" />.</summary>
    public void Interact(IWorldConnection connection, CharacterEntity character, IDoodadHost? host, ObjectGuid doodadGuid)
    {
        DoodadUseRefusal? refusal;
        Doodad? doodad = null;
        if (host is null)
            refusal = DoodadUseRefusal.NoHost;
        else if (!host.Doodads.TryGet(doodadGuid, out doodad))
            refusal = character.IsDead ? DoodadUseRefusal.Dead : DoodadUseRefusal.NotFound;
        else
            refusal = Check(character, host, doodad);

        if (refusal is { } refused)
        {
            LogRefused(logger, refused, character.Name, doodadGuid);
            return;
        }

        // A use that passed its checks ends the user's item cast and doodad cast, each out loud.
        host!.ItemUses.Interrupt(character.Guid);
        host.DoodadCasts.Interrupt(character.Guid);

        uint castMs = doodad!.Template.InteractionTimeMs;
        if (castMs == 0)
        {
            Complete(host, connection, character, doodad);
            return;
        }

        DoodadCasts casts = host.DoodadCasts;
        DateTime endsAt = host.Clock.GetUtcNow().UtcDateTime + TimeSpan.FromMilliseconds(castMs);
        casts.Start(new PendingDoodadUse(character, doodad, character.Position, casts.TakeCastId(), endsAt,
            castMs / 1000f, connection));
    }

    /// <summary>The first check that refuses <paramref name="character" /> a use of <paramref name="doodad" />; null when none does.</summary>
    internal DoodadUseRefusal? Check(CharacterEntity character, IDoodadHost host, Doodad doodad) =>
        CheckUse(character, host, doodad, askBusy: true);

    bool IDoodadCastOwner.CanComplete(IDoodadHost host, in PendingDoodadUse use)
    {
        // The ability cast aside: starting one ended this cast already.
        if (CheckUse(use.Character, host, use.Doodad, askBusy: false) is not { } refused)
            return true;

        LogRefused(logger, refused, use.Character.Name, use.Doodad.Guid);
        return false;
    }

    void IDoodadCastOwner.Completed(IDoodadHost host, in PendingDoodadUse use) =>
        Complete(host, use.Connection, use.Character, use.Doodad);

    void IDoodadCastOwner.Interrupted(IDoodadHost host, in PendingDoodadUse use) =>
        LogInterrupted(logger, use.Character.Name, use.Doodad.Guid);

    /// <summary>
    /// A spent doodad's respawn time came (<see cref="DoodadRespawns" />): it is ready again, its script's OnRespawn runs,
    /// and every client that sees it is told. A doodad already ready is left alone: OnRespawn never runs on one unspent.
    /// </summary>
    internal void Respawned(IDoodadHost host, Doodad doodad)
    {
        // Ready already (a script set it so before its time): it was never spent, or no longer is, so nothing respawns.
        if (doodad.State == DoodadState.Ready)
            return;

        doodad.State = DoodadState.Ready;
        host.DoodadChanged(doodad);

        // Ready from here: a script that despawns it again in OnRespawn has its OnDespawn run.
        DoodadState before = DoodadState.Ready;
        TimeSpan? respawn = null;
        bool changed = false;
        if (scripts.For(doodad.Template) is { } script)
        {
            _context.Begin(doodad, host, _random);
            try
            {
                script.OnRespawn(_context);
            }
            catch (Exception e)
            {
                scripts.Failed(script.GetType(), OnRespawnHook, e);
            }
            finally
            {
                respawn = _context.RespawnRequested;
                changed = _context.Changed;
                _context.End();
            }
        }

        Settle(host, doodad, before, changed, respawn);
    }

    private DoodadUseRefusal? CheckUse(CharacterEntity character, IDoodadHost host, Doodad doodad, bool askBusy)
    {
        if (character.IsDead)
            return DoodadUseRefusal.Dead;

        if (doodad.State == DoodadState.Despawned
            || !host.Doodads.TryGet(doodad.Guid, out Doodad? here) || !ReferenceEquals(here, doodad))
        {
            return DoodadUseRefusal.NotFound;
        }

        DoodadTemplateView template = doodad.Template;
        if (template.Interaction == DoodadInteraction.None)
            return DoodadUseRefusal.NotInteractive;

        // Written so a distance that is not a number refuses.
        if (!(Vector3.Distance(character.Position, doodad.Position) <= template.InteractionRange))
            return DoodadUseRefusal.OutOfRange;

        // Asked afresh on every use, its script's CanSee included: a doodad hidden from the user is never usable by it.
        QuestCatalog catalog = world.Data?.Quests ?? QuestCatalog.Empty;
        if (!visibility.IsVisibleTo(doodad, character, catalog))
            return DoodadUseRefusal.NotVisible;

        // The usability gate, then the doodad's state: the rule the client's CanInteract is told by.
        if (DoodadVisibility.UseRefusal(doodad, character, catalog) is { } unusable)
            return unusable;

        if (askBusy && character.Spells.IsCasting)
            return DoodadUseRefusal.Busy;

        return scripts.For(template) is { } script ? AskCanUse(script, character, doodad) : null;
    }

    private DoodadUseRefusal? AskCanUse(DoodadScript script, CharacterEntity character, Doodad doodad)
    {
        // No instance: CanUse is read only, so an act on the players around the doodad (loot, auras, restore) throws
        // there, and the throw refuses.
        _context.Begin(doodad, host: null, _random, user: character);
        try
        {
            if (script.CanUse(_context) is not { } line)
                return null;

            LogScriptRefused(logger, character.Name, doodad.Guid, line);
            return DoodadUseRefusal.ScriptRefused;
        }
        catch (Exception e)
        {
            scripts.Failed(script.GetType(), CanUseHook, e);
            return DoodadUseRefusal.ScriptRefused;
        }
        finally
        {
            _context.End();
        }
    }

    /// <summary>
    /// The use takes effect, at once or as its cast completes: the script's OnUse, then, unless it threw, the quest credit,
    /// the spending and the template's respawn.
    /// </summary>
    private void Complete(IDoodadHost host, IWorldConnection? connection, CharacterEntity character, Doodad doodad)
    {
        DoodadTemplateView template = doodad.Template;
        DoodadState before = doodad.State;
        TimeSpan? respawn = null;
        bool changed = false;
        bool ran = true;
        if (scripts.For(template) is { } script)
        {
            _context.Begin(doodad, host, _random, connection, character);
            try
            {
                script.OnUse(_context);
            }
            catch (Exception e)
            {
                ran = false;
                scripts.Failed(script.GetType(), OnUseHook, e);
            }
            finally
            {
                // What the script did before a throw stays done, its respawn request included.
                respawn = _context.RespawnRequested;
                changed = _context.Changed;
                _context.End();
            }
        }

        if (ran)
        {
            CreditQuests(character, template);

            // A script that spent the doodad its own way is left as it set it.
            if (template.UseLimit == DoodadUseLimit.OncePerInstance && doodad.State == DoodadState.Ready)
            {
                doodad.State = template.SpentLook == DoodadSpentLook.Despawn ? DoodadState.Despawned : DoodadState.Used;
            }

            if (respawn is null && doodad.State != DoodadState.Ready && template.RespawnSeconds is { } seconds)
                respawn = TimeSpan.FromSeconds(seconds);
        }

        Settle(host, doodad, before, changed, respawn);
    }

    /// <summary>
    /// The use-doodad objectives' credit, the user's alone (each player on the step uses the doodad for their own credit;
    /// a use credits only its user): QuestService gives +1 to each one for this template in the current stage of the
    /// user's Active quests. Contained, as a kill's credit is: a throw costs this use its credit, never the spending after
    /// it, so a once-per-instance doodad is still spent.
    /// </summary>
    private void CreditQuests(CharacterEntity character, DoodadTemplateView template)
    {
        if (quests is null)
            return;

        try
        {
            quests.DoodadUsed(character, template.Id);
        }
        catch (Exception e)
        {
            _creditFailures.Failed(e);
        }
    }

    /// <summary>
    /// What follows a hook or a spend: OnDespawn for a doodad that has just left every view, the respawn asked for, and
    /// the instance told of a changed state.
    /// </summary>
    private void Settle(IDoodadHost host, Doodad doodad, DoodadState before, bool changed, TimeSpan? respawn)
    {
        if (before != DoodadState.Despawned && doodad.State == DoodadState.Despawned
            && scripts.For(doodad.Template) is { } script)
        {
            _context.Begin(doodad, host, _random);
            try
            {
                script.OnDespawn(_context);
            }
            catch (Exception e)
            {
                scripts.Failed(script.GetType(), OnDespawnHook, e);
            }
            finally
            {
                respawn = _context.RespawnRequested ?? respawn;
                changed |= _context.Changed;
                _context.End();
            }
        }

        // Only a doodad that is not ready respawns: a request from an unlimited doodad's script, or from an OnUse that
        // threw after it, would otherwise run OnRespawn on a doodad that was never spent.
        if (respawn is { } delay && doodad.State != DoodadState.Ready)
            host.Respawns.Schedule(doodad, host.Clock.GetUtcNow().UtcDateTime + delay);

        if (changed || doodad.State != before)
            host.DoodadChanged(doodad);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Doodad use refused ({Refusal}): {Character} on doodad {Doodad}")]
    private static partial void LogRefused(ILogger logger, DoodadUseRefusal refusal, string character, ObjectGuid doodad);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Doodad use refused by its script: {Character} on doodad {Doodad}: {Line}")]
    private static partial void LogScriptRefused(ILogger logger, string character, ObjectGuid doodad, string line);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Doodad cast interrupted: {Character} on doodad {Doodad}")]
    private static partial void LogInterrupted(ILogger logger, string character, ObjectGuid doodad);
}
