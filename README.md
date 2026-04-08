# Avalon.Server
[![.NET CI Pipeline](https://github.com/WoozChucky/Avalon.Server/actions/workflows/dotnet-ci.yml/badge.svg)](https://github.com/WoozChucky/Avalon.Server/actions/workflows/dotnet-ci.yml)

Official server-side solution for the Avalon MMORPG: API, authentication, world simulation, networking, persistence,
telemetry, and extensibility frameworks.

## High-Level Overview

Avalon is split into bounded components that can scale and evolve independently:

- Public REST API (account + meta operations)
- Real‑time Auth server (login / token / world selection)
- Real‑time World server (simulation, state replication, gameplay logic)
- Shared foundational libraries (domain model, networking, value objects, metrics, configuration)
- Infrastructure services (Redis, Postgres)
- Tooling (migrations, benchmarking, scripting, migration console)

Communication paths:

- Clients → API (HTTPS + JWT) for out‑of‑band operations (account, web UX, management)
- Game Client → Auth Server (custom TCP packet protocol) for authentication & world ticket exchange
- Auth Server ↔ Redis (session, ephemeral keys, pub/sub)
- World Server ↔ Redis (cross‑node coordination, session/materialized view, pub/sub)
- World Server ↔ Databases (persistent character/world state)
- API ↔ Databases (account + world metadata) & Redis (caching, notifications)

## Solution Structure (Key Projects)

Server layer:

| Project | Role |
|---|---|
| `src/Server/Avalon` | [Aspire](https://dotnet.microsoft.com/en-us/apps/aspire) host for all server components |
| `src/Server/Avalon.Api` | ASP.NET Core REST API; OpenAPI generation; JWT issuance; JSON serialization |
| `src/Server/Avalon.Server.Auth` | Hosted service wrapping AuthServer (packet dispatcher, login flow, MFA) |
| `src/Server/Avalon.Server.World` | Hosted service running the simulation loop (maps, entities, spells, spawning) |
| `src/Server/Avalon.World` | Core world implementation (maps, grid, entities, spells, sessions, connections) |
| `src/Server/Avalon.World.Public` | Public abstractions (interfaces) consumed by other layers |
| `src/Server/Avalon.World.Scripts` / `.Abstractions` | Scripting system boundary for gameplay extensions |
| `src/Server/Avalon.Infrastructure` | Redis replicated cache, MFA hashing, config binding, helper services |
| `src/Server/Avalon.Database.*` | EF Core contexts and repositories (Auth, Character, World) + migrations |
| `src/Server/Avalon.Hosting` | Uniform host bootstrap (`AvalonHostBuilder`): converters, telemetry, configuration |
| `src/Server/Avalon.ServiceDefaults` | Shared service registration (logging, OpenTelemetry, resiliency, service discovery) |
| `src/Server/Avalon.PluginFramework` | Foundation for dynamic plugin loading (future roadmap) |

Shared libraries:

| Project | Role |
|---|---|
| `src/Shared/Avalon.Common` | `ValueObject<T>`, common utilities, JSON converters |
| `src/Shared/Avalon.Domain` | Rich domain model (Auth, Accounts, Devices, Worlds, etc.) |
| `src/Shared/Avalon.Configuration` | Strongly typed configuration objects |
| `src/Shared/Avalon.Network.*` | Custom packet protocol, attributes, base handlers, contracts |
| `src/Shared/Avalon.Metrics` | OpenTelemetry integration points |

Tooling & Tests:

| Project | Role |
|---|---|
| `tools/Avalon.Benchmarking` | Micro-benchmarks for performance-sensitive components |
| `tests/Avalon.Shared.UnitTests` | Unit tests for shared libraries |
| `tests/Avalon.Server.Auth.UnitTests` | Unit tests for authentication server components |
| `tests/Avalon.Server.World.UnitTests` | Unit tests for world server and simulation logic |
| `tests/Avalon.Api.UnitTests` | Unit tests for the REST API |

## Core Cross-Cutting Concepts

### Value Objects

`ValueObject<TValue>` in `Avalon.Common` wraps primitives like `AccountId` and `WorldId` for strong typing. They serialize to their underlying primitive via custom `System.Text.Json` converters and appear as scalars in OpenAPI through a custom schema transformer. See → [ValueObject — OpenAPI Integration](docs/valueobject-openapi.md)

### OpenAPI & Scalar UI

The API exposes an interactive Scalar UI at `/scalar` and raw schema at `/openapi/v1.json`, built on `Microsoft.AspNetCore.OpenApi`. A custom schema transformer produces clean scalar definitions for value object types. See → [ValueObject — OpenAPI Integration](docs/valueobject-openapi.md)

### Authentication & Security

JWT issuance and validation, MFA (Otp.NET) with Redis-backed ephemeral secrets, BCrypt password hashing, refresh tokens, and session tracking in `AuthDb`. See → [Security — Session Management](docs/security-session-management.md)

### Networking

Custom TCP layer (`Avalon.Network.Tcp`) with Protobuf-net serialization and reflection-based packet handler registration. Auth and World servers share packet abstractions via `Avalon.Network.Packets`. See → [Networking — Packet Protocol](docs/networking-packet-protocol.md)

### Caching & Pub/Sub

Redis (via `IReplicatedCache`) manages ephemeral session keys, MFA secrets, world exchange tokens, and cross-service pub/sub events. See → [Redis Cache Keys](docs/redis-cache-keys.md)

### Persistence

Postgres via Npgsql EF Core with three distinct DbContexts (`AuthDbContext`, `CharacterDbContext`, `WorldDbContext`) for separation of concerns and independent scaling. Design-time factories enable `dotnet ef` without a running host.

### Telemetry & Logging

Serilog for structured logging; OpenTelemetry instrumentation covers HTTP, EF Core, Redis, and runtime metrics. See → [Configuration Reference](docs/configuration-reference.md)

### World Simulation

`WorldServer` hosted service runs the tick loop at ~60 Hz. Each `MapInstance` manages entities, spell queues, creature AI, and state broadcast. See → [Spell System](docs/spell-system.md) · [Creature System](docs/creature-system.md) · [Architecture — Startup Flow](docs/architecture-startup-flow.md)

### Scripting & Extensibility

`Avalon.World.Scripts.Abstractions` isolates contracts for externally defined gameplay logic. Future dynamic loading planned via `PluginFramework`.

## Running Locally

Prerequisites: .NET 10 SDK, Docker (for infra services).

1. Start infra (Redis + PostgreSQL):
   ```bash
   docker compose up -d
   ```
   Optionally add Redis Insight for a GUI over Redis:
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.tools.yml up -d
   ```
2. Run the API — migrations are applied automatically on startup:
   ```bash
   dotnet run --project src/Server/Avalon.Api
   ```
3. Run Auth Server:
   ```bash
   dotnet run --project src/Server/Avalon.Server.Auth
   ```
4. Run World Server:
   ```bash
   dotnet run --project src/Server/Avalon.Server.World
   ```
5. Open API docs: `https://localhost:<port>/scalar` (Scalar UI) or `/openapi/v1.json`

## Migrations Workflow

> **Note:** During early development, `Avalon.Api` is used as the EF design-time startup project and applies migrations automatically on startup. Migration generation and execution will be decoupled as the project matures.

Generate a migration:
```bash
dotnet ef migrations add <Name> \
  --project src/Server/Avalon.Database.Auth \
  --startup-project src/Server/Avalon.Api \
  --context AuthDbContext
```
Replace `Auth` / `AuthDbContext` with `Character` / `CharacterDbContext` or `World` / `WorldDbContext` as needed.

## Testing

```bash
dotnet test
```

Run a specific project: `dotnet test tests/Avalon.Server.Auth.UnitTests`

## Benchmarking

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking
```

Use to regress-check simulation hot paths.

## Feature Documentation

| Document | Description |
|---|---|
| [Networking — Packet Protocol](docs/networking-packet-protocol.md) | Header fields, auth lifecycle, world handoff, Redis patterns, failure modes |
| [Networking — Graceful Shutdown](docs/networking-graceful-shutdown.md) | Connection lifecycle, `SDisconnectPacket` schema, shutdown sequences |
| [Security — Session Management](docs/security-session-management.md) | Auth flow, world key CSPRNG, bearer token validation, duplicate session guard |
| [Architecture — Startup Flow](docs/architecture-startup-flow.md) | Bootstrap sequence for API, Auth Server, and World Server |
| [ValueObject — OpenAPI Integration](docs/valueobject-openapi.md) | Schema transformer pattern, registration, and shape transformation |
| [Configuration Reference](docs/configuration-reference.md) | All `appsettings.json` keys, validation rules, environment override guidance |
| [Redis Cache Keys](docs/redis-cache-keys.md) | All Redis key patterns and pub/sub channels: purpose, TTL, writer/consumer |
| [Spell System](docs/spell-system.md) | Spell lifecycle, power cost deduction, AoE targeting, creature spell support |
| [Creature System](docs/creature-system.md) | Creature lifecycle, AI scripting, XP rewards, respawn/remove timers |
| [Character Login Flow](docs/character-login-flow.md) | World-select → spawn sequence, inventory on login, instance ID design |
| [Architecture Decisions](docs/architecture-decisions.md) | ADRs: World/Auth DB decoupling, chat command handler pattern, specializations |

For the full list of pending work items see [TODO.md](TODO.md).

## Roadmap

- Plugin hot-reload & isolation boundaries
- Horizontal world shard scaling (multi-process coordination via Redis pub/sub)
- Observability dashboards (Grafana / Prometheus integration)
- More test coverage (property-based / fuzzing for packet protocol)
- Rate limiting & advanced DDoS mitigation

## License

MIT (see repository root). Some vendor components (DotRecast, Raylib bindings) under their respective licenses.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for setup, extension patterns, and PR guidelines.
