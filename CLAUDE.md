# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Restore, build, test
dotnet restore
dotnet build --no-restore
dotnet test --no-build

# Run a single test project
dotnet test tests/Avalon.Server.Auth.UnitTests
dotnet test tests/Avalon.Server.World.UnitTests
dotnet test tests/Avalon.Shared.UnitTests
dotnet test tests/Avalon.Api.UnitTests

# Run a specific test class or method
dotnet test tests/Avalon.Server.Auth.UnitTests --filter "FullyQualifiedName~CAuthHandlerShould"

# Start infrastructure (Redis + Postgres)
docker compose up -d redis postgres

# Run individual servers
dotnet run --project src/Server/Avalon.Api
dotnet run --project src/Server/Avalon.Server.Auth
dotnet run --project src/Server/Avalon.Server.World

# Publish (Release)
dotnet publish src/Server/Avalon.Server.World/Avalon.Server.World.csproj -c Release
dotnet publish src/Server/Avalon.Server.Auth/Avalon.Server.Auth.csproj -c Release
dotnet publish src/Server/Avalon.Api/Avalon.Api.csproj -c Release

# Benchmarks
dotnet run -c Release --project tools/Avalon.Benchmarking

# Add a migration (EF design-time)
# Avalon.Api is the startup project by design (early-development convenience).
# Migrations are applied automatically when Avalon.Api starts.
dotnet ef migrations add <Name> \
  --project src/Server/Avalon.Database.Auth \
  --startup-project src/Server/Avalon.Api \
  --context AuthDbContext
