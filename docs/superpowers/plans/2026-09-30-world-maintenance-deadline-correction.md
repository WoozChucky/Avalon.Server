# World Maintenance Deadline Correction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep a world Online and open to non-Admins during a scheduled maintenance countdown, then disconnect and refuse them at the stored deadline until maintenance is disabled.

**Architecture:** Retain the existing persisted intent, revision, heartbeat, broadcasts, and Admin controls. Put the deadline decision in `WorldMaintenanceState` and use it consistently for display and authoritative admission. At the cutoff, the simulation tick marks authenticated non-Admin connections as blocked before either packet-processing pass and starts their graceful disconnect.

**Tech Stack:** .NET 10, C#, EF Core/PostgreSQL, Redis, protobuf-net, xUnit, NSubstitute.

**Spec:** `docs/superpowers/specs/2026-09-29-world-maintenance-admission-design.md`

## Global Constraints

- Work only in `Avalon.Server`, on the existing `feat/world-maintenance` branch and draft PR #671. Add commits; do not discard or rewrite the published branch.
- The default countdown is five minutes; `/maintenance on 10` admits non-Admins for ten minutes and closes them at its persisted UTC deadline. Overrides remain whole minutes 1–60.
- Keep the TCP listener open to identify Admins. Only the Admin flag bypasses the cutoff; unknown or restricted worlds remain indistinguishable.
- Preserve the existing warning schedule, normal close/despawn/save path, #663 same-connection leave, #665 startup ordering, and appended packet enum values.
- Database reads stay off the 60 Hz simulation tick. Missing or unreadable authoritative state fails closed for new admission. Missed notifications are repaired by the existing five-second reconciliation.
- The native client repository is outside this work. The server PR documents the response codes as a handoff.

## Review Focus

1. A player whose key exchange begins before the deadline but completes afterward must be refused; Task 2 tests this.
2. A pending-spawn database read may complete before the deadline but its tick continuation run afterward; Task 2 tests this.
3. A gameplay packet queued before zero must not execute after zero in either session or map pass; Task 3 tests this.
4. A newly accepted unauthenticated socket after zero must remain long enough to identify an Admin; Task 3 tests this.
5. A world that restarts during the countdown must admit until the original deadline and show Online while ready; Tasks 1 and 3 test this.

---

### Task 1: Make the stored deadline drive public status and Auth selection

**Files:**
- Modify: `src/Shared/Avalon.Domain/Auth/WorldMaintenanceState.cs`
- Modify: `src/Server/Avalon.Infrastructure/WorldReadiness.cs`
- Modify: `src/Server/Avalon.Server.Auth/Handlers/CWorldListHandler.cs`
- Modify: `src/Server/Avalon.Server.Auth/Handlers/CWorldSelectHandler.cs`
- Modify: `src/Server/Avalon.Api/Services/WorldService.cs`
- Test: `tests/Avalon.Server.Auth.UnitTests/Services/WorldReadinessShould.cs`
- Test: `tests/Avalon.Server.Auth.UnitTests/Handlers/CWorldListHandlerShould.cs`
- Test: `tests/Avalon.Server.Auth.UnitTests/Handlers/CWorldSelectHandlerShould.cs`
- Test: `tests/Avalon.Api.UnitTests/Services/WorldListFlagsShould.cs`

**Interfaces:** Produce `WorldMaintenanceState.IsCutoffActive(DateTime nowUtc): bool` (`Enabled && (DeadlineUtc is null || nowUtc >= DeadlineUtc)`; an enabled row missing its deadline fails closed). Change `WorldReadiness.Resolve(WorldMaintenanceState state, bool ready, DateTime nowUtc): WorldStatus`: active cutoff => Maintenance, else ready => Online, else Offline. Auth list and API status callers use an injected `TimeProvider` (optional constructor parameter for existing tests where needed); Auth selection already has one.

- [ ] **Step 1: Write failing tests.** At 12:00 with deadline 12:10, ready worlds show Online and a Player selects successfully; at exactly 12:10 and afterward status is Maintenance and selection refuses before slot/key creation; an unready world stays Offline during countdown and answers WorldUnavailable; Admin bypasses after zero. A missing deadline on an enabled row fails closed.
- [ ] **Step 2: Run focused tests.** `dotnet test tests/Avalon.Server.Auth.UnitTests --no-restore --filter "FullyQualifiedName~WorldReadinessShould|FullyQualifiedName~CWorldListHandlerShould|FullyQualifiedName~CWorldSelectHandlerShould"` and the API world-list test; expect red for countdown behavior.
- [ ] **Step 3: Implement deadline status/admission.** Use the same UTC now value for each list response; preserve visibility and readiness precedence. Keep the existing complete-visible-set status sort before pagination.
- [ ] **Step 4: Run focused tests and `dotnet test tests/Avalon.Api.UnitTests --no-restore`.** Expect green.
- [ ] **Step 5: Commit.** `fix(world): show scheduled worlds online until cutoff`.

### Task 2: Admit world sessions and pending spawns throughout the countdown

