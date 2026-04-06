# Avalon.Server — Implementation Tracker

This document catalogues every actionable `TODO` comment in the project source code (vendor/DotRecast items are excluded).  
Each entry describes its **context**, **implementation details**, **behaviour & requirements**, and **tests** required.

> **Status legend:** 🔴 Not started · 🟡 In progress · 🟢 Done

---

## Summary Table

| ID        | Status | Area                    | File                                                                                       | Short description                                     |
|-----------|--------|-------------------------|--------------------------------------------------------------------------------------------|-------------------------------------------------------|
| TODO-001  | 🟢     | Networking / Shutdown   | `src/Shared/Avalon.Network.Tcp/AvalonTcpServer.cs:72`                                      | Close all open client connections on `StopAsync`      |
| TODO-002  | 🟢     | Networking / Shutdown   | `src/Shared/Avalon.Network.Tcp/AvalonTcpServer.cs:73`                                      | Send disconnect packet to all clients on stop         |
| TODO-003  | 🟢     | Networking / Shutdown   | `src/Server/Avalon.Server.Auth/AuthServer.cs:55`                                           | Send disconnect packet in Auth `OnStoppingAsync`      |
| TODO-004  | 🟢     | Networking / Shutdown   | `src/Server/Avalon.World/WorldServer.cs:164`                                               | Send disconnect packet in World `OnStoppingAsync`     |
| TODO-005  | 🟢     | Networking / Shutdown   | `src/Server/Avalon.World/WorldServer.cs:271`                                               | Notify in-game player before forced kick              |
| TODO-006  | 🟢     | Security                | `src/Server/Avalon.Server.Auth/Handlers/CWorldSelectHandler.cs:48`                         | Replace `System.Random` world key with CSPRNG         |
| TODO-007  | 🔴     | Security                | `src/Server/Avalon.Api/Authentication/AV/AvalonAuthenticationHandler.cs:34`               | Validate the `Avalon` bearer token                    |
| TODO-008  | 🟢     | Session Integrity       | `src/Server/Avalon.Server.Auth/Handlers/CWorldSelectHandler.cs:47`                         | Guard against duplicate world sessions                |
| TODO-009  | 🔴     | Configuration           | `src/Server/Avalon.Hosting/Networking/PacketReader.cs:36`                                  | Buffer size from configuration                        |
| TODO-010  | 🔴     | Configuration           | `src/Server/Avalon.Server.Auth/Handlers/CRequestServerInfoHandler.cs:17`                   | Externalise hardcoded client version                  |
| TODO-011  | 🔴     | Configuration           | `src/Server/Avalon.Server.Auth/Handlers/CRequestServerInfoHandler.cs:25`                   | Externalise hardcoded server version                  |
| TODO-012  | 🔴     | Configuration           | `src/Server/Avalon.Server.Auth/Handlers/CAuthHandler.cs:54`                               | Failed-login lockout threshold from configuration     |
| TODO-013  | 🔴     | Auth Features           | `src/Server/Avalon.Server.Auth/Handlers/CAuthHandler.cs:65`                               | Re-enable commented-out MFA flow                      |
| TODO-014  | 🔴     | Auth Features           | `src/Shared/Avalon.Network.Tcp/AvalonTcpClient.cs:451`                                     | Replace `"TODO"` placeholder with real client version |
| TODO-015  | 🔴     | Spell System            | `src/Server/Avalon.World/Spells/ChunkSpellSystem.cs:32`                                    | Deduct power cost when queuing a spell                |
| TODO-016  | 🔴     | Spell System            | `src/Server/Avalon.World.Public/Scripts/SpellScript.cs:47`                                 | Implement `Clone()` in `SpellScript` base class       |
| TODO-017  | 🔴     | Spell System            | `src/Shared/Avalon.Domain/World/SpellTemplate.cs` + `SpellMetadata.cs`                    | Add `AnimationId` to spell template and metadata      |
| TODO-018  | 🔴     | Spell System            | `src/Server/Avalon.World/Maps/Chunk.cs:370,387`                                            | Use spell `AnimationId` instead of hardcoded `1`      |
| TODO-019  | 🔴     | Spell System            | `src/Server/Avalon.World/Scripts/Creatures/CreatureCombatScript.cs:173`                    | Creature combat script — spell support                |
| TODO-020  | 🔴     | Spell System            | `src/Server/Avalon.World/Handlers/CharacterAttackHandler.cs:71`                            | AoE attack support (target-less spells)               |
| TODO-021  | 🔴     | Creature System         | `src/Server/Avalon.World.Public/Creatures/ICreatureMetadata.cs`                            | Add `Experience`, `RespawnTimer`, `RemoveTimer`       |
| TODO-022  | 🔴     | Creature System         | `src/Server/Avalon.World/Maps/Chunk.cs:262,280`                                            | Use template experience; verify script null thread safety |
| TODO-023  | 🔴     | Creature System         | `src/Server/Avalon.World/Entities/CreatureRespawner.cs:22,28`                              | Use template-defined respawn/remove timers            |
| TODO-024  | 🔴     | Character System        | `src/Server/Avalon.World/Handlers/CharacterSelectHandler.cs:175`                           | Send inventory to client on login                     |
| TODO-025  | 🔴     | Character System        | `src/Server/Avalon.World/Handlers/CharacterMovementHandler.cs:44`                          | Server-side collision / navmesh validation            |
| TODO-026  | 🔴     | Character System        | `src/Server/Avalon.World/Handlers/CharacterSelectHandler.cs:75`                            | Instance ID formalisation (pre-instance placeholder)  |
| TODO-027  | 🔴     | World Architecture      | `src/Server/Avalon.World/World.cs:183`                                                     | Replace unnamed world timer magic integers            |
| TODO-028  | 🔴     | World Architecture      | `src/Server/Avalon.Server.World/Handlers/ChatMessageHandler.cs:17`                         | Introduce proper chat command handler architecture    |
| TODO-029  | 🔴     | World Architecture      | `src/Server/Avalon.Server.World/Extensions/ServiceExtensions.cs:33`                        | Decouple World server from Auth database              |
| TODO-030  | 🔴     | Domain Model            | `src/Shared/Avalon.Domain/Characters/CharacterSpell.cs:16`                                 | `CharacterSpell` specializations design               |
| TODO-031  | 🔴     | Infrastructure          | `src/Shared/Avalon.Metrics/FakeMetricsManager.cs:42`                                       | Complete `IDisposable` implementation                 |

