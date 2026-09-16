# DbContext Lifetime

A repository creates a `DbContext` per method call and never holds one.

## What was wrong

Every world packet handler is built once, from the **root** provider, in the `WorldServer`
constructor (`ActivatorUtilities.CreateInstance`). The repositories those handlers take were
registered `AddScoped`, and so were the three contexts behind them. A scoped service resolved
from the root provider lives as long as the root provider does, so every handler shared one
`CharacterDbContext`, one `AuthDbContext` and one `WorldDbContext` for the life of the process,
across every connected player.

`DbContext` is not thread-safe, and the continuations a handler enqueues resume off the tick
thread. One faulted save poisons the shared change tracker for everyone; tracked entities are
never released, so it grows without bound.

Nothing said so. There is no `launchSettings.json`, no `appsettings.Development.json` and no
`DOTNET_ENVIRONMENT`, so the host resolved Production, where `ValidateScopes` and
`ValidateOnBuild` are off by default. The framework would have refused to start.

## Why scope-per-packet and scope-per-connection do not fit

A handler starts a query and hands the task off:

```csharp
connection.EnqueueContinuation(characterRepository.FindByAccountAsync(id), chars => { ... });
```

`Execute` returns before the query completes, so a scope disposed at the end of the handler
disposes the context mid-query. Character select is five of these, each continuation starting the
next — one logical operation spanning many ticks, so there is no packet whose end is the
operation's end either.

Scope-per-connection survives that, and keeps the hazard in miniature: one context still outlives
many operations, still crosses the tick thread and the continuation thread, and still holds every
entity that player ever touched.

A context per method call has neither problem. Its lifetime is the query's lifetime, which is the
one lifetime that is actually known.

## What a repository author does now

Take `IDbContextFactory<TContext>`, and open a context inside each method:

```csharp
public async Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
{
    await using var context = await CreateContextAsync(cancellationToken);

    return await context.Accounts.AsNoTracking()
        .FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
}
```

Rules that follow:

- A context may not be stored in a field, returned, or captured by anything that outlives the
  call. `EntityFrameworkRepository<TEntity, TKey, TContext>` no longer exposes one; the third type
  parameter exists so a derived repository still gets its own typed `DbSet`s.
- Nothing tracked survives a call. The repositories were already written detached — `UpdateAsync`
  attaches and sets `Modified` explicitly — so `track: true` now only affects the returned graph,
  not what happens on the next call. It says nothing about the next one, which is why
  `StaticData` no longer asks for it.
- **Name a principal by its foreign key, never by a navigation.** See below.
- Repositories are registered singleton. They hold a factory, not state.
- **Two repository calls share neither a context nor a transaction.** Every write method already
  called `SaveChangesAsync` itself, so two calls were never one unit of work; what changed is that
  an explicit `BeginTransactionAsync` opened elsewhere can no longer reach them.

## Entities handed back are detached, and `Add` cascades over detached

`DbSet.Add` walks the reachable navigation graph and marks every node that is `Detached` as
`Added` — key set or not. Nodes already tracked `Unchanged` are skipped, and on a shared context
the principal usually was, so the walk passed over it. A repository that creates a context per
call disposes it before returning, so everything it hands back is detached:

```csharp
Account account = await accounts.CreateAsync(account);   // the row exists; the object is detached
await devices.CreateAsync(new Device { Account = account, AccountId = account.Id, … });
```

That second call inserts the account again. It is not a silent corruption — it is
`UNIQUE constraint failed: Accounts.Id` — but it lands *after* the account row has committed,
so registration leaves an account that can never complete registration, and character creation
leaves a `Character` row with no stats, no items and no reply to the client, burning the name and
a slot the player cannot reuse.

Two things close it, and they are independent:

- **A repository write covers one row. It never writes a graph.** Every write goes through
  `DbContextWriteExtensions` (`TrackForInsert` / `TrackForUpdate`), which tracks the root and
  **throws** on anything reachable through a navigation, naming both types. This is what covers
  call sites nobody has audited.
- **Call sites do not assign a redundant navigation.** All three that did also set the foreign
  key, so the navigation bought nothing and its only effect was this.

It refuses rather than guesses because the obvious guess is wrong in a way that cannot be seen.
"Already carries its key" reads like "a row that exists", but `EntityEntry.IsKeySet` is `true`
**unconditionally** for a key with no store generation. Fifteen types here are client-keyed, and
for those a key test can never answer "new" — so a parent handed a brand-new child would insert the
parent, drop the child, and **return success**. That is worse than the cascade it replaced: the
cascade at least threw. `ChunkPoolRepository.FindAllWithMembershipsAsync` already hands back
detached pools with `Memberships` populated and the class inherits `CreateAsync`, so it was one
caller away, not one refactor away.

