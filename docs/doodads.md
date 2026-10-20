# Doodads (server design)

Doodads are placed world objects that a player clicks to use: a chest, a barrel, a shrine, a fountain, a quest object. Each one is a template row, plus a script when it needs one. A doodad is never a combat target. It has no health, takes no hits and is never aimed at by an ability. Its state belongs to its instance and is never saved. All the code is World-side, in `src/Server/Avalon.World/Doodads/`. Nothing in `Avalon.World.Public` reaches a doodad: the only additions there are the two `GameEntityFields` bits below, and they grant nothing.

This page is the authority on the subsystem. Other pages cover their own side of it:
- [world simulation](world-simulation.md): the replication, view and use bullets in full, and `/reload`;
- [item use](item-use.md) and [item-use protocol](item-use-protocol.md) §6: the cast bar a doodad shares with items;
- [instanced maps](instanced-maps.md): where respawns and doodad cast bars sit in the tick;
- [packet handlers](packet-handlers.md): reflection-bound registration;
- [tick profiling](tick-profiling.md): how doodads count in `candidateChecks` and `visiblePairs`;
- [tooling](tooling.md): the `doodad-catalog` export.

Slice S1 was the framework and the wire. Slice S2 places procedural doodads from each map's rules (see "Placement") and seeds the forest's set (see "The forest set"). Slice S3 adds the use-doodad quest objective and seeds the caravan quest's doodads (see "The caravan quest's doodads"), and places a town's fixed doodads, Glimmerdell's notice board the first (see "Placement").

## Templates and catalog

- **Data** (World DB reference data, migration `AddDoodadTemplates`, configured in `WorldDbContext.Configure(EntityTypeBuilder<DoodadTemplate>)`, read by `DoodadTemplateRepository`):
  - `DoodadTemplates` (`Avalon.Domain/World/DoodadTemplate.cs`) holds:
    - the id and name;
    - `ModelKit`, `ModelKey`, `UsedModelKey`, `ReadyClip`, `UseClip` and `UsedClip` (`UsedModelKey`, null to keep `ModelKey`, names a model in the same `ModelKit` folder);
    - `Interaction`: None 0 or Click 1;
    - `InteractionTimeMs`: 0 is an instant use;
    - `InteractionRange` (5 m by default) and `AreaRadius`;
    - `UseLimit`: OncePerInstance 1 or Unlimited 2;
    - `SpentLook`: ShowUsed 1 or Despawn 2;
    - `RespawnSeconds`: null never respawns;
    - `ScriptName`, `LootTableId`, `MinGold`/`MaxGold` and `RestorePct`;
    - `Collision` (None 0 or Box 1), with `CollisionHalfX`, `CollisionHalfZ` and `CollisionHeight`;
    - a visibility gate and a usability gate. Each is a `*QuestId`, a `*Condition` (NotStarted 1, OnObjective 2, Completed 3, NotCompleted 4) and a `*ObjectiveId`. A gate quest has no foreign key, because the catalog checks it.
  - `DoodadAuraOutcomes` is keyed by `(DoodadTemplateId, AuraId)`, with a `Weight` of at least 1. Its rows cascade from the template.
  - The loot table's foreign key is `Restrict`: a table that a doodad drops from cannot be deleted.
  - Every enumeration is stored as a number and is append-only.
- **Check constraints** refuse a row the world could not run:
  - the enum ranges;
  - the range in (0, 10] and the radius in [0, 30];
  - `0 <= MinGold <= MaxGold` and `RestorePct <= 100`;
  - a gate's quest and its condition are both set or both null;
  - a solid box has finite half extents above 0 and a finite height of at least 1.049.
  - The height bound is **1.049, not 1.05**, on purpose. The column is a float that both databases compare as a double, so a stored `1.05f` reads 1.0499999523 and would fail `>= 1.05`. The catalog keeps the exact `>= 1.05f` rule (`DoodadCatalog.MinSolidHeight`). A box 1.049 m tall therefore passes the database, and the catalog refuses it at load. `DoodadSchemaShould.Accept_a_box_exactly_1_05_m_tall` and `DoodadCatalogShould.Load_a_solid_box_exactly_1_05_m_tall_with_its_gates_and_outcomes` pin both sides.