---

## Networking & Graceful Shutdown

> See also: [docs/networking-graceful-shutdown.md](docs/networking-graceful-shutdown.md)

---

### TODO-001 — Close all connections on `AvalonTcpServer.StopAsync` 🟢

**File:** `src/Shared/Avalon.Network.Tcp/AvalonTcpServer.cs:72`

**Context**  
`StopAsync()` sets `Running = false` and logs "Server stopped", but never closes any of the accepted client sockets. The sockets remain in an indeterminate state: the server-side loop exits but remote endpoints keep their TCP connection open until OS idle timeout.

**Implementation Details**  
1. Add a thread-safe collection (e.g. `ConcurrentDictionary<Guid, IAvalonTcpConnection>`) to `AvalonTcpServer`, populated inside `HandleNewConnection` and cleaned up on connection close.
2. In `StopAsync`, iterate the collection and call `connection.Close()` on each entry, catching per-connection exceptions to avoid aborting the loop.
3. After closing all connections, cancel `Cts` to stop `InternalServerLoop`.
4. Add `Socket.Close()` / `Socket.Dispose()` call at the tail of `StopAsync`.

**Behaviour & Requirements**
- All accepted connections are closed when the server stops — no phantom TCP half-open states.
- Per-connection close failures must be logged but must not prevent other connections from being closed.
- `StopAsync` completes synchronously (or `await`s a short drain timeout of ~2 s).

**Tests**
- Given a server with 3 mock connections, `StopAsync` closes all 3.
- A connection that throws on `Close()` does not prevent the other 2 from closing.
- After `StopAsync`, `IsRunning` returns `false`.

---

### TODO-002 — Send disconnect packet to all clients before close 🟢

**File:** `src/Shared/Avalon.Network.Tcp/AvalonTcpServer.cs:73`

**Context**  
Companion to TODO-001. Clients receive no signal that the server is shutting down — they experience an abrupt TCP reset and have no chance to show the player a clean "Server is shutting down" message.

**Implementation Details**  
1. Define (or reuse) a `SDisconnectPacket` in `Avalon.Network.Packets` carrying a `string Reason` and `ushort ReasonCode`.
2. In `StopAsync`, before calling `Close()` on each connection, call `connection.Send(SDisconnectPacket.Create("Server shutdown", DisconnectReason.ServerShutdown, ...))`.
3. Add a short flush delay (`Task.Delay(200ms)`) after the batch `Send` to let the OS drain the send buffers.
4. `Close()` is still called regardless of whether `Send` succeeded.

