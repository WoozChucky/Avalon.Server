# CLAUDE.md

Guidance for agents working in this repository. Avalon is an MMORPG server: a TCP auth server, TCP world servers, a REST API and the tooling around them. This file holds the commands, the project map, the working rules and the invariants that cut across subsystems. Every subsystem has its own page under `docs/` (listed below): **read that page before changing the subsystem** — the rules there encode races, orderings and owner decisions that are not obvious from the code.

## Commands

```bash
# Restore, build, test. Build with CI=true before pushing: src/Directory.Build.props promotes
# MA0032, MA0040 and MA0045 (cancellation-token hygiene) to errors only when CI is true.
dotnet restore
CI=true dotnet build --no-restore
CI=true dotnet test --no-build

# One test project, or one class or method
dotnet test tests/Avalon.Server.World.UnitTests
dotnet test tests/Avalon.Server.Auth.UnitTests --filter "FullyQualifiedName~CAuthHandlerShould"

# Infrastructure (Redis + Postgres; compose password 123), then the servers
docker compose up -d redis postgres
dotnet run --project src/Server/Avalon.Api          # all four API services; needs a JWT signing key, see below
dotnet run --project src/Server/Avalon.Server.Auth
dotnet run --project src/Server/Avalon.Server.World
dotnet run --project src/Server/Avalon             # Aspire AppHost: everything at once

# Benchmarks (game-server performance work needs numbers from here)
dotnet run -c Release --project tools/Avalon.Benchmarking

# Wire schema, after ANY packet contract change (WireSchemaShould fails otherwise)
dotnet run --project tools/Avalon.Exporter -- proto
dotnet run --project tools/Avalon.Exporter -- opcodes
dotnet run --project tools/Avalon.Exporter -- corpus

# Generated map pieces (ForestPiecesShould / TownPiecesShould fail when stale)
dotnet run --project tools/Avalon.ChunkGen -- forest
dotnet run --project tools/Avalon.ChunkGen -- town

# EF migration (Avalon.Api is the design-time startup project; contexts: AuthDbContext,
# WorldDbContext, CharacterDbContext). World and Character factories need their connection
# string in the environment even for "migrations add" (a placeholder is fine):
Database__World__ConnectionString="Host=127.0.0.1;Port=1;Database=design_time_only" \
  dotnet ef migrations add <Name> --project src/Server/Avalon.Database.World \
  --startup-project src/Server/Avalon.Api --context WorldDbContext
```

Target framework: .NET 10 (`global.json`). The long notes — every EF design-time rule, seed-migration ordering, ChunkGen details, publish commands, and setting the REST API's JWT signing key (`Application:Authentication:IssuerSigningKey`, required, at least 32 bytes, never committed; user-secrets locally, a Kubernetes Secret in Helm) — are in [docs/development-setup.md](docs/development-setup.md).

## Architecture

