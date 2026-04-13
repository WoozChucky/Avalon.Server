# Avalon.Server — Implementation Tracker

This document catalogues every actionable `TODO` comment in the project source code (vendor/DotRecast items are excluded).  
Each entry describes its **context**, **implementation details**, **behaviour & requirements**, and **tests** required.

> **Status legend:** 🔴 Not started · 🟡 In progress · 🟢 Done

---

## Summary Table

| ID        | Status | Area                    | File                                                                                       | Short description                                     |
|-----------|--------|-------------------------|--------------------------------------------------------------------------------------------|-------------------------------------------------------|
| TODO-007  | 🔴     | Security                | `src/Server/Avalon.Api/Authentication/AV/AvalonAuthenticationHandler.cs:34`               | Validate the `Avalon` bearer token                    |
| TODO-017  | 🔴     | Spell System            | `src/Shared/Avalon.Domain/World/SpellTemplate.cs` + `SpellMetadata.cs`                    | Add `AnimationId` to spell template and metadata      |
| TODO-018  | 🔴     | Spell System            | `src/Server/Avalon.World/Instances/MapInstance.cs`                                         | Use spell `AnimationId` instead of hardcoded `1`      |
| TODO-019  | 🔴     | Spell System            | `src/Server/Avalon.World/Scripts/Creatures/CreatureCombatScript.cs:173`                    | Creature combat script — spell support                |
| TODO-020  | 🔴     | Spell System            | `src/Server/Avalon.World/Handlers/CharacterAttackHandler.cs:71`                            | AoE attack support (target-less spells)               |
| TODO-024  | 🔴     | Character System        | `src/Server/Avalon.World/Handlers/CharacterSelectHandler.cs:175`                           | Send inventory to client on login                     |
| TODO-025  | 🔴     | Character System        | `src/Server/Avalon.World/Handlers/CharacterMovementHandler.cs:44`                          | Server-side collision / navmesh validation            |
| TODO-027  | 🔴     | World Architecture      | `src/Server/Avalon.World/World.cs:183`                                                     | Replace unnamed world timer magic integers            |
| TODO-029  | 🔴     | World Architecture      | `src/Server/Avalon.Server.World/Extensions/ServiceExtensions.cs:33`                        | Decouple World server from Auth database              |
| TODO-030  | 🔴     | Domain Model            | `src/Shared/Avalon.Domain/Characters/CharacterSpell.cs:16`                                 | `CharacterSpell` specializations design               |
| TODO-031  | 🔴     | Infrastructure          | `src/Shared/Avalon.Metrics/FakeMetricsManager.cs:42`                                       | Complete `IDisposable` implementation                 |

---

## Security

> See also: [docs/security-session-management.md](security-session-management.md)

---

### TODO-007 — Validate the `Avalon` bearer token 🔴

**File:** `src/Server/Avalon.Api/Authentication/AV/AvalonAuthenticationHandler.cs:34`

**Context**  
The handler extracts the token from the `Authorization: Avalon <token>` header but immediately constructs a hardcoded `ClaimsPrincipal` with a test claim `("test", "value")`. Any non-empty string is accepted as valid.

**Implementation Details**  
1. **Determine token type**: If this handler is for internal admin operations (separate from the JWT bearer path), validate against a configured shared secret (`AvalonAuthenticationSchemeOptions.SharedSecret`). Add `SharedSecret` (string) to `AvalonAuthenticationSchemeOptions`, populated from `IConfiguration["Avalon:SharedSecret"]` (environment variable / secrets manager in production).
2. Use constant-time comparison (`CryptographicOperations.FixedTimeEquals`) to compare the provided token against the configured secret.
3. Extract meaningful claims from the token if it is a structured token (e.g. signed JWT), or set a static admin claim if it is an opaque key.
4. Remove the hardcoded `("test", "value")` claim entirely.

**Behaviour & Requirements**
- Invalid/missing token → `AuthenticateResult.Fail(...)`.
- Valid token → `AuthenticateResult.Success(ticket)` with real claims.
- Shared secret must never be hardcoded in source; must come from configuration/secrets.
- Timing-safe comparison to prevent timing attacks.

**Tests**
- Valid shared secret → success with expected claims.
- Wrong token → fail.
- Empty token → fail.
- Timing-safe: assert `FixedTimeEquals` is used (check via static analysis or inspect method body in tests).

