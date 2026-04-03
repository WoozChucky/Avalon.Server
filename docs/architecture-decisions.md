# Architecture Decisions

This document records significant architectural decisions, their rationale, and planned evolution. It covers open design questions raised by TODO comments.

Related TODOs: [TODO-027](../TODO.md#todo-027), [TODO-028](../TODO.md#todo-028), [TODO-029](../TODO.md#todo-029), [TODO-030](../TODO.md#todo-030), [TODO-031](../TODO.md#todo-031)

---

## ADR-001 — World Server / Auth Database Decoupling (TODO-029)

**Status:** Planned

### Context

`Avalon.Server.World/Extensions/ServiceExtensions.cs` registers `AddAuthDatabase()`, which adds `AuthDbContext` and `IAccountRepository` (Auth) into the World server's DI container. This creates a direct EF Core dependency between two separately scalable components.

### Why This Is Problematic

- The World server must be able to scale horizontally without a shared database write path.
- Auth and World can be deployed independently; a World-side migration should not require Auth DB access.
- Principles of bounded context (DDD) dictate that the World domain operates on its own data.

### Root Cause

`IAccountRepository` (Auth) is used in the World server for:
1. Reading account online status on character selection.
2. Setting `account.Online = false` on disconnect.

### Decision

Replace direct DB access with Redis-backed state:

| Current (DB)                                | Target (Redis)                              |
|---------------------------------------------|---------------------------------------------|
| `_accountRepository.FindByIdAsync(id)`      | `_cache.GetAsync($"account:{id}:session")`  |
| `account.Online = true; UpdateAsync()`      | `_cache.SetAsync($"account:{id}:online", 1)`|
| `account.Online = false; UpdateAsync()`     | `_cache.DeleteAsync($"account:{id}:online")`|

The Auth server remains the **sole writer** of `AuthDbContext`. It listens on Redis pub/sub for World-side events and updates the DB accordingly.

### Migration Path

1. Auth server: on successful auth, write `account:{id}:session` JSON (containing `AccountId`, `WorldId`, `LoginTime`) to Redis.
2. World server: read Redis for session validation; no `AuthDbContext`.
3. Auth server: subscribe to `world:characters:disconnect` and clear DB online state.
4. Remove `AddAuthDatabase()` from World DI.
5. Integration test: World host starts without `AuthDbContext` in service collection.

---

## ADR-002 — Chat Command Handler Architecture (TODO-028)

**Status:** Planned

### Context

`ChatMessageHandler` processes all `CChatMessagePacket` packets. Slash commands (`/invite`, `/who`, etc.) are conceptually different from free-text chat. The current approach has extensive commented-out command logic mixed into a single handler.

### Decision

Introduce a **Command Dispatcher** pattern:

```
CChatMessagePacket
        │
ChatMessageHandler
        │
  message.StartsWith('/') ?
        ├── YES → CommandDispatcher.DispatchAsync(ctx, commandLine)
        │              └── Resolve ICommand by name or alias
        │                    ├── Found → ICommand.ExecuteAsync(ctx, args)
        │                    └── Not Found → send "Unknown command" to sender
        └── NO  → BroadcastToChunk (existing behaviour)
```

### `ICommand` Interface

```csharp
public interface ICommand
{
    string Name { get; }
    string[] Aliases { get; }
    Task ExecuteAsync(WorldPacketContext<CChatMessagePacket> ctx, string[] args,
                      CancellationToken token = default);
}
```

### DI Registration

```csharp
services.AddSingleton<ICommand, GroupInviteCommand>();
services.AddSingleton<ICommand, WhoCommand>();
// etc.
```

`CommandDispatcher` resolves all `ICommand` registrations via `IEnumerable<ICommand>` injection, building a lookup by `Name` and `Aliases` (case-insensitive).

### Initial Commands to Implement

| Command         | Alias   | Description                         |
|-----------------|---------|-------------------------------------|
| `/invite`       | `/inv`  | Invite player to group (from commented-out code) |
| `/who`          | —       | Show online players in zone (future) |

---

## ADR-003 — World Timer Constants (TODO-027)

**Status:** Planned

### Context

`World.cs` defines `WorldTimersCount = 5` and only names `HotReloadTimer = 0`. Timers 1–4 are either unnamed or unused.

### Decision

Audit and name all timers. If fewer than 5 are used, reduce `WorldTimersCount`.

### Proposed Constants

```csharp
private const ushort WorldTimersCount  = 2; // adjust after audit
private const ushort HotReloadTimer    = 0;
private const ushort WorldSaveTimer    = 1; // periodic state persistence (if used)
// Add more as needed
```

All `_timers[N]` accesses must use the named constant. This is primarily a code clarity change with no runtime behaviour impact.

---

## ADR-004 — `CharacterSpell` Specializations (TODO-030)

**Status:** Design decision pending

### Context

`CharacterSpell` has a `// TODO: Specializations?` comment. The question is whether a spell learned by a character can be "specced" into a branch that modifies its class, damage, area, or animation.

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

## ADR-005 — `FakeMetricsManager` Dispose (TODO-031)

**Status:** Trivial — implement in place

### Decision

`FakeMetricsManager` holds no resources. Complete the `Dispose(bool disposing)` method with an explicit comment and idempotency guard:

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

## Component Boundary Map

```
┌───────────────────────────────────────────────────────────────────────┐
│                         Game Client                                    │
└──────────────────────────────┬────────────────────────────────────────┘
                               │ TCP (custom packet protocol)
          ┌────────────────────┼───────────────────────┐
          ▼                    │                        ▼
┌─────────────────┐            │             ┌──────────────────────┐
│  Auth Server    │◄───────────┘             │    World Server      │
│ (ticket issuer) │  ──── Redis pub/sub ────►│ (simulation engine)  │
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
         │              REST API                        │
         │  (account mgmt, OpenAPI, JWT issuance)       │
         └──────────────┬──────────────────────────────┘
                        │ EF Core + Redis
                        ▼
                Both databases + Redis
```

The dashed coupling (`World → Auth DB`) highlighted by TODO-029 will be removed, replaced by Redis-only communication.