| Component | Project | Role |
|---|---|---|
| REST API host | `src/Server/Avalon.Api` | Runs the API services `Application:Services` names, all four in one process when unset (production until #802); the `avalon-api` chart and its route manifest (`files/routes.json`); OpenAPI with Scalar at `/scalar`; the EF design-time startup project ([docs/api-services.md](docs/api-services.md)) |
| API services | `src/Server/Avalon.Api.Identity`, `.Worlds`, `.Commerce`, `.Distribution` | identity (accounts, MFA, tokens, client auth, game admission; migrates the auth schema), worlds (world content, characters, public tooltips), commerce (checkout, payments), distribution (launcher, releases, channels); none references another (`ApiServiceBoundariesShould`) |
| API shared | `src/Server/Avalon.Api.Hosting`, `src/Server/Avalon.Api.Contract` | The host builder, pipeline, startup, token validation, rate limiting and world database plumbing every service runs on; the REST contract (DTOs) |
| Auth server | `src/Server/Avalon.Server.Auth` | TCP login, MFA, world list and select, world-key issuance |
| World server | `src/Server/Avalon.Server.World` | Tick loop host, connections, packet dispatch, world lifecycle |
| Core world | `src/Server/Avalon.World` | Instances, entities, abilities, creatures and AI, parties, quests, auras, items, chat commands |
| Modding API | `src/Server/Avalon.World.Public` | The future public surface for scripts and addons (see Working rules) |
| Scripts | `src/Server/Avalon.World.Scripts`, `.Scripts.Abstractions` | Script compilation, loading and hot reload |
| Map generation | `src/Server/Avalon.World.Generation` | Chunk layouts, pools, groups, procedural layout generation |
| Combat maths | `src/Server/Avalon.Combat` | Pure combat rules shared by the world server, the API and the balance simulator; references `Avalon.Domain` only (`CombatAssemblyShould`) |
| Hosting | `src/Server/Avalon.Hosting` | Host builder, the TCP server (`Networking/ServerBase`), telemetry |
| Infrastructure | `src/Server/Avalon.Infrastructure` | `IReplicatedCache` (Redis), `CacheKeys` (every Redis key), MFA hash service, `ISecureRandom`, and `Login/` (the login policy shared by the auth server and the API) |
| Databases | `src/Server/Avalon.Database`, `.Database.Auth`, `.Database.World`, `.Database.Character` | EF Core (Npgsql) contexts, repositories, migrations, design-time factories |
| Balance | `src/Server/Avalon.Balance.Core`, `.Balance.Data`, `.Balance.Contract`, `.Balance.Service` | Simulator library (Core references Combat and Domain only, `BalanceCoreAssemblyShould`), seed reader, DTOs, in-cluster service |
| Aspire | `src/Server/Avalon`, `src/Server/Avalon.ServiceDefaults` | Local AppHost and shared service defaults |
| Shared | `src/Shared/Avalon.Common`, `.Configuration`, `.Domain`, `.Metrics`, `.Network.Packets`, `.Network.Packets.Abstractions` | `ValueObject<T>` and utilities, options classes, the domain model, OpenTelemetry, packet contracts (protobuf-net) |
| Tools | `tools/*` | `Avalon.Exporter` (+ `.Emitters`: wire schema, catalogs, navmesh vectors), `Avalon.ChunkGen`, `Avalon.Balance`, `Avalon.Benchmarking`, `Avalon.Commerce.Check`, `Avalon.EmailVerification.Check` |
| Vendored | `vendor/DotRecast` | Navmesh (Recast/Detour); not ours to restyle |

`src/Server/Avalon.PluginFramework` is an empty placeholder project.

Three Postgres contexts: `AuthDbContext` (shared by everything, with Redis), and per world a `WorldDbContext` (reference data) and a `CharacterDbContext` (characters, items). A world server holds one world's pair (`Database:World`, `Database:Characters`); the REST API holds them per world under `Database:Worlds:<id>`, worlds both and identity only Characters ([docs/api-worlds.md](docs/api-worlds.md)). EF sensitive-data logging is on only in Development, and `Microsoft.EntityFrameworkCore` logs at Warning and above.

## Working rules

- **PR titles and player notes** (`.github/workflows/pr-check.yml`, the only required check — verify the build and tests yourself before merging). Title: `type(scope)!: summary (#123)`; type one of `feat`, `fix`, `docs`, `chore`, `refactor`, `test`, `ci`, `perf`, `build`, `style`, `revert`; scope optional, lowercase `[a-z0-9-]+`, comma-separated; `!` for a breaking change; summary not starting with a space and not ending with a period; optional trailing ` (#123)` or ` (#123, #456)`. The body needs a line `Player note: <one sentence>` in patch-note style, third person, starting with what changed, never "I" or "we" (`Player note: Fixed an issue where heals could raise health above the maximum.`; internal work: `Player note: No gameplay changes: faster builds.`). `[item:14]`, `[ability:210]` or `[item:14@3]` link an item or ability. `gh pr create --body` skips the PR template, so write the line yourself. The rules live in the `avalon-release-notes` repository.
- **Code style**: Microsoft's C# conventions, encoded in `.editorconfig` and enforced by every build (warnings fail CI): [docs/coding-standard.md](docs/coding-standard.md).
- **Schema and seed data change only through EF migrations**, never by hand on a database. Balance tables (`ClassLevelStats`, `ClassStatFactors`, `CombatFormula`, `CreatureBaseStats`, `CreatureRarityModifiers`, `CreatureTemplates`, `AbilityTemplates`, `ItemTemplates`, `CharacterCreateInfos`, `VendorStocks`, `AuraTemplates`, `AuraStatModifiers`) change only through `HasData` plus a generated migration, because the balance simulator reads `HasData` as the seed; `ModelDriftShould` fails when `HasData` changed without one. A seed migration that points existing rows at rows it also inserts must be reordered by hand (EF emits `UpdateData` before `InsertData`). CI never runs migrations: verify new ones against a throwaway Postgres.
- **Packet contracts**: after any change, re-export the wire schema (Commands above) or `WireSchemaShould` fails. A retired opcode is deleted outright, with its packet class, and its number may be reused once clients re-vendored the schema (#697); nothing in `NetworkPacketType` is `[Obsolete]`. Every other wire enum is append-only. The game client (repository Avalon.Client) vendors the generated schema.
- **`Avalon.World.Public` is the future modding API.** Nothing there may grant privilege: no setter for an access level, no way to grant a kill, end an aura, move a creature home or change what a client sees. Privileged state lives World-side (`internal`, or on the concrete World type) and shared non-API types go in `Avalon.Common`.
- **Game-server performance is critical.** Changes on the tick path, packet handling, replication, combat and AI need benchmark evidence (`tools/Avalon.Benchmarking`) and must not add per-tick or per-packet allocations.
- Commits, PRs and issues carry no AI-assistant attribution (no session links, no `Co-Authored-By` trailers). Code, docs, tests and commits never cite other games or game servers; explain a term on its own.

## Cross-cutting invariants

- **Tick-thread rule** ([docs/world-simulation.md](docs/world-simulation.md)). World state changes on the tick thread only. Slow work (database reads, navmesh bakes, instance builds, saves, reload prepares, script compiles, Redis writes) runs off the tick and hands its result back through a queue the tick drains or a connection continuation (`EnqueueContinuation`). Instance membership, registry indexes, party mutators, `OnlineCharacters` and ignore-list writes assert `TickThreadGuard.AssertOnTick` (enabled by `Game:TickThreadGuard`, and always in the World test assembly).
- **A new client-to-server opcode needs a session-filter entry** (`MapSessionFilter` for in-map packets, `WorldSessionFilter` before a character), not just a handler; a packet no filter accepts is dropped with a "could not find a handler" warning ([docs/packet-handlers.md](docs/packet-handlers.md)).
- **Reflection-bound registration: a reference grep proves nothing.** Packet handlers are found by attribute scan; AI, ability, quest, item and aura scripts by type name (`ScriptManager`, names stored in the database) and built with `ActivatorUtilities.CreateInstance` at fixed call sites with fixed runtime arguments (AI: `(creature, instance)`; ability: `(ability, caster, aim, arena)`; quest, item and aura scripts: none, over `QuestScriptServices`, which hands out only loggers and `TimeProvider`). The `*ConstructibilityShould` tests build every shipped and seeded script that way. Never rename or delete such a type on grep evidence alone.
- **Commands run on the tick and never await**: `ICommand.Execute` is `void`; slow work goes through `ctx.Then` (`CommandsNeverBlockTheTickShould`).
- **Access levels are `[Flags]`**: test with `AccessLevels.ForWorld(...).Allows(...)` or `RequiredAccess.Allows(...)`, never `<=`/`>=` (PTR 32 and Tournament 16 are numerically above Admin 4).
- **Rows are written by column, never whole**: account writes (`AccountRepository` / `IAccountRepository` methods such as `TryRecordLoginAsync`, `MarkOfflineAsync`, `SetPasswordAsync`) never write back a row that was read, so a ban or lock written in between survives. Character names change only by insert or `TryRenameAsync`.
- **Memory is authoritative for a character in the world**: change items only through `IInventoryService` and money only through `IWallet`; writing a container directly, or `ICharacterInventory.Load` after select, is neither saved nor sent ([docs/inventory-and-saves.md](docs/inventory-and-saves.md)).
- **An open NPC conversation is checked on every use**: any handler acting on it (bank, shop, quest, dialogue choice) re-checks the dialogue leash (`NpcInteraction.IsWithinLeash`).
- **Value objects** (`ValueObject<T>`: `AccountId`, `WorldId`, `CharacterId`, ...) stay inside the server: EF uses `HasConversion`, packets declare primitives, REST DTOs declare primitives (`ApiContractShould`). Nothing serializes them automatically ([docs/valueobject-openapi.md](docs/valueobject-openapi.md)).
- **Rules that time things read the container's `TimeProvider`** (encounters, taunts, cooldowns, PvP and party timers, maintenance deadlines), so tests can drive them; new timing code does the same rather than calling `DateTime.UtcNow`.

## Subsystems

### Networking
- [Packet protocol](docs/networking-packet-protocol.md): custom TCP with protobuf-net; every packet is a `NetworkPacket` (header: `NetworkPacketType` opcode, flags, protocol, version) around a payload. Also [schema design](docs/networking-proto-schema-design.md), [session crypto](docs/crypto-v1-derivation.md), [graceful shutdown](docs/networking-graceful-shutdown.md).
- [Packet handlers, session filters, reflection-bound registration](docs/packet-handlers.md): auth handlers implement `IAuthPacketHandler<T>` (DI); world handlers `IWorldPacketHandler<T>` or `WorldPacketHandler<T>` with `[PacketHandler(NetworkPacketType.X)]`, built by `ActivatorUtilities`.

### Auth and accounts
- [Auth server login flow](docs/auth-server.md): handshake, password and MFA login, world list and select, the one-time world key (its `DEL` spends it), per-source and per-username login budgets (`SourceBudget`, `UsernameBudget`), `Online` owned by one connection, the online sweep, unique usernames and emails, single-use TOTP, `PostLoginGuard`, and the Redis keys. Exactly one auth server is supported.
- [REST API authentication](docs/api-authentication.md): access JWTs and PATs revalidated per request (`AccountAccessCheck`), refresh rotation with a 5 s grace, the shared login policy, reauthentication for sensitive actions, rate limiting, registration budgets, credentials version (`cver`), email change.
- [Redis keys](docs/redis-cache-keys.md) (most literals live in `CacheKeys`), [session management](docs/security-session-management.md), Steam: [authentication](docs/steam-authentication.md), [workloads](docs/steam-authentication-workloads.md), [account links](docs/steam-account-links.md).

### REST API
- [API services](docs/api-services.md): identity, worlds, commerce and distribution, one binary; `Application:Services`; the route manifest (a new endpoint needs an owner in `RouteOwnershipShould`); the chart's `services`, `routes` and `networkPolicy`; the rollout (#802).
- [API reference](docs/api-reference.md) and the published OpenAPI document (the Avalon.Dashboard repository generates its client from it).
- [Multi-world API](docs/api-worlds.md): `/world/{worldId}/...` routes on `[WorldScoped]` controllers, `WorldRouteMiddleware` (non-disclosing 404 before 503), per-world databases, public routes and link previews.
- [Live template editing](docs/live-template-editing.md): `PUT` item, ability, creature and aura templates with `If-Match`; only worlds in `Application:Templates:EditableWorlds`; reload requested over Redis.
- [Commerce](docs/commerce-configuration.md): purchases, payment notifications, sandbox purchases.

### World
- [World simulation](docs/world-simulation.md): `WorldServer` ticks at about 60 Hz in two passes (session packets, then each instance's map packets); instances are built off the tick and published on it; `InstanceTicker` contains each instance's failures. Also creature placement, town NPCs and dialogue, AI scripts and locomotion contracts, abilities (aim, shapes, costs, telegraphs, cast ids), the combat formula, haste, regeneration, ground loot, interest-based replication and `/reload`.
- [Instanced maps](docs/instanced-maps.md), [map generation](docs/map-generation.md) (chunk layouts, procedural forest, ChunkGen), [creature system](docs/creature-system.md), [character login flow](docs/character-login-flow.md) (client view), [readiness barrier](docs/character-readiness-barrier.md).

### Gameplay
- [Parties](docs/parties.md) / [protocol](docs/party-protocol.md): in-memory `PartyService` (tick thread only), party instances, leave countdown, health scaling, shared kills, loot and experience.
- [Quests](docs/quests.md) / [protocol](docs/quest-protocol.md): `QuestCatalog` validation, `QuestService` (tick thread only), stages and objectives, quest drops, saves, quest scripts.
- [Auras](docs/auras.md) / [protocol](docs/aura-protocol.md): `AuraSystem` per instance, snapshots, schedules anchored at expiry, stacking, saves, replication.
- [Inventory, gold, vendors and saves](docs/inventory-and-saves.md): Character DB ownership, `ICharacterSaver` (one transaction, chained per character), one world session per account, Change Character, character names, item requests, equipment, bank, vendors, stats and the character sheet.
- [Item use](docs/item-use.md) / [protocol](docs/item-use-protocol.md): equip or run an `ItemScript` through `IItemUseContext`; cooldowns, cast bar, consumption.
- [Chat and commands](docs/chat-and-commands.md): adding a command, channels, whispers, rate limit, ignore list.

### Operations and tooling
- [World maintenance](docs/world-maintenance.md): maintenance cutoffs, derived world status, admission at every door, restart drain.
- [Configuration reference](docs/configuration-reference.md), [instrumentation](docs/instrumentation.md), [benchmarks](docs/benchmarks.md), [GC pressure](docs/gc-pressure.md), [DbContext lifetime](docs/dbcontext-lifetime.md), [tooling](docs/tooling.md), [decision log](docs/architecture-decisions.md).
- [Balance simulator and service](docs/balance.md): `Simulation.Run` over the `HasData` seed; the in-cluster service behind the API's `/balance/*` proxy.
- [Development setup](docs/development-setup.md): every command, EF design-time rules, the JWT signing key.

## Testing

- **Write few, meaningful tests (owner rule).** A test exists to catch a behaviour breaking, not to cover lines. Test rules and outcomes through the code's real surface: ordering, races, security checks, money and items, persistence, wire formats, startup refusals. Do not write:
  - tests of trivial code (a property that stores its value, a constructor assignment, a mapper that copies fields, configuration binding that restates `appsettings`);
  - tests of the substitutes or of the test harness itself;
  - tests that pin incidental detail (log wording, the order of calls nothing relies on);
  - near-duplicates. Input variations go in one `[Theory]`, and a table of cases is one data-driven test, not one test per row.

  A refactor that moves code moves its tests and adds none for the move itself. Before adding a test, check that no existing one already fails when this behaviour breaks. When in doubt, one scenario test through the public surface beats several tests of its parts.
- xUnit with NSubstitute; files are `<Subject>Should.cs`, methods `Should_<verb>_<condition>` or a descriptive sentence.
- No real Redis or Postgres in unit tests: external dependencies are substituted (database tests use SQLite in memory).
- Auth handler tests build handlers directly (`new CAuthHandler(...)` with `NullLoggerFactory` and substitutes).
- Guard tests protect invariants that are easy to break silently: `ModelDriftShould`, `SeedIntegrityShould`, `WireSchemaShould`, the `*ConstructibilityShould` script tests, `WorldHostGraphShould`, `CommandsNeverBlockTheTickShould`, `ApiContractShould`, `CombatAssemblyShould`, `BalanceCoreAssemblyShould`, `SimulatorParityShould`, and the API split's (`RouteOwnershipShould`, `EveryRouteReachableShould`, `ApiHostGraphShould`, `ApiServiceBoundariesShould`, `CrossServiceAuthenticationShould`). Never delete one to make a change pass.

## Open work

Tracked as GitHub issues on `WoozChucky/Avalon.Server`. A few code comments still carry the older `TODO-0NN` numbers (for example `TODO-029`, decoupling the world server from the auth database, now issue #168).