---

## Spell System

> See also: [docs/spell-system.md](spell-system.md)

---

### TODO-017 — Add `AnimationId` to `SpellTemplate` and `SpellMetadata` 🔴

**File:** `src/Shared/Avalon.Domain/World/SpellTemplate.cs` + `src/Server/Avalon.World.Public/Spells/SpellMetadata.cs`

**Context**  
`MapInstance.BroadcastUnitAttackAnimation` and `MapInstance.BroadcastFinishCastAnimation` hardcode animation ID `1` because neither `SpellTemplate` nor `SpellMetadata` carries an animation identifier.

**Implementation Details**  
1. Add `uint AnimationId { get; set; }` to `SpellTemplate` (persisted column, default `1`).
2. Add `uint AnimationId { get; init; }` to `SpellMetadata`.
3. Update `SpellMetadata.Clone()` to include `AnimationId`.
4. Update the code path that maps `SpellTemplate → SpellMetadata` (inside world data loading) to copy `AnimationId`.
5. Create a database migration: `AddAnimationIdToSpellTemplate` in `Avalon.Database.World`.

**Behaviour & Requirements**
- Each spell in the DB can specify its animation ID.
- Default `1` maintains backward compatibility for existing records.
- Animation ID `0` is treated as "no animation" by the client.

**Tests**
- `SpellMetadata.Clone()` preserves `AnimationId`.
- DB migration adds column with default `1` without data loss (migration test or script review).
- World data load correctly maps `SpellTemplate.AnimationId` → `SpellMetadata.AnimationId`.

---

### TODO-018 — Use `AnimationId` from spell in instance broadcasts 🔴

**File:** `src/Server/Avalon.World/Instances/MapInstance.cs`

**Depends on:** TODO-017

**Implementation Details**  
In `MapInstance.BroadcastUnitAttackAnimation`:
```csharp
// Before:
SUnitAttackAnimationPacket.Create(attacker.Guid, 1, ...)
// After:
SUnitAttackAnimationPacket.Create(attacker.Guid, spell?.Metadata.AnimationId ?? 1u, ...)
```
Same pattern for `BroadcastFinishCastAnimation`.

**Tests**
- Spell with `AnimationId = 5` → packet carries `5`.
- Null spell fallback → packet carries `1`.

---

### TODO-019 — Creature combat script — spell support 🔴

**File:** `src/Server/Avalon.World/Scripts/Creatures/CreatureCombatScript.cs:173`

**Context**  
`Creature.SendAttackAnimation(null)` hardcodes melee for all creatures. Creatures cannot use spells.

**Implementation Details**  
1. Add `IReadOnlyList<SpellId> SpellIds { get; set; }` to `ICreatureMetadata`.
2. In `CreatureCombatScript`, build a local list of `ISpell` instances from the metadata at script initialisation.
3. In `AttackTarget`, before the melee path: check if any spell is off cooldown and within range. If yes, call `Chunk.QueueSpell(Creature, target, spell)` and update cooldowns.
4. Fall back to melee if no spell is available.

**Behaviour & Requirements**
- Creatures with spells in their template can cast them during combat.
- Spell cooldown is tracked per spell instance on the creature.
- Creature cannot cast while moving (unless the spell has `CastTime == 0`).
- Melee attack is used when no spell is available or ready.

**Tests**
- Creature with a spell in template → `context.QueueSpell` called on attack cycle when spell is off cooldown.
- On cooldown → melee path taken.
- Moving creature with non-instant spell → spell not cast.

---

### TODO-020 — AoE attack support (target-less spells) 🔴

**File:** `src/Server/Avalon.World/Handlers/CharacterAttackHandler.cs:71`

**Context**  
`CharacterAttackHandler` requires a non-null target from `packet.Target`. Spells with `SpellEffect.AoE` semantics have no single target.

**Implementation Details**  
1. Before the target lookup, resolve the spell from the character's spell list using `packet.SpellId` (if the packet carries it) or from the auto-attack context.
2. Check `spell.Metadata.Effects.HasFlag(SpellEffect.AoE)`.
3. If AoE: skip the `GetTarget` call, pass `target = null` to `context.QueueSpell`.
4. If not AoE and `target == null`: return early with an error log (existing behaviour).
5. AoE `SpellScript` is responsible for finding targets within range in `Update()`.