**Behaviour & Requirements**
- Each connected client receives a `SDisconnectPacket` with reason `ServerShutdown` before the socket is closed.
- Send failure for one client must not delay or prevent sending to others.
- Packet is defined in `Avalon.Network.Packets`; client-side handling of this packet shows a reconnect dialog.

**Tests**
- Mock `IAvalonTcpConnection.Send` is invoked with a `SDisconnectPacket` before `Close()` for each connection.
- `Send` throwing an exception still results in `Close()` being called.

---

### TODO-003 — Auth `OnStoppingAsync` — send disconnect packet 🟢

**File:** `src/Server/Avalon.Server.Auth/AuthServer.cs:55`

**Context**  
`OnStoppingAsync` iterates `Connections` and closes each `IAuthConnection` with no prior notification. The client experiences a clean TCP close but no application-level reason.

**Implementation Details**  
Same pattern as TODO-002. Loop `Connections`, call `connection.Send(SDisconnectPacket.Create(...))`, then `connection.Close()`.  
Consider extracting a `GracefulShutdownHelper.ShutdownAll(IEnumerable<IConnection>)` utility if TODO-002, TODO-003, and TODO-004 all share the same shape.

**Behaviour & Requirements**
- Auth clients receive `SDisconnectPacket` before their socket is closed.

**Tests**
- All `IAuthConnection` mocks in `Connections` receive `Send(SDisconnectPacket)` then `Close()`.

---

### TODO-004 — World `OnStoppingAsync` — send disconnect packet 🟢

**File:** `src/Server/Avalon.World/WorldServer.cs:164`

**Context / Implementation / Tests**  
Identical pattern to TODO-003 applied to `IWorldConnection`. Refer to the same `GracefulShutdownHelper` utility if extracted.

---

### TODO-005 — Notify in-game player before forced kick 🟢

**File:** `src/Server/Avalon.World/WorldServer.cs:271`

**Context**  
`DelayedDisconnect` is invoked via Redis pub/sub when another connection for the same account comes in. The player in-game is kicked silently — no HUD message, no reconnect prompt.

**Implementation Details**  
1. Before calling `connection.Close()`, send a `SServerKickPacket` (or extend `SDisconnectPacket` with `DisconnectReason.DuplicateLogin`).
2. The client displays a reason string from the packet payload.
3. Log the kick at `Information` level with account ID.

**Behaviour & Requirements**
- Player receives a reason-carrying packet before disconnect.
- Auth side already handles the kick by publishing to `world:accounts:disconnect`; this is purely the World-side notification.

**Tests**
- Given a World connection with a character, `DelayedDisconnect` sends the kick packet then calls `Close()`.
- No packet send when `connection` is `null` (guard already in place via `?.`).

---

## Security

> See also: [docs/security-session-management.md](docs/security-session-management.md)

---

### TODO-006 — Cryptographically secure world key generation 🟢

**File:** `src/Server/Avalon.Server.Auth/Handlers/CWorldSelectHandler.cs:48`

**Context**  
```csharp
var worldKey = new byte[32];
new Random().NextBytes(worldKey);
```
`System.Random` is a pseudo-random number generator seeded from the clock. World keys generated within milliseconds of each other may be related. This violates OWASP A02:2021 — Cryptographic Failures.

**Implementation Details**  
Replace the two lines with:
```csharp
var worldKey = RandomNumberGenerator.GetBytes(32); // System.Security.Cryptography
```
No other changes required — downstream consumption of `worldKey` is unchanged.

**Behaviour & Requirements**
- World key must be 256 bits of cryptographically secure random data.
- Keys are single-use (TTL 5 minutes in Redis, already implemented).
- No `System.Random` instance may be used for security-sensitive values anywhere in the codebase.

**Tests**
- Unit test: mock `RandomNumberGenerator` (or verify indirectly via reflection that `System.Random` is not called in this method path).
- Integration: two rapid world-select calls produce statistically distinct keys (not a strict unit test — more of a sanity assertion).

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

## Session Integrity

> See also: [docs/security-session-management.md](docs/security-session-management.md)

---

### TODO-008 — Guard against duplicate world sessions 🟢

**File:** `src/Server/Avalon.Server.Auth/Handlers/CWorldSelectHandler.cs:47`

**Context**  
Nothing prevents an authenticated account from selecting a world when they are already in one. A malicious or bugged client can initiate a second world-entry flow, creating two conflicting sessions.

