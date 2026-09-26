# Configuration Reference

This document covers all configuration keys for the Avalon server.

---

## Overview

Avalon uses strongly-typed configuration classes bound from `appsettings.json` (or environment variables / secrets manager in production) via `IOptions<T>`. Configuration classes reside in `src/Shared/Avalon.Configuration`.

---

## Configuration Classes

| Class                     | Namespace                    | Bound from                   |
|---------------------------|------------------------------|------------------------------|
| `DatabaseConfiguration`   | `Avalon.Configuration`       | `ConnectionStrings:*`        |
| `CacheConfiguration`      | `Avalon.Configuration`       | `Cache:*`                    |
| `AuthenticationConfig`    | `Avalon.Api.Config`          | `Application:Authentication:*` (REST API) |
| `HostingConfiguration`    | `Avalon.Configuration`       | `Hosting:*`                  |
| `AuthConfiguration`       | `Avalon.Server.Auth.Configuration` | `Application:*`        |
| `GameConfiguration`       | `Avalon.World.Configuration` | `Game:*`                     |

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
  "Issuer": "Avalon"
}
```

**Validation rules:**
- `MinClientVersion`, `ServerVersion`: required, must match `^\d+\.\d+\.\d+$` (SemVer).
- The five login limits: minimum `1`.
- `Issuer`: required, non-empty.

---

## Hosting Configuration (`HostingConfiguration`)

Section in `appsettings.json`: `"Hosting"`

| Key                     | Type   | Default  | Description                                             |
|-------------------------|--------|----------|---------------------------------------------------------|
| `Host`                  | string | `"0.0.0.0"` | Bind address                                         |
| `Port`                  | int    | `21000`  | TCP listen port                                         |
| `PacketReaderBufferSize`| int    | `4096`   | Internal read-buffer size in bytes for `PacketReader`   |

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

---

## Cache Configuration (`CacheConfiguration`)

Section in `appsettings.json`: `"Cache"`

| Key        | Type   | Description                           |
|------------|--------|---------------------------------------|
| `Host`     | string | Redis endpoint, e.g. `"localhost:6379"` |
| `Password` | string | Redis AUTH password                   |

```json
"Cache": {
  "Host": "localhost:6379",
  "Password": "your-redis-password"
}
```

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
  "CharacterSaveInterval": "00:05:00"
}
```

### Creature locomotion

`CreatureLocomotion` selects which `ICreatureLocomotion` each `MapInstance` builds:

- **`Waypoint`** (default) — walks a navmesh path with no awareness of other agents. Two creatures sent to the same point occupy it.
- **`Crowd`** — DotRecast `DtCrowd`. Agents actively steer around one another, and around players when `CrowdIncludesPlayers` is set. A map with no baked navmesh falls back to `Waypoint` and logs a warning naming the map.

Under `Crowd`, player agents exist only as obstacles: `PlayerInputHandler` remains the sole authority on where a character is, and the crowd never writes a character's position.

Before enabling `Crowd` on a busy map, note that DotRecast budgets roughly 25 agents per crowd at about 0.5 ms per frame, there is one crowd per `MapInstance`, and there is no agent cap.

### Creature levels, stats and the experience band

A creature's level is rolled from its template's `MinLevel`–`MaxLevel` at spawn. Health, damage and
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

## REST API JWT Signing Key

Section: `Application:Authentication` in `Avalon.Api` (**never committed to source control**, #482)

| Key                | Type   | Default      | Description                                                    |
|--------------------|--------|--------------|----------------------------------------------------------------|
| `IssuerSigningKey` | string | _(required)_ | HMAC-SHA256 key that signs and validates the API's access JWTs |

`JwtSigningKey.Create` runs when `AddAuth` registers authentication, so the API refuses to start, naming the
setting, when the key is missing, has leading or trailing whitespace, is shorter than 32 bytes in UTF-8, or is
the value once committed to `appsettings.json` (public now). The key is deliberately absent from `appsettings.json`.

```bash
# Development: user-secrets (the Avalon.Api project has a UserSecretsId)
dotnet user-secrets set "Application:Authentication:IssuerSigningKey" "$(openssl rand -base64 48)" --project src/Server/Avalon.Api

# Everywhere else: environment variable
Application__Authentication__IssuerSigningKey=<random value, at least 32 bytes>
```

The Helm chart passes it, with the other secrets, through a Kubernetes Secret (`secretKeyRef`): either one
you manage, named by `existingSecret`, or one the chart creates from `--set-file
authentication.issuerSigningKey=<file>`. It refuses to render with neither. The `ValidateIssuerKey` setting
is gone: the signing key is always validated.

---

## REST API Login Limits

Section: `Application:Authentication` in `Avalon.Api` (#478)

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
options, so `AddInfrastructure` refuses a value below `1` at startup, naming the setting.

```bash
Application__Authentication__MaxFailedLoginAttempts=5
```

The per-source budget keys on the caller's address after `UseForwardedHeaders`, which trusts only a
loopback proxy by default. Behind any other proxy every caller is the proxy's address, one source for
everyone, so configure the proxy as trusted before relying on this budget
(see [Security — Session Management](security-session-management.md#login-policy-shared-by-both-servers)).

---

## REST API Personal Access Tokens

The `Authorization: Avalon avp_...` scheme takes no configuration and there is no shared secret. Each
token belongs to one account; only its SHA-256 hash is stored, and `AvalonAuthenticationHandler` looks
it up per request. See [Security — Session Management](security-session-management.md#rest-api-authentication).

---

## Applying Configuration at Startup

All configuration classes use:

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
Application__Authentication__IssuerSigningKey=<from-vault>
```

---

## Startup Validation

All config classes opt into startup validation to fail fast on misconfiguration:

```csharp
.ValidateDataAnnotations()
.ValidateOnStart()
```

This causes the application to throw an `OptionsValidationException` at startup rather than at runtime when the missing/invalid value is first accessed.

The REST API's `AuthenticationConfig` is bound directly rather than through `IOptions<T>`, so its signing
key is checked by `JwtSigningKey.Create` instead: startup throws `InvalidOperationException` naming the
setting (see [REST API JWT Signing Key](#rest-api-jwt-signing-key)).
