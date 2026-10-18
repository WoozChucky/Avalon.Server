# Doodads (server design)

Doodads are placed world objects that a player clicks to use: a chest, a barrel, a shrine, a fountain, a quest object. Each one is a template row, plus a script when it needs one. A doodad is never a combat target. It has no health, takes no hits and is never aimed at by an ability. Its state belongs to its instance and is never saved. All the code is World-side, in `src/Server/Avalon.World/Doodads/`. Nothing in `Avalon.World.Public` reaches a doodad: the only additions there are the two `GameEntityFields` bits below, and they grant nothing.

This page is the authority on the subsystem. Other pages cover their own side of it:
- [world simulation](world-simulation.md): the replication, view and use bullets in full, and `/reload`;
- [item use](item-use.md) and [item-use protocol](item-use-protocol.md) §6: the cast bar a doodad shares with items;
- [instanced maps](instanced-maps.md): where respawns and doodad cast bars sit in the tick;
- [packet handlers](packet-handlers.md): reflection-bound registration;
- [tick profiling](tick-profiling.md): how doodads count in `candidateChecks` and `visiblePairs`;
- [tooling](tooling.md): the `doodad-catalog` export.

Slice S1, the framework and the wire, seeds and places no doodad. Every rule below is live, but no instance holds a doodad yet.

## Templates and catalog

- **Data** (World DB reference data, migration `AddDoodadTemplates`, configured in `WorldDbContext.Configure(EntityTypeBuilder<DoodadTemplate>)`, read by `DoodadTemplateRepository`):
  - `DoodadTemplates` (`Avalon.Domain/World/DoodadTemplate.cs`) holds:
    - the id and name;
    - `ModelKit`, `ModelKey`, `UsedModelKey`, `ReadyClip`, `UseClip` and `UsedClip`;
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
- **Reload** (`/reload doodads`) works forward only. A doodad already placed keeps the template view it was placed from, and the next instance build places from the new catalog. A `/reload quests` moves every viewer's gates (see "Per-viewer views and gates").
- **Export.** `tools/Avalon.Exporter -- doodad-catalog` writes `schema/doodads/doodad-catalog-v1.json` (empty in S1). It lists only what a client draws: the model kit and keys, the clips, the interaction kind, time and range, solidity and the spent look. It lists no gates, loot, gold, restore, radius or aura outcomes. `DoodadCatalogExportShould.List_only_what_a_client_draws` pins the field names, so nothing in the file can reveal a shrine's outcome. See [tooling](tooling.md).

## The object type and the wire fields

- **`ObjectType.Doodad` (7)** (`Avalon.Common/ObjectGuid.cs`, appended after Loot 6). A doodad's guid is `new ObjectGuid(ObjectType.Doodad, IObject.GenerateId())`. `schema/vectors/object-guid-v1.txt` carries the type.
- **`Doodad`**:
  - **Fixed at spawn:** its template, position and yaw (`Orientation.y`), and its effective gates. The spawn's own gate wins when the spawn sets one, and the template's applies otherwise. A solid template keeps no gate, even one the spawn passes.
  - **Never moves.** Its `IWorldObject` setters throw.
  - **State** is `Ready`, `Used` or `Despawned`. It is per instance and is set on the tick only. Every change raises `ViewVersion`.
  - `MapInstance.AddDoodad` files it in the instance's `DoodadIndex`, at build time only, and refuses a guid of another type.
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
  - Its packets name the doodad's template, so they go only to the user and to the connections that hear the user, as an item's do, whose view holds the doodad (`CharacterCharacterGameState.Knows`): a player a gated doodad is hidden from never learns of it through another's cast bar. That is one lookup per listener as the bar starts and ends, never per tick, and each packet is still encoded once and shared (`DoodadUseShould.Let_only_the_first_of_two_casts_spend_the_doodad`).
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
  2. If it did not throw, the quest credit follows. That is a placeholder in S1: the use-doodad objective kind arrives with the quest slice, and until then a use credits nothing.
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
  - `OnSpawn(IDoodadContext)`: at placement, off the tick, in the instance build. In S1 nothing calls it, because placement arrives with S2.
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

## Placement

S2 (procedural placement in the forest: chunk doodad slots, seeded rules, required rules and the navmesh obstacles of solid doodads) and S3 (fixed town spawns and the quest doodads) fill this section. S1 places nothing: `MapInstance.AddDoodad` exists, and no build calls it yet.

## What a doodad-unaware client does

Today's game client (`C:\dev\vulkan-multithreaded`, inspected read-only at `2ef944d5`) knows nothing of doodads. S1 sends it none, since nothing is seeded or placed. Once doodads are placed, it behaves like this:

- **An unknown type is decoded, not refused.** Any type above Portal (5) decodes to `WorldObjectKind::Unknown` (`gamenet/src/ObjectGuid.h:26-33`).
- **The object is stored but never drawn.** It is kept in the snapshot (`gamenet/src/session/SessionWorldState.cpp:50`) but never given an entity (`game/src/WorldReplicationSystem.cpp:313-319`, `Refuse(... "carries a type this client has no name for")`). The client says so once per instance and counts the rest (`:478-484` and `:497-507`). There is no assert, throw or disconnect.
- **Gap: an undrawn doodad can still be offered for use.** `InteractCandidate` (`game/src/DialogueSystem.cpp:71-80`) checks only `CanInteract`, the position and the range, not the kind. A doodad this client never drew, with `CanInteract` and within 5 m, still offers "E" and sends `CMSG_INTERACT`. The server runs that interact by its own rules as for any client.
- **A doodad cast bar shows a placeholder name.** On the user's own HUD it reads "Ability 0" (`game/src/HudSystem.cpp:495-511`), because the start packet has `AbilityId` and `ItemTemplateId` 0 and the client does not read `DoodadTemplateId`.
- **New fields are skipped.** Unknown protobuf fields are ignored (lite runtime 7.36.2, `gamenet/generated/avalon.pb.h:4`), so `ObjectState` 26 and 27 and the cast packets' `DoodadTemplateId` cost the client nothing.
- **An unknown quest objective kind still shows.** It is displayed as Unknown and otherwise normally (`gamenet/src/session/SessionQuests.cpp:29-39`). This matters once the quest slice adds the use-doodad kind.
- **Gap: solid doodads block the server's navmesh but not the client's** (from S2 on). This is an accepted gap until the client draws doodads and bakes them in.

The client half (drawing doodads by template, the used look and its clips, the cast bar's name, and the interact prompt's kind) goes to an issue on `WoozChucky/Avalon.Client`, filed with the quest slice. The server never waits for it.