**Implementation Details**  
1. Before issuing a new world key, check Redis for an existing `world:*:keys:*` key or a dedicated _in-world_ flag: `await _cache.GetAsync($"account:{account.Id}:inWorld")`.
2. If set, respond with an error packet (extend `SWorldSelectPacket` with an error code or send `SWorldSelectErrorPacket`).
3. Set `account:{account.Id}:inWorld` with a TTL equal to the world key TTL (5 minutes) when issuing a key. The World server must clear this key when the character enters the world (`CWorldSelectHandler` consumer in World) and when they disconnect.

**Behaviour & Requirements**
- An account already holding a valid world key cannot obtain a second one.
- After the 5-minute TTL expires (key never consumed) the account may retry.
- After the character enters the world, the "inWorld" flag transitions to a connection-backed state.

**Tests**
- Account with existing `inWorld` key → receives error response, new key not issued.
- Account without existing key → proceeds normally.
- Redis TTL expiry → user can re-select world.

---

## Configuration Externalization

> See also: [docs/configuration-reference.md](docs/configuration-reference.md)

---

### TODO-009 — Packet reader buffer size from configuration 🔴

**File:** `src/Server/Avalon.Hosting/Networking/PacketReader.cs:36`

**Context**  
`_bufferSize = 4096` is a hardcoded constant. Deployments handling large packets (spell payloads, inventory dumps) may need larger buffers without a code change.

**Implementation Details**  
1. Add `PacketReaderBufferSize` (type: `int`, default: `4096`) to `AvalonTcpServerConfiguration` (or a new `NetworkConfiguration` class).
2. Inject `IOptions<AvalonTcpServerConfiguration>` (or relevant config class) into `PacketReader` constructor.
3. Replace `_bufferSize = 4096` with `_bufferSize = options.Value.PacketReaderBufferSize`.
4. Add the key to `appsettings.json` with the default value documented.

**Behaviour & Requirements**
- Default 4096 preserves backward compatibility.
- Value must be validated: minimum 512, maximum 65535 (enforce in configuration binding).
- Invalid values should throw a descriptive startup error (use `ValidateDataAnnotations()`).

**Tests**
- `PacketReader` constructed with `BufferSize = 8192` uses an internal buffer of 8192.
- Value below minimum throws at startup validation.

---

### TODO-010 — Externalise hardcoded client version 🔴

**File:** `src/Server/Avalon.Server.Auth/Handlers/CRequestServerInfoHandler.cs:17`

**Context**  
`if (ctx.Packet.ClientVersion != "0.0.1")` requires a code change and redeployment for every client version update.

**Implementation Details**  
1. Add `MinClientVersion` (string, default `"0.0.1"`) to `AuthConfiguration` or `ApplicationConfiguration`.
2. Inject `IOptions<AuthConfiguration>` into `CRequestServerInfoHandler`.
3. Replace the literal with `_authConfig.MinClientVersion`.

**Behaviour & Requirements**
- Outdated clients are rejected with a closed connection and a warning log.
- Version comparison is an exact string match (e.g. `"1.2.0"`). If semver range support is needed later, it should be introduced as a separate story.

**Tests**
- Client version == configured → connection proceeds.
- Client version != configured → connection closed, log warning emitted.

---

### TODO-011 — Externalise hardcoded server version 🔴

**File:** `src/Server/Avalon.Server.Auth/Handlers/CRequestServerInfoHandler.cs:25`

**Context**  
`SServerInfoPacket.Create(1_000_000, ...)` sends a hardcoded integer as the server version.

**Implementation Details**  
1. Add `ServerVersion` (uint) to `ApplicationConfiguration`. Alternatively, derive from the executing assembly's `AssemblyInformationalVersionAttribute` at startup and store in a singleton `IServerInfo`.
2. Replace `1_000_000` with the injected value.

**Behaviour & Requirements**
- Server version in the handshake packet matches the deployed build.
- No code change required when releasing a new version (driven by configuration or assembly metadata).

**Tests**
- Configured server version `999` is present in the outgoing `SServerInfoPacket`.

---

### TODO-012 — Failed-login lockout threshold from configuration 🔴

**File:** `src/Server/Avalon.Server.Auth/Handlers/CAuthHandler.cs:54`

**Context**  
`if (account.FailedLogins >= 5)` — the lockout threshold is hardcoded. Security policy on brute-force protection should be configurable without redeploy.

