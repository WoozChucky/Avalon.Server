# Architecture Decisions

This document records significant architectural decisions, their rationale, and planned evolution.

---

## ADR-001 — World Server / Auth Database Decoupling

**Status:** Planned

### Context

`Avalon.Server.World/Extensions/ServiceExtensions.cs` registers `AddAuthDatabase()`, which adds `AuthDbContext` and `IAccountRepository` (Auth) into the World server's DI container. This creates a direct EF Core dependency between two separately scalable components.

### Why This Is Problematic

- The World server must be able to scale horizontally without a shared database write path.
- Auth and World can be deployed independently; a World-side migration should not require Auth DB access.
- Principles of bounded context (DDD) dictate that the World domain operates on its own data.

### Root Cause

The World server reads the auth database; it no longer writes it (world entry and the game session come from the
REST game admission, not from the database). It reads:
1. the account's locale at character select (`IAccountRepository`, in `CharacterSelectHandler`);
2. the account's access level, for the maintenance entry gate (`WorldEntryGate`);
3. the world's maintenance row (`IWorldMaintenanceRepository`).

The table below predates the game admission: the `Online` writes it lists are gone, so only the reads remain to move.

### Decision

Replace direct DB access with Redis-backed state:

| Current (DB)                                | Target (Redis)                              |
|---------------------------------------------|---------------------------------------------|
| `_accountRepository.FindByIdAsync(id)`      | `_cache.GetAsync($"account:{id}:session")`  |
| `account.Online = true; UpdateAsync()`      | `_cache.SetAsync($"account:{id}:online", 1)`|
| `account.Online = false; UpdateAsync()`     | `_cache.DeleteAsync($"account:{id}:online")`|

The Auth server remains the **sole writer** of `AuthDbContext`. It listens on Redis pub/sub for World-side events and updates the DB accordingly.

### Migration Path

1. Auth server: on successful auth, write `account:{id}:session` JSON (containing the account id, the world id and the login time) to Redis.
2. World server: read Redis for session validation; no `AuthDbContext`.
3. Auth server: subscribe to `world:characters:disconnect` and clear DB online state.
4. Remove `AddAuthDatabase()` from World DI.
5. Integration test: World host starts without `AuthDbContext` in service collection.

---

## ADR-002 — Chat Command Handler Architecture

**Status:** Implemented

### Context

`ChatMessageHandler` processes all `CChatMessagePacket` packets. Slash commands (`/invite`, `/reload`, etc.) are conceptually different from free-text chat.

### Decision

A **Command Dispatcher** pattern routes slash-prefixed messages to `ICommand` implementations:

```
CChatMessagePacket
        │
ChatMessageHandler
        │
  message.StartsWith('/') ?
        ├── YES → CommandDispatcher.Dispatch(connection, packet)   (synchronous, on the tick)
        │              └── Resolve ICommand by name or alias
        │                    ├── Found → ICommand.Execute(ctx, args)
        │                    └── Not Found → send "Unknown command." to sender
        └── NO  → said on ChatChannel.Say to the sender's MapInstance
```

### `ICommand` Interface

```csharp
public interface ICommand
{
    string Name { get; }
    string[] Aliases { get; }
    AccountAccessLevel RequiredAccess => AccessLevels.Player;

    // Runs on the tick, to completion; asynchronous work goes through ctx.Then(task, callback),
    // whose callback runs on a later tick (revised with party play: commands no longer await).
    void Execute(CommandContext ctx, string[] args);
}
```

### DI Registration

```csharp
services.AddSingleton<ICommand, ReloadCommand>();
services.AddSingleton<ICommand, InviteCommand>();
// etc.
```

`CommandDispatcher` resolves all `ICommand` registrations via `IEnumerable<ICommand>` injection, building a lookup by `Name` and `Aliases` (case-insensitive). Unknown commands reply with "Unknown command."

---

## ADR-003 — World Timer Constants

**Status:** Obsolete

`World` no longer keeps an array of timers to name: the AI script hot reload, the one timer in use, is its own
`IntervalTimer`, polled every `Game:ScriptHotReloadIntervalSeconds`.

---

## ADR-004 — `CharacterSpell` Specializations

**Status:** Design decision pending. Spells are abilities since #163 (`CharacterAbility`, `AbilityTemplate`,
`AbilityScript`); the names below are the original proposal's, and none of its types exists yet.

### Context

`CharacterSpell` has an open question about whether a spell learned by a character can be "specced" into a branch that modifies its class, damage, area, or animation.

### Options

**Option A — Enum-based path (simple)**

```csharp
public SpecializationPath? Specialization { get; set; } // nullable
```

Where `SpecializationPath` is a flat enum. Specialization influences `SpellScript` variant selection by the `IScriptManager`.

**Option B — FK to a tree node (extensible)**

```csharp
public SpecializationNodeId? SpecializationNodeId { get; set; }
```

A `SpecializationNode` table holds a tree structure. More flexible but more complex to implement.

### Decision

Implement **Option A** initially. The `SpecializationPath` enum starts with a small set (e.g. `None`, `Fire`, `Frost`, `Arcane` for Wizard). This can be evolved into Option B if the design demands branching trees.

### `SpellScript` Selection with Specialization

```csharp
// In ScriptManager.GetSpellScript:
Type? GetSpellScript(string scriptName, SpecializationPath? path = null)
{
    if (path.HasValue)
    {
        string variantName = $"{scriptName}_{path}"; // e.g. "Fireball_FireMastery"
        if (_scripts.TryGetValue(variantName, out var variant))
            return variant;
    }
    return _scripts.GetValueOrDefault(scriptName); // default script
}
```

