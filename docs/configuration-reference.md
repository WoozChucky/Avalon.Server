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
| `AuthenticationConfig`    | `Avalon.Configuration`       | `Authentication:*`           |
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
| `MaxFailedLoginAttempts`    | int    | `5`       | Consecutive failed logins before account lock  |
| `Issuer`                    | string | `"Avalon"` | Issuer name embedded in MFA OTP URIs           |

```json
"Application": {
  "MinClientVersion": "0.0.1",
  "ServerVersion": "1.0.0",
  "MaxFailedLoginAttempts": 5,
  "Issuer": "Avalon"
}
```

**Validation rules:**
- `MinClientVersion`, `ServerVersion`: required, must match `^\d+\.\d+\.\d+$` (SemVer).
- `MaxFailedLoginAttempts`: minimum `1`.
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
| `CreatureLocomotion`             | enum   | `Waypoint` | `Waypoint` or `Crowd` — see below |
| `CrowdIncludesPlayers`           | bool   | `false`    | Registers players as crowd obstacles so creatures steer around them. Ignored under `Waypoint` |
| `CreatureAgentRadius`            | float  | `0.6`      | Separation radius in world units. Range `0.05`–`10.0` |
| `MeleeSlotCount`                 | int    | `6`        | Standing positions on the ring around a target. Range `1`–`16` |
| `MeleeSlotRadius`                | float  | `1.5`      | Ring radius. Range `0.5`–`20.0` |

```json
"Game": {
  "WorldId": 1,
  "PlayerRadius": 2000,
  "MaxCharactersPerAccount": 5,
  "CharacterLoadTimeoutSeconds": 15,
  "ScriptHotReloadIntervalSeconds": 5,
  "CreatureLocomotion": "Waypoint",
  "CrowdIncludesPlayers": false,
  "CreatureAgentRadius": 0.6,
  "MeleeSlotCount": 6,
  "MeleeSlotRadius": 1.5
}
```

### Creature locomotion

`CreatureLocomotion` selects which `ICreatureLocomotion` each `MapInstance` builds:

- **`Waypoint`** (default) — walks a navmesh path with no awareness of other agents. Two creatures sent to the same point occupy it.
- **`Crowd`** — DotRecast `DtCrowd`. Agents actively steer around one another, and around players when `CrowdIncludesPlayers` is set. A map with no baked navmesh falls back to `Waypoint` and logs a warning naming the map.

Under `Crowd`, player agents exist only as obstacles: `PlayerInputHandler` remains the sole authority on where a character is, and the crowd never writes a character's position.

Before enabling `Crowd` on a busy map, note that DotRecast budgets roughly 25 agents per crowd at about 0.5 ms per frame, there is one crowd per `MapInstance`, and there is no agent cap.

### Melee slots and attack range

`MeleeSlotRadius` **must not exceed the creature attack range** (`CreatureCombatScript.AttackRange`, currently `1.5`). Set it higher and creatures walk to their slot, arrive, and stand outside attack range dealing no damage. The range attribute permits it, so the World server logs a warning at instance construction naming both values.

The slot count is bounded by the ring's circumference. At radius `1.5` there are about `9.42` units of ring; with a `1.2` agent diameter (twice the default `CreatureAgentRadius`), six slots leave `1.57` between adjacent centres, while eight leave `1.18` — narrower than one creature.

---

## Avalon Internal Authentication

Section: environment variable or secrets manager (**never committed to source control**)

| Key                   | Type   | Default       | Description                                               |
|-----------------------|--------|---------------|-----------------------------------------------------------|
| `Avalon:SharedSecret` | string | _(required)_  | Shared secret for `Authorization: Avalon <token>` scheme  |

> **Warning:** This key must **never** appear in `appsettings.json`. Use environment variables or a secrets manager (Azure Key Vault, AWS Secrets Manager, dotnet user-secrets) in all environments.

```bash
# Environment variable:
Avalon__SharedSecret=<minimum-32-char-random-value>
```

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
    if (account.FailedLogins >= _authConfig.MaxFailedLoginAttempts)
    { ... }
}
```

---

## Environment-Specific Overrides

Use `appsettings.{Environment}.json` (e.g. `appsettings.Production.json`) to override defaults per environment without changing the base file. Sensitive values (database passwords, `SharedSecret`) must come from environment variables or a secrets manager, not files.

```bash
# Environment variable override syntax (.NET):
Application__MinClientVersion=1.5.0
Application__MaxFailedLoginAttempts=10
Avalon__SharedSecret=<from-vault>
```

---

## Startup Validation

All config classes opt into startup validation to fail fast on misconfiguration:

```csharp
.ValidateDataAnnotations()
.ValidateOnStart()
```

This causes the application to throw an `OptionsValidationException` at startup rather than at runtime when the missing/invalid value is first accessed.
