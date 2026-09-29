# World Maintenance and Admission Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Admins put one world into persistent maintenance through the API or in-game chat, drain players with a five-minute countdown, and derive truthful online/offline status from world readiness.

**Architecture:** The auth database stores maintenance intent and revision; a Redis notification prompts the world to reload it, while periodic reconciliation covers missed messages. The world publishes a short-lived readiness heartbeat. Auth and the world both gate admission, and the world coordinates chat warnings and the final disconnect.

**Tech Stack:** .NET 10, C#, EF Core/PostgreSQL, Redis, protobuf-net, xUnit, NSubstitute.

**Spec:** `docs/superpowers/specs/2026-09-29-world-maintenance-admission-design.md`

## Global Constraints

- Base execution on `origin/main` at `35664848` or later; #665's deferred listener startup is already merged there. Bring the approved spec and this plan onto that base before implementation.
- One active world process per world ID; the heartbeat is not leader election.
- Default grace: 5 minutes; accepted override: integer 1–60 minutes. Warnings: immediately, 3 minutes, 1 minute, 30 seconds, then each second 10 through 0 that falls within the chosen grace.
- `AccountAccessLevel` is a flags enum. Use an Admin-flag test for the bypass and `/maintenance` command. Preserve unknown/restricted world indistinguishability.
- Preserve #663's same-connection character leave flow. Preserve packet enum numeric values; append `Maintenance` results/reasons.
- Redis notification is a hint; the auth database is authoritative. No database I/O runs on the 60 Hz simulation tick.
- `WorldDto.Available` already means that the API configured and migrated the world's databases. Add `Ready` for runtime readiness; do not reuse `Available`.

## Review Focus

1. A stalled tick with a live process must lose its ready heartbeat and stop new auth selections; Task 2 tests this.
2. Two simultaneous `on` requests must share one deadline, while a subsequent `off` must advance the revision; Task 1 tests this.
3. An inaccessible world ID must still answer `WorldUnavailable`, even when that row is in maintenance; Task 4 tests this.
4. A connection returned to character selection by #663 must not reselect during maintenance; Task 6 tests this.
5. An old `on` notification arriving after `off` must not restart warnings or disconnect players; Task 7 tests this.

---

### Task 1: Persist and serialize maintenance transitions

**Files:**
- Modify: `src/Shared/Avalon.Domain/Auth/World.cs`
- Create: `src/Server/Avalon.Database.Auth/Repositories/WorldMaintenanceRepository.cs`
- Modify: `src/Server/Avalon.Database.Auth/Extensions/ServiceExtensions.cs`
- Create: `src/Server/Avalon.Database.Auth/Migrations/` EF-generated `AddWorldMaintenance` migration and update the model snapshot
- Test: `tests/Avalon.Api.UnitTests/Services/WorldMaintenanceRepositoryShould.cs`

**Interfaces:** Produce `WorldMaintenanceState(bool Enabled, long Revision, DateTime? DeadlineUtc)` and `IWorldMaintenanceRepository.ReadAsync(WorldId id, CancellationToken ct): Task<WorldMaintenanceState?>`, `TransitionAsync(WorldId id, bool enabled, TimeSpan grace, DateTime nowUtc, CancellationToken ct): Task<WorldMaintenanceState?>`. Later tasks use these exact contracts. A missing ID returns null.

- [ ] **Step 1: Write failing repository tests.** Assert `on` at 12:00 with default grace returns revision 1/deadline 12:05; repeated and concurrent `on` keep that deadline/revision; `off` clears it and increments once; missing ID returns null. Assert migration maps old `Maintenance` to enabled with a five-minute deadline, old `Online`/`Offline` to open.
- [ ] **Step 2: Run the focused test.** `dotnet test tests/Avalon.Api.UnitTests --filter FullyQualifiedName~WorldMaintenanceRepositoryShould`; expect failure for the missing state/repository.
- [ ] **Step 3: Implement the model, additive migration, and repository.** Generate `AddWorldMaintenance` with `dotnet ef migrations add AddWorldMaintenance --project src/Server/Avalon.Database.Auth --startup-project src/Server/Avalon.Api --context AuthDbContext`. Use a conditional revision update and retry on conflict; do not load a tracked `World` then overwrite concurrent writes. Keep the legacy `Status` column temporarily so existing API/Auth code still builds; Task 4 removes it after both readers change. Set `UpdatedAt` only on real transitions. Register the repository with `AddAuthDatabase()`.
- [ ] **Step 4: Run focused tests and migration/model checks.** The focused test passes; `dotnet build Avalon.sln --no-restore` passes.
- [ ] **Step 5: Commit.** `feat(world): persist maintenance intent and revisions`.