**Implementation Details**  
1. Add `MaxFailedLoginAttempts` (int, default `5`, min `1`) to `AuthConfiguration`.
2. Inject `IOptions<AuthConfiguration>` into `CAuthHandler`.
3. Replace `5` with `_authConfig.MaxFailedLoginAttempts`.

**Behaviour & Requirements**
- Lockout triggers after `MaxFailedLoginAttempts` consecutive failures.
- Lockout resets to zero on successful login (already implemented).
- Default `5` preserves existing behaviour.

**Tests**
- With threshold `3`, account locks after exactly 3 failures.
- With threshold `10`, account does not lock after 5 failures.
- Successful login resets counter regardless of threshold.

---

## Authentication Features

> See also: [docs/security-session-management.md](docs/security-session-management.md)

---

### TODO-013 — Re-enable MFA flow 🔴

**File:** `src/Server/Avalon.Server.Auth/Handlers/CAuthHandler.cs:65`

**Context**  
An entire MFA verification block is commented out. `IMFAHashService` is injected and wired in DI but never invoked. All dependencies (`Otp.NET`, Redis ephemeral secret storage) already exist.

**Implementation Details**  
1. Uncomment the MFA block.
2. Confirm `IMFASetupRepository` (or equivalent) is available; if the `_databaseManager` reference was removed, replace it with a direct `IMFASetupRepository` injection.
3. Ensure `AuthResult.MFA_REQUIRED` is a valid enum value in the shared packet model.
4. Verify `SAuthResultPacket.Create(null, mfaHash, AuthResult.MFA_REQUIRED, ...)` compiles with the current packet API.
5. Add a `CMFAVerifyHandler` if it does not already exist to complete the second step of the flow.

**Behaviour & Requirements**
- Account with confirmed MFA setup → `AuthResult.MFA_REQUIRED` response with an ephemeral hash; no `SWorldListPacket` yet.
- Account without MFA → unchanged (proceeds to world list).
- MFA token submitted via `CMFAVerifyPacket` → validated against `IMFAHashService`; success sends world list.
- Ephemeral hash stored in Redis with a short TTL (~5 minutes).

**Tests**
- Account with confirmed MFA → response is `MFA_REQUIRED`, hash is non-null, no world list sent.
- Account without MFA → response is `OK`, world list sent.
- MFA token verify: correct token → success; incorrect → fail; expired hash → fail.

---

### TODO-014 — Replace `"TODO"` placeholder in `AvalonTcpClient` 🔴

**File:** `src/Shared/Avalon.Network.Tcp/AvalonTcpClient.cs:451`

**Context**  
```csharp
var packet = CRequestServerInfoPacket.Create("TODO");
```
The literal string `"TODO"` is sent as the client version. This will be rejected by the server once TODO-010 is implemented (version check against configured `MinClientVersion`).

**Implementation Details**  
1. Add a `ClientVersion` (string) property to `AvalonTcpClientConfiguration`.
2. In `RequestServerInfoPacket()`, replace `"TODO"` with `_configuration.ClientVersion`.

**Behaviour & Requirements**
- Client sends its real version string on handshake.
- Version string must match the server's `MinClientVersion` for the connection to proceed.

**Tests**
- `CRequestServerInfoPacket` constructed with the configured version string, not the placeholder.

---

## Spell System

> See also: [docs/spell-system.md](docs/spell-system.md)

---

### TODO-015 — Deduct power cost on `QueueSpell` 🔴

**File:** `src/Server/Avalon.World/Spells/ChunkSpellSystem.cs:32`

**Context**  
`QueueSpell` adds a spell to the active queue with no check against the caster's `CurrentPower`. Players can spam any spell with no resource constraint.

**Implementation Details**  
1. Before `_spellQueue.Add(...)`, check `character.CurrentPower >= spell.Metadata.Cost`.
2. If insufficient, return `false` (the method already returns `bool`).
3. If sufficient, deduct: `character.CurrentPower -= (int)spell.Metadata.Cost`.  
   Power type must be `PowerType.Mana` or `PowerType.Energy` for a cost to apply; `PowerType.Fury` may have different mechanics (accumulation, not depletion — defer until Fury system is designed).
4. Include the updated `CurrentPower` field in the next `GameEntityFields.CharacterUpdate` broadcast.

**Behaviour & Requirements**
- Casting a spell with `Cost > 0` reduces caster power by that amount.
- Spell cannot be cast when `CurrentPower < Cost`; `QueueSpell` returns `false`.
- Zero-cost spells (`Cost == 0`) always pass the check.
- Power change is reflected in the next chunk state update to the client.

