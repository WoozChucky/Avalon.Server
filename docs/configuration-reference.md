# Configuration Reference

This document covers all configuration keys for the Avalon server.

---

## Overview

Avalon uses strongly-typed configuration classes bound from `appsettings.json` (or environment variables / secrets manager in production) via `IOptions<T>`. Configuration classes reside in `src/Shared/Avalon.Configuration`.

---

## Configuration Classes

| Class                     | Namespace                    | Bound from                   |
|---------------------------|------------------------------|------------------------------|
| `DatabaseConfiguration`   | `Avalon.Configuration`       | `Database:*` (the REST API: `Database:Auth` and `Database:Worlds`) |
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
- `Security:CertificatePath` (auth server): required. `Security:CertificatePassword` is optional.

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

**Validation rules:**
- `Host`: required, checked at startup. `Password` is optional.

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
  "PvpOffDelay": "00:05:00"
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

## REST API Worlds

The API serves any number of worlds, one per `Worlds` row in the auth database (#523). Each world has its own world database (content) and characters database; only the auth database and Redis are shared. Adding a world is a configuration change only. The world servers are unchanged: each serves one world, from its own `Database:World` and `Database:Characters`.

| Key | Description |
|-----|-------------|
| `Database:Auth:ConnectionString` | The shared auth database |
| `Database:Worlds:<id>:World:ConnectionString` | World `<id>`'s content database |
| `Database:Worlds:<id>:Characters:ConnectionString` | World `<id>`'s characters database |

`<id>` is the world's id in the auth `Worlds` table. The environment form is `Database__Worlds__2__World__ConnectionString`.

```json
"Database": {
  "Auth": { "ConnectionString": "…" },
  "Worlds": {
    "1": { "World": { "ConnectionString": "…" }, "Characters": { "ConnectionString": "…" } },
    "2": { "World": { "ConnectionString": "…" }, "Characters": { "ConnectionString": "…" } }
  }
}
```

**Startup refuses to run**, naming the setting (never its value), when there is no world, a world id is not a positive integer up to 65535 written without leading zeros or sign, a world has only one of its two strings, or a string is blank. The check runs right after the other startup validations (see [Startup Validation](#startup-validation)), before any migration. It is not an options validation, so OpenAPI generation, which skips that startup work, needs no world configured.

**At startup** the API migrates the auth database (a failure stops it), then each world's two databases. A world whose migration fails is logged with its id and the exception type and answers 503 until the next restart; the other worlds serve. There is no retry. Every unreachable world adds the driver's connect timeout to startup.

**Routes:** world content and characters are under `/world/{worldId}/...`. A world this API is not configured for, or that the caller may not enter, answers 404 (the same 404 either way); an unavailable world answers 503. `GET /character` lists the caller's characters on every world, with `unavailableWorlds`; `GET /world` says for each world whether it is `configured` and `available`. Character ids are unique only within one world, so a consumer keys a character by `(worldId, id)`; the Redis presence keys are not per world yet (#556).

**Helm:** a `worlds` map, keyed by world id.

- Chart-managed Secret (no `existingSecret`): give `worlds.<id>.world.connectionString` and `worlds.<id>.characters.connectionString`, from files (`--set-file`). The chart's Secret holds them under `database-world-<id>-connection-string` and `database-characters-<id>-connection-string`.
- `existingSecret`: give no strings. Your Secret holds each world's two keys, under those default names or the names you set in `worlds.<id>.worldKey` / `worlds.<id>.charactersKey` (custom names are accepted only with `existingSecret`). A world with no custom names is still listed, for example `--set worlds.2.worldKey=`. Add the keys to the Secret before upgrading.
- The auth string, `database.auth.connectionString`, is required with a chart-managed Secret (from a file, `--set-file`; trimmed, so a blank one is refused too) and must be left empty with `existingSecret`, whose Secret holds it under `database-auth-connection-string` (#564).
- The chart refuses to render with no world, a world id the API would refuse, a world missing one of its strings (chart-managed), a string given inline with `existingSecret`, a key that is not a valid Secret key name, a key two settings would read (the chart's own keys included), and the removed `database.world` / `database.characters` values.

The API's `appsettings.json` lists no world, so the published image ships none. Local development gets world 1 (the docker compose databases) from `appsettings.Development.json`, which only the Development environment loads, and the Aspire AppHost sets the same pair. Every other environment has exactly the worlds its environment variables or Helm values give it, and the API refuses to start with none.

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

Each host logs its five limits at Information when it starts (the Auth server as `Application`, the API
as `Application:Authentication`), so the two lines can be compared.

The per-source budget keys on the caller's address after the forwarded headers are applied, so behind a
proxy it needs [REST API Forwarded Headers](#rest-api-forwarded-headers) set up.

---

## REST API Forwarded Headers

Section: `Application:ForwardedHeaders` in `Avalon.Api` (#478 review)

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

Section: `Application:RateLimiting` in `Avalon.Api` (#561)

| Key                             | Type | Default | Description |
|---------------------------------|------|---------|-------------|
| `Enabled`                       | bool | `true`  | When false, nothing is limited |
| `AnonymousPermitsPerMinute`     | int  | `60`    | Requests a minute per source for a caller that is not signed in |
| `AuthenticatedPermitsPerMinute` | int  | `300`   | Requests a minute per account for a caller with a valid access token or personal access token |

Every request counts, whatever the endpoint, in a sliding window of one minute in six segments, held in
memory (the API runs as one replica; each replica would count on its own). A request with a valid access
token or personal access token is counted against its account, after the account has been revalidated;
any other, a request whose token is not valid included, against its source: the login budgets' rule, the
IPv4 address or the IPv6 /64, after the [forwarded headers](#rest-api-forwarded-headers) are applied.
Every caller with no peer address shares one partition. `/health` and `/alive` are never limited. The login
and registration budgets are separate and still apply.

A refused request gets 429 ProblemDetails with `Detail` `LOCKED` and a `Retry-After` header in seconds, the
same whichever partition refused it, and increments the counter `avalon.api.rate_limit.rejections` (meter
`avalon-api`), tagged `partition=anonymous` or `partition=authenticated`.

The section is bound as `IOptions<RateLimitingConfig>` and validated at startup: a limit below `1` stops
the API, naming the setting.

```bash
Application__RateLimiting__AnonymousPermitsPerMinute=60
Application__RateLimiting__AuthenticatedPermitsPerMinute=300
```

The Helm chart passes `rateLimiting.enabled`, `rateLimiting.anonymousPermitsPerMinute` and
`rateLimiting.authenticatedPermitsPerMinute`, each only when set; empty, the API's defaults apply.

---

## REST API Email

Section: `Application:Email` in `Avalon.Api` (#510)

| Key               | Type   | Default                                   | Description |
|-------------------|--------|-------------------------------------------|-------------|
| `Sender`          | enum   | `None`                                    | `None` or `Pickup`. Which email sender the API uses |
| `PickupDirectory` | string | `avalon-mail` under `Environment.SpecialFolder.LocalApplicationData` | Where `Pickup` writes its `.eml` files; created when missing, with mode 0700 on Unix |
| `From`            | string | none                                      | The address every email is sent from. Required, as a bare address (`noreply@example.com`, no display name), when `Sender` is `Pickup` |

Email change (`POST /account/email/change` and `/account/email/confirm`) is on only while a sender is
configured:

- `None`, the default, registers no sender. Both endpoints answer 501 "Email change is unavailable until
  email delivery exists".
- `Pickup` writes each email as an RFC 5322 `.eml` file (plain text, UTF-8) into `PickupDirectory`, which
  any mail client opens. Nothing leaves the machine. The files hold the confirm tokens, so it is
  **Development only**.

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
  - any of the three send-budget values above below 1;
  - `Sender` `Pickup` outside Development (`Application:Email:Sender`);
  - `Sender` `Pickup` with a missing or invalid `From` (`Application:Email:From`).
- At startup the API logs the sender and whether email change is on.

```bash
# Development only
dotnet user-secrets set "Application:Email:Sender" "Pickup" --project src/Server/Avalon.Api
dotnet user-secrets set "Application:Email:From" "noreply@avalon.monster" --project src/Server/Avalon.Api
```

A real provider (SMTP or HTTP) is not implemented yet; it will sit behind the same `IEmailSender`, and its
credentials will load from user-secrets or the environment, like the JWT signing key.

---

## REST API Personal Access Tokens

The `Authorization: Avalon avp_...` scheme takes no configuration and there is no shared secret. Each
token belongs to one account; only its SHA-256 hash is stored, and `AvalonAuthenticationHandler` looks
it up per request. See [Security — Session Management](security-session-management.md#rest-api-authentication).

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
Application__Authentication__IssuerSigningKey=<from-vault>
```

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
`Database:Auth:ConnectionString`. The API then checks `Database:Worlds` right after these checks, in
`ApiStartup` (`WorldDatabaseSettings`, see [REST API Worlds](#rest-api-worlds)), rather than as an
options validation, because OpenAPI generation starts the host with no world configured. The message
names the missing or malformed setting.

This causes the application to throw an `OptionsValidationException` at startup rather than at runtime when the missing/invalid value is first accessed.

Each host runs these checks itself, right after building the host and before its migrations and
its cache connection (`AuthStartup`, `WorldStartup`, `ApiStartup`), because `ValidateOnStart` alone
would run them only when the host starts, after that work had already failed on the missing value.
The API skips them, with the migrations, when `AVALON_OPENAPI_GENERATION_ONLY` is set.

The REST API's `Application:*` classes (`ApplicationConfig` and the sections under it) do not use
`ValidateOnStart`, except `Application:Cache`, which is also bound as `IOptions<CacheConfiguration>`
and validated at startup like the servers' `Cache`, and `Application:RateLimiting`, bound only as
`IOptions<RateLimitingConfig>` and validated the same way (see
[REST API Rate Limiting](#rest-api-rate-limiting)). They are bound directly rather than through `IOptions<T>`, and `ServiceRegistration`
checks them by hand at startup: the signing key (`JwtSigningKey.Create`, see
[REST API JWT Signing Key](#rest-api-jwt-signing-key)), the login limits (`LoginLimitsValidation.Validate`),
the account-creation cap, the email-change send caps, the forwarded-headers entries
(`ForwardedHeadersSetup.BuildOptions`) and the email sender (`AddEmail`). Each check throws
`InvalidOperationException`, and its message names the setting.