Owned entities are exempt: they are part of the root's row, have no foreign key a caller could
name them by, and the rule has nothing to offer them. `ChunkTemplate.SpawnSlots` is the case. They
are reached on every write and handled, not bypassed.

**Known limitation.** An owner that carries owned collections cannot currently be *updated* through
a repository. The owned nodes take the root's `Modified` state, EF refuses to re-track them
(*"another instance with the same key value for {'Id'} is already being tracked"*), and the update
does not land even when nothing about the owned rows changed. That is preferred to what it
replaced: under the key test this rule dropped, the same update succeeded and **duplicated** the
owned rows — two slots became four, with no error. Nothing calls that path today; fixing it is its
own change, and an early return on owned nodes is not the fix, because it drops them.
`RepositoryWritePathShould.Fail_loudly_when_an_owner_with_owned_rows_is_updated` pins it as it is.

A caller that genuinely wants to write several rows writes them itself, on one context, through
`IDbTransactionRunner`.

## Writes that must commit together

Use `IDbTransactionRunner<TContext>`: one context, one transaction, rollback on throw.

```csharp
await _authTransaction.ExecuteAsync(async (context, token) =>
{
    // statements against `context`, not against repositories
}, cancellationToken);
```

The one caller today is `AccountService.UpdateStatusAsync` — ban and deactivate. It sets the
account status, revokes every refresh token and revokes every personal access token; a ban that
revoked no credentials would leave the banned account a live session for up to thirty days. A
repository call inside the body would open its own context and commit on its own, so the two
statements that are shared with a repository are `public static` overloads taking the context:
one definition, two lifetimes.

`MapChunkPlacementRepository.ReplaceForMapAsync` needs nothing: both of its saves are in one
method, so the context it creates spans its own transaction.

## What scope validation buys

`ValidateScopes` and `ValidateOnBuild` are now on for every host, in every environment, through
`AvalonServiceProvider`. A singleton that captures a scoped service is a defect wherever it runs,
and turning the checks on only outside Production is what let this one ship.

They are what makes the fix enforceable rather than advisory: `CreatureSpawner` and
`CreaturePlacementService` are singletons that inject a repository, and the day a repository goes
back to `AddScoped` the world host fails to build. `WorldHostGraphShould`,
`AuthHostGraphShould` and `ApiHostGraphShould` compose each host's real registrations and build
them under the same options, so that failure arrives from `dotnet test` rather than from a
deployment.

## Testing a write path

The write paths are covered against a real relational database — SQLite over one connection held
open for the life of the fixture. The connection *is* the database (`:memory:` is dropped when the
last connection to it closes) and the repositories open a context per call, so every context is
handed that one connection. Each context takes a `DbContextOptions<TContext>`; `OnConfiguring`
returns early when the caller has already configured one, and the Npgsql path is unchanged.

`AccountRegistrationShould` and `CharacterCreationShould` drive the real service and the real
handler — the handler's continuation chain pumped the way the tick loop pumps it — over real
repositories. With the cascade restored and the navigations put back, both fail on the duplicate
insert, which is what they exist to say.

`RepositoryWritePathShould` holds the rule itself: a dependent named by foreign key lands, a
dependent naming its principal by navigation is refused, a parent carrying a new client-keyed child
is refused, and an owned collection is written with its owner. The third of those is the one a key
test passes while writing nothing.

## Known gaps

Proving that a write *inside* the transaction body enlists would need two contexts on two
connections; the fixture deliberately shares one, so `AccountStatusChangeShould` covers what the
ban does rather than what a rollback would undo.

`track: true` on the base reads is now dead weight: the context is disposed before the entity
returns, so the flag shapes only the graph handed back and nothing that happens next.

Counting the write surface is harder than it looks and no count here should be trusted as a risk
measure. Roughly thirteen call sites are insert-shaped; the update-shaped surface is far larger —
around twenty-five, of which `IAccountRepository.UpdateAsync` alone is about fourteen.

Unrelated and pre-existing: `docs/map-generation.md:386` says the importer's `ReplaceForMapAsync`
handles the upsert. It does not, and `ReplaceForMapAsync` has no caller in `src/`, `tools/` or
`tests/` at all.

The audit that preceded this found four write sequences that were already not atomic before the
change and still are not — refresh-token rotation, password change, email change confirmation, and
registration. Each is same-context and could adopt the runner. Character creation is five writes
across two databases and five ticks, so no single transaction covers it.