**Behaviour & Requirements**
- AoE spells do not require a `Target` in the attack packet.
- Non-AoE spells still require a valid target.
- AoE script determines radius and affected units internally.

**Tests**
- AoE spell, null target → `QueueSpell` called with `target = null`.
- Single-target spell, null target → early return, spell not queued.
- Single-target spell, valid target → existing behaviour unchanged.

---

## Character System

> See also: [docs/character-login-flow.md](character-login-flow.md)

---

### TODO-024 — Send inventory to client on login 🔴

**File:** `src/Server/Avalon.World/Handlers/CharacterSelectHandler.cs:175`

**Context**  
`OnInventoryReceived` loads equipment, bag, and bank items into the entity's inventory containers but does not send them to the client. The client has no data about any equipped/carried items.

**Implementation Details**  
1. Define `SInventoryPacket` in `Avalon.Network.Packets` (if not already existing) carrying a list of `ItemSlot { Container, Slot, ItemId, Quantity, Durability }`.
2. After `entity[InventoryType.*].Load(...)`, collect all loaded items from equipment and bag.
3. Call `connection.Send(SInventoryPacket.Create(items, connection.CryptoSession.Encrypt))`.
4. Bank items may be deferred to a separate "open bank" interaction — do not include them in the login packet unless the client expects it.

**Behaviour & Requirements**
- Client receives all equipment and bag items on character login.
- Empty inventory → packet with 0 items (not skipped).
- Bank items are not sent on login (sent on bank-open interaction).
- Packet format matches client-side deserialization expectation.

**Tests**
- Character with 2 equipment + 3 bag items → `SInventoryPacket` carries 5 items with correct slots.
- Empty inventory → packet sent with 0 items.
- Bank items not included in login packet.

---

### TODO-025 — Server-side collision / navmesh movement validation 🔴

**File:** `src/Server/Avalon.World/Handlers/CharacterMovementHandler.cs:44`

**Context**  
The server logs a desync warning when client-reported distance diverges from server-interpolated distance, but always trusts the client position. This allows wall-walking and speed hacks.

