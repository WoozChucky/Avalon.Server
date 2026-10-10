# Configuration Reference

This document covers all configuration keys for the Avalon server.

---

## Overview

Avalon uses strongly-typed configuration classes bound from `appsettings.json` (or environment variables / secrets manager in production). The TCP servers read them through `IOptions<T>`. The REST API runs as up to four services from one binary ([API services](api-services.md)), and a process binds only the sections of the services it runs (see [REST API Services](#rest-api-services)): identity binds `Application` once as `ApplicationConfig` and the worlds service as `WorldsConfig`, each registering the parts as plain singletons, the shared hosting reads the token validation and forwarded-headers settings directly, and only the rows below marked `IOptions<T>` go through the options system there. The classes live next to the code that reads them: `src/Shared/Avalon.Configuration` holds the database and hosting ones, and the rest sit in their host or library project.

---

## Configuration Classes

Hosts: **API** is `Avalon.Api`, with the API services that read the class in parentheses; **Auth** the auth server, **World** the world server.

| Class                       | Namespace                          | Bound from | Hosts | Validated |
|-----------------------------|------------------------------------|------------|-------|-----------|
| `DatabaseConfiguration`     | `Avalon.Configuration`             | `Database` (`Database:Auth`, `Database:Characters`, `Database:World`, each with a `ConnectionString`) | API, Auth, World | At startup by `DatabaseConnectionsValidation`: Auth needs `Database:Auth`, World all three, the API `Database:Auth` only. See [Startup Validation](#startup-validation) |
| _(no class)_                | `Avalon.Api.Hosting`               | `Application:Services`, read by `ApiServiceSelection` | API (every process) | When the host is built: an empty list or an unknown name stops it. See [REST API Services](#rest-api-services) |
| _(no class)_                | `Avalon.Api.Hosting`               | `Application:Startup:AuthSchemaWaitSeconds`, read by `AuthSchemaGate` | API (every process; only one without identity waits) | At startup: anything but a whole number of at least 1 stops it |
| _(no class)_                | `Avalon.Api.Hosting.Worlds`        | `Database:Worlds:<id>:World` and `Database:Worlds:<id>:Characters`, read by `WorldDatabaseSettings` for the parts a process reads | API (worlds both, identity Characters) | By hand in `ApiStartup`, right after the options. See [REST API Worlds](#rest-api-worlds) |
| `CacheConfiguration`        | `Avalon.Infrastructure.Configuration` | `Cache` (Auth, World); `Application:Cache` (API) | API (identity, worlds, commerce), Auth, World | `ValidateOnStart` (`Host` required) in all three hosts; in the API only in a process whose services need Redis |
| `HostingConfiguration`      | `Avalon.Configuration`             | `Hosting` (with `Hosting:ProxyProtocol` and `Hosting:Telemetry`). `Hosting:Telemetry:NoSpanPacketTypes` lists the packet types, by `NetworkPacketType` name, whose handlers open no span and no log scope (their duration and errors are still recorded); default `CMSG_PLAYER_INPUT`, `CMSG_PONG`, and a configured list replaces it. The default types' handlers write `{ConnectionId}` and `{PacketType}` into their own lines (`HighRatePacketLog`); a type added to the list loses its handlers' log scope with nothing in its place. See [instrumentation](instrumentation.md#packet-handlers) | Auth, World | `ValidateOnStart` on its own properties. The nested `ProxyProtocol` is not annotation-checked: a trusted network that is not valid CIDR throws when the TCP server is built; a `NoSpanPacketTypes` entry that is not an exact `NetworkPacketType` name throws when the server is built |
| `HostingSecurity`           | `Avalon.Server.Auth.Configuration` | `Hosting:Security` | Auth | `ValidateOnStart` (`CertificatePath` required) |
| `WorldHostingSecurity`      | `Avalon.World.GameAuth`            | `Hosting:Security` | World | `ValidateOnStart` (`CertificatePath` required: the world's TLS certificate, a current leaf with its private key, loaded before it listens) |
| `GameAdmissionOptions`      | `Avalon.World.GameAuth`            | `World:Admission` (`ApiUrl`, `ServerId`, `ClientCertificatePath`, `ClientCertificatePassword`, `ApiCertificateSha256`; `WorldId` from `Game:WorldId`) | World | `ValidateOnStart`: a fixed HTTPS origin, a server id, the workload client certificate and the API leaf's SHA-256 pin. See [game server admission](steam-authentication-workloads.md#world-transport-and-coordinated-protocol-cutover) |
| `AuthConfiguration`         | `Avalon.Server.Auth.Configuration` | `Application` | Auth | `ValidateOnStart` |
| `GameConfiguration`         | `Avalon.World.Configuration`       | `Game` | World | `ValidateOnStart` (`WorldId` also required by a post-configure step) |
| `RegenConfiguration`        | `Avalon.World.Configuration`       | `Regen` | World | `ValidateOnStart` |
| `WorldShutdownConfiguration` | `Avalon.World.Configuration`     | `World:Shutdown` | World | `ValidateOnStart` (both required; `DrainTime` 0 to 1 h, `SaveMargin` 21 s to 1 h). See [World Shutdown Drain](#world-shutdown-drain) |
| `TokenValidationConfig`     | `Avalon.Api.Hosting.Config`        | `Application:Authentication`: `SigningKey`, `SigningKeyId`, `ValidationKeys`, `Issuer`, `Audience`, `ValidateIssuer`, `ValidateAudience`, `ClockSkewInMinutes` | API (every service) | By hand while the host is built: the token keys (`JwtKeys.Create`, see [REST API JWT Signing Key](#rest-api-jwt-signing-key)) |
| `ApplicationConfig`         | `Avalon.Api.Identity.Config`       | `Application` | API (identity) | Not as a whole; its sections below |
| `AuthenticationConfig`      | `Avalon.Api.Identity.Config`       | `Application:Authentication`, all of it (it extends `TokenValidationConfig`) | API (identity) | By hand in `AddIdentity` (`IdentityServiceRegistration`): the login limits (`LoginLimitsValidation.Validate`), the account-creation cap and the email-change send caps |
| `ForwardedHeadersConfig`    | `Avalon.Api.Hosting.Config`        | `Application:ForwardedHeaders` | API (every service) | By hand in `AddApiHosting` (`ForwardedHeadersSetup.BuildOptions`) |
| `EmailConfig`               | `Avalon.Api.Identity.Config`       | `Application:Email` | API (identity) | By hand in `AddEmail` |
| `GameAuthConfig`            | `Avalon.Api.Identity.Config`       | `Application:GameAuth`: `HostKey` | API (identity) | Before the api serves (`IdentityStartupCheck`, `GameAuthHostKey`), see [REST API JWT Signing Key](#rest-api-jwt-signing-key) |
| `RateLimitingConfig`        | `Avalon.Api.Hosting.Config`        | `Application:RateLimiting`, as `IOptions<RateLimitingConfig>` only | API (every service) | `ValidateOnStart` (each limit at least 1; each `ExemptSources` entry by `ExemptSourcesValidation`, see [Exempt sources](#exempt-sources)) |
| `LoadTestOptions`           | `Avalon.Api.Identity.LoadTest`     | `Application:LoadTest`, as `IOptions<LoadTestOptions>` | API (identity) | `ValidateOnStart` (`MaxAccounts` at least 1). See [REST API Load-Test Accounts](#rest-api-load-test-accounts) |
| `MapAssetConfig`            | `Avalon.Api.Worlds.Config`         | `Application:MapAssets`, as `IOptions<MapAssetConfig>` | API (worlds) | Not validated |
| `NotificationConfig`        | `Avalon.Api.Identity.Config`       | `Application:Notification` | API (identity) | Not validated |
| `EnvironmentConfig`         | `Avalon.Api.Hosting.Config`        | `Application:Environment` | API (identity) | Not validated |
| `WorldsConfig`              | `Avalon.Api.Worlds.Config`         | `Application`: `PublicWorldId`, `PublicSiteUrl`, `Balance` | API (worlds) | `PublicSiteUrl` by hand (`PublicSiteSettings.Create`); see [REST API Worlds](#rest-api-worlds) |
| `PreviewConfiguration`      | `Avalon.Api.Worlds.Previews`       | `Application:Previews`, as `IOptions<PreviewConfiguration>` | API (worlds) | Not validated; a bad colour only leaves `theme-color` out |
| `DistributionConfiguration` | `Avalon.Api.Distribution`          | `Application:Distribution` | API (distribution) | Not validated; left incomplete, the `/client` endpoints answer 503 |
| `BalanceConfiguration`      | `Avalon.Api.Worlds.Balance`        | `Application:Balance` | API (worlds) | Not validated; left incomplete, the admin `/balance` endpoints answer 503 |
| `TemplateEditingOptions`    | `Avalon.Api.Worlds.Templates`      | `Application:Templates` | API (worlds) | `ValidateOnStart` (`ReloadTimeout` greater than zero). See `Application:Templates` below |
| `CommerceConfiguration`     | `Avalon.Api.Commerce`              | `Application:Commerce`, as `IOptions<CommerceConfiguration>` | API (commerce) | `ValidateOnStart` (`CommerceOptionsValidator`, only while `Enabled`). See [Commerce](commerce-configuration.md) |
| `BalanceServiceOptions`     | `Avalon.Balance.Service`           | `Balance` | Balance service | `ValidateOnStart` (`SharedSecret` required, 32 characters or more). See [Balance Service](#balance-service) |

The API's `Cache` settings are therefore under `Application:Cache` (`Application__Cache__Host`), not `Cache`,
while its database settings are under the top-level `Database`, as on the servers.

---

## Auth Server Configuration (`AuthConfiguration`)

Section in `appsettings.json`: `"Application"`

| Key                         | Type   | Default   | Description                                    |
|-----------------------------|--------|-----------|------------------------------------------------|
| `MinClientVersion`          | string | `"0.0.1"` | Minimum client version accepted during handshake |
| `ServerVersion`             | string | `"1.0.0"` | Server version sent in `SServerInfoPacket` to clients |
| `MaxFailedLoginAttempts`    | int    | `5`       | Failed password or MFA-code attempts at one username, from every source, before it is locked (counted in Redis, #484) |
| `LockoutDurationMinutes`    | int    | `15`      | The window those attempts are counted over, and how long the lock lasts from the failure that set it |
| `MaxFailedLoginsPerSource`  | int    | `10`      | Password and MFA-code attempts one source address may make, across every account, per window (#471) |
| `FailedLoginSourceWindowMinutes` | int | `15`   | The window, fixed from a source's first attempt, those are counted over |
| `MaxFailedMfaAttempts`      | int    | `5`       | Codes one MFA hash allows; the last wrong one deletes the hash |
| `OnlineSweepIntervalSeconds` | int  | `30`      | How often the auth server clears `Online` on accounts whose session is none of its live connections (#555) |
| `Issuer`                    | string | `"Avalon"` | Issuer name embedded in MFA OTP URIs           |

The five login limits are shared with the REST API (#478): both servers spend the same Redis budgets, so
the API's `Application:Authentication` values of the same names must match these. See
[REST API Login Limits](#rest-api-login-limits).

```json
"Application": {
  "MinClientVersion": "0.0.1",
  "ServerVersion": "1.0.0",
  "MaxFailedLoginAttempts": 5,
  "LockoutDurationMinutes": 15,
  "MaxFailedLoginsPerSource": 10,
  "FailedLoginSourceWindowMinutes": 15,
  "MaxFailedMfaAttempts": 5,
  "OnlineSweepIntervalSeconds": 30,
  "Issuer": "Avalon"
}
```

**Validation rules:**
- `MinClientVersion`, `ServerVersion`: required, must match `^\d+\.\d+\.\d+$` (SemVer).
- The five login limits: minimum `1`.
- `OnlineSweepIntervalSeconds`: minimum `1`.
- `Issuer`: required, non-empty.

---

## Hosting Configuration (`HostingConfiguration`)

Section in `appsettings.json`: `"Hosting"` (auth and world servers)

| Key                     | Type   | Default  | Description                                             |
|-------------------------|--------|----------|---------------------------------------------------------|
| `Host`                  | string | `"0.0.0.0"` | Bind address                                         |
| `Port`                  | int    | `21000`  | TCP listen port (the auth server's `appsettings.json` sets 21000, the world server's 21001; unset is 0). 0 binds a free port, which `ServerBase.BoundEndPoint` and the "Listening for connections on" log report (#841); tests use it |
| `PacketReaderBufferSize`| int    | `4096`   | Internal read-buffer size in bytes for `PacketReader`   |
| `SendBufferCapacity`    | int    | `100`    | The auth server's outbox (`ChannelOutbox`): packets one connection holds while waiting to be sent; when full, the oldest is dropped. The world server does not read it (#875) |
| `TcpKeepAliveTimeSeconds` | int | `60` | TCP keepalive on every accepted socket (#571): idle seconds before the first probe |
| `TcpKeepAliveIntervalSeconds` | int | `10` | Seconds between unanswered keepalive probes |
| `TcpKeepAliveRetryCount` | int | `3` | Unanswered probes before the OS closes the connection. With the defaults a half-open connection (the peer gone without a FIN or RST) closes within about 90 s |

```json
"Hosting": {
  "Host": "0.0.0.0",
  "Port": 21000,
  "PacketReaderBufferSize": 4096,
  "Security": {
    "CertificatePath": "cert-tcp.pfx",
    "CertificatePassword": "avalon"
  }
}
```

**Validation rules:**
- `PacketReaderBufferSize`: minimum `512`, maximum `65535`.
- `SendBufferCapacity`: minimum `10`, maximum `10000`.
- `TcpKeepAliveTimeSeconds`, `TcpKeepAliveIntervalSeconds`: minimum `1`, maximum `32767`; `TcpKeepAliveRetryCount`: minimum `1`, maximum `127`. Checked at startup, which names the setting. An option the platform cannot set is skipped with one Warning, and the connection is still served.
- Keepalive covers the TCP connection the server accepted. Behind a proxy that terminates TCP it covers only the proxy-to-server leg; the client-to-proxy leg needs the proxy's own keepalive or idle timeout.
- `Security:CertificatePath`: required on both servers (the auth server's TLS certificate, and the world server's, which
  must be current and carry its private key). `Security:CertificatePassword` is optional.

---

## Network Configuration (`NetworkConfiguration`)

Section in `appsettings.json`: `"Network"` (world server). Read once at startup (#875).

| Key | Type | Default | Description |
|---|---|---|---|
| `SendThreads` | int | about half the processors, 1 to 8 | Dedicated send threads (`Network.Send.<i>`); each connection belongs to one for its life |
| `MaxPendingBytes` | int | `524288` (512 KiB) | Bytes one connection may have queued or being written; past it the connection is sent `SDisconnect(SlowConnection)` and closed. Nothing is ever dropped |
| `MaxWriteStall` | TimeSpan | `00:00:10` | How long one write may stay pending; past it the connection is closed without a notice |

**Validation rules:** `SendThreads` 1 to 64; `MaxPendingBytes` 65536 to 67108864; `MaxWriteStall` 1 s to 5 min. Checked at startup, which names the setting.

---

## Cache Configuration (`CacheConfiguration`)

Section in `appsettings.json`: `"Cache"` (auth and world servers), `"Application:Cache"` (REST API)

| Key        | Type   | Description                           |
|------------|--------|---------------------------------------|
| `Host`     | string | Redis endpoint, e.g. `"localhost:6379"` |
| `Username` | string | Redis ACL user (#803). Left out, the connection signs in as the default user with `Password` alone |
| `Password` | string | Redis AUTH password (of `Username`, or of the default user) |

```json
"Cache": {
  "Host": "localhost:6379",
  "Password": "your-redis-password"
}
```

**Validation rules:**
- `Host`: required, checked at startup. `Username` and `Password` are optional.

With a `Username` the connection leaves out the client's own probes of the server (`CLUSTER`, `CONFIG`, `INFO` and the
tie-breaker key), which a restricted user may not run, and refuses admin commands; the auth and world servers name no
user and sign in as the default user. The users each REST API service signs in as are in
[Redis users per API service](redis-cache-keys.md#redis-users-per-api-service).

---

## World Configuration (`GameConfiguration`)

Section in `appsettings.json`: `"Game"` (World server only)

| Key                              | Type   | Default    | Description |
|----------------------------------|--------|------------|-------------|
| `WorldId`                        | ushort | —          | Identifies this world to the Auth server |
| `PlayerRadius`                   | float  | `2000`     | Currently unused by the simulation |
| `MaxCharactersPerAccount`        | ushort | `5`        | Rejects character creation beyond this count |
| `CharacterLoadTimeoutSeconds`    | int    | `15`       | How long a pending character select may wait before it is cancelled |
| `ScriptHotReloadIntervalSeconds` | int    | `5`        | Poll interval for `IScriptHotReloader` |
| `ExperienceBandDecay`            | float  | `0.75`     | Experience multiplier per level the player is outside the map's level band. Range `0.01`–`1.0` |
| `CreatureLocomotion`             | enum   | `Waypoint` | `Waypoint` or `Crowd` — see below |
| `CrowdIncludesPlayers`           | bool   | `false`    | Registers players as crowd obstacles so creatures steer around them. Ignored under `Waypoint` |
| `CreatureAgentRadius`            | float  | `0.6`      | Separation radius in world units. Range `0.05`–`10.0` |
| `MeleeSlotCount`                 | int    | `6`        | Standing positions on the ring around a target. Range `1`–`16` |
| `MeleeSlotRadius`                | float  | `1.5`      | Ring radius. Range `0.5`–`20.0` |
| `MaxMoney`                       | ulong  | `9999999999` | Most copper a character can hold; an addition past it is refused whole. Large enough that normal play never reaches it |
| `CharacterSaveInterval`          | TimeSpan | `00:05:00` | How often every in-world character is saved; first saves are staggered across one interval by character id. Range `00:00:10`–`01:00:00` |
| `PvpOffDelay`                    | TimeSpan | `00:05:00` | How long a PvP flag stays on after its owner asks to turn it off (#164). Any player-on-player hit restarts a running timer at this length, for both players. Range `00:00:01`–`01:00:00` |
| `InterestRadius`                 | float  | `60`       | Metres, on X/Z. A one-shot effect broadcast (hit, start/finish/interrupted cast, ability fired, attack swing, death, revive) goes only to connections whose character is within this distance of the effect or involved in it (#532), and a character, creature or projectile enters a client's view within it (#593). At least `1` and finite; startup refuses anything else |
| `InterestRemoveMargin`           | float  | `10`       | Metres added to `InterestRadius` before an object already in a client's view is removed from it (#593), so one standing near the edge does not flicker in and out. `0` or more and finite (`0` turns the margin off); startup refuses anything else |
| `FuryFromDamageTaken`            | float  | `50`       | Fury a character whose pool is Fury gains when hit (#526): `floor(health lost / max health × this)`, the health lost capped at what it had before the hit. `0` or more and finite (`0` turns it off); startup refuses anything else |
| `FuryDecayPerSecond`             | float  | `5`        | Fury lost per second out of combat, down to 0 (#526); never in combat. `0` or more and finite (`0` turns it off); startup refuses anything else |
| `MaxPartySize`                   | int    | `6`        | Most characters in one party; a party's instance also holds at most `min(this, the map's MaxPlayers)`. Range `2`–`40` |
| `AbandonedInstanceLifetimeMinutes` | int  | `15`       | Minutes an abandoned dungeon instance (a Normal map's, solo or a party's, that a player entered and that is now empty) lives: re-entry within it returns the same instance, and the per-tick expiry pass frees it once it has passed. `0` frees it on the next tick, so the next entry builds a new one (a load-test world). An instance nobody has entered yet keeps a fixed 15 minutes; towns never expire. `0` or more; startup refuses a negative value. Chart value `server.game.abandonedInstanceLifetimeMinutes`, rendered only when set ([instanced maps](instanced-maps.md#expiry-cleanup)) |
| `PartyInviteTimeoutSeconds`      | int    | `60`       | Seconds a party invite stays open before it expires. Range `1`–`3600` |
| `PartyLeaveGraceSeconds`         | int    | `60`       | Seconds a character who stopped being a member may stay in the party's instance before it is moved to town. Range `1`–`3600` |
| `PartyReturnRetrySeconds`        | int    | `5`        | Seconds before that move to town, when it failed (a town lookup or build that faulted), is tried again, at most 5 times while the character is still in that instance and not back in the party (#700). Range `1`–`3600` |
| `PartyExperienceModeCooldownSeconds` | int | `60`      | Seconds after a switch of the party's experience mode before the leader may switch it again. Range `0`–`86400` (`0` turns the wait off) |
| `PartyHealthPerExtraPlayer`      | float  | `0.6`      | Creature health added per player present beyond the first in a party's instance, as a share of its base (`0.6` is +60 %). `0` or more; startup refuses anything else |
| `PartyEligibilityRange`          | float  | `60`       | Metres, on X/Z, from a corpse within which a party member not in the creature's encounter still shares the kill's loot and experience. At least `1`; its own setting, not tied to `InterestRadius` |
| `PartyExperienceBonusPerExtra`   | float  | `0.10`     | Experience added to a shared kill per counted member beyond the first (`0.1` is +10 %). `0` to `10`; startup refuses anything else |
| `PartyExperienceLevelGap`        | int    | `5`        | A character this many levels or more above a creature gets no experience from it, in a party or solo. Range `1`–`1000` |
| `MaxActiveQuests`                | int    | `20`       | How many quests a character may hold at once; an accept past it is answered `LogFull` (#433). Range `1`–`100` |
| `MaxIgnoredCharacters`           | int    | `50`       | How many characters one character may ignore (#723); `/ignore` past it is refused with a system line. Range `1`–`500` |
| `MaxAurasPerUnit`                | int    | `32`       | How many auras one unit may hold at once; a new aura past it is refused and logged (auras). Range `1`–`256` |
| `ChatMessagesPerMinute`          | int    | `10`       | How many player chat messages one character may send in any sliding 60 seconds (#722). Plain chat, `/p`, `/w` and `/ignore` (#723) share the one budget; other commands (`/invite`, `/pvp`, ...) are not counted, and a message that is refused (unknown whisper target, not in a party, usage error) does not use any of it. A message over the limit is not delivered and the sender is told how many seconds to wait. `0` or below turns the limit off. The default is in `appsettings.json`, not in code |
| `TickThreadGuard`                | bool   | `false`    | Turns on the tick-thread assertion (#639): instance membership, the instance registry's indexes, parties, who is online and ignore lists throw `InvalidOperationException` when changed from any thread but the world tick. For development and tests (set `Game__TickThreadGuard=true` in the environment of a local World server); off, each check costs one read of a flag. Read once, when the world server starts |

```json
"Game": {
  "WorldId": 1,
  "PlayerRadius": 2000,
  "MaxCharactersPerAccount": 5,
  "CharacterLoadTimeoutSeconds": 15,
  "ScriptHotReloadIntervalSeconds": 5,
  "ExperienceBandDecay": 0.75,
  "CreatureLocomotion": "Waypoint",
  "CrowdIncludesPlayers": false,
  "CreatureAgentRadius": 0.6,
  "MeleeSlotCount": 6,
  "MeleeSlotRadius": 1.5,
  "MaxMoney": 9999999999,
  "CharacterSaveInterval": "00:05:00",
  "PvpOffDelay": "00:05:00",
  "InterestRadius": 60,
  "InterestRemoveMargin": 10,
  "FuryFromDamageTaken": 50,
  "FuryDecayPerSecond": 5,
  "MaxPartySize": 6,
  "AbandonedInstanceLifetimeMinutes": 15,
  "PartyInviteTimeoutSeconds": 60,
  "PartyLeaveGraceSeconds": 60,
  "PartyReturnRetrySeconds": 5,
  "PartyExperienceModeCooldownSeconds": 60,
  "PartyHealthPerExtraPlayer": 0.6,
  "PartyEligibilityRange": 60,
  "PartyExperienceBonusPerExtra": 0.10,
  "PartyExperienceLevelGap": 5,
  "MaxActiveQuests": 20,
  "MaxIgnoredCharacters": 50,
  "MaxAurasPerUnit": 32,
  "ChatMessagesPerMinute": 10
}
```

### Creature locomotion

`CreatureLocomotion` selects which `ICreatureLocomotion` each `MapInstance` builds:

- **`Waypoint`** (default) — walks a navmesh path with no awareness of other agents. Two creatures sent to the same point occupy it.
- **`Crowd`** — DotRecast `DtCrowd`. Agents actively steer around one another, and around players when `CrowdIncludesPlayers` is set. A map with no baked navmesh falls back to `Waypoint` and logs a warning naming the map.

Under `Crowd`, player agents exist only as obstacles: `PlayerInputHandler` remains the sole authority on where a character is, and the crowd never writes a character's position.

Before enabling `Crowd` on a busy map, note that DotRecast budgets roughly 25 agents per crowd at about 0.5 ms per frame, there is one crowd per `MapInstance`, and there is no agent cap.

### Creature levels, stats and the experience band

A creature's level is rolled from its template's `MinLevel`–`MaxLevel` at spawn, except on a procedural map with
depth bands (`ProceduralDepthBands`), where it is rolled from the band of the piece it spawns in (set pieces from the
highest band, the boss at its top); see docs/map-generation.md. Health, damage and
experience then come from the `CreatureBaseStats` row for that level, scaled by the template's own
modifiers and by its `CreatureRarity` (`Normal`, `Elite`, `Rare`, `Boss`) through
`CreatureRarityModifiers`. Both tables are seeded and tuned as data, so rebalancing is a migration
rather than a code change.

A template may set `Exp` to override the derived experience outright — `null` means derive, and any
value including `0` is used verbatim, which is why the column is nullable.

`MapTemplate.MinLevel`/`MaxLevel` **do not constrain spawning.** A level 6 creature in a 1–5 map is
legal. The band's only job is scaling rewards: a player outside it earns
`ExperienceBandDecay ^ levelsOut` of the experience, symmetrically and with no grace, so one level out
pays 75% and nine levels out pays 7.5%. A map with either bound unset is unbanded and scales nothing.

These numbers are provisional. They are calibrated against a player whose health does not change with
level, which is a known gap — creature and character numbers are to be revisited together once gear and
character stat scaling exist to compensate.

### Melee slots and attack range

`MeleeSlotRadius` **must not exceed the creature attack range** (`CreatureCombatScript.AttackRange`, currently `1.5`). Set it higher and creatures walk to their slot, arrive, and stand outside attack range dealing no damage. The range attribute permits it, so the World server logs a warning at instance construction naming both values.

The slot count is bounded by the ring's circumference. At radius `1.5` there are about `9.42` units of ring; with a `1.2` agent diameter (twice the default `CreatureAgentRadius`), six slots leave `1.57` between adjacent centres, while eight leave `1.18` — narrower than one creature.

---

## REST API Services

The REST API is one binary that runs the API services `Application:Services` names (#794,
[API services](api-services.md)). Production runs each service in its own release (#802).

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Application:Services` | string[], or one string | unset: all four | The services this process runs, any of `identity`, `worlds`, `commerce` and `distribution`, without regard to case; they run in that order whatever the order given. Unset, the process runs all four. An empty list or an unknown name stops startup, naming the setting. Helm `services`, rendered as `Application__Services__<n>` only when given |
| `Application:Startup:AuthSchemaWaitSeconds` | int | `300` | How long a process without identity waits at startup until the auth database has no migration its build knows of pending (identity migrates it), checking every 5 s, before it fails, naming the setting. Whole seconds, at least 1. Helm `startup.authSchemaWaitSeconds`, rendered only for a release without identity, together with a `startupProbe` that allows the wait and a minute more |

```bash
Application__Services__0=worlds
Application__Startup__AuthSchemaWaitSeconds=300
```

What each service reads. A process reads the union for the services it runs, and nothing it does not read is required
of it; `appsettings.json` keeps every service's non-secret defaults.

| Setting | identity | worlds | commerce | distribution |
|---|:-:|:-:|:-:|:-:|
| `Application:Services` | yes | yes | yes | yes |
| `Database:Auth:ConnectionString` | yes | yes | yes | yes |
| `Database:Worlds:<id>:Characters:ConnectionString` | yes | yes | | |
| `Database:Worlds:<id>:World:ConnectionString` | | yes | | |
| `Application:Cache:{Host,Username,Password}` | yes | yes | yes | |
| `Application:Authentication:{ValidationKeys,Issuer,Audience,ValidateIssuer,ValidateAudience,ClockSkewInMinutes}` (token validation) | yes | yes | yes | yes |
| `Application:Authentication:{SigningKey,SigningKeyId}` and `Application:GameAuth:HostKey` (any other process refuses to start with `SigningKey` set) | yes | | | |
| The rest of `Application:Authentication` (token lifetimes, the refresh cookie, the login, registration and email-change limits) | yes | | | |
| `Application:StoreAuthentication:*`, `Application:SteamWebLink:*`, `Application:GameWorkloads:*`, `Kestrel:Endpoints:GameInternal:*`, `Application:Email:*`, `Application:Notification:*` | yes | | | |
| `Application:StoreAuthentication:{Environment,SteamIdentityPrefix}` | yes | | yes | |
| `Application:Templates:*`, `Application:MapAssets:*`, `Application:PublicWorldId`, `Application:PublicSiteUrl`, `Application:Previews:*`, `Application:Balance:*` | | yes | | |
| `Application:Commerce:*` | | | yes | |
| `Application:Distribution:*` | | | | yes |
| `Application:ForwardedHeaders:*`, `Application:RateLimiting:{Enabled,AnonymousPermitsPerMinute,AuthenticatedPermitsPerMinute,ExemptSources}`, `Application:ApiDocs:Enabled` | yes | yes | yes | yes |
| `Application:RateLimiting:{ClientAuthPermitsPerMinute,WorkloadPermitsPerMinute}`, `Application:LoadTest:*` | yes | | | |
| `Application:Startup:AuthSchemaWaitSeconds` | | yes | yes | yes |

Commerce binds `Application:StoreAuthentication` without identity's validator, since it reads only those two keys
(purchases and licences are scoped by them); a process running both binds the section once. The Helm chart renders a
service's values only into a release that runs it, and a chart-managed Secret holds only that release's keys
([API services](api-services.md#deploying-with-the-chart)); its `ci/test.sh` checks that a release of each service
alone renders exactly the settings of that service's column the chart sets.

**Local runs.** Identity refuses to start without `Application:StoreAuthentication:SteamAppId` and
`SteamPublisherKey`, in every environment; the API's `appsettings.Development.json` holds placeholders (Steam's test
app id 480 and a dummy publisher key), which only a Steam sign-in would use. A local world also needs
`Application:GameWorkloads:Servers`, the named Kestrel endpoints (`Public`, `PublicHttp` and `GameInternal` with its
certificate) and, on the world server, `Hosting:Security` and `World:Admission`: the Aspire AppHost passes them as
environment variables, and `tools/Avalon.LocalDev setup` writes them to the user-secrets of the API and of the world
server ([Development setup](development-setup.md#from-clone-to-client-in-world)).

---

## REST API Worlds

The API serves any number of worlds, one per `Worlds` row in the auth database (#523). Each world has its own world database (content) and characters database; only the auth database and Redis are shared. Adding a world is a configuration change only. The world servers are unchanged: each serves one world, from its own `Database:World` and `Database:Characters`.

| Key | Description |
|-----|-------------|
| `Database:Auth:ConnectionString` | The shared auth database |
| `Database:Worlds:<id>:World:ConnectionString` | World `<id>`'s content database |
| `Database:Worlds:<id>:Characters:ConnectionString` | World `<id>`'s characters database |

`<id>` is the world's id in the auth `Worlds` table. The environment form is `Database__Worlds__2__World__ConnectionString`. A process reads only the strings of the databases its services read: both for worlds, the Characters string for identity, none for commerce or distribution.

```json
"Database": {
  "Auth": { "ConnectionString": "…" },
  "Worlds": {
    "1": { "World": { "ConnectionString": "…" }, "Characters": { "ConnectionString": "…" } },
    "2": { "World": { "ConnectionString": "…" }, "Characters": { "ConnectionString": "…" } }
  }
}
```

**Startup refuses to run**, in a process that reads a world database, naming the setting (never its value), when there is no world, a world id is not a positive integer up to 65535 written without leading zeros or sign, a world lacks a string the process reads, or a string is blank. The check runs right after the other startup validations (see [Startup Validation](#startup-validation)), before any database call. It is not an options validation, so OpenAPI generation, which skips that startup work, needs no world configured.

**At startup** a process running identity migrates the auth database (a failure stops it), and any other waits for it ([REST API Services](#rest-api-services)). Then each process checks the world databases it reads, without migrating them: each world server migrates its own. A world that cannot be reached is logged with its id and the exception type and answers 503, while the other worlds serve, until a recheck in the background reaches its databases (after 5 seconds, then at doubling intervals of at most a minute; [Multi-world API](api-worlds.md#startup-and-availability)). Every unreachable world adds the driver's connect timeout to startup.

**Routes:** world content and characters are under `/world/{worldId}/...`. A world this API is not configured for, or that the caller may not enter, answers 404 (the same 404 either way); an unavailable world answers 503. `GET /character` lists the caller's characters on every world, with `unavailableWorlds`; `GET /world` says for each world whether it is `configured` and `available`. The anonymous `/public/...` routes are `GET /public/world`, `GET /public/world/{worldId}/item/{id}` and `GET /public/world/{worldId}/ability/{id}`: they serve only worlds every player may enter unless the caller is signed in and may enter more, the list omits unavailable worlds, and any other world answers the same 404 (503 when its databases failed). Character ids are unique only within one world, so a consumer keys a character by `(worldId, id)`. The Redis presence keys name the world too (`presence:world:{worldId}:character:{id}`, #556), and one character's presence is `GET /world/{worldId}/observability/character/{id}`, under the same 404/503 world check; `GET /observability/online` and `GET /observability/instance/{instanceId}` stay cross-world.

**Helm:** a `worlds` map, keyed by world id. A release renders the strings of the parts its services read; one that runs only commerce or distribution ignores it.

- Chart-managed Secret (no `existingSecret`): give `worlds.<id>.world.connectionString` and `worlds.<id>.characters.connectionString`, from files (`--set-file`). The chart's Secret holds them under `database-world-<id>-connection-string` and `database-characters-<id>-connection-string`.
- `existingSecret`: give no strings. Your Secret holds each world's two keys, under those default names or the names you set in `worlds.<id>.worldKey` / `worlds.<id>.charactersKey` (custom names are accepted only with `existingSecret`). A world with no custom names is still listed, for example `--set worlds.2.worldKey=`. Add the keys to the Secret before upgrading.
- The auth string, `database.auth.connectionString`, is required with a chart-managed Secret (from a file, `--set-file`; trimmed, so a blank one is refused too) and must be left empty with `existingSecret`, whose Secret holds it under `database-auth-connection-string` (#564).
- The chart refuses to render with no world, a world id the API would refuse, a world missing one of its strings (chart-managed), a string given inline with `existingSecret`, a key that is not a valid Secret key name, a key two settings would read (the chart's own keys included), and the removed `database.world` / `database.characters` values.

The API's `appsettings.json` lists no world, so the published image ships none. Local development gets world 1 (the docker compose databases) from `appsettings.Development.json`, which only the Development environment loads, and the Aspire AppHost sets the same pair. Every other environment has exactly the worlds its environment variables or Helm values give it, and a process that reads a world database refuses to start with none.

The settings below are the worlds service's.

**`Application:PublicWorldId`** (optional `ushort`): the world `GET /public/world` names as `defaultWorldId`. It falls back to the first world the caller may read when unset, or when that world is unavailable or not readable by the caller, so set it to the live world in a deployment.

**`Application:PublicSiteUrl`** (optional string, no default; Helm `publicSiteUrl`): the public website's base URL, e.g. `https://avalon.example`. It must be an absolute http or https URL with no query or fragment (a trailing slash is dropped); anything else stops the API at startup, naming the setting. `GET /public/preview/item/{id}` and `/ability/{id}` (link previews for bots that run no JavaScript; `?world=N`, else the default world) name `<PublicSiteUrl>/item/{id}` as the page's `og:url`, adding `?world=N` only when `world` was given. Unset, a preview leaves `og:url` out and its link is relative (`/item/{id}`).

**`Application:Templates`** (live template editing; defaults in the API's `appsettings.json`):
- `EditableWorlds` (list of world ids, default empty; Helm `templates.editableWorlds`, rendered as indexed env `Application__Templates__EditableWorlds__0`, `__1`, ...): the worlds on which an admin may `PUT` item, ability and creature templates. A `PUT` for any other world answers 403 `This world is not editable`, so with the default no world is editable. An edit reaches the running world on save, so keep production worlds out. The Aspire AppHost lists the local Development world (the id it keys the world databases by).
- `ReloadTimeout` (TimeSpan, default `00:00:10`; Helm `templates.reloadTimeout`): how long a save waits for the world server to answer its reload request over Redis before the response reports the reload `pending`. The save is already committed either way. It must be greater than zero, else the API refuses to start, naming `Application:Templates:ReloadTimeout`.

Both Helm values render only when set.

**`Application:Previews`** (defaults in the API's `appsettings.json`): `SiteName` (`og:site_name` and the not-found title; left out when empty), `AbilityColour` and `RarityColours` (an `ItemRarity` name to a `#RRGGBB` colour; the `theme-color` of an ability and of an item of that rarity). They mirror the Dashboard's `rarity.ts`. A missing or malformed colour leaves `theme-color` out; it never fails the request. Override one with e.g. `Application__Previews__RarityColours__Epic`.

---

## World Maintenance and Readiness

Maintenance is stored per world in the shared auth database (`MaintenanceEnabled`, revision, and a UTC deadline). It survives a world server restart. The world process loads it after the world and before opening the listener, subscribes to revision notifications, and rereads the database every five seconds if a notification is missed. Both reads run off the simulation tick; the next tick applies the newest revision read, then sends the warning due and, at the deadline, closes the sessions. Players may enter throughout the scheduled countdown. At the deadline only Admin accounts may enter; the listener stays open so they can verify access.

Admin operators can use `POST /world/{id}/maintenance` with optional JSON `{"graceMinutes": 5}`, `DELETE /world/{id}/maintenance`, and `GET /world/{id}/maintenance`. The grace defaults to five minutes and accepts whole minutes from 1 to 60. An Admin already in that world can also use `/maintenance on [minutes]`, `/maintenance off`, and `/maintenance status`. Repeating `on` preserves the first deadline; `off` cancels the remaining warnings and advances the revision.

The public `WorldStatus` is derived: an active cutoff gives `Maintenance`; before the deadline a fresh ready heartbeat gives `Online`, and no heartbeat gives `Offline`. The world refreshes a five-second Redis heartbeat each second only while its listener is open and simulation ticks complete. The API's `WorldDto.Ready` reports that heartbeat separately from `Available`, which means the API process serving the request has that world configured and reached its databases at startup. World create and general update requests cannot set status.

At enable, the world broadcasts a System chat warning. It warns again at three minutes, one minute, thirty seconds, and every second from ten through zero that falls within the grace. At zero it sends the warning before a maintenance disconnect, stops processing authenticated non-Admin packets, then runs the normal despawn and save path. Admin sessions remain connected. The REST game admission (the world list it offers, a join ticket's issue and its redemption), character select, and final spawn enforce the deadline; if the authoritative maintenance row cannot be read, a new entry is refused.

A non-Admin gets no join ticket for a world past its deadline (`WorldUnavailable`), and the auth server's world list shows that world as `Maintenance`, so the client should show a maintenance message for such a world. It must also understand `DisconnectReason.Maintenance` from the shared wire schema and show a maintenance message for it.

## World Shutdown Drain

Section in `appsettings.json`: `"World:Shutdown"` (World server only, #768).

| Key          | Type     | Shipped    | Purpose |
|--------------|----------|------------|---------|
| `DrainTime`  | TimeSpan | `00:00:00` | How long a stopping world warns its players and waits for them to leave. `0` closes everyone at once, as before |
| `SaveMargin` | TimeSpan | `00:00:30` | What the close and the character saves get after the drain. At least 21 s: the shutdown's 20 s wait for saves (`WorldServer.DefaultSaveDrainLimit`) plus the drain's 1 s backstop |

On a stop (SIGTERM: a rollout, a node drain, an eviction) the world stops its listener, then warns players on the maintenance schedule ("The world restarts for an update in 5 minutes.", then 3 min, 1 min, 30 s and each second from 10, then "Restarting now."). The drain ends at its deadline, or as soon as no non-Admin player is connected; the world then closes every session, Admins included, and saves. The drain is held in memory only: it never touches the persisted maintenance row, so the next start is not in maintenance, and persisted maintenance runs on beside it. During the drain the realm list shows the world `Offline`, since its ready heartbeat has stopped.

The host's stop timeout is `DrainTime + SaveMargin` (30 s with the shipped values, the .NET default). The Helm chart takes `shutdown.drainSeconds` (default 0) and `shutdown.saveMarginSeconds` (default 60, at least 21), renders them into `World__Shutdown__DrainTime` and `World__Shutdown__SaveMargin`, and sets `terminationGracePeriodSeconds` to drain + margin + 15, so Kubernetes never kills the pod mid-countdown.

---

## World Metrics Export

The world server exports traces, metrics and logs over OTLP (`UseOtlpExporter` in `Avalon.ServiceDefaults`) when
`OTEL_EXPORTER_OTLP_ENDPOINT` is set. The OpenTelemetry SDK reads its `OTEL_*` settings through `IConfiguration`. The
world chart renders them from `otel` values, and only when `otel.endpoint` is set:

| Value | Renders | Default | Purpose |
|---|---|---|---|
| `otel.endpoint` | `OTEL_EXPORTER_OTLP_ENDPOINT` | empty: nothing is exported | The collector, e.g. `http://otel-collector:4317` (OTLP gRPC) |
| `otel.serviceName` | `OTEL_SERVICE_NAME` | the release's full name | The `service.name` resource attribute |
| `otel.resourceAttributes` | `OTEL_RESOURCE_ATTRIBUTES` | `{}` | Extra resource attributes, e.g. `{deployment.environment: production}`. `avalon.world.id` always comes first, from `server.game.worldId`, and a value for it here is ignored |
| `otel.metricExportIntervalMs` | `OTEL_METRIC_EXPORT_INTERVAL` | empty: the SDK's 60000 | How often metrics are exported, in milliseconds. A positive integer; with `otel.endpoint` set, anything else refuses to render (with no endpoint the value is not read) |

The load-test world sets `otel.metricExportIntervalMs: 10000`, so each 60-second window of a
[load-test ramp](load-testing.md) holds six samples of every series. A rate needs two samples in its window, and with
the default 60 seconds a 60-second window has one. Leave it unset elsewhere: a shorter interval multiplies what every
world pushes to the collector and stores in Prometheus.

---

## REST API JWT Signing Key

Sections: `Application:Authentication` and `Application:GameAuth` (**the private keys are never committed to source
control**, #482). Access tokens are ES256 only since #801: identity signs them with its private key under a key id,
and every API service checks them with the public key that key id names, so no other service can mint one.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Application:Authentication:SigningKey` | string | _(required by identity)_ | The EC P-256 private key identity signs with, PKCS#8, as PEM or as the base64 of its DER. A process that does not run identity refuses to start while it is set (an empty value counts as unset) |
| `Application:Authentication:SigningKeyId` | string | _(required by identity)_ | The key id (`kid`) written into each token's header, e.g. `2026-10`: at most 64 letters, digits, `.`, `_` and `-` |
| `Application:Authentication:ValidationKeys:<key id>` | string | _(none)_ | A public key, the base64 of its DER SubjectPublicKeyInfo (or PEM); not secret. Every service checks a token against the key its key id names. A process that signs accepts its own key without it listed (a listed key under its key id must be its own); any other refuses to start with none |
| `Application:GameAuth:HostKey` | string | _(required by identity)_ | The key identity's game-auth cryptography (proofs, replay receipts, the Steam OpenID state) derives its keys from, at least 32 bytes. Identity refuses to start without it. A deployment from before #801 gives it the value of its old HS256 key (`Application:Authentication:IssuerSigningKey`), from which they were derived then, so what was protected with them stays readable |

`JwtKeys.Create` runs while the host is built (`AvalonApiHost.CreateBuilder`), in every API process whatever its
services, and stops startup, naming the setting, for a missing or unparsable private key, a key on another curve than
P-256, a missing or malformed key id, a public key that does not parse or holds a private key, a public key listed
under identity's key id that is not its own, a private key in a process that does not sign, and a process with no key
to validate with. The game-auth host key is refused (`GameAuthHostKey.From`) when it is missing, has leading or
trailing whitespace, is shorter than 32 bytes in UTF-8, or is the HS256 key once committed to `appsettings.json`
(public now); it is checked before identity serves (`IdentityStartupCheck`), so the docs build needs none. A token is
checked only against the public key its key id names, and only when it is ES256 (`JwtKeys.Resolve`): `ValidAlgorithms`
is ES256 alone, so an HS256 token, an HMAC keyed with a public key, or a token naming an unknown key id finds no key.
HS256 is refused whatever is configured: `Application:Authentication:IssuerSigningKey`, the HS256 key of before #801,
is ignored, and a process that still has it logs one warning at startup naming it. The process logs at startup the key
ids it accepts. A process with `AVALON_OPENAPI_GENERATION_ONLY=true` makes a throwaway key in memory. The keys are
deliberately absent from `appsettings.json`.

```bash
# Development: user-secrets (the Avalon.Api project has a UserSecretsId); see docs/development-setup.md
dotnet user-secrets set "Application:Authentication:SigningKey" "$(cat jwt-es256.pem)" --project src/Server/Avalon.Api

# Everywhere else: environment variables
Application__Authentication__SigningKey=<the private key, PEM or the base64 of its DER>
Application__Authentication__SigningKeyId=2026-10
Application__Authentication__ValidationKeys__2026-10=<the base64 of the public key>
Application__GameAuth__HostKey=<random value, at least 32 bytes>
```

The Helm chart passes the private key and the host key, with the other secrets, through a Kubernetes Secret
(`secretKeyRef`), and only where identity runs: either one you manage, named by `existingSecret`, with the keys
`jwt-signing-private-key` and `game-auth-host-key`, both required, or one the chart creates from
`--set-file authentication.signingKey=<file>` and `--set-file gameAuth.hostKey=<file>`. The key id and the public keys
are plain values (`authentication.signingKeyId`, required where identity runs, and
`authentication.validationKeys.<key id>`, required where it does not). The chart renders no HS256 key. It refuses the
removed `authentication.legacyIssuerSigningKey`, whatever its value, saying to remove it and the Secret's old key
`jwt-signing-key`, and the removed `authentication.issuerSigningKey`, whose value belongs in `gameAuth.hostKey`. Key
generation and rotation: [Development setup](development-setup.md#rest-api-signing-key).

---

## REST API Docs

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Application:ApiDocs:Enabled` | bool | `false` | Serves the OpenAPI document (`/openapi/v1.json`) and Scalar (`/scalar`) outside Development (#803). In Development they are always served; elsewhere only with this on. The Helm chart never sets it. The published document comes from the docs build (`AVALON_OPENAPI_GENERATION_ONLY`), which does not depend on it |

---

## REST API Login Limits

Section: `Application:Authentication`, read by identity (#478)

| Key                              | Type | Default | Description |
|----------------------------------|------|---------|-------------|
| `MaxFailedLoginAttempts`         | int  | `5`     | Password and MFA-code attempts at one username, from every source and both servers, before it is locked |
| `LockoutDurationMinutes`         | int  | `15`    | That budget's window, and how long the lock lasts |
| `MaxFailedLoginsPerSource`       | int  | `10`    | Attempts one source address may make, across every account, per window |
| `FailedLoginSourceWindowMinutes` | int  | `15`    | The source budget's window |
| `MaxFailedMfaAttempts`           | int  | `5`     | Codes one MFA hash allows |

The REST login, MFA verify and the current-password checks (password change, `POST /mfa/setup`,
`POST /pat`, `POST /pat/admin`) run the Auth server's login policy over **the same Redis keys**, so these
must equal the Auth server's `Application:*` values of the same names: a key counted against two
different limits locks at whichever is lower. The defaults match. The section is bound without validated
options, so identity's registration (`AddIdentity`) refuses a value below `1` at startup, naming the setting.

```bash
Application__Authentication__MaxFailedLoginAttempts=5
```

Each host logs its five limits at Information when it starts (the Auth server as `Application`, an API
process running identity as `Application:Authentication`), so the two lines can be compared.

The per-source budget keys on the caller's address after the forwarded headers are applied, so behind a
proxy it needs [REST API Forwarded Headers](#rest-api-forwarded-headers) set up.

---

## REST API Forwarded Headers

Section: `Application:ForwardedHeaders`, read by every API service (#478 review)

| Key             | Type     | Default | Description |
|-----------------|----------|---------|-------------|
| `KnownProxies`  | string[] | `[]`    | Addresses of proxies whose `X-Forwarded-For` is believed, such as `10.0.0.2` |
| `KnownNetworks` | string[] | `[]`    | Networks of such proxies in CIDR form, such as a cluster's pod network `10.0.0.0/8` |
| `ForwardLimit`  | int      | `1`     | Proxy hops read from `X-Forwarded-For`, from the right |

Loopback is always trusted, and nothing else is by default, which fails safe: a caller cannot choose its own
address. The address decides the caller's login source budget, so an ingress left out of these makes every
REST caller one source, and ten failures by anyone refuse REST logins for everyone for the window. Set them
wherever the API runs behind a proxy (docker, Kubernetes ingress, CDN), as narrowly as the deployment
allows.

- Startup refuses, naming the setting:
  - an entry that does not parse, or a network without a prefix;
  - a network wider than **/8 for IPv4** or **/32 for IPv6** (`0.0.0.0/0` and `::/0` included). A network
    that broad is not a proxy network, and every caller on it could choose its own source;
  - a `ForwardLimit` below 1.
- Outside Development the API logs a warning at startup when `KnownProxies` and `KnownNetworks` are both
  empty.
- A request that carries `X-Forwarded-For` from a peer that is not trusted is served with the peer's own
  address, and logged as a warning at most once a minute, with the number not logged since.
- A request with no peer address has its `X-Forwarded-*` headers removed before they are read (and is
  logged the same way), so it keeps no address and still gets the 400 below.

```bash
Application__ForwardedHeaders__KnownNetworks__0=10.0.0.0/8
Application__ForwardedHeaders__KnownProxies__0=10.1.2.3
Application__ForwardedHeaders__ForwardLimit=1
```

The Helm chart passes `forwardedHeaders.knownProxies`, `forwardedHeaders.knownNetworks` and
`forwardedHeaders.forwardLimit` (empty lists and 1 by default). The chart has no ingress of its own, so fill
them in for whatever ingress fronts the service.

A caller with no peer address at all is refused with 400 on the endpoints that spend a login source budget
(login, registration, MFA verify, password change, starting an email change, MFA setup, token minting).

---

## REST API Rate Limiting

Section: `Application:RateLimiting`, read by every API service (#561)

| Key                             | Type | Default | Description |
|---------------------------------|------|---------|-------------|
| `Enabled`                       | bool | `true`  | When false, nothing is limited |
| `AnonymousPermitsPerMinute`     | int  | `60`    | Requests a minute per source for a caller that is not signed in |
| `AuthenticatedPermitsPerMinute` | int  | `300`   | Requests a minute per account for a caller with a valid access token or personal access token |
| `ClientAuthPermitsPerMinute`    | int  | `20`    | Identity: launcher sign-in requests (`client/auth`: code, token, refresh, revoke) a minute per source, on top of the above (#591) |
| `WorkloadPermitsPerMinute`      | int  | `16384` | Identity: requests a minute per authenticated game server on the game workload listener, every player's lease heartbeat included, counted instead of the limits above ([game server admission](steam-authentication-workloads.md)) |
| `ExemptSources`                 | string[] | `[]` | Sources exempt from every per-source limit, for load machines: IP addresses or networks in CIDR form. See [Exempt sources](#exempt-sources) below |

Every request counts, whatever the endpoint, in a sliding window of one minute in six segments, held in
memory: each API process counts the requests it serves, and each replica on its own. While one process runs
every service that is one budget; once the services run apart, a caller has one budget per service process
([API services](api-services.md#authentication-across-services)). A request with a valid access
token or personal access token is counted against its account, after the account has been revalidated;
any other, a request whose token is not valid included, against its source: the login budgets' rule, the
IPv4 address or the IPv6 /64, after the [forwarded headers](#rest-api-forwarded-headers) are applied.
Every caller with no peer address shares one partition. `/health` and `/alive` are never limited. The login
and registration budgets are separate and still apply.

A personal access token is looked up before the limiter, so it can be counted against its account, only on
an endpoint that requires authorization and only while limiting is enabled. Each source may fail that lookup
10 times a minute (a made-up or revoked token), with at most 2 of its lookups running at once; past either, its requests are counted as anonymous without the
lookup, so a flood of made-up tokens cannot force a database query per request. Authorization still answers
401 for any such request the limiter lets through.

A refused request gets 429 ProblemDetails with `Detail` `LOCKED` and a `Retry-After` header in seconds, the
same whichever partition refused it. `Retry-After` is a lower bound: the limiter's own hint when it gives
one, otherwise one segment (10 s), the soonest a permit can come back; a request sent then may still be
refused if the permits spent in older segments have not left the window yet. A refusal also increments the counter `avalon.api.rate_limit.rejections` (meter
`avalon-api`), tagged `partition=anonymous`, `partition=authenticated` or `partition=workload`.

The section is bound as `IOptions<RateLimitingConfig>` and validated at startup: a limit below `1` stops
the API, naming the setting.

```bash
Application__RateLimiting__AnonymousPermitsPerMinute=60
Application__RateLimiting__AuthenticatedPermitsPerMinute=300
Application__RateLimiting__ClientAuthPermitsPerMinute=20
```

The Helm chart passes `rateLimiting.enabled`, `rateLimiting.anonymousPermitsPerMinute`,
`rateLimiting.authenticatedPermitsPerMinute` and `rateLimiting.clientAuthPermitsPerMinute` (the last only to a
release that runs identity), each only when set, and `rateLimiting.exemptSources` (below); empty, the API's defaults apply. `WorkloadPermitsPerMinute` has no
chart value.

The launcher sign-in endpoints (`client/auth/*`, #591) also carry the named policy `client-auth`: a
separate sliding window per source (the same source rule), counted in addition to the limits above, so a
signed-in caller is held to it too. It answers with the same 429.

### Exempt sources

`ExemptSources` names the machines a load test runs from, so hundreds of bots behind one address are not refused as
one caller. A request whose source is listed (the address the limits count, after the
[forwarded headers](#rest-api-forwarded-headers)) skips every per-source limit: the anonymous partition, the
`client-auth` policy, the per-source failed-login budget (`MaxFailedLoginsPerSource`, which registration, MFA verify,
the current-password checks and starting an email change spend too) and the per-source account-creation cap.
Per-account and per-username limits still apply: a signed-in request from a listed source is counted against its
account, and each username keeps its failed-login budget and its lock. The auth server's TCP login is not affected.
[REST API authentication](api-authentication.md#rest-api-auth) has the details, the personal access token lookup an
exempt source makes among them.

Each entry is an address (`203.0.113.7`, that address only) or a network (`203.0.113.0/28`); an IPv4-mapped IPv6
entry is read as its IPv4 address. Startup refuses, naming the entry and why:
- an entry that does not read exactly as written: an IPv4 address must be four plain decimal parts (`10.1`,
  `010.0.0.5` and hex forms are refused, since the parser would read them as other addresses), and a network must
  have no address bits past its prefix (`10.1.0.5/16` is refused rather than widened to all of `10.1.0.0/16`);
- a network wider than **/24 (IPv4)** or **/64 (IPv6)**: an exempt range is a hole in every per-source limit, so it
  stays the size of a few machines;
- an entry that overlaps loopback, or a proxy `Application:ForwardedHeaders` trusts (`KnownProxies`,
  `KnownNetworks`): a trusted proxy forwards other callers, so exempting it would exempt everyone behind it.

List each load machine on its own (`/32`), and remove the entries once the run is over. Behind a proxy, the machine's
address is the one the API resolves only when that proxy is trusted in
[forwarded headers](#rest-api-forwarded-headers); an entry naming the proxy itself is refused.

```bash
Application__RateLimiting__ExemptSources__0=203.0.113.7/32
```

The Helm chart renders `rateLimiting.exemptSources` (empty by default) as indexed env into every release, since each
service process runs its own limiter.

---

## REST API Load-Test Accounts

Section: `Application:LoadTest`, read by identity

| Key           | Type | Default | Description |
|---------------|------|---------|-------------|
| `Enabled`     | bool | `false` | Turns on `POST` and `DELETE /admin/load-test/accounts`, which create and remove runs of load-test bot accounts ([API services](api-services.md#load-test-accounts)). Off, an admin's request to either gets the standard Not Found response (authorization still runs first) |
| `MaxAccounts` | int  | `5000`  | The most load-test accounts that may exist at once, across every run; a create that would pass it is refused with 409. At least 1 |

Bound as `IOptions<LoadTestOptions>` and validated at startup: a `MaxAccounts` below 1 stops identity, naming the
setting. Keep it off except where load tests run: the bots hold `Player | PTR`, so they can enter any world that
admits either.

```bash
Application__LoadTest__Enabled=true
Application__LoadTest__MaxAccounts=5000
```

The Helm chart renders `loadTest.enabled` and `loadTest.maxAccounts` into a release that runs identity, and into no
other, each only when set (empty, the defaults above apply); a `maxAccounts` that is not a whole number of at least 1
refuses to render.

---

## REST API Email

Section: `Application:Email`, read by identity (#510)

| Key               | Type   | Default                                   | Description |
|-------------------|--------|-------------------------------------------|-------------|
| `Sender`          | enum   | `None`                                    | `None`, `Pickup` or `Resend`. Which email sender the API uses |
| `PickupDirectory` | string | `avalon-mail` under `Environment.SpecialFolder.LocalApplicationData` | Where `Pickup` writes its `.eml` files; created when missing, with mode 0700 on Unix |
| `From`            | string | none                                      | The address every email is sent from. Required, as a bare address (`noreply@example.com`, no display name), when `Sender` is `Pickup` or `Resend` |
| `FromName`        | string | none                                      | `Resend`: the display name of the `From` header; no control characters, `<`, `>` or `"` |
| `ResendApiKey`    | string | none                                      | `Resend`: its server API key, required, without whitespace. A secret: never logged; Helm reads it from `email.existingSecret` (key `email.resendApiKeyKey`, default `resend-api-key`) |
| `VerificationSiteOrigin` | string | none                               | The website origin the verification links (and the email-change confirm link) open: an HTTPS origin with no path, query, fragment or user info (in Development an http loopback origin too). Unset, current-address verification is off |
| `VerificationCooldownSeconds` | int | `60`                              | The least time between two verification emails to one account |
| `MaxVerificationSendsPerAccount` | int | `5`                            | Verification emails one account may request an hour |
| `MaxVerificationSendsPerSource` | int | `20`                            | Verification emails one source may request an hour |

Email change (`POST /account/email/change` and `/account/email/confirm`) is on only while a sender is
configured:

- `None`, the default, registers no sender. Both endpoints answer 501 "Email change is unavailable until
  email delivery exists".
- `Pickup` writes each email as an RFC 5322 `.eml` file (plain text, UTF-8) into `PickupDirectory`, which
  any mail client opens. Nothing leaves the machine. The files hold the confirm tokens, so it is
  **Development only**.
- `Resend` sends each email once through Resend's HTTP API, plain text, with a 30 s timeout and no retry: a send
  whose outcome is unknown is never repeated.

Current-address verification (`GET`, `POST /account/email/verification` and `POST /account/email/verification/confirm`)
needs a sender and `VerificationSiteOrigin`; without either, a request for a verification email answers 501. A
verification link lasts 30 minutes; a request within the cooldown, or past either hourly budget, is answered 429
`LOCKED`.

With a sender, starting a change sends the confirm token to the new address and a notice with no token to
the old one, and answers 202 with no body. If the confirmation cannot be sent, the pending change is
deleted and the answer is 503 "Email could not be sent"; a retry starts afresh. A notice that cannot be
sent is logged at Warning and the change still starts. Sender failures are logged by exception type and
recipient domain only, never the subject, body or token.

Sending is budgeted under `Application:Authentication` (#510 review). The slots are taken just before
the send, after every other check, and are never given back. Past either budget the answer is 429
`LOCKED`, and nothing is stored or sent. A new start whose confirmation is sent also voids the account's earlier pending change, so
only the latest token confirms. Each email is sent with its own 30 s timeout, never the request's token. The per-address send budget can be spent by any accounts, so three accounts can block an address as a change target for the window; account creation is capped per source, which keeps the impact small.

| Key                             | Type | Default | Description |
|---------------------------------|------|---------|-------------|
| `MaxEmailChangeSendsPerAccount` | int  | `3`     | Confirmations one account may have sent per window |
| `MaxEmailChangeSendsPerAddress` | int  | `3`     | Confirmations sent to one new address, from every account, per window |
| `EmailChangeSendWindowMinutes`  | int  | `60`    | The window, fixed from the first send |

- Startup refuses, naming the setting:
  - any of the three send-budget values above below 1, or any of the three verification values;
  - a `VerificationSiteOrigin` that is not such an origin;
  - `Sender` `Pickup` outside Development (`Application:Email:Sender`);
  - `Sender` `Pickup` or `Resend` with a missing or invalid `From` (`Application:Email:From`);
  - `Sender` `Resend` without a valid `ResendApiKey`, or with a `FromName` it refuses.
- At startup the API logs the sender and whether email change is on.

```bash
# Development only
dotnet user-secrets set "Application:Email:Sender" "Pickup" --project src/Server/Avalon.Api
dotnet user-secrets set "Application:Email:From" "noreply@avalon.monster" --project src/Server/Avalon.Api
```

The Helm chart renders `email` only into a release that runs identity: `email.sender` (`None` or `Resend`; it
refuses `Pickup`), `email.from`, `email.fromName`, `email.verificationSiteOrigin`, the three verification values,
and, with `Resend`, the key's Secret reference, which it requires together with `from` and `verificationSiteOrigin`.

---

## REST API Personal Access Tokens

The `Authorization: Avalon avp_...` scheme takes no configuration and there is no shared secret. Each
token belongs to one account and is minted by identity; only its SHA-256 hash is stored, and
`AvalonAuthenticationHandler` (`Avalon.Api.Hosting`) looks it up per request in every API service. See [Security — Session Management](security-session-management.md#rest-api-authentication).

---

## Balance Service

The balance service (`Avalon.Balance.Service`) is reached only from inside the cluster, by the API's worlds service.
Both sides hold the same shared secret.

**Service** (`Balance`, as `Balance__<Name>` in the environment):

| Setting | Default | Meaning |
|---------|---------|---------|
| `SharedSecret` | none, required | The value callers send in `X-Balance-Secret`; at least 32 characters, else the service refuses to start. `/health` and `/alive` do not check it |
| `GitHubToken` | empty | A fine-grained token for exports; without it `POST /exports` answers 503. Never logged |
| `Repository` | `WoozChucky/Avalon.Server` | The repository exports open their draft pull request in |
| `MaxQueued` | 3 | Runs waiting behind the running one; one more gets 429 |
| `MaxRunsPerRow` | 1000 | The most `runsPerRow` a request may ask for |
| `MaxOverrides` | 500 | The most override keys a request may carry |
| `ResultTtl` | `01:00:00` | How long a finished run is kept before it answers 404 |
| `MaxRetainedFinished` | 100 | Finished runs kept; past this the oldest goes, whatever its age |
| `RunWorker` | `true` | False builds the worker paused, nothing drains the queue. A test seam |

Exports branch from the commit in the assembly's informational version (`+<sha>`), which CI sets with
`-p:SourceRevisionId="$(git rev-parse HEAD)"` (the checked-out commit; `github.sha` is main's head on a manual release); a build without it answers exports with 503.

**API** (`Application:Balance`, read by the worlds service):

| Setting | Meaning |
|---------|---------|
| `Url` | The service's in-cluster address, e.g. `http://avalon-balance:8080` |
| `SharedSecret` | Sent as `X-Balance-Secret`; the same value as the service's `Balance:SharedSecret` |

Both are needed: with either empty the admin `/balance/*` endpoints answer 503 and nothing else changes. In the
Helm charts the secret is the key `balance-shared-secret` of the Secret, for both charts; the api chart renders the
secret's reference and, only when set, `balance.url` as `Application__Balance__Url`, both only into a release that runs
worlds.

---

## Applying Configuration at Startup

These configuration classes (`GameConfiguration`, `RegenConfiguration`,
`AuthConfiguration`, and the `HostingConfiguration` both share) use:

1. A property with a data annotation (`[Required]`, `[Range(...)]`, `[RegularExpression(...)]`).
2. `services.AddOptions<TConfig>().BindConfiguration(section).ValidateDataAnnotations().ValidateOnStart()` in the appropriate DI extension method.
3. `IOptions<TConfig>` injection in the consuming class.

Example for `AuthConfiguration`:

```csharp
// In IServiceCollection extension:
services.AddOptions<AuthConfiguration>()
    .BindConfiguration("Application")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// In handler:
public class CAuthHandler(IOptions<AuthConfiguration> authConfig, ...)
{
    private readonly AuthConfiguration _authConfig = authConfig.Value;

    // Usage:
    if (UsernameBudget.Locks(_authConfig, taken))
    { ... }
}
```

---

## Environment-Specific Overrides

Use `appsettings.{Environment}.json` (e.g. `appsettings.Production.json`) to override defaults per environment without changing the base file. Sensitive values (database passwords, the REST API's JWT signing key) must come from environment variables, user-secrets or a secrets manager, not committed files.

```bash
# Environment variable override syntax (.NET):
Application__MinClientVersion=1.5.0
Application__MaxFailedLoginAttempts=10
Application__Authentication__SigningKey=<from-vault>
```

---

## Log Levels

Every host builds its Serilog logger in `AddCustomLogging` at a minimum of Debug, then reads
`Serilog` from configuration. `AddSerilog` passes every level through to Serilog, so Serilog's
minimum decides what is written, whatever `Logging:LogLevel` says. The API and the auth server
keep the Debug minimum, without the lines that only repeat:

- The API's `appsettings.json` raises to Warning `Microsoft.Extensions.Http` and `System.Net.Http.HttpClient` (the
  `HttpClient` factory's handler cleanup, a pair every ten seconds once a named client's handler has expired, and the
  per-request lines of every outgoing request), `Microsoft.AspNetCore.Hosting.Diagnostics` and
  `Microsoft.AspNetCore.Routing` (the framework's own lines for each request, which `RequestLoggingMiddleware`'s one
  line already covers) and `Microsoft.Extensions.Diagnostics.HealthChecks` (each probe's check run), and to
  Information `Microsoft.AspNetCore.Server.Kestrel` (each connection's open and close) and
  `Microsoft.AspNetCore.Authentication` (a request without a token; a token that fails validation stays an
  Information line). `RequestLoggingMiddleware` leaves out a health probe (`/health`, `/alive`) that answered 200; one
  that failed is logged like any request.
- The auth and world servers log a TCP connection that closed without sending a byte, which is what a load
  balancer's or the kubelet's TCP probe does every few seconds, at Trace, below their Debug minimum (#528): its
  disconnect, and on a proxy-protocol listener the dropped connection from the trusted proxy.
- The balance service has no Serilog; its `appsettings.json` sets `Logging:LogLevel` to Information with
  `Microsoft.AspNetCore` at Warning, so its probes write nothing.

Any of these categories can be lowered again through the environment, for example
`Serilog__MinimumLevel__Override__Microsoft.AspNetCore.Routing=Debug`.

The world server runs at **Information** (#819): its `appsettings.json` sets
`Serilog:MinimumLevel:Default` to `Information`, so its Debug lines are not written. They include the
combat path's routine lines, which run on the tick many times a second in a busy instance: every hit
on a character, every character and creature death, and every cast refused for its cost, interrupted
by movement or dropped because its caster left. Warnings and errors are unchanged. To see the Debug
lines again, lower the minimum through the environment:

```bash
Serilog__MinimumLevel__Default=Debug
```

On Kubernetes, set it through the world chart (#834): `logging.minimumLevel` (one of Verbose, Debug, Information,
Warning, Error, Fatal; empty keeps the default) and `logging.overrides`, a map from logging category to level that
renders `Serilog__MinimumLevel__Override__<category>`. One category can be lowered alone that way (for the combat
lines, `Avalon.World.Entities.CharacterEntity`, `Avalon.World.Abilities.InstanceAbilityCastSystem` and
`Avalon.World.Scripts.Creatures.CreatureCombatScript`). The chart refuses an unknown level.

---

## Database Logging

Two rules hold in the API, the auth server and the world server (#558):

- **Sensitive data logging is on only in Development.** It puts every command's parameter values
  (password verifiers, token hashes, MFA secrets on the auth database) in the logs. It is not a
  setting: `AddAvalonDatabases` sets `DatabaseConfiguration.EnableSensitiveDataLogging` from the
  host environment and overwrites whatever configuration says, so no configuration or Helm value
  can turn it on outside Development. The API's per-world contexts (`ConfiguredWorldDbContextFactory`)
  apply the same rule, `SensitiveDataLoggingAllowed`. A container with no `IHostEnvironment`, and the
  design-time factories used by `dotnet ef`, get it off.
- **Every `Microsoft.EntityFrameworkCore` category logs at Warning and above**, for every logging
  provider: Serilog (a minimum-level override in `AddCustomLogging`) and any other, such as the
  OpenTelemetry log exporter (a `Microsoft.Extensions.Logging` filter rule, general and again by
  provider name for `Serilog` and `OpenTelemetry`, so a provider's own `Default` level cannot lift
  it). To see the command log again in Development, raise the category per provider, and in
  Serilog, for example in an `appsettings.Development.json` next to the host:

```json
{
    "Logging": {
        "Serilog": { "LogLevel": { "Microsoft.EntityFrameworkCore.Database.Command": "Information" } },
        "OpenTelemetry": { "LogLevel": { "Microsoft.EntityFrameworkCore.Database.Command": "Information" } }
    },
    "Serilog": { "MinimumLevel": { "Override": { "Microsoft.EntityFrameworkCore.Database.Command": "Information" } } }
}
```

---

## Startup Validation

These configuration classes (`GameConfiguration`, `RegenConfiguration`,
`AuthConfiguration`, `HostingConfiguration`, the auth server's `HostingSecurity`, and
`CacheConfiguration` in all three hosts) opt into startup validation to fail fast on misconfiguration:

```csharp
.ValidateDataAnnotations()
.ValidateOnStart()
```

`DatabaseConfiguration` is validated at startup too, but by `DatabaseConnectionsValidation`
(`ValidateDatabasesOnStart` in `Avalon.Database`) rather than by annotations, because the databases a
host needs differ: the auth server needs `Database:Auth:ConnectionString`, the world server that and
`Database:Characters:ConnectionString` and `Database:World:ConnectionString`, and the REST API
`Database:Auth:ConnectionString`. An API process that reads a world database then checks `Database:Worlds` right
after these checks, in `ApiStartup` (`WorldDatabaseSettings`, see [REST API Worlds](#rest-api-worlds)), rather than as an
options validation, because OpenAPI generation starts the host with no world configured. The message
names the missing or malformed setting.

This causes the application to throw an `OptionsValidationException` at startup rather than at runtime when the missing/invalid value is first accessed.

Each host runs these checks itself, right after building the host and before its migrations and
its cache connection (`AuthStartup`, `WorldStartup`, `ApiStartup`), because `ValidateOnStart` alone
would run them only when the host starts, after that work had already failed on the missing value.
The API skips them, with the migrations, when `AVALON_OPENAPI_GENERATION_ONLY` is set.

The REST API's `Application:*` settings are bound directly, by the services that read them, rather than through
`IOptions<T>`, and do not use `ValidateOnStart`, except `Application:Cache`, bound as `IOptions<CacheConfiguration>` in
a process whose services need Redis and validated at startup like the servers' `Cache`, `Application:RateLimiting`,
bound only as `IOptions<RateLimitingConfig>` and validated the same way (see
[REST API Rate Limiting](#rest-api-rate-limiting)), and identity's `Application:LoadTest`, the worlds service's
`Application:Templates` and commerce's `Application:Commerce`, validated the same way. The rest are checked by hand while the host is built: in every
process `Application:Services` (`ApiServiceSelection`), the token keys (`JwtKeys.Create` in
`AvalonApiHost.CreateBuilder`, see [REST API JWT Signing Key](#rest-api-jwt-signing-key)) and the forwarded-headers entries
(`ForwardedHeadersSetup.BuildOptions` in `AddApiHosting`); in identity the login limits
(`LoginLimitsValidation.Validate`), the account-creation cap, the email-change send caps and the email sender
(`AddIdentity`, `AddEmail`); in worlds the public site URL (`PublicSiteSettings.Create`). Each check throws
`InvalidOperationException`, and its message names the setting.