```

Target framework: **.NET 10**. Docker compose credentials default to password `123`.

## Architecture

Avalon is split into three independently deployable real-time servers plus a REST API:

| Component | Project | Role |
|---|---|---|
| REST API | `Avalon.Api` | HTTPS/JWT, account management, OpenAPI (Scalar UI at `/scalar`) |
| Auth Server | `Avalon.Server.Auth` | TCP login flow, MFA, world-key issuance |
| World Server | `Avalon.Server.World` | Game simulation loop, packet dispatch, world lifecycle |
| Core world | `Avalon.World` | Instanced maps, entities, spell/creature systems, chat commands |

Shared libraries under `src/Shared/`:
- `Avalon.Network.Tcp` / `.Packets` / `.Packets.Abstractions` — custom TCP server, packet protocol (Protobuf-net)
- `Avalon.Domain` — rich domain model (Account, Character, Spell, Creature, etc.)
- `Avalon.Common` — `ValueObject<T>`, utilities, JSON converters, math
- `Avalon.Configuration` — strongly-typed config binding classes
- `Avalon.Metrics` — OpenTelemetry integration

Infrastructure: `Avalon.Infrastructure` — `IReplicatedCache` (Redis wrapper), `IMFAHashService`, `CacheKeys` (all Redis key strings centralized here), `ISecureRandom`.

Three separate Postgres DbContexts (via Npgsql EF Core): `AuthDbContext`, `CharacterDbContext`, `WorldDbContext`. Design-time factories in each `Avalon.Database.*` project enable `dotnet ef` without a running host.

## Packet Protocol

All client↔server communication is custom TCP with Protobuf-net. Every packet wraps a `NetworkPacket` (header + payload). The header carries `NetworkPacketType` (opcode), `NetworkPacketFlags` (encryption/compression bitmask), `NetworkProtocol` (channel grouping), and `Version`.

**Adding a packet handler:**
1. Define the packet contract in `Avalon.Network.Packets` with a `NetworkPacketType` enum value.
2. For Auth: implement `IAuthPacketHandler<TPacket>` in `Avalon.Server.Auth/Handlers/`.
3. For World (server layer): implement `IWorldPacketHandler<TPacket>` in `Avalon.Server.World/Handlers/`, decorated with `[PacketHandler(NetworkPacketType.X)]` — handlers are registered via reflection scan in `WorldServer` constructor.
4. For World (core layer): implement `WorldPacketHandler<TPacket>` in `Avalon.World/Handlers/`, also discovered by attribute scan.

Auth handlers are registered in DI and resolved manually; World handlers use `ActivatorUtilities.CreateInstance` in `WorldServer`.

## Auth Flow

```
CClientInfoPacket → SHandshakePacket → CHandshakePacket → SHandshakeResultPacket
→ CAuthPacket → SAuthResultPacket (BCrypt verify, lockout, MFA check)
→ CWorldListPacket → SWorldListPacket
→ CWorldSelectPacket → SWorldSelectPacket (CSPRNG world key written to Redis, SETNX inWorld mutex)
→ [new TCP to World] CExchangeWorldKeyPacket → SExchangeWorldKeyPacket (key consumed from Redis, inWorld mutex cleared)
```

**Key Redis patterns** (all string literals in `CacheKeys`):
- `world:{worldId}:keys:{base64}` — one-time world entry token (5 min TTL)
- `account:{accountId}:inWorld` — duplicate session mutex via `SETNX` (5 min TTL)
- `auth:account:{accountId}:mfa` — Redis Hash with MFA state (2 min TTL)
- `world:accounts:disconnect` — pub/sub: Auth→World, force-disconnect by accountId
- `auth:accounts:online` — pub/sub: login event (reserved, no subscriber yet)
- `world:{worldId}:select` — pub/sub: world-select event (reserved, for future sharding)

## World Simulation

`WorldServer` is an `IHostedService` that runs the tick loop at ~60 Hz (16.67ms intervals). On each tick it calls `World.Update(deltaTime)`, which ticks all active `MapInstance` objects via `IInstanceRegistry`.

- **MapInstance** (`Avalon.World/Instances/MapInstance.cs`) is the core simulation unit: manages entities in a flat tick context, runs `ChunkSpellSystem`, creature AI scripts, broadcasts state to clients. Implements `ISimulationContext`.
- **InstanceRegistry** (`Avalon.World/Instances/InstanceRegistry.cs`) owns all live instances. `GetOrCreateTownInstance` returns a shared persistent instance; `GetOrCreateNormalInstance` returns per-player instances with 15-min re-entry windows. `ProcessExpiredInstances` cleans up expired normal instances.
- **ISimulationContext** — minimal contract used by AI scripts, spell system, and respawner instead of the old `IChunk`.
- **ChunkSpellSystem** manages the spell queue; deducts power cost on `QueueSpell`, ticks active `SpellScript` instances.
- **SpellScript** / **CreatureAiScript** — scriptable gameplay logic; hot-reloadable via `IScriptHotReloader`. `SpellScript.Clone()` has a virtual base implementation using `MemberwiseClone` (subclasses override for extra mutable state).
- **CreatureRespawner** — manages respawn and corpse-removal timers from `ICreatureMetadata`.
- **EntityTrackingSystem** — tracks which entities are visible to which connections.

## Chat Commands

Commands live in `Avalon.World/Chat/`. To add a new command:
1. Implement `ICommand` (has `Name`, `Aliases[]`, `ExecuteAsync`).
2. Register as `services.AddSingleton<ICommand, YourCommand>()` in `ServiceExtensions.AddWorldServices`.
3. `CommandDispatcher` resolves all `ICommand` registrations from DI automatically.

`ChatMessageHandler` routes `/`-prefixed messages to `ICommandDispatcher`; unknown commands reply with "Unknown command." Non-slash messages broadcast to the current `MapInstance`.

## ValueObject Pattern

`ValueObject<TPrimitive>` (in `Avalon.Common`) wraps primitives like `AccountId`, `WorldId`, `CharacterId`. They:
- Serialize as their underlying primitive via `ValueObjectJsonConverterFactory`.
- Appear as scalar types in OpenAPI via `ValueObjectOpenapiSchemaTransformer`.

## Testing Conventions

- Framework: **xUnit** + **NSubstitute** for mocks.
- Test files are named `<Subject>Should.cs`, methods follow `Should_<verb>_<condition>` or descriptive names.
- No integration test infrastructure (no real Redis/Postgres in unit tests) — all external dependencies are substituted.
- Auth handler tests construct handlers directly via `new CAuthHandler(...)` with `NullLoggerFactory` and `NSubstitute` fakes.

## Key Open TODOs

See `TODO.md` for full details. High-priority open items:
- **TODO-007** — `AvalonAuthenticationHandler` bearer token validation is a hardcoded stub
- **TODO-013** — MFA second-factor flow is commented out in `CAuthHandler` (all dependencies exist)
- **TODO-017/018** — `AnimationId` missing from `SpellTemplate`/`SpellMetadata` (needs EF migration)
- **TODO-024** — Inventory not sent to client on character login
- **TODO-029** — World server depends on `AuthDbContext` via `AddAuthDatabase()` — should use Redis-backed `IAccountSessionService` instead