**Implementation Details**  
1. After computing `interpolatedPosition`, raycast from `connection.Character.Position` to `clientSentPosition` using `IMapNavigator.HasVisibility` (or DotRecast's `DtNavMeshQuery.Raycast` directly).
2. If the raycast hits geometry (blocked path): reject the client position, send a `SPositionCorrectionPacket` with the last valid server position, and update `connection.Character.Position` to the server value.
3. If `differenceDistances >= MaxDistanceDiffCheck` (speed hack): log the anti-cheat event; apply same correction.
4. Keep a consecutive-rejection counter per connection; after N rejections, flag or disconnect.

**Behaviour & Requirements**
- Movement within navigable navmesh → client position accepted.
- Movement through walls → server-corrected position broadcast back to client.
- Speed hack detection → correction + anti-cheat log.
- Server position is authoritative; it is what other players see.

**Tests**
- Valid navmesh movement → character position updated to client value.
- Position across a wall → `SPositionCorrectionPacket` sent, character stays at last valid position.
- Excessive distance diff → warning logged.

---

## World Architecture

> See also: [docs/architecture-decisions.md](architecture-decisions.md)

---

### TODO-027 — Named world timer constants 🔴

**File:** `src/Server/Avalon.World/World.cs:183`

**Context**  
`WorldTimersCount = 5` defines 5 timers; only `HotReloadTimer = 0` is named. Timers 1-4 are accessed by unnamed integers (or simply unused).

**Implementation Details**  
1. Audit all `_timers[N]` accesses in `World.cs` to identify what each index represents.
2. For each used timer, add a named constant: `private const ushort SaveTimer = 1`, etc.
3. If timers are genuinely unused, reduce `WorldTimersCount` to the actual count used.
4. Wherever `_timers[N]` appears with a numeric literal, replace with the named constant.

**Behaviour & Requirements**
- No numeric literal timer-index access remains in `World.cs`.
- `WorldTimersCount` exactly matches the number of distinct timers.

**Tests**
- Static analysis / code review (no runtime test needed).
- Existing hot-reload behaviour continues working after rename.

---

### TODO-029 — Decouple World server from Auth database 🔴

**File:** `src/Server/Avalon.Server.World/Extensions/ServiceExtensions.cs:33`

**Context**  
`AddAuthDatabase()` registers `AuthDbContext` and `IAccountRepository` inside the World server's DI container. The World server should be independent of authentication infrastructure.

**Implementation Details**  
1. Identify all usages of `IAccountRepository` within the World project scope (likely `CharacterSelectHandler` checking account online status).
2. Replace the direct DB call with a Redis-backed `IAccountSessionService` that reads/writes `account:{id}:online` keys. Auth server sets these keys; World server only reads them.
3. Move any remaining account-state mutations (e.g. setting `account.Online = false` on disconnect) to a pub/sub event that the Auth server handles.
4. Remove `AddAuthDatabase()` from `ServiceExtensions.cs` in the World project.
5. Verify the World starts cleanly without `AuthDbContext` registered.

**Behaviour & Requirements**
- World server has no direct EF Core dependency on `AuthDbContext`.
- Account online status is communicated via Redis keys/events (partial architecture already uses pub/sub for `world:accounts:disconnect`).
- No regression in authentication flow.

**Tests**
- Integration test: World host starts without `AuthDbContext` in its service collection.
- `IAccountSessionService` correctly reads Redis key set by Auth server.

---

## Domain Model

---

### TODO-030 — `CharacterSpell` specializations design 🔴

**File:** `src/Shared/Avalon.Domain/Characters/CharacterSpell.cs:16`

**Context**  
The `// TODO: Specializations?` comment raises an open design question about whether a spell learned by a character can belong to a talent tree or specialization path that modifies its behaviour.

**Implementation Details (Design Decision)**  
Recommended approach:
1. Add `SpecializationPath? Specialization` (nullable) to `CharacterSpell`. `SpecializationPath` is an enum or int FK to a future `SpecializationNode` table.
2. When a character's spell is loaded at login, `SpellScript` selection passes the specialization path to the script manager, which may return a variant script.
3. Spells without a specialization use the default script (unchanged behaviour).

**Behaviour & Requirements**
- `CharacterSpell` optionally carries a specialization path; null means "default".
- Specialization path influences which `SpellScript` variant is selected.
- No breaking change to characters without a specialization.

**Tests**
- `CharacterSpell` with `Specialization = null` → default script selected.
- `CharacterSpell` with `Specialization = SpecializationPath.FireMastery` → variant script selected (if registered).

---

## Infrastructure

---

### TODO-031 — Complete `FakeMetricsManager.Dispose` 🔴

**File:** `src/Shared/Avalon.Metrics/FakeMetricsManager.cs:42`

**Context**  
The `Dispose(bool disposing)` pattern has a `// TODO release managed resources here` comment but no implementation. `FakeMetricsManager` is a no-op stub, but leaving an incomplete `Dispose` is a code smell and may hide future resource leaks if the class gains state.

**Implementation Details**  
1. Confirm `FakeMetricsManager` holds no unmanaged or managed disposable resources (audit fields).
2. Replace the `// TODO` comment with either actual resource release code or an explicit `// No managed resources.` comment.
3. Add a `bool _disposed` guard and throw `ObjectDisposedException` on subsequent productive method calls if desired.

**Behaviour & Requirements**
- `Dispose()` is safe to call multiple times (idempotent).
- No `ObjectDisposedException` on the first call.

**Tests**
- `Dispose()` called twice → no exception thrown.
- After `Dispose()`, all public methods remain callable (fake/no-op — no need to enforce disposed state check unless explicitly desired).

---

## Implementation Sequencing

The following order minimises blockers:

```
Phase 1 — Security & Infrastructure
  TODO-007  Token validation                    [Security]
  TODO-031  FakeMetricsManager Dispose          [Infra, trivial]

Phase 2 — Spell & Domain data model
  TODO-017  SpellTemplate AnimationId           [unblocks 018, 019]
  TODO-030  CharacterSpell specializations      [Domain design]

Phase 3 — Spell & Creature runtime
  TODO-018  Instance animation id               [needs 017]
  TODO-019  Creature spell support              [needs 017]
  TODO-020  AoE targeting                       [Spell]

Phase 4 — Character system
  TODO-024  Send inventory                      [Character]

Phase 5 — Architecture & validation
  TODO-025  Server-side collision               [Character, complex]
  TODO-027  Timer constants                     [World]
  TODO-029  Decouple World from Auth DB         [Architecture, complex]
```
