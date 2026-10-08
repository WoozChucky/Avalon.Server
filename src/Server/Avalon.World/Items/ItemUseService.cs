using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Items;

/// <summary>
/// One CMSG_ITEM_USE, on the tick, answered with exactly one SMSG_ITEM_USE_RESULT: at once, or when its cast ends.
/// In order: dead (before anything else, so no script hook, teleport or quest start ever runs for a dead user); the
/// slot (Bag or Equipment); a worn item is taken off to the lowest free Bag slot (ItemEquip.Unequip: TargetFull when
/// there is none; asked before the template, so an item whose template is gone comes off too) and nothing else is asked;
/// the template; gear is equipped (ItemEquip, then the stats refresh a drag-to-equip runs; an equip or unequip that
/// passed its checks ends the item cast running, answered Interrupted, just before it moves anything) and nothing
/// else is asked; otherwise a script is needed, then an ability cast refuses (AlreadyCasting), then the item's and its
/// group's cooldown, then the script found and built and the user's instance (InternalError), then the script's
/// CanUse. A use that passed all of them ends the item cast running, which is answered Interrupted, then runs OnUse at
/// once or starts a cast bar (UseCastTimeMs) whose completion needs the same item instance still in the same Bag slot,
/// asks CanUse again and runs OnUse. After OnUse returns: the cooldown starts and what it consumed is taken through the
/// inventory service, so it is saved and sent like any change. Every script hook is contained: a throw is logged at
/// Error and answered InternalError, with nothing consumed and no cooldown. These per-request Error logs (a missing or
/// unbuildable script type, a hook that throws) are written at most once per script and step per
/// <see cref="ThrottledErrorLog.Interval" />, counting the ones left out. Scripts are built once per type through
/// QuestScriptServices (loggers and the clock only). Tick thread only; a DI singleton.
/// </summary>
public sealed class ItemUseService(
    ItemUseTools tools,
    IScriptManager scripts,
    IServiceProvider services,
    ILogger<ItemUseService> logger)
{
    private readonly QuestScriptServices _scriptServices = new(services);
    private readonly Dictionary<Type, ItemScript?> _built = [];
    private readonly Dictionary<string, (DateTimeOffset LastLogged, int Suppressed)> _errorLogs = new(StringComparer.Ordinal);

    public void Use(IWorldConnection connection, CharacterEntity character, uint requestId, uint container, uint slot)
    {
        ItemUseAnswer? answer;
        try
        {
            answer = Begin(connection, character, requestId, container, slot);
        }
        catch (Exception e)
        {
            logger.LogError(e, "CMSG_ITEM_USE {RequestId} of {Character} threw; answering InternalError",
                requestId, character.Name);
            answer = ItemUseAnswer.Of(ItemUseResult.InternalError);
        }

        if (answer is { } now)
            ItemUseReply.Send(connection, requestId, now);
    }

    /// <summary>The answer to send now, or null when a cast bar started and its end answers.</summary>
    private ItemUseAnswer? Begin(IWorldConnection connection, CharacterEntity character, uint requestId, uint container,
        uint slot)
    {
        if (character.IsDead)
            return ItemUseAnswer.Of(ItemUseResult.Dead);

        if (!SlotRef.TryParse(container, slot, out SlotRef at) || !InventoryMove.IsUsable(character, at))
            return ItemUseAnswer.Of(ItemUseResult.NotFound);

        // A worn item is taken off before any template is looked up, so one whose template a reload removed still can be.
        if (at.Container == InventoryType.Equipment)
        {
            return character.Container(InventoryType.Equipment).TryGet(at.Slot, out _)
                ? Unequip(character, at.Slot)
                : ItemUseAnswer.Of(ItemUseResult.NotFound);
        }

        if (at.Container != InventoryType.Bag
            || !character.Container(InventoryType.Bag).TryGet(at.Slot, out InventoryItem item))
        {
            return ItemUseAnswer.Of(ItemUseResult.NotFound);
        }

        if (FindTemplate(item.TemplateId) is not { } template)
            return ItemUseAnswer.Of(ItemUseResult.NotUsable);

        if (ItemEquip.IsWearable(template))
            return Equip(character, at.Slot, template);

        return BeginScript(connection, character, requestId, item, template);
    }

    /// <summary>
    /// An equip that passed its checks ends the item cast running (answered Interrupted) just before it moves anything;
    /// a refused equip leaves the cast alone.
    /// </summary>
    private ItemUseAnswer Equip(CharacterEntity character, ushort bagSlot, ItemTemplate template)
    {
        ItemUseResult equipped = ItemEquip.Equip(character, tools.Economy.InventoryOf(character), FindTemplate, bagSlot,
            template, logger, () => HostOf(character)?.ItemUses.Interrupt(character.Guid));
        if (equipped == ItemUseResult.Ok)
            CharacterStatsRefresh.AfterGearChange(character, tools.World.Data, logger);
        return ItemUseAnswer.Of(equipped);
    }

    /// <summary>
    /// Takes a worn item off to the lowest free Bag slot. As an equip does, one that passed its checks ends the item cast
    /// running (answered Interrupted) just before it moves anything, and a refused one (TargetFull) leaves the cast alone.
    /// </summary>
    private ItemUseAnswer Unequip(CharacterEntity character, ushort equipmentSlot)
    {
        ItemUseResult unequipped = ItemEquip.Unequip(character, tools.Economy.InventoryOf(character), FindTemplate,
            equipmentSlot, () => HostOf(character)?.ItemUses.Interrupt(character.Guid));
        if (unequipped == ItemUseResult.Ok)
            CharacterStatsRefresh.AfterGearChange(character, tools.World.Data, logger);
        return ItemUseAnswer.Of(unequipped);
    }

    private ItemUseAnswer? BeginScript(IWorldConnection connection, CharacterEntity character, uint requestId,
        InventoryItem item, ItemTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.UseScript))
            return ItemUseAnswer.Of(ItemUseResult.NotUsable);

        if (character.Spells.IsCasting)
            return ItemUseAnswer.Of(ItemUseResult.AlreadyCasting);

        TimeSpan left = character.ItemCooldowns.Remaining(template.Id, template.UseCooldownGroup, tools.Time.GetUtcNow());
        if (left > TimeSpan.Zero)
            return ItemUseAnswer.Cooldown(left);

        if (ScriptFor(template) is not { } script)
            return ItemUseAnswer.Of(ItemUseResult.InternalError);

        if (HostOf(character) is not { } host)
        {
            logger.LogWarning("Item use of {Character}: its instance {InstanceId} was not found", character.Name,
                character.InstanceId);
            return ItemUseAnswer.Of(ItemUseResult.InternalError);
        }

        var use = new ItemUse(connection, character, requestId, item, template, script, host);
        if (!TryCanUse(use, out string? refusal))
            return ItemUseAnswer.Of(ItemUseResult.InternalError);
        if (refusal is not null)
            return ItemUseAnswer.Refusal(refusal);

        // A use that passed its checks ends the item cast running, which is answered Interrupted.
        host.ItemUses.Interrupt(character.Guid);

        if (template.UseCastTimeMs is { } castMs && castMs > 0)
        {
            StartCast(use, castMs);
            return null;
        }

        return Complete(use, recheck: false);
    }

    private void StartCast(ItemUse use, uint castMs)
    {
        ItemUseCasts casts = use.Host.ItemUses;
        casts.Start(new PendingItemUse
        {
            Character = use.Character,
            Item = use.Template.Id,
            StartPosition = use.Character.Position,
            CastId = casts.TakeCastId(),
            CastTimeSeconds = castMs / 1000f,
            CanComplete = () => StillHeld(use),
            Completed = () => ItemUseReply.Send(use.Connection, use.RequestId, Finish(use)),
            Interrupted = () =>
            {
                // A hook that throws answers InternalError, as every hook does; nothing was spent either way.
                bool ran = RunHook(use, static (s, c) => s.OnInterrupted(c), nameof(ItemScript.OnInterrupted));
                ItemUseReply.Send(use.Connection, use.RequestId,
                    ItemUseAnswer.Of(ran ? ItemUseResult.Interrupted : ItemUseResult.InternalError));
            },
        });

        // Answered here only when this cancel ended it: a hook that ended the cast itself (by moving the user, say) has
        // already been answered through the cast's Interrupted callback.
        if (!RunHook(use, static (s, c) => s.OnCastStart(c), nameof(ItemScript.OnCastStart))
            && casts.Cancel(use.Character.Guid))
        {
            ItemUseReply.Send(use.Connection, use.RequestId, ItemUseAnswer.Of(ItemUseResult.InternalError));
        }
    }

    /// <summary>The used item is still the same instance in the same Bag slot.</summary>
    private static bool StillHeld(ItemUse use) =>
        use.Character.Container(InventoryType.Bag).TryGet(use.Item.Slot, out InventoryItem now)
        && now.InstanceId.Equals(use.Item.InstanceId);

    private ItemUseAnswer Finish(ItemUse use)
    {
        try
        {
            if (use.Character.IsDead
                || !use.Character.Container(InventoryType.Bag).TryGet(use.Item.Slot, out InventoryItem now)
                || !now.InstanceId.Equals(use.Item.InstanceId))
            {
                return ItemUseAnswer.Of(ItemUseResult.Interrupted);
            }

            return Complete(use with { Item = now }, recheck: true);
        }
        catch (Exception e)
        {
            if (!Throttled("complete", out int suppressed))
            {
                logger.LogError(e, "Completing the item use {RequestId} of {Character} threw. {Suppressed} earlier throws were not logged",
                    use.RequestId, use.Character.Name, suppressed);
            }

            return ItemUseAnswer.Of(ItemUseResult.InternalError);
        }
    }

    private ItemUseAnswer Complete(ItemUse use, bool recheck)
    {
        if (recheck)
        {
            if (!TryCanUse(use, out string? refusal))
                return ItemUseAnswer.Of(ItemUseResult.InternalError);
            if (refusal is not null)
                return ItemUseAnswer.Refusal(refusal);
        }

        ItemUseContext context = ContextOf(use);
        if (!RunHook(use, context, static (s, c) => s.OnUse(c), nameof(ItemScript.OnUse)))
            return ItemUseAnswer.Of(ItemUseResult.InternalError);

        // Only after a use that ran: a refusal or a throw starts no cooldown.
        use.Character.ItemCooldowns.Start(use.Template.Id, use.Template.UseCooldownGroup,
            TimeSpan.FromMilliseconds(use.Template.UseCooldownMs ?? 0u), tools.Time.GetUtcNow());

        if (context.Consumed > 0)
        {
            InventoryRemoveResult removed = tools.Economy.InventoryOf(use.Character).TryRemove(use.Item.InstanceId, context.Consumed);
            if (removed != InventoryRemoveResult.Ok)
            {
                logger.LogError("Consuming {Count} of item {Item} after its use by {Character} failed: {Result}",
                    context.Consumed, use.Item.InstanceId, use.Character.Name, removed);
            }
        }

        return ItemUseAnswer.Of(ItemUseResult.Ok);
    }

    private bool TryCanUse(ItemUse use, out string? refusal)
    {
        try
        {
            refusal = use.Script.CanUse(ContextOf(use));
            return true;
        }
        catch (Exception e)
        {
            LogHookThrow(e, use, nameof(ItemScript.CanUse));
            refusal = null;
            return false;
        }
    }

    private bool RunHook(ItemUse use, Action<ItemScript, IItemUseContext> hook, string name) =>
        RunHook(use, ContextOf(use), hook, name);

    private bool RunHook(ItemUse use, ItemUseContext context, Action<ItemScript, IItemUseContext> hook, string name)
    {
        try
        {
            hook(use.Script, context);
            return true;
        }
        catch (Exception e)
        {
            LogHookThrow(e, use, name);
            return false;
        }
    }

    private void LogHookThrow(Exception e, ItemUse use, string hook)
    {
        string script = use.Script.GetType().Name;
        if (!Throttled($"{script}.{hook}", out int suppressed))
        {
            logger.LogError(e, "Item script {Script} {Hook} threw for {Character} using item template {Item}. " +
                               "{Suppressed} earlier throws of it were not logged",
                script, hook, use.Character.Name, use.Template.Id.Value, suppressed);
        }
    }

    /// <summary>
    /// True when this key logged within <see cref="ThrottledErrorLog.Interval" /> (and it is counted as left out);
    /// otherwise false, with the count left out since the last log, and the clock restarted.
    /// </summary>
    private bool Throttled(string key, out int suppressed)
    {
        DateTimeOffset now = tools.Time.GetUtcNow();
        if (_errorLogs.TryGetValue(key, out (DateTimeOffset LastLogged, int Suppressed) last)
            && now - last.LastLogged < ThrottledErrorLog.Interval)
        {
            _errorLogs[key] = (last.LastLogged, last.Suppressed + 1);
            suppressed = 0;
            return true;
        }

        suppressed = last.Suppressed;
        _errorLogs[key] = (now, 0);
        return false;
    }

    private ItemUseContext ContextOf(ItemUse use) => new(use.Connection, use.Character, use.Host, use.Item, use.Template,
        tools);

    private IItemUseHost? HostOf(CharacterEntity character) =>
        tools.World.InstanceRegistry.GetInstanceById(character.InstanceId) as IItemUseHost;

    // StaticData keeps the item templates as a list and offers no keyed lookup, as the inventory service's search does.
    private ItemTemplate? FindTemplate(ItemTemplateId id) =>
        tools.World.Data.ItemTemplates.FirstOrDefault(t => t.Id == id);

    /// <summary>Built once per type; a type that cannot be built answers InternalError for its items from then on.</summary>
    private ItemScript? ScriptFor(ItemTemplate template)
    {
        Type? type = scripts.GetItemScript(template.UseScript!);
        if (type is null)
        {
            if (!Throttled($"missing:{template.UseScript}", out int suppressed))
            {
                logger.LogError("Item template {Item} names item script {Script}, and no ItemScript is called that. " +
                                "{Suppressed} earlier uses naming it were not logged",
                    template.Id.Value, template.UseScript, suppressed);
            }

            return null;
        }

        // A type that could not be built was logged when it failed and is not built again.
        if (_built.TryGetValue(type, out ItemScript? built))
            return built;

        try
        {
            built = (ItemScript)ActivatorUtilities.CreateInstance(_scriptServices, type);
        }
        catch (Exception e)
        {
            if (!Throttled($"unbuildable:{type.Name}", out int suppressed))
            {
                logger.LogError(e, "Item script {Script} cannot be built; item template {Item} cannot be used. " +
                                   "{Suppressed} earlier failures of it were not logged",
                    type.Name, template.Id.Value, suppressed);
            }

            built = null;
        }

        _built[type] = built;
        return built;
    }

    private sealed record ItemUse(
        IWorldConnection Connection,
        CharacterEntity Character,
        uint RequestId,
        InventoryItem Item,
        ItemTemplate Template,
        ItemScript Script,
        IItemUseHost Host);
}