**Tests**
- Sufficient power → spell queued, `CurrentPower` reduced by `Cost`.
- Insufficient power → returns `false`, `CurrentPower` unchanged.
- Zero-cost spell → queued regardless of `CurrentPower`.
- `PowerType.Fury` caster → no deduction (pend Fury mechanic).

---

### TODO-016 — Implement `SpellScript.Clone()` in base class 🔴

**File:** `src/Server/Avalon.World.Public/Scripts/SpellScript.cs:47`

**Context**  
`Clone()` is declared `abstract`, forcing every concrete spell script to write its own implementation. The base class already holds the full constructor state (`Spell`, `Caster`, `Target`, `ChainedScripts`).

**Implementation Details**  
1. Change `public abstract SpellScript Clone()` to `public virtual SpellScript Clone()`.
2. Implement using `MemberwiseClone()`:
   ```csharp
   public virtual SpellScript Clone()
   {
       var clone = (SpellScript)MemberwiseClone();
       clone.ChainedScripts.Clear();
       foreach (var script in ChainedScripts)
           clone.ChainedScripts.Add(script.Clone());
       return clone;
   }
   ```
   Note: `ChainedScripts` is `protected List<SpellScript>` — make a fresh list in the clone.
3. Concrete subclasses that have extra mutable state (e.g. elapsed timers, position buffers) should `override Clone()` and call `base.Clone()`.

**Behaviour & Requirements**
- `Clone()` produces an independent instance; mutating the clone does not affect the original.
- `ChainedScripts` are recursively cloned.
- Existing concrete spell scripts that already implement `Clone()` continue to compile (they override the virtual).

**Tests**
- Clone shares the same `Spell` reference but has an independent `ChainedScripts` list.
- Mutating a chained script on the clone does not affect the original's chain.
- A subclass without `override Clone()` produces a valid clone via the base implementation.

---

### TODO-017 — Add `AnimationId` to `SpellTemplate` and `SpellMetadata` 🔴

**File:** `src/Shared/Avalon.Domain/World/SpellTemplate.cs` + `src/Server/Avalon.World.Public/Spells/SpellMetadata.cs`

**Context**  
`Chunk.BroadcastUnitAttackAnimation` and `Chunk.BroadcastFinishCastAnimation` hardcode animation ID `1` because neither `SpellTemplate` nor `SpellMetadata` carries an animation identifier.

**Implementation Details**  
1. Add `uint AnimationId { get; set; }` to `SpellTemplate` (persisted column, default `1`).
2. Add `uint AnimationId { get; init; }` to `SpellMetadata`.
3. Update `SpellMetadata.Clone()` to include `AnimationId`.
4. Update the code path that maps `SpellTemplate → SpellMetadata` (inside world data loading) to copy `AnimationId`.
5. Create a database migration: `Avalon.Database.Migrator` — `AddAnimationIdToSpellTemplate`.

**Behaviour & Requirements**
- Each spell in the DB can specify its animation ID.
- Default `1` maintains backward compatibility for existing records.
- Animation ID `0` is treated as "no animation" by the client.

**Tests**
- `SpellMetadata.Clone()` preserves `AnimationId`.
- DB migration adds column with default `1` without data loss (migration test or script review).
- World data load correctlys maps `SpellTemplate.AnimationId` → `SpellMetadata.AnimationId`.

---

### TODO-018 — Use `AnimationId` from spell in chunk broadcasts 🔴

**File:** `src/Server/Avalon.World/Maps/Chunk.cs:370,387`

**Depends on:** TODO-017

**Implementation Details**  
In `BroadcastUnitAttackAnimation`:
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
1. After TODO-021, add `IReadOnlyList<SpellId> SpellIds { get; set; }` to `ICreatureMetadata`.
2. In `CreatureCombatScript`, build a local list of `ISpell` instances from the metadata at script initialisation.
3. In `AttackTarget`, before the melee path: check if any spell is off cooldown and within range. If yes, call `Chunk.QueueSpell(Creature, target, spell)` and update cooldowns.
4. Fall back to melee if no spell is available.

**Behaviour & Requirements**
- Creatures with spells in their template can cast them during combat.
- Spell cooldown is tracked per spell instance on the creature.
- Creature cannot cast while moving (unless the spell has `CastTime == 0`).
- Melee attack is used when no spell is available or ready.

**Tests**
- Creature with a spell in template → `QueueSpell` called on attack cycle when spell is off cooldown.
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
3. If AoE: skip the `GetTarget` call, pass `target = null` to `chunk.QueueSpell`.
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