### Task 2: Publish readiness and derive runtime status

**Files:**
- Modify: `src/Server/Avalon.Infrastructure/CacheKeys.cs`
- Create: `src/Server/Avalon.Infrastructure/WorldReadiness.cs`
- Create: `src/Server/Avalon.Server.World/Presence/WorldReadyHeartbeatService.cs`
- Modify: `src/Server/Avalon.Server.World/Program.cs`
- Modify: `src/Server/Avalon.World/WorldServer.cs` (from #665)
- Test: `tests/Avalon.Server.World.UnitTests/Presence/WorldReadyHeartbeatServiceShould.cs`
- Test: `tests/Avalon.Server.Auth.UnitTests/Services/WorldReadinessShould.cs`

**Interfaces:** Produce `IWorldReadiness.IsReadyAsync(ushort worldId, CancellationToken ct): Task<bool>` and `WorldStatus Resolve(WorldMaintenanceState state, bool ready)` in `WorldReadiness`. The heartbeat service reads `WorldServer.IsListening` and a monotonic completed-tick counter; it never writes Redis before both are true.

- [ ] **Step 1: Write failing tests.** Assert no heartbeat during load or before listening; heartbeat refreshed at one-second intervals with five-second TTL while ticks advance; stalled ticks stop renewal; graceful stop removes the key; an expired/missing or failed Redis read resolves to offline. Assert maintenance wins over readiness in `Resolve`.
- [ ] **Step 2: Run the focused tests.** `dotnet test tests/Avalon.Server.World.UnitTests --filter FullyQualifiedName~WorldReadyHeartbeatServiceShould` and the auth readiness test; expect failure for missing types.
- [ ] **Step 3: Implement readiness key, reader, and publisher.** Expose listener-open and completed-tick state from the #665 world server; use `TimeProvider` and the existing cache abstraction. Register the publisher after `WorldServer` in the world host. Keep the publisher off the simulation tick.
- [ ] **Step 4: Run both focused test classes and `dotnet build Avalon.sln --no-restore`.** Expect pass.
- [ ] **Step 5: Commit.** `feat(world): report live world readiness`.

### Task 3: Share control service and expose the Admin API

**Files:**
- Create: `src/Server/Avalon.Infrastructure/WorldMaintenance/WorldMaintenanceControl.cs`
- Modify: `src/Server/Avalon.Infrastructure/CacheKeys.cs`
- Modify: `src/Server/Avalon.Infrastructure/Extensions/ServiceExtensions.cs`
- Modify: `src/Server/Avalon.Api/ServiceRegistration.cs`
- Modify: `src/Server/Avalon.Api/Controllers/WorldController.cs`
- Create: `src/Server/Avalon.Api/Contract/WorldMaintenanceRequest.cs`
- Create: `src/Server/Avalon.Api/Contract/WorldMaintenanceDto.cs`
- Modify: `src/Server/Avalon.Api/Services/WorldService.cs`
- Modify: `src/Server/Avalon.Api/Contract/CreateWorldRequest.cs`, `UpdateWorldRequest.cs`, and `WorldDto.cs`
- Test: `tests/Avalon.Api.UnitTests/Controllers/WorldControllerShould.cs`
- Test: `tests/Avalon.Api.UnitTests/Services/WorldMaintenanceControlShould.cs`

**Interfaces:** Produce `IWorldMaintenanceControl.SetAsync(WorldId id, bool enabled, TimeSpan grace, string actor, CancellationToken ct): Task<WorldMaintenanceState?>`; it calls Task 1's repository and publishes `world:{id}:maintenance` with the committed revision. Add `GET/POST/DELETE /world/{id}/maintenance`; `POST` accepts `GraceMinutes` 1–60, default 5. `WorldMaintenanceDto` exposes `Enabled`, `Revision`, `DeadlineUtc`, and `Ready`.

- [ ] **Step 1: Write failing tests.** Admin may read/enable/disable; Player and GameMaster cannot; invalid grace 0/61 is rejected; missing world returns 404; a repeated `on` keeps the deadline; publish failure after commit returns the committed state and logs the delivery failure. `WorldDto.Status` is derived, `Ready` is separate from existing `Available`, create/update no longer accept writable status, and `SortBy=status` sorts the complete visible set before paging.
- [ ] **Step 2: Run focused API tests.** `dotnet test tests/Avalon.Api.UnitTests --filter "FullyQualifiedName~WorldControllerShould|FullyQualifiedName~WorldMaintenanceControlShould"`; expect failures for missing actions/contracts.
- [ ] **Step 3: Implement control service and API.** Keep world metadata edits in `WorldService`; call the shared control service for transitions and log actor/world/revision. Register the service through a shared extension so Task 8 can use it in the world host. Use Task 2's readiness reader for status and detail. For `SortBy=status`, fetch the complete access-filtered set, derive statuses, sort, then paginate with the original total count; other sorts keep their existing database path. Remove writable status from API requests; leave the legacy database column until Task 4.
- [ ] **Step 4: Run focused tests and API build.** `dotnet test tests/Avalon.Api.UnitTests --no-restore`; expect pass.
- [ ] **Step 5: Commit.** `feat(api): control and display world maintenance`.

### Task 4: Gate auth selection and publish derived status

**Files:**
- Modify: `src/Shared/Avalon.Network.Packets/Auth/SWorldSelectPacket.cs`
- Modify: `src/Server/Avalon.Server.Auth/Handlers/CWorldListHandler.cs`
- Modify: `src/Server/Avalon.Server.Auth/Handlers/CWorldSelectHandler.cs`
- Modify: `src/Shared/Avalon.Domain/Auth/World.cs`
- Modify: `src/Server/Avalon.Api/Contract/WorldPaginateFilters.cs`
- Modify: `src/Server/Avalon.Database.Auth/AuthDbContext.cs`
- Create: `src/Server/Avalon.Database.Auth/Migrations/` EF-generated `RemoveStoredWorldStatus` migration and update the model snapshot
- Test: `tests/Avalon.Server.Auth.UnitTests/Handlers/CWorldListHandlerShould.cs`
- Test: `tests/Avalon.Server.Auth.UnitTests/Handlers/CWorldSelectHandlerShould.cs`

**Interfaces:** Append `WorldSelectResult.Maintenance = 3`. Consume Tasks 1–2's maintenance row and readiness reader. The selection gate runs after account/world access checks and before `AccountInWorld` reservation.

- [ ] **Step 1: Write failing tests.** Visible maintenance world gives non-Admin `Maintenance` with no slot/key; Admin gets a key when ready; offline world gives `WorldUnavailable` to all; hidden or unknown world gives `WorldUnavailable` even if maintenance is set; world list carries derived status; failed readiness read fails closed.
- [ ] **Step 2: Run both focused handler tests.** Expect failures for the new result and gate.
- [ ] **Step 3: Implement status derivation and selection gate.** Preserve the current select budget and access check ordering. Append only the packet enum value; retain all old numeric values. Generate `RemoveStoredWorldStatus` using the same `dotnet ef migrations add` arguments as Task 1 with its new name. Remove the legacy mapped status, its seed values, and the SQL status sort selector after API and Auth use derived status; Task 3's in-memory status-sort path preserves the public behavior.
- [ ] **Step 4: Run `dotnet test tests/Avalon.Server.Auth.UnitTests --no-restore`.** Expect pass.
- [ ] **Step 5: Commit.** `feat(auth): gate world selection during maintenance`.

### Task 5: Enforce world key exchange

**Files:**
- Modify: `src/Shared/Avalon.Network.Packets/Generic/SDisconnectPacket.cs`
- Modify: `src/Server/Avalon.Server.World/Handlers/ExchangeWorldKeyHandler.cs`
- Test: `tests/Avalon.Server.World.UnitTests/Handlers/ExchangeWorldKeyHandlerShould.cs`

**Interfaces:** Append `DisconnectReason.Maintenance` after existing values. The handler consumes Task 1's `IWorldMaintenanceRepository` and returns a maintenance disconnect before setting `AccountId` for a non-Admin key. Preserve current account status, credentials-version, and world-access checks.

- [ ] **Step 1: Write failing tests.** A key issued just before maintenance is refused at exchange; Admin key succeeds; database failure refuses entry; no account/session state becomes accepted on refusal; the disconnect is sent before close.
- [ ] **Step 2: Run `dotnet test tests/Avalon.Server.World.UnitTests --filter FullyQualifiedName~ExchangeWorldKeyHandlerShould`.** Expect failure.
- [ ] **Step 3: Implement the authoritative exchange gate.** Keep the existing one-use key semantics and current security checks. Use `GracefulShutdownHelper` for the maintenance refusal.
- [ ] **Step 4: Run the focused tests.** Expect pass.
- [ ] **Step 5: Commit.** `feat(world): reject maintenance entries at key exchange`.

### Task 6: Gate character selection and pending spawn

**Files:**
- Create: `src/Server/Avalon.World/Maintenance/WorldEntryGate.cs`
- Modify: `src/Server/Avalon.World/Handlers/CharacterSelectHandler.cs`
- Modify: `src/Server/Avalon.World/Handlers/CharacterLoadedHandler.cs`
- Modify: `src/Server/Avalon.World/Characters/CharacterReadinessBarrier.cs`
- Modify: `src/Server/Avalon.World/WorldServer.cs`
- Test: `tests/Avalon.Server.World.UnitTests/Characters/CharacterSelectChainShould.cs`
- Test: `tests/Avalon.Server.World.UnitTests/Characters/WorldServerBarrierTickShould.cs`
- Test: `tests/Avalon.Server.World.UnitTests/Handlers/CharacterLeaveHandlerShould.cs`

**Interfaces:** Produce `IWorldEntryGate.CheckAsync(AccountId accountId, CancellationToken ct): Task<bool>`; false includes maintenance or an unreadable authoritative state. Consume Task 1's repository. Never call it synchronously from a simulation tick: enqueue its task as a connection continuation, then resume the select or release on the tick only after success. Track one pending check per select/release so a timeout pass cannot start a database read every tick.

- [ ] **Step 1: Write failing tests.** Refuse select after #663 leave and before `BeginSelect`; a select begun just before maintenance cannot complete a pending spawn; client-loaded and timeout releases both honor the final gate; Admin bypasses; a failed read refuses/cleans up; a failed or cancelled pending select leaves no orphan online character.
- [ ] **Step 2: Run the three focused world test classes.** Expect failure for the missing gate.
- [ ] **Step 3: Implement asynchronous checks at select and both release paths.** Keep the existing select identity (`OwnsSelect`) and leave-in-progress rules. Reuse normal cleanup and maintenance disconnect; no database round trip on the tick thread.
- [ ] **Step 4: Run `dotnet test tests/Avalon.Server.World.UnitTests --no-restore`.** Expect pass.
- [ ] **Step 5: Commit.** `feat(world): guard character entry during maintenance`.

### Task 7: Reconcile maintenance and drain existing players

**Files:**
- Create: `src/Server/Avalon.World/Maintenance/WorldMaintenanceCoordinator.cs`
- Modify: `src/Server/Avalon.World/WorldServer.cs`
- Modify: `src/Server/Avalon.Server.World/Program.cs`
- Modify: `src/Server/Avalon.Server.World/Extensions/ServiceExtensions.cs`
- Test: `tests/Avalon.Server.World.UnitTests/Maintenance/WorldMaintenanceCoordinatorShould.cs`

**Interfaces:** Produce `InitializeAsync(CancellationToken ct)`, `ApplyNotificationAsync(long revision, CancellationToken ct)`, `ReconcileAsync(CancellationToken ct)`, `ApplyCommitted(WorldMaintenanceState state)`, `Advance(DateTime nowUtc, IReadOnlyList<IWorldConnection> connections)`, and `WhenDrainedAsync(CancellationToken ct)` on `WorldMaintenanceCoordinator`. The first three and final method perform I/O/waiting off tick; `ApplyCommitted` applies a newer committed revision locally; `Advance` only reads applied state, enqueues chat/disconnect packets, and tracks close tasks. World startup initializes before `StartListening()` from #665; a background loop reconciles every five seconds.

- [ ] **Step 1: Write failing tests.** Start warnings at enable; announce 3m/1m/30s/10..0 exactly once; skip stale thresholds after a delayed tick; cancel on newer `off`; ignore duplicate/older notifications; restart in maintenance without replaying countdown; late discovery past deadline disconnects; only non-Admins close, and zero chat precedes disconnect/save drain.
- [ ] **Step 2: Run `dotnet test tests/Avalon.Server.World.UnitTests --filter FullyQualifiedName~WorldMaintenanceCoordinatorShould`.** Expect failure.
- [ ] **Step 3: Implement coordinator and world wiring.** Subscribe to `world:{id}:maintenance`, reload the database row on notification, and reconcile every five seconds from a hosted background loop. Use committed UTC deadline, `TimeProvider`, and `System` chat packets; call the existing close/despawn path. Track closes and await drain off tick, including character saves. Do not stop the process or its listener for maintenance.
- [ ] **Step 4: Run focused tests and the world test project.** Expect pass.
- [ ] **Step 5: Commit.** `feat(world): announce maintenance and drain players`.

### Task 8: Add the in-game Admin command and finish documentation

**Files:**
- Create: `src/Server/Avalon.World/Chat/MaintenanceCommand.cs`
- Modify: `src/Server/Avalon.Server.World/Extensions/ServiceExtensions.cs`
- Modify: `docs/configuration-reference.md`
- Modify: `docs/character-login-flow.md`
- Test: `tests/Avalon.Server.World.UnitTests/Chat/MaintenanceCommandShould.cs`

**Interfaces:** `/maintenance on [minutes]`, `/maintenance off`, `/maintenance status`; consume Task 3's `IWorldMaintenanceControl` and Task 1's state. Require `AccountAccessLevel.Admin` exactly. Reply with persisted state, revision, and deadline; the coordinator receives the same committed transition without waiting for Redis.

- [ ] **Step 1: Write failing command tests.** Admin on/off/status works; GameMaster/Player/Console see the existing unknown-command response; invalid minutes are refused without a write; repeated on reports the original deadline; a database failure reports command failure and changes no local state.
- [ ] **Step 2: Run `dotnet test tests/Avalon.Server.World.UnitTests --filter FullyQualifiedName~MaintenanceCommandShould`.** Expect failure.
- [ ] **Step 3: Implement command and documentation.** Register the command; document API, status meaning, heartbeat TTL, five-minute default, and countdown. The command applies the committed state to the local coordinator after the write.
- [ ] **Step 4: Run the full solution build and relevant test projects.** `dotnet build Avalon.sln --no-restore`; `dotnet test tests/Avalon.Api.UnitTests --no-build`; `dotnet test tests/Avalon.Server.Auth.UnitTests --no-build`; `dotnet test tests/Avalon.Server.World.UnitTests --no-build`. Expect pass.
- [ ] **Step 5: Commit.** `feat(world): add Admin maintenance command`.

## Execution Handoff

The packet/schema additions require the client to re-vendor the shared contracts and display maintenance selection/disconnect reasons. That client work is a separate repository handoff after the server behavior and tests pass. Check `git diff --check` and the final worktree state before branch review.