- **Catalog.** `DoodadCatalog` is the `Doodads` reload area (`ReloadArea.Doodads`, `DoodadsPatch`, `StaticData.Doodads`; it is `DoodadCatalog.Empty` before the first load).
  - It is built off the tick against the loot tables, the auras and the quest rows read in the same prepare, so a doodad and what it names are one generation. Scripts are resolved through `ScriptManager.GetDoodadScript`.
  - Each template becomes an immutable `DoodadTemplateView`, with its gates (`DoodadGate`), its outcomes (in aura id order, plus their `OutcomeWeightTotal`) and its script type resolved.
  - A template that breaks a rule is left out, with one Error naming it and the first rule it broke (`DoodadCatalog.Refused`). Every other template still loads.
  - The rules are checked in this order:
    1. The id is 0.
    2. The name is blank.
    3. A model kit, model key, used model key or clip is not a plain asset name (`AssetNames.IsPlain`, the game client's rule: 1 to 128 UTF-8 bytes, not starting with `.`, no `..`, and no `/`, `\`, `:` or control character).
    4. An enum value is one this build does not know, or a gate is half set (a quest without a condition, or the reverse).
    5. `InteractionTimeMs` is above 60,000.
    6. `InteractionRange` is not finite, or is outside (0, 10].
    7. `AreaRadius` is not finite, or is outside [0, 30].
    8. A solid box has a half extent that is not finite or not above 0, or a height below `1.05f`.
    9. A solid template has a visibility or usability gate. A hidden solid doodad would be an unseen wall.
    10. A solid template despawns. Its box stays in the navmesh.
    11. `RespawnSeconds` is 0. Use null for a doodad that never respawns.
    12. A gate names a quest that is not loaded.
    13. An OnObjective gate names no objective, or names an objective that is not one of its quest's.
    14. Any other gate condition carries an objective id.
    15. `ScriptName` does not name a loaded `DoodadScript`.
    16. The loot table is not loaded.
    17. `MinGold` is above `MaxGold`.
    18. `RestorePct` is above 100.
    19. An outcome's aura is not loaded, an outcome's weight is 0, or the weights total more than `uint.MaxValue`.
  - `DoodadCatalogShould.Refuse_a_template_that_breaks_a_rule` holds a row for every rule above but the weights' total, each asserting its refusal reason (`DoodadRefusal.Reason`).
- **Solid and scripted.** A template whose script declares its own `CanSee` (`DoodadScripts.OverridesCanSee`) has `ScriptCanSee` set. For a solid template the flag is forced off, with one Warning per template at load.
- **Reload** (`/reload doodads`) works forward only. A doodad already placed keeps the template view it was placed from, and the next instance build places from the new catalog. A `/reload quests` moves every viewer's gates (see "Per-viewer views and gates"). After either apply, a loaded quest whose UseDoodad objective names a template the live catalog lacks, or one that is not `Click`, is logged at Error and stays loaded ([quests](quests.md)).
- **Export.** `tools/Avalon.Exporter -- doodad-catalog` writes `schema/doodads/doodad-catalog-v1.json` (the forest set, templates 1 to 4, and the caravan quest's strongbox and notice board, 5 and 6). It lists only what a client draws: the model kit and keys (`usedModelKey` resolves in the same `modelKit` folder, as the file's `$comment` says), the clips, the interaction kind, time and range, solidity and the spent look. It lists no gates, loot, gold, restore, radius or aura outcomes. `DoodadCatalogExportShould.List_only_what_a_client_draws` pins the field names, so nothing in the file can reveal a shrine's outcome. See [tooling](tooling.md).

## The object type and the wire fields

- **`ObjectType.Doodad` (7)** (`Avalon.Common/ObjectGuid.cs`, appended after Loot 6). A doodad's guid is `new ObjectGuid(ObjectType.Doodad, IObject.GenerateId())`. `schema/vectors/object-guid-v1.txt` carries the type.
- **`Doodad`**:
  - **Fixed at spawn:** its template, position and yaw (`Orientation.y`), and its effective gates. The spawn's own gate wins when the spawn sets one, and the template's applies otherwise. A solid template keeps no gate, even one the spawn passes.
  - **Never moves.** Its `IWorldObject` setters throw.
  - **State** is `Ready`, `Used` or `Despawned`. It is per instance and is set on the tick only. Every change raises `ViewVersion`.
  - `MapInstance.AddDoodad` files it in the instance's `DoodadIndex`, at build time only, and refuses a guid of another type. The registry seals the index as it publishes the instance (`DoodadIndex.Seal`), and every add after that throws, since the tick reads the cell lists without a lock (`InstanceRegistryShould.Leave_A_Finished_Build_Out_Of_The_Registry_Until_It_Is_Published`).
- **`DoodadIndex`** is a grid on X/Z over the cells of the instance's spatial grid (`GridCells`, the one layout both are built on), written at build time only.
  - `Visit` hands a struct visitor exactly the doodads within a reach on X/Z, and allocates nothing.
  - A doodad whose X or Z is not finite is in `All` but in no cell.
  - `DoodadIndexShould` pins it.
- **`ObjectState`** (`Avalon.Network.Packets/State/ObjectState.cs`) gains two fields:
  - `DoodadTemplateId` (field 26), which the client maps to a model through the doodad catalog;
  - `DoodadState` (field 27). Its wire enum, `Avalon.Network.Packets.State.DoodadState`, is Unknown 0, Ready 1, Used 2, and is append-only. Despawned has no wire value: a despawned doodad is never described, and it leaves every view by a remove (`ObjectStateWriter.ToWire`).
- **What an add and an update carry.**
  - An add carries the position, the yaw, the template, the state, and `CanInteract`, the last only when it is true.
  - An update carries only what is marked, and `CanInteract` is the recipient's answer, false included. A client told it can use a doodad is therefore also told when it no longer can (`ObjectStateWriter.From(Doodad, ...)`).
- **Change marks.** A change on the tick goes through `MapInstance.DoodadChanged`, which queues the doodad once. The next broadcast tick marks it `GameEntityFields.DoodadState | CanInteract`, bits 17 and 18, which are outside `All`. Nothing walks the doodads that did not change.
- **A doodad with rules is encoded per recipient.** For a doodad with rules (`DoodadVisibility.AsksRules`), `CanInteract` is each recipient's own answer, so its add and update entries take `StatePacketAssembler`'s per-recipient path (`MapInstance.OwnDoodadEntry`). Any other doodad's answer is what its state allows (`DoodadVisibility.Usable`), the same for every recipient, so its entries are encoded once per broadcast and shared, as a creature's are (`DoodadReplicationShould.Send_a_spent_doodad_as_used_and_a_despawned_one_as_removed`). Both read the recipient's view, which `MapInstance.DescribeNewObject` and `DescribeUpdatedObject` are handed (`CharacterCharacterGameState.CanInteractWith`).
- **Cast packets** carry `DoodadTemplateId`: field 8 on `SUnitStartCastPacket`, field 5 on `SUnitFinishCastPacket` and `SCharacterInterruptedCastPacket` ([item-use protocol](item-use-protocol.md) §6).
- No new opcode exists: the interact reuses `CMSG_INTERACT`, so no session filter changes. The full write-up is in [world simulation](world-simulation.md), in the "Doodads are world objects of their own type" bullet.

## Per-viewer views and gates

- **A fourth tracked kind.** Each view has a doodad tracker (`CharacterCharacterGameState._doodadTrackingSystem`) beside the character, creature and world-object ones. Its candidates come from the instance's `DoodadIndex` within `Interest.Reach`, not from the spatial grid.
  - `MapInstance.DecideView` hands each view a `DoodadViewInputs`. An instance without doodads hands the default and reads nothing.
  - A doodad is listed by the creatures' interest rule: 60 m, kept to 70 m once held.
  - A despawned doodad is in no view.
- **Rules.** `DoodadVisibility.AsksRules` is true when a doodad has a visibility gate, a script that decides who sees it, or a usability gate. Only such a doodad has its rules asked. One without rules is seen by everyone in range, and usable while its state allows it (`DoodadVisibility.Usable`: a Click doodad, not despawned, and Ready or Unlimited).
- **The four gate conditions** (`DoodadGates.Allows`, over the viewer's `QuestLog`):
  - **NotStarted:** the quest is neither held nor turned in.
  - **OnObjective:** the quest is held and is not ready to turn in, its current stage holds the objective, and the objective's count is below the catalog's. A quest the catalog no longer has lets no one through. The stage's objectives are walked by index, so the check allocates nothing.
  - **Completed:** the quest was turned in.
  - **NotCompleted:** the quest was not turned in.
  - A condition this build does not know lets no one through.
- **The order of the rules.**
  1. The visibility gate.
  2. For a template with `ScriptCanSee`, the script's `CanSee` (`DoodadVisibility.IsVisibleTo`). Null keeps the gate's answer (`IDoodadViewContext.DataGateAllows`), and true or false overrides it.
  3. Past visibility, `DoodadVisibility.UseRefusal`: the usability gate, then the doodad's own state. This is the one statement of "can use": the view decision and the interact checks both ask it, so what a client is offered and what an interact lets through never differ. A doodad hidden from a viewer is never usable by that viewer.
- **When the rules are asked.** Never per tick. A view keeps one `DoodadAnswer` per doodad with rules (seen, usable, and the `ViewVersion` it was decided at), and asks again only at these moments:
  - **first sight:** the first decision that finds the doodad in range;
  - **the viewer's quests move:** `QuestLog.GateVersion` moved, raised by every load, accept, count change, stage, state change, abandon and turn-in. A `/reload quests` counts too, since the view compares the catalog's reference. The answers held are then put aside for comparison. Those still within reach are asked again, and the rest are dropped, to be asked when they come back;
  - **the viewer's level moves:** a script's `CanSee` may read `IDoodadViewContext.ViewerLevel`, so the view keeps the level it last decided at and counts a change as the quests moving (one compare per decision);
  - **the doodad moves:** its `ViewVersion` moved, raised by every state change and by `IDoodadContext.RefreshVisibility`;
  - **each interact:** the checks ask afresh;
  - **cast completion:** the checks run again as a doodad cast completes.
  - `Reset` (an instance move) drops every answer. A steady view over gated doodads asks nothing and allocates nothing (`DoodadReplicationShould.Decide_views_over_unchanged_gated_doodads_without_allocating`).
- **Per-viewer `CanInteract`.** `CharacterCharacterGameState.CanInteractWith` gives the answer the view's decision holds for the doodad as it is now. When the view holds none, the answer is no, failing closed. When the answer changes for a viewer that goes on seeing the doodad, that viewer alone gets an update marked `CanInteract`. The update merges with the frame's state mark into one entry when there is one (`DoodadReplicationShould.Tell_each_viewer_whether_it_can_use_a_doodad`).
- **A gated doodad never reaches a viewer its rules refuse.** This holds through the shared encoding, a `Reset` after an instance transfer, and a party member standing 1 m from someone who may see it (`DoodadReplicationShould.Send_a_doodad_only_to_the_viewers_its_rules_allow`), and through the cast bar (below).
- **The script's `CanSee` is contained.**
  - It reads the doodad and the viewer's name, level, class and quests through one `DoodadViewContext`; a level gained or lost asks it again, as above. The container's `DoodadVisibility` reuses that context, opening it before the call and closing it after, so asking allocates nothing. A script that kept the context gets a throw from every member.
  - A throw is logged at Error, at most once per script per `ThrottledErrorLog.Interval`. The gate's answer then stands and is held like any other, so nothing escapes the view decision.
- **A solid doodad is never hidden:**
  - the catalog refuses a gated solid template;
  - the `Doodad` constructor drops a spawn's gate on one;
  - a solid template's `CanSee` override is ignored (`Doodad.AsksScript`);
  - `IDoodadContext.Despawn` throws for one;
  - and the catalog refuses a solid template that despawns.

## Interaction and the cast bar

- **Dispatch by guid type.** `InteractHandler` (`CMSG_INTERACT`) first drops a dead or missing character, and an interact whose instance is not found. A target guid of type `Doodad` then goes to `DoodadUseService.Interact`, the user being the `CharacterEntity` and the instance the `IDoodadHost`. Without the service, a doodad interact is dropped. Any other guid takes the NPC dialogue path. `DoodadUseService` is a DI singleton and runs on the tick thread only.
- **The checks, in order** (`DoodadUseRefusal`). The first that fails refuses:
  1. `NoHost`: the instance is not an `IDoodadHost`.
  2. `Dead`: the user is dead.
  3. `NotFound`: no doodad here has the guid, or the doodad is despawned.
  4. `NotInteractive`: the template's interaction is None.
  5. `OutOfRange`: the distance is above the template's `InteractionRange`. The check is written `!(distance <= range)`, so a position that is not a number refuses.
  6. `NotVisible`: `DoodadVisibility.IsVisibleTo` hides the doodad from the user. It is asked afresh, the script's `CanSee` included.
  7. `NotUsable`: the usability gate refuses.
  8. `Spent`: a once-per-instance doodad is not ready.
  9. `Busy`: an ability cast is running.
  10. `ScriptRefused`: the script's `CanUse` returns a line, or throws.
  - **Every refusal is silent.** Nothing is sent back. The refusal is one Debug line through a generated `LoggerMessage`, and it allocates nothing (`DoodadUseShould.Refuse_a_use_the_rules_forbid`).
- **A use that passed** first ends the user's item cast and doodad cast, out loud. Then it either completes at once (`InteractionTimeMs` 0) or starts a cast bar.
- **The cast bar** is `DoodadCasts`: one per instance, at most one cast per character, ticked right after the item cast bars. Only the active casts are visited, and a tick with none reads nothing.
  - Its end time (`PendingDoodadUse.EndsAt`) is absolute, on the instance's `TimeProvider`, so nothing counts it down.
  - Its cast ids come from the instance's ability cast ids.
  - Its packets name the doodad's template, so they go only to the user and to the connections whose view holds the doodad (`CharacterCharacterGameState.Knows`) among those that hear the user, as an item's packets do. A player a gated doodad is hidden from therefore never learns of it through another's cast bar. That is one lookup per listener as the bar starts and ends, never per tick, and each packet is still encoded once and shared (`DoodadUseShould.Let_only_the_first_of_two_casts_spend_the_doodad`).
  - **A listener that loses the doodad mid-cast hears no end.** The listeners are chosen again as the bar ends, from each view as it then stands, so one whose view dropped the doodad since the start gets no finish and no interrupt. That happens when the doodad despawns, the listener's quests move, a script calls `RefreshVisibility`, or the listener leaves the doodad's reach. Its bar runs out on its own. Nothing leaks, and it is what an item's or an ability's listener sees when it walks out of range mid-cast, so a client times a bar out by its cast time.
  - **It ends early, out loud**, when the user:
    - moves or dies, checked each tick against the start position (damage alone never ends it);
    - leaves the instance (`EndCastsOf`);
    - starts an ability cast that `CastAbilityHandler` accepts;
    - uses an item that passed its checks, gear included (`ItemUseService.InterruptCasts`);
    - or uses another doodad.
  - Every broadcast and owner call is contained, so one that fails never leaves a cast stuck.
  - `DoodadUseShould.End_a_cast_when_the_user_moves_dies_leaves_or_casts` and `End_an_item_cast_when_its_user_uses_a_doodad_that_passed_its_checks` pin these.
- **Two casts racing on one doodad: the first wins.** When a cast's time has run, the checks run again with the ability cast left out (`IDoodadCastOwner.CanComplete`). A refusal ends the cast interrupted. Of two players casting at one once-per-instance doodad, the first to complete spends it, and the other's cast ends refused. On the same tick, the first is the first that `DoodadCasts.Update` reaches (`DoodadUseShould.Let_only_the_first_of_two_casts_spend_the_doodad`).
- **Completion** is the same for an instant use and a finished cast (`DoodadUseService.Complete`):
  1. The script's `OnUse` runs.
  2. If it did not throw, the quest credit follows: `QuestService.DoodadUsed` gives +1, capped, to every `UseDoodad` objective for the template in the current stage of each of the user's Active quests, and settles the stages as a kill does ([quests](quests.md)). **The user alone is credited**, never its party, whoever else stands by: each player on the step uses the doodad for their own credit, and a use credits only its user. The credit is contained: a throw is logged at Error, at most once per `ThrottledErrorLog.Interval`, and costs the use only its credit, so a once-per-instance doodad is still spent. A credit raises the user's `QuestLog.GateVersion`, so an `OnObjective` gate on that objective closes for the user alone at its next view decision once the count fills. `QuestDoodadCreditShould` pins it.
  3. A once-per-instance doodad that is still `Ready` is spent: `Used` for the ShowUsed look, `Despawned` for Despawn. A script that spent it its own way is left as it set it.
  4. A respawn is scheduled for a template with `RespawnSeconds`. A script's `RespawnAfter` takes the place of the template's.
  - **A doodad is spent only after `OnUse` returns.** An `OnUse` that throws leaves the doodad `Ready` and credits nothing, and what it did before the throw stays done: its loot on the ground, its auras, its heals. **A once-per-instance script must therefore not throw after its effect**, or the doodad could be used again, effect and all. Each stock script makes its effect its last act (`DoodadUseShould.Leave_the_doodad_ready_when_its_script_throws`).
  - **Despawn.** A doodad that goes `Despawned` on the tick runs its script's `OnDespawn`, whether the framework or a script's `Despawn()` despawned it. Every state change reaches clients through `DoodadChanged`.
- **Respawns** are `DoodadRespawns`, one per instance: a min-heap (`PriorityQueue`) on absolute respawn times from the instance's `TimeProvider`.
  - Step 1b of `MapInstance.Update` peeks at the soonest once, and reads nothing while none waits. The cost is per respawn, never per doodad.
  - An instance nobody is in does not tick. It catches up on its next occupied tick.
  - Scheduling a doodad again moves its time (`Doodad.RespawnAt`), and the superseded heap entry is dropped when it comes up. A doodad made `Ready` by any path, a script's early `SetState` included, has no respawn time, so an entry left for it is dropped too.
  - A respawn sets the doodad `Ready`, runs `OnRespawn` and calls `DoodadChanged`.
  - **A doodad that is already `Ready` is skipped.** A respawn request for it is never scheduled: one from an unlimited doodad's script, or from an `OnUse` that threw after asking. A respawn that comes due for one a script already made ready does nothing: no `OnRespawn` and no update (`DoodadUseShould.Respawn_a_spent_doodad_after_its_delay`).

## Scripts and the context

- **`DoodadScript`** is the sixth `ScriptManager` kind. It is reflection-bound, as item scripts are (CLAUDE.md, "Reflection-bound registration"):
  - it is named by `DoodadTemplate.ScriptName`;
  - `ScriptManager` finds it by `Type.Name` once, at `Load`, and never hot-reloads it;
  - `DoodadScripts` (a DI singleton) builds it once per type with `ActivatorUtilities.CreateInstance` over `QuestScriptServices`, which hands out loggers and `TimeProvider` only, with no runtime arguments.
  - One instance serves every doodad that names it, in every instance, so it keeps no per-doodad or per-use state.
  - A type that cannot be built is logged once, and its doodads run without a script.
  - `DoodadScriptConstructibilityShould` builds every shipped and seeded script that way, and refuses one that asks for the world.
  - Never rename or delete a doodad script on grep evidence alone.
- **Hooks**:
  - `OnSpawn(IDoodadContext)`: at placement, off the tick, in the instance build, before the doodad is added (`DoodadPlacementService`). A throw is logged through the scripts' throttle, and the doodad is still placed.
  - `CanUse(IDoodadUseContext)`: read only, and asked at the interact and again at cast completion. It returns null to allow the use, or a line to refuse it.
  - `OnUse(IDoodadUseContext)`: abstract.
  - `OnDespawn` and `OnRespawn`.
  - `CanSee(IDoodadViewContext)`: read only and allocation-free. It is asked only at the moments listed under "When the rules are asked".
  - **Every hook is contained.** A throw is logged at Error, at most once per script and hook per `ThrottledErrorLog.Interval` (`DoodadScripts.Failed`), and the caller goes on. A `CanUse` that throws refuses the use. An `OnUse` that throws leaves the doodad ready, as described under "Completion".
- **`IDoodadContext`** is what every hook reads and acts through:
  - it reads the template id, name, position, state, `AreaRadius`, `RestorePct`, `AuraOutcomes` and `OutcomeWeightTotal`;
  - `SetState`: Ready or Used only;
  - `Despawn()`: throws for a solid doodad;
  - `RespawnAfter(delay)`: the delay must be above zero;
  - `NextRandom(max)`: from the use service's `ILootRandom`;
  - `RefreshVisibility()`: raises `ViewVersion`, so every viewer asks `CanSee` again at its next decision.
- **`IDoodadUseContext`** adds the user and the acts of a use:
  - `UserName` and `UserInParty`;
  - `DropLoot()`, `ApplyAuraInRadius(aura, radius)` and `RestoreInRadius(healthPct, powerPct, radius)`, each answering how many it reached;
  - `Tell(line)`: a system line to the user.
- **One reusable `DoodadUseContext` per owner.** `Begin` and `End` bracket each hook, so a hook allocates no context, and a context kept past its hook throws from every member.
  - In `CanUse` the context has no instance, so an act throws there, and the throw refuses.
  - In `OnSpawn` (spawn mode) the live acts do nothing and answer 0. `Despawn`, `RespawnAfter`, `RefreshVisibility` and `Tell` are ignored there too.
  - The owner reads `Changed` and `RespawnRequested` before `End`, to tell the instance and schedule the respawn.
- **The area acts** reach `IDoodadHost.PlayersWithin`: the living players of the instance within the radius of the doodad on X/Z, never a creature. They are measured as the act runs, so as the use completes. A distance that is not a number is out of reach, and a negative or non-finite radius reaches no one. The context keeps one list for this between uses.

## Stock scripts

The stock scripts are in `Avalon.World/Doodads/Scripts/`, and `StockDoodadScriptsShould` pins them.

- **`LootDoodadScript`**, the chest and the barrel, calls `DropLoot()`. That is `IDoodadHost.DropLootAt`, which follows creature drops:
  - The template's loot table and copper range are rolled at use (`ILootRoller.Roll(table, minGold, maxGold, ...)`). A table that is missing drops only gold, with a Warning.
  - **Who is eligible:** the user and every member of the user's party in this instance, by `CharacterEntity.PartyId`, dead or alive.
  - **A leave countdown leaves nobody eligible.** When the user is in one, nobody is eligible, as for a killer in one, so the drops are free for all at once.
  - Each drop is allocated by the instance's `ILootAllocator` (`PartyLootAllocator`, with the `LootGracePeriod`).
  - The drops are placed on the ring around the doodad, stored, and told to every connection here in one shared `SLootSpawnedPacket`, using the instance's `KillBuffers`.
  - The drop is contained: a failure is logged, and what was stored before it stays.
- **`ShrineDoodadScript`** is a gamble:
  - **The roll happens at use.** It draws one of the template's `AuraOutcomes` by weight, with `NextRandom(OutcomeWeightTotal)` taking the first outcome whose running weight passes the roll.
  - It applies that aura within `AreaRadius`, with no caster and `AuraSource.None`: no scaling, no combat and no credit. Each application is contained and throttled.
  - **The outcome is never revealed beforehand.** It does not exist until the draw, and neither the wire nor the catalog export carries the outcomes.
- **`FountainDoodadScript`** calls `RestoreInRadius(RestorePct, RestorePct, AreaRadius)`:
  - **Restore by percentage:** each living player within the radius, measured on X/Z from the doodad, gets that share of their maximum health and of their maximum power.
  - The health goes through the combat service, with the user as the healer, capped at the maximum. Power is capped likewise.
  - **Players only:** creatures never.

## The forest set

Migration `SeedForestDoodads` seeds the first doodads (`WorldDbContext`, `Configure(EntityTypeBuilder<DoodadTemplate>)` and `Configure(EntityTypeBuilder<DoodadAuraOutcome>)`), with what they name: loot tables 12 and 13 and auras 8 to 10. Its inserts run in foreign-key order, the auras and loot tables before the doodads, and the doodads before their outcomes. Every one is clicked, used from 5 m, once per instance (`OncePerInstance`), shows its used look once spent (`ShowUsed`), never respawns and has no gate. `SeedIntegrityShould.Seed_each_forest_doodad_as_designed` pins every column, and `Load_every_seeded_doodad_through_the_catalog_without_refusing_any` loads them as the world does.

| Id | Name | Kit / model / used model | Cast | Radius | Script | What a use does | Collision |
|---|---|---|---|---|---|---|---|
| 1 | Forest Chest | `props-kit` / `Chest_Wood` / none (clips `Chest_Closed`, `Chest_Open`, `Chest_Opened`) | instant | 0 | `LootDoodadScript` | table 12, 50 to 150 copper | none |
| 2 | Old Barrel | `props-kit` / `Barrel` / `avalon_barrel_broken` | instant | 0 | `LootDoodadScript` | table 13, 0 to 20 copper | none |
| 3 | Wayside Shrine | `town-kit` / `avalon_shrine` / `avalon_shrine_spent` | instant | 10 m | `ShrineDoodadScript` | one of Blessed (weight 40), Emboldened (40) or Hexed (20) | box half extents 0.8 x 0.8 m (1.6 m across), 2.2 m tall |
| 4 | Mossy Fountain | `town-kit` / `avalon_forest_fountain` / `avalon_forest_fountain_dry` | 2 s | 10 m | `FountainDoodadScript` | 50 % of health and power | box half extents 1.05 x 1.05 m (2.1 m across), 1.8 m tall |

- **The loot tables** are listed under "The seeded loot" in [world simulation](world-simulation.md): table 12 "Forest chest" rolls potions and then the creatures' shared pools, richer than a kill; table 13 "Forest barrel" rolls only potions.
- **The auras** are listed in [auras](auras.md): 8 Blessed, 9 Emboldened and 10 Hexed, stat auras of 2 minutes, 2 minutes and 1 minute.
- **A solid box is measured from the model** in the kit's bounds table (`tools/Avalon.ChunkGen/Kit/kit-bounds.json`). Its half extents are half the model's footprint rounded up to 0.05 m, and its height is the model's, rounded up to 0.05 m and never below 1.05 m. The fountain's footprint is round, and a box over its full ±1.2 m would reach 0.5 m past the basin at the corners. So its box is drawn in by the decor tolerance (0.15 m) on each side, to ±1.05 m: its sides stay within the tolerance of the rim. `DoodadCollisionShould` pins the rule for every seeded solid template, checks the box against both the ready and the used model within the tolerance, and checks it fits a `doodad_large` slot, whose 2 m edge clearance (`ForestPiecesShould.EdgeClearance`) is the most a half extent may be.
- **The forest's rules** (`Maps/ProceduralMaps/2.json`, `doodadRules`). The first four are optional; the fifth is required:
  1. the chest at a `doodad` slot, 1 to 3 per run, 25 m apart, from depth 2 on;
  2. the barrel at a `doodad` slot, 2 to 5 per run, 10 m apart;
  3. the shrine at a `doodad_large` slot, at most one, from depth 3 on;
  4. the fountain at a `doodad_large` slot, at most one, from depth 1 on;
  5. the caravan's strongbox (see "The caravan quest's doodads") at a `doodad` slot, exactly one, from depth 4 on, **required**. Being required, it is planned before the four above, so it changes where they fall for a given seed. `CommittedForestDoodadsShould` proves it finds a slot for every seed from 0 to 999.
- **The scenarios place them too.** The forest scenarios (`ScenarioWorld.NewForestInstance`) build through `ChunkLayoutInstanceFactory`, so they plan and place the forest's doodads. `ForestLayouts` plans each seed's doodads as the factory does and bakes the navmesh with their boxes, and the cached bake refuses a layout whose boxes differ.

## The caravan quest's doodads

Migration `SeedLostCaravan` seeds quest 9, "The Lost Caravan" ([quests](quests.md)), with its two doodads and its texts. Its inserts run in foreign-key order: the doodads and the texts before the quest, and the quest and its stages before the objectives that name the doodads. `SeedIntegrityShould.Seed_the_lost_caravan_as_designed` pins how the quest and its two doodads fit together: the quest it follows, its giver and ender, each objective against its doodad's gate, and its texts in both locales.

- **The quest.** Marta Ledgerwell (creature 11) offers it once quest 6, "What the Husks Carry", is turned in, from level 4, and takes it back herself. It has three stages, one objective each:
  0. open the caravan's strongbox in the forest (objective 901, `UseDoodad` doodad 5, through a 1.5 s cast bar);
  1. pin the caravan's fate to the notice board in Glimmerdell (objective 902, `UseDoodad` doodad 6, instant);
  2. speak with Marta (objective 903, `Talk` creature 11).
  - Its rewards are 600 experience, 350 copper and 2 Greater Health Potions (item 56).
  - Its texts are 67 to 74; the talk objective reuses text 38 ("Speak with Marta Ledgerwell"). The ptPT translations are a draft that needs a native speaker's review before deploying.
- **Both doodads** are clicked and used from 5 m, by every player for themselves (`Unlimited`, so never spent), with radius 0, `ShowUsed`, no respawn, no script and no collision. A use does nothing but credit the quest, and the credit goes to the user alone (see "Completion").

| Id | Name | Kit / model / used model | Cast | Gate | Placed |
|---|---|---|---|---|---|
| 5 | Caravan Strongbox | `props-kit` / `Crate_Metal` / `avalon_strongbox_open` | 1.5 s | visibility `OnObjective(9, 901)` | the forest's required rule 5 |
| 6 | Notice Board | `town-kit` / `avalon_notice_board` / `avalon_notice_board_pinned` | instant | usability `OnObjective(9, 902)` | Glimmerdell's fixed doodad spawn 1, at (9, 0, 24) |

- **The two gate kinds.**
  - **The strongbox has a visibility gate.** It is seen only by a player whose quest 9 is on objective 901, still unfilled. Anyone else is never sent it. Once a use credits 901, the user's quest moves to stage 1, which raises `QuestLog.GateVersion`, so the user's next view decision removes it. The strongbox stays `Ready`, and another player still on the step goes on seeing it, and can open it for their own credit, after which it is removed from their view too (`QuestDoodadCreditShould.Hide_the_seeded_strongbox_from_its_user_once_opened`).
  - **The board has a usability gate only.** Everyone in range sees it, as a part of the square, but only a player on objective 902 is told `CanInteract` and can use it. Any other player's interact is refused as `NotUsable`, silently (`QuestDoodadCreditShould.Credit_the_seeded_notice_board_only_to_a_player_on_its_step`).
- **The board is a doodad-drawn town prop, never hidden.** It was decor in `town_sw_01`; it is now drawn only as a doodad, placed by Glimmerdell's fixed spawn (see "Placement"), and its collision box stays in the chunk. So nothing may hide it: no visibility gate on its row or template, no despawn, and no script (`TownDoodadSpawnsShould`).
- **Each used model lives in its template's own kit folder** (`usedModelKey` resolves in `modelKit`). The strongbox is a props-kit `Crate_Metal`, so `avalon_strongbox_open` belongs in the client's props-kit folder, and the board's `avalon_notice_board_pinned` in town-kit. Both models are made outside the repository, with the rest of the custom art, and measured in the kit's bounds table. The catalog checks only that the names are plain, so the seed does not wait for the art.
- **The used looks never show in v1.** Both templates are `Unlimited` and have no script, so nothing ever spends them: each stays `Ready`, and `avalon_strongbox_open` and `avalon_notice_board_pinned` are never displayed. They are catalogued so that a future script, or a change to the template, can show them.
- **Neither is solid**, so their gates are allowed (a solid doodad is never hidden).

## Placement

A procedural map's doodads come from its doodad rules (`ProceduralDoodadRule`, written by `ChunkCatalogSeeder` from `Maps/ProceduralMaps/<mapId>.json`; see [map generation](map-generation.md#spawn-tables-and-procedural-map-configs)) and the doodad slots of its pieces (`ChunkTemplate.DoodadSlots`). `ChunkLayoutInstanceFactory` runs `IDoodadPlacementService` (`DoodadPlacementService`) in two steps of the off-tick build. A map's fixed spawns (`MapDoodadSpawns`, a town's) are placed by the second step, before the plan's doodads.

- **Plan**, before the navmesh bake, from the layout, the rules and the catalog alone. It is deterministic for a seed, in every process: one `Random(layout.Seed ^ DoodadSeedSalt)` (`0x0D00DAD5`), over slots sorted rather than taken in load order, since a chunk's slots are owned rows that Postgres loads in no guaranteed order.
  1. The candidates are every doodad slot of the layout in world space (`ChunkRotation.LocalToWorld`), in chunk order and each chunk's slots sorted by tag (ordinal), `LocalX`, `LocalZ`, `Yaw` and `LocalY`, with yaw `(slot.Yaw + 90 * chunk.Rotation) % 360` and the chunk's depth. A slot within 15 m (X/Z) of the entry spawn or 8 m of a portal is dropped.
  2. The rules run required first, then by `Sequence`. A rule whose template the catalog lacks is skipped: with an Error when the rule is required (its doodad, and so its quest, is missing), otherwise with a Warning.
  3. Each rule draws its count (`MinCount` to `MaxCount`), takes the candidates not yet taken with its tag (ignoring case) and a depth within `MinDepth` to `MaxDepth`, shuffles them (Fisher-Yates), and takes them in order until it has its count, skipping one nearer than `MinSpacing` to a doodad of the same template, or nearer than 2 m to any doodad planned.
  4. A required rule left short adds what it lacks to `RequiredShort`, for the fallback.
  5. `DoodadPlan.Obstacles`: each solid doodad planned yields its world box, in plan order (see [map generation](map-generation.md#solid-doodads-in-the-bake)). The build sets them on the layout (`ChunkLayout.Obstacles`) before the bake, so the bake cuts them out and the layout packet carries them.
- **Fixed spawns** (migration `AddMapDoodadSpawns`): a `MapDoodadSpawns` row places one doodad at a fixed spot of one map, as a `MapCreatureSpawns` row places a creature, for every layout kind. Its position is the layout's `EntrySpawnWorldPos` plus the row's offset, its yaw the row's (0 to 360, a check constraint), its offsets finite (each above -1,000,000 and below Infinity, which refuses NaN, a check constraint each), and its template a foreign key (`Restrict`). `IMapDoodadSpawnRepository.FindByMapAsync` reads the map's rows once per build, off the tick, at the start of **Place**, and they are placed before the plan's doodads.
  - A walk-through doodad's height is the navmesh's under it (`SampleGroundHeight`, which keeps the authored height off the mesh, as a town creature's is). A solid one keeps its authored height, and a row that faces a solid one off a quarter turn is refused with a Warning.
  - **v1 places no solid town doodad**. Nothing is baked from a fixed spawn: the plan's obstacles come from the procedural plan alone, before the bake. A solid one would need its box authored in ChunkGen.
  - **The notice board is walk-through as a doodad** (template 6, `Collision = None`). Its collision box is a chunk proxy, kept in `town_sw_01.obj` by `SquareBuilder.DoodadProp` ([map generation](map-generation.md#decor-and-building-recipes)), so the box stays however the doodad is shown. A doodad-drawn prop is therefore never hidden: neither its row nor its template has a visibility gate, its template never despawns once spent, and it has no script, since a script could despawn it or hide it through `CanSee`. `TownDoodadSpawnsShould` pins this, since a hidden one would leave an invisible wall, and refuses a seeded row on any map but Glimmerdell (map 1), so a new map's fixed doodads extend it first. The guard is seed-time because only `HasData` writes these rows and the doodad templates: any future live-edit design for doodad templates or spawns must enforce the same rule.
  - A row's gates replace the template's under the rules of step 4 below (dropped on a solid template, or when they name a quest or objective the world lacks). A row whose template the catalog lacks is skipped with an Error.
  - The seed is Glimmerdell's notice board: row 1, doodad 6, offset (-6, 0, 9) from the entry spawn (15, 0, 15), so world (9, 0, 24), yaw 90, with no gate of its own. `DoodadPlacementShould.Place_a_town_doodad_at_its_offset_on_the_ground` pins the placement rules, and `QuestDoodadCreditShould.Credit_the_seeded_notice_board_only_to_a_player_on_its_step` the board end to end in a Glimmerdell instance.
- **Place**, after the bake and the creatures, on the instance being built, before it is published. Each doodad is contained on its own: one that throws is logged and skipped, and the others are placed.
  1. A walk-through doodad is snapped to the navmesh under its slot (`FindGround`; its nearest point when just beside the mesh, as a creature's slot is). With no mesh within reach, a required one moves to the fallback and an optional one is skipped with a Warning.
  2. A solid doodad keeps its slot's height, unsnapped. A doodad slot stands on the floor (written at local y 0, where a spawn slot is written 1 m up), so a solid doodad's model and its box in the bake stand on the ground.
  3. **The fallback**: each required doodad short of a slot is tried at up to 64 random open navmesh points (`MapNavigator.TryFindOpenPoint`: `FindRandomPointAroundCircle` from the entry spawn's polygon, over every polygon connected to it, so never on an island such as a blocker's flat top; polygons drawn by area, kept only with no wall within 1.5 m, `FindDistanceToWall`). It draws from the placement's random stream, one per build (`Random(layout.Seed ^ 0x0FA11BAC)`), which it shares with `LootRandom`, the stream the scripts' `OnSpawn` draws from through `NextRandom`; its navmesh draws go through one `RcRand` over that stream. The entry's polygon found once per doodad rather than per try. A point is taken at 15 m or more from the entry, 8 m from every portal and 2 m from every doodad placed, facing yaw 0. The fallback ignores the rule's depth range and its same-template spacing: it keeps only those clearances. After 64 tries an Error names the map, the seed and the template. A solid template is never placed off its slot, since its box was never baked into the navmesh: an Error, and it is not placed. The seeder refuses a required rule on a solid template, so this only follows a reload.
  4. Each doodad gets its gates: the rule's, when set, replace the template's. A gate on a solid template is dropped with one Warning per spawn, so a solid doodad is never hidden. A gate naming a quest the world's quest catalog does not hold, or an objective that is not that quest's, is dropped with a Warning, and the template's gate stands.
  5. Then its script's `OnSpawn` runs in spawn mode (`DoodadUseContext.Begin` with no host), and `MapInstance.AddDoodad` files it.
- Nothing of this runs on the tick. `DoodadPlacementShould` pins the plan's tags, depths, counts and spacing (the same seed giving the same plan, with each chunk's slots loaded in either order, and different seeds different ones), the fallback (over 200 seeds, each point reachable from the entry) and the gates, `SolidDoodadNavmeshShould` that a solid doodad stops a walk and rides in the layout packet, and `CommittedForestDoodadsShould` proves the committed forest places every required doodad at a slot for seeds 0 to 999.

## What today's client does

The game client (`C:\dev\vulkan-multithreaded`, inspected read-only on `main` at `a482fe46`) took the S1 wire in WoozChucky/Avalon.Client#402: the schema re-vendored from server main `831e6f41`, and the doodad catalog vendored beside the item, ability and aura catalogs. From S2 the forest places the set above, and from S3 the caravan quest's doodads, and the client behaves like this:

- **A doodad is decoded and drawn as a placeholder.** It names `Doodad = 7` (and `Loot = 6`, `gamenet/include/gamenet/session/WorldStateTypes.h:32-35`) and reads `ObjectState` 26 and 27 (`gamenet/src/session/SessionWorldState.cpp:127-135`). A doodad is drawn as a teal placeholder (`game/src/WorldReplicationSystem.cpp:49-50` and `:59`), not its model, under a name-only nameplate: its catalog row's name, or "Doodad <id>" without a row (`game/src/NameplateSystem.cpp:159-166`). A type above Doodad still decodes to Unknown (`gamenet/src/ObjectGuid.h:26-33`) and is refused at spawn, once per instance (`game/src/WorldReplicationSystem.cpp:320-323`), with no assert, throw or disconnect.
- **Its vendored catalog is empty** (`gamenet/schema/doodads/doodad-catalog-v1.json`, `"doodads": []`), so every doodad is named "Doodad 1" to "Doodad 6" today.
- **E offers only a doodad that has a catalog row**, within that row's `interactionRange` (`game/src/DialogueSystem.cpp:76-87` and `:100-107`), with the prompt "Press E to use ..." (`:552-554`). With the empty catalog, E offers no doodad. A click on a doodad's plate still sends `CMSG_INTERACT` while `CanInteract` is true, row or not, and never makes it the combat target (`:352-372`). The server runs that interact by its own rules, as for any client.
- **The doodad cast bar is named from the catalog** (`game/src/HudSystem.cpp:502-506`), "Doodad <id>" without a row, and a doodad cast starts no cooldown (`game/src/PlayerCastState.cpp:155-159`).
- **Re-vendoring the catalog brings the names and E.** Once `doodad-catalog-v1.json` holds templates 1 to 6, the plates and the cast bar read "Forest Chest", "Old Barrel", "Wayside Shrine", "Mossy Fountain", "Caravan Strongbox" and "Notice Board", and E offers each within 5 m while its `CanInteract` is true (`game/src/DialogueSystem.cpp:98`), so the board only to a player on its step. The models, the used look and the clips wait for the rest of #401.
- **New fields are skipped.** Unknown protobuf fields are ignored (lite runtime 7.36.2, `gamenet/generated/avalon.pb.h:4`).
- **An unknown quest objective kind still shows.** It is decoded as Unknown (`gamenet/src/session/SessionQuests.cpp:29-39`), and nothing in `game/` reads the kind, so it is displayed normally: quest 9's two `UseDoodad` objectives (kind 5) show their text and count like any other.
- **Quest 9 can be done, with placeholders.** Marta offers the quest after quest 6, and its stages show. The strongbox reaches a player on its step as a teal placeholder, not a crate, named "Doodad 5" until the catalog is re-vendored, and with the empty catalog it is opened only by clicking its plate; the board likewise. The notice board is still drawn from the vendored decor (`devproject/data/world/decor/town_sw_01.json`), with the doodad's placeholder at the same spot.
- **The board leaves `town_sw_01`'s decor on the next re-vendor.** The server's decor file no longer holds it, so a client that re-vendors the decor before it draws doodads by their model loses the board from the square (its collision box stays in the chunk). The client must re-vendor the chunks and the decor together with doodad drawing (WoozChucky/Avalon.Client#401 Part B4).
- **Solid doodads block both navmeshes.** Their boxes reach the client in `SChunkLayoutPacket.Obstacles` (field 8), which it decodes (`gamenet/src/session/SessionMap.cpp:71`), and its bake appends them after its chunks with the server's vertices and faces (`game/src/ChunkGeometry.cpp:202`), so a walk stops at a solid doodad on both sides (seen on `origin/main` at `631ef5e5`).

The client work is WoozChucky/Avalon.Client#401, and its Part A was merged in #402. The server never waits for it.