## Creature System

> See also: [docs/creature-system.md](docs/creature-system.md)

---

### TODO-021 — Add `Experience`, `RespawnTimer`, `RemoveTimer` to `ICreatureMetadata` 🔴

**File:** `src/Server/Avalon.World.Public/Creatures/ICreatureMetadata.cs`

**Context**  
`ICreatureMetadata` only contains movement speeds and start position. Experience reward, respawn timing, and corpse removal timing are hardcoded at usage sites.

**Implementation Details**  
1. Add to `ICreatureMetadata`:
   ```csharp
   uint Experience { get; set; }          // XP awarded to the killer
   TimeSpan RespawnTimer { get; set; }    // Time before creature re-spawns
   TimeSpan BodyRemoveTimer { get; set; } // Time before corpse is removed
   ```
2. Update the concrete implementation (locate via `ICreatureMetadata` implementors).
3. Update the world data loading / creature spawn seeding code to populate the new fields.
4. Defaults: `Experience = 20`, `RespawnTimer = 3 min`, `BodyRemoveTimer = 2 min`.

**Behaviour & Requirements**
- Per-template configuration of XP, respawn, and body removal.
- Existing runtime behaviour preserved via defaults matching current hardcoded values.

**Tests**
- Data model carries all three fields.
- World data loader maps DB values to `ICreatureMetadata` correctly.
- Default values match hardcoded originals.

---

### TODO-022 — Use template experience; verify script null thread safety 🔴

**File:** `src/Server/Avalon.World/Maps/Chunk.cs:262,280`

**Context**  
Two issues in `OnCreatureKilled`:
1. `const uint creatureExperience = 20U` — hardcoded XP regardless of creature type.
2. `creature.Script = null` — the comment asks whether this must be scheduled to the main thread.

**Implementation Details (XP)**  
Replace `const uint creatureExperience = 20U` with `creature.Metadata.Experience` (depends on TODO-021).

**Implementation Details (Thread Safety)**  
Audit the `Chunk.Update` method: if `creature.Script?.Update(deltaTime)` is called on the same thread as `OnCreatureKilled`, the assignment is safe (single-threaded game loop). Document this explicitly with a comment confirming single-thread execution. If the update loop is parallelised, protect with a volatile write or replace `AiScript?` with an `Interlocked.Exchange` pattern.

**Behaviour & Requirements**
- XP reward matches the creature's template definition.
- Script null-assignment does not produce `NullReferenceException` in any concurrency scenario.

**Tests**
- Killing a creature with `Metadata.Experience = 150` grants the player 150 XP.
- Killing a creature while the script is updating (if parallelised) does not throw.

---

### TODO-023 — Creature respawner — use template-defined timers 🔴

**File:** `src/Server/Avalon.World/Entities/CreatureRespawner.cs:22,28`

**Depends on:** TODO-021

**Implementation Details**  
Replace:
```csharp
respawnTimer.SetInterval((long)TimeSpan.FromMinutes(3).TotalMilliseconds);
removeTimer.SetInterval((long)TimeSpan.FromMinutes(2).TotalMilliseconds);
```
With:
```csharp
respawnTimer.SetInterval((long)creature.Metadata.RespawnTimer.TotalMilliseconds);
removeTimer.SetInterval((long)creature.Metadata.BodyRemoveTimer.TotalMilliseconds);
```

**Tests**
- Creature with `RespawnTimer = 1 minute` → timer fires in 60 seconds (simulated with fast-forward deltaTime).
- Creature with `BodyRemoveTimer = 30 seconds` → corpse removed after 30 seconds.

---

## Character System