---

## ADR-005 — `FakeMetricsManager` Dispose

**Status:** Planned (trivial)

`FakeMetricsManager` holds no resources. The `Dispose(bool disposing)` method should be completed with an idempotency guard:

```csharp
private bool _disposed;

protected virtual void Dispose(bool disposing)
{
    if (_disposed) return;
    // No managed or unmanaged resources to release.
    _disposed = true;
}

public void Dispose()
{
    Dispose(true);
    GC.SuppressFinalize(this);
}
```

---

## ADR-006 — Splitting the REST API into services

**Status:** Implemented in code (#794); not yet deployed apart. Production runs all four services in the one
`avalon-api` deployment until the rollout (#802). ES256 access tokens (#801) and the hardening questions (#803) are
tracked on their own.

### Context

`Avalon.Api` was one process that served everything over HTTP: accounts and credentials, MFA, tokens, launcher sign-in
and game admission, world content and characters for every world, public tooltips, commerce and its payment webhooks,
and client distribution. Every deployment of it held every secret (the signing key, every world's connection strings,
the build store's key, the balance service's secret), connected to every database, and restarted the process that
renews game sessions for a change to any of them. An internet-facing webhook ran beside the key that signs Admin
tokens, and a launcher update depended on the health of everything else.

### Decision

- **Four services, cut along state**: `identity` (accounts, credentials, tokens, store and Steam sign-in, links and
  consolidation, game admission and its workload listener), `worlds` (the world registry, world content, characters,
  presence, public tooltips, the balance proxy), `commerce` (checkout, purchases, payment notifications, the
  reconciliation worker) and `distribution` (launcher updates, releases, the changelog, channels). Each owns its
  tables, secrets and providers; what shares state tightly stays together (game admission with identity, the public
  tooltips with world content).
- **One binary, one image, one chart.** Each service is a library (`Avalon.Api.Identity`, `.Worlds`, `.Commerce`,
  `.Distribution`) on a shared hosting library (`Avalon.Api.Hosting`) and the contract (`Avalon.Api.Contract`). The
  host, `Avalon.Api`, runs the services `Application:Services` names, all four when it is unset. A deployment's
  secrets, not its code, carry its privilege. No service library references another.
- **No calls between services at request time.** Every service validates tokens and reloads accounts from the auth
  database itself. Identity alone mints tokens and migrates the auth schema; the other services wait for it at
  startup.
- **One route manifest** (`files/routes.json` in the chart) says which service owns each path. The chart renders a
  Traefik `IngressRoute` from it, and the tests check every endpoint against it. Public paths, base URLs and the
  published OpenAPI document do not change.
- **Rollout beside the running process**, route group by route group, dev world and channel first, each step one values
  change from its rollback, then `avalon-api` shrinks to identity.

### Consequences

- Without configuration the host behaves as before the split; the homelab release renders unchanged, which the chart's
  test pins.
- Each process counts its own requests against the in-memory rate limits; the security budgets stay in Redis, shared.
- Until #801 every service needs the HS256 signing key, which can also mint tokens; least privilege for tokens needs
  ES256 first.
- A new endpoint needs an owner decided in `RouteOwnershipShould`'s table, and a new first path segment of a service
  other than identity needs a manifest rule.

### Alternatives considered

| Alternative | Why not |
|---|---|
| One image and chart per service | Three more images and charts to build, promote and clean up for the same code; the libraries leave the door open |
| Splitting identity into accounts and game access | Both halves would hold the signing key or the game-auth key and share the same records |
| Keeping HS256 and sharing the key | Every service could mint an Admin token |
| A gateway process in the cluster, or calls between services | An extra hop and a runtime dependency; Traefik already routes by path |

The details, the route table and the deployment are in [API services](api-services.md).

---

## Component Boundary Map

```
┌───────────────────────────────────────────────────────────────────────┐
│                         Game Client                                    │
└──────────────────────────────┬────────────────────────────────────────┘
                               │ TCP + TLS (custom packet protocol)
          ┌────────────────────┼───────────────────────┐
          ▼                    │                        ▼
┌─────────────────┐            │             ┌──────────────────────┐
│  Auth Server    │◄───────────┘             │    World Server      │
│ (login, worlds) │  ──── Redis pub/sub ────►│ (simulation engine)  │
│                 │  ◄─── Redis pub/sub ────  │                      │
└────────┬────────┘                          └──────────┬───────────┘
         │ EF Core                                      │ EF Core
         ▼                                              ▼
┌─────────────────┐                          ┌──────────────────────┐
│   Auth DB       │                          │  Character + World DB│
│ (accounts, MFA) │          Redis           │ (characters, items,  │
└─────────────────┘    (sessions, cache,     │  world templates)    │
                        pub/sub)             └──────────────────────┘

         ┌─────────────────────────────────────────────┐
         │              REST API (one binary)           │
         │  identity · worlds · commerce · distribution │
         └──────────────┬──────────────────────────────┘
                        │ EF Core + Redis
                        ▼
                Both databases + Redis
```

> The game client also calls the REST API's identity service for its game context and join tickets, and the World
> server redeems those tickets and renews its game sessions there over mutual TLS
> ([game server admission](steam-authentication-workloads.md)). The World server still has a direct coupling to
> `AuthDbContext` (via `AddAuthDatabase()`). ADR-001 describes the plan to replace this with Redis-only communication.