**Files:**
- Modify: `src/Server/Avalon.Server.World/Handlers/ExchangeWorldKeyHandler.cs`
- Modify: `src/Server/Avalon.World/Maintenance/WorldEntryGate.cs`
- Modify: `src/Server/Avalon.World/Maintenance/WorldMaintenanceCoordinator.cs`
- Modify: `src/Server/Avalon.World/Characters/CharacterReadinessBarrier.cs`
- Modify: `src/Server/Avalon.World/Handlers/CharacterSelectHandler.cs`
- Test: `tests/Avalon.Server.World.UnitTests/Handlers/ExchangeWorldKeyHandlerShould.cs`
- Test: `tests/Avalon.Server.World.UnitTests/Maintenance/WorldEntryGateShould.cs`
- Test: `tests/Avalon.Server.World.UnitTests/Characters/CharacterSelectChainShould.cs`
- Test: `tests/Avalon.Server.World.UnitTests/Characters/WorldServerBarrierTickShould.cs`

**Interfaces:** Consume Task 1's `IsCutoffActive(nowUtc)`. Change `IWorldEntryGate.CheckAsync(AccountId accountId, CancellationToken ct)` to return `Task<WorldEntryDecision>` where `WorldEntryDecision(bool Allowed, DateTime ValidUntilUtc)` is valid only while `nowUtc < ValidUntilUtc`. For non-Admins, the gate sets `ValidUntilUtc` to the earlier of the stored maintenance deadline and five seconds after its authoritative read; a disabled row gets the five-second limit. Verified Admins use the five-second limit regardless of the deadline. This prevents an old open result from surviving a later remote enable's minimum one-minute grace even if its notification is missed. `WorldMaintenanceCoordinator.RunIfEntryAllowed` compares the applied deadline with its injected clock under its existing local transition lock. Exchange and entry gates use injected clocks off tick. An Admin-flag account remains exempt from the cutoff but still needs a fresh decision.

- [ ] **Step 1: Write failing tests.** Player key exchange, #663 reselect, and client-loaded/timeout pending spawn succeed during countdown. At exact deadline, each refuses with maintenance reason. Make an exchange repository read and a pending-spawn gate read complete before zero, then advance the clock before their continuations; both refuse. A formerly open result older than five seconds also refuses, even if the local coordinator missed a remote enable. Admin succeeds after zero; failed database/account reads still fail closed.
- [ ] **Step 2: Run the four focused world test classes.** Expect red for countdown or crossing-deadline cases.
- [ ] **Step 3: Implement deadline-aware world gates.** Preserve current one-use key and select identity rules. Keep database I/O on worker tasks; both select and final-spawn continuations reject an expired decision, and final spawn also checks the locally applied deadline. Do not add a cross-process database lock: a decision's five-second lifetime is shorter than the minimum one-minute scheduled grace.
- [ ] **Step 4: Run `dotnet test tests/Avalon.Server.World.UnitTests --no-restore`.** Expect green.
- [ ] **Step 5: Commit.** `fix(world): defer maintenance admission cutoff to deadline`.

### Task 3: Cut off non-Admin packets before tick processing

**Files:**
- Modify: `src/Server/Avalon.World/Maintenance/WorldMaintenanceCoordinator.cs`
- Modify: `src/Server/Avalon.World/WorldServer.cs`
- Modify: `src/Server/Avalon.World/WorldConnection.cs`
- Modify: `docs/character-login-flow.md`
- Modify: `docs/configuration-reference.md`
- Test: `tests/Avalon.Server.World.UnitTests/Maintenance/WorldMaintenanceCoordinatorShould.cs`
- Test: `tests/Avalon.Server.World.UnitTests/Characters/WorldServerBarrierTickShould.cs`
- Create: `tests/Avalon.Server.World.UnitTests/WorldConnection/WorldConnectionMaintenanceCutoffShould.cs`

**Interfaces:** `WorldConnection.BlockForMaintenance()` marks an authenticated non-Admin connection as unable to dispatch queued session/map packets, including those received before the cutoff. `WorldMaintenanceCoordinator.Advance` calls this when the deadline is reached, before graceful close; it skips unauthenticated sockets so key exchange can identify Admins. `WorldServer.Update` runs `Advance` before `UpdateSession` and before `_world.Update` calls `UpdateMap`.

- [ ] **Step 1: Write failing tests.** At 12:09:59.999, a Player session/map packet executes; at 12:10:00, a packet queued before zero is not dispatched in either pass and the zero-second chat precedes the disconnect packet. Admin packets still execute. An unauthenticated socket is not blocked before key exchange; a Player accepted after zero is refused at exchange and an Admin may enter. After restart before zero, the original deadline remains effective and admissions continue.
- [ ] **Step 2: Run focused coordinator, tick, and packet-dispatch tests.** Expect red for after-zero packet handling.
- [ ] **Step 3: Implement cutoff ordering and dispatch block.** Keep the listener open and existing close/despawn/save behavior. Set the block on authenticated non-Admins synchronously on tick; `WorldConnection.OnReceive` discards later packets, and both queue passes discard queued gameplay packets while blocked even if close is waiting on its outbox. Update documentation to distinguish scheduled countdown from active maintenance.
- [ ] **Step 4: Run the full solution tests and Release build.** `dotnet test Avalon.sln --no-restore -v quiet -clp:ErrorsOnly`; `dotnet build Avalon.sln -c Release --no-restore -v quiet -clp:ErrorsOnly`; expect zero failures/errors. Check `git diff --check`.
- [ ] **Step 5: Commit.** `fix(world): stop player packets at maintenance deadline`.

## Execution Handoff

Run one fresh whole-branch review after these three tasks, address important findings with regression tests, then update draft PR #671 and mark it ready only when the corrected behavior and full verification are green. Keep the worktree for PR feedback. Client UI changes remain a separate repository handoff.