> See also: [docs/character-login-flow.md](docs/character-login-flow.md)

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
1. After computing `interpolatedPosition`, raycast from `connection.Character.Position` to `clientSentPosition` using `IChunkNavigator.Raycast` (or DotRecast's `DtNavMeshQuery.Raycast`).
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

### TODO-026 — Instance ID formalisation 🔴

**File:** `src/Server/Avalon.World/Handlers/CharacterSelectHandler.cs:75`

**Context**  
`character.InstanceId = Guid.NewGuid().ToString()` generates a random instance ID per login, making no two sessions technically share the same instance. This is a placeholder because instance support is not yet built.

**Implementation Details (Phase 1 — pre-instances)**  
1. Define a well-known "main world" instance GUID per `WorldId` (e.g. derived from `Guid.Parse(worldId.ToString("N").PadLeft(32, '0'))` or a seeded deterministic value).
2. Set `character.InstanceId` to this fixed GUID for all characters entering the default open world.
3. Add a `TODO: Phase 2 — route through IInstanceManager for instanced zones` comment.

**Behaviour & Requirements (Phase 1)**  
- All characters in the same world share the same well-known instance GUID.
- `MapInfo.InstanceId` sent to client is the well-known value, not random.

**Tests**
- Two characters logging into the same world → same `InstanceId` in `SCharacterSelectedPacket`.

---

## World Architecture

> See also: [docs/architecture-decisions.md](docs/architecture-decisions.md)

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

### TODO-028 — Chat command handler architecture 🔴

**File:** `src/Server/Avalon.Server.World/Handlers/ChatMessageHandler.cs:17`

**Context**  
`ChatMessageHandler` has extensive commented-out slash-command logic (e.g. `/inv`) mixed with broadcast chat. Adding commands requires modifying this file and the commented block pattern is fragile.

**Implementation Details**  
1. Define `ICommand` interface:
   ```csharp
   public interface ICommand
   {
       string Name { get; }        // e.g. "inv"
       string[] Aliases { get; }  // e.g. ["invite"]
       Task ExecuteAsync(WorldPacketContext<CChatMessagePacket> ctx, string[] args);
   }
   ```
2. Implement `CommandDispatcher` registered as a singleton that scans DI for all `ICommand` registrations.
3. In `ChatMessageHandler.ExecuteAsync`: if `message.StartsWith("/")`, extract command name and args, dispatch to `CommandDispatcher`. If command not found, send a `SChatMessagePacket` back to sender with "Unknown command".
4. Implement `GroupInviteCommand : ICommand` as the first concrete command (restoring the commented-out `/inv` logic).
5. Register commands in DI via `services.AddSingleton<ICommand, GroupInviteCommand>()`.

**Behaviour & Requirements**
- Any class implementing `ICommand` registered in DI is automatically discoverable.
- Unknown commands return a client-visible error message.
- Non-slash messages are broadcast to the chunk as before.
- Commands are case-insensitive.

**Tests**
- `/inv PlayerName` → `GroupInviteCommand.ExecuteAsync` called with args `["PlayerName"]`.
- `/INVITE PlayerName` (alias, uppercase) → same command invoked.
- `/unknown` → error response to sender, no broadcast.
- Plain `Hello` → broadcast to chunk.
- No registered commands → chat still works.

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
Phase 1 — Foundation & Security (unblock everything downstream)
  TODO-006  CSPRNG world key                    [Security, trivial]
  TODO-007  Token validation                    [Security]
  TODO-009  Buffer size config                  [Config]
  TODO-010  Client version config               [Config]
  TODO-011  Server version config               [Config]
  TODO-012  Lockout threshold config            [Config]
  TODO-031  FakeMetricsManager Dispose          [Infra, trivial]

Phase 2 — Creature & Spell data model
  TODO-021  ICreatureMetadata expansion         [unblocks 022, 023, 019]
  TODO-017  SpellTemplate AnimationId           [unblocks 018]
  TODO-016  SpellScript.Clone base impl         [Spell]
  TODO-030  CharacterSpell specializations      [Domain design]

Phase 3 — Creature runtime wiring
  TODO-022  Template experience + thread safety [needs 021]
  TODO-023  Respawner template timers           [needs 021]
  TODO-019  Creature spell support              [needs 021, 017]
  TODO-018  Chunk animation id                  [needs 017]

Phase 4 — Character system
  TODO-024  Send inventory                      [Character]
  TODO-026  Instance ID formalisation           [Character]
  TODO-015  Power cost deduction                [Spell]
  TODO-020  AoE targeting                       [Spell]

Phase 5 — Networking & Shutdown
  TODO-001  Close all connections               [Network]
  TODO-002  Disconnect packet (TcpServer)        [Network, needs packet definition]
  TODO-003  Disconnect packet (Auth)            [Network]
  TODO-004  Disconnect packet (World)           [Network]
  TODO-005  In-game kick notification           [Network]
  TODO-014  TcpClient version placeholder       [needs TODO-010]

Phase 6 — Auth features & session
  TODO-008  Duplicate world session guard       [Session]
  TODO-013  Re-enable MFA                       [Auth]

Phase 7 — Architecture & validation
  TODO-025  Server-side collision               [Character, complex]
  TODO-027  Timer constants                     [World]
  TODO-028  Command handler architecture        [World]
  TODO-029  Decouple World from Auth DB         [Architecture, complex]
```
