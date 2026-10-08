# Avalon.Server
[![.NET CI Pipeline](https://github.com/WoozChucky/Avalon.Server/actions/workflows/dotnet-ci.yml/badge.svg)](https://github.com/WoozChucky/Avalon.Server/actions/workflows/dotnet-ci.yml)

Official server-side solution for the Avalon ARPG: API, authentication, world simulation, networking, persistence,
telemetry, and extensibility frameworks.

## High-Level Overview

Avalon is split into bounded components that can scale and evolve independently:

- Public REST API (account + meta operations)
- Real‑time Auth server (login, MFA, world list; world entry is the REST game admission)
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
| `src/Server/Avalon.Api` | ASP.NET Core REST API host: runs the API services `Application:Services` names (all four when unset); OpenAPI generation |
| `src/Server/Avalon.Api.Identity`, `.Worlds`, `.Commerce`, `.Distribution` | The four API services ([API services](docs/api-services.md)): accounts, tokens and game admission; world content and characters; checkout and payments; launcher and client distribution |
| `src/Server/Avalon.Api.Hosting`, `.Contract` | What every API service runs on (pipeline, startup, token validation, rate limiting, world databases); the REST contract (DTOs) |
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

Tooling & Tests:

| Project | Role |
|---|---|
| `tools/Avalon.Benchmarking` | Micro-benchmarks for performance-sensitive components |
| `tools/Avalon.LocalDev` | Local runs: the world's local TLS certificates and user-secrets (`setup`), a game ticket for the client (`login`), the client's REST chain to a join ticket (`check`) |
| `tests/Avalon.Shared.UnitTests` | Unit tests for shared libraries |
| `tests/Avalon.Server.Auth.UnitTests` | Unit tests for authentication server components |
| `tests/Avalon.Server.World.UnitTests` | Unit tests for world server and simulation logic |
| `tests/Avalon.Api.UnitTests` | REST API host-level tests: the contract, route ownership, each service's host |
| `tests/Avalon.Api.Hosting.UnitTests`, `tests/Avalon.Api.<Service>.UnitTests` | The shared API hosting's tests, and each API service's |

## Core Cross-Cutting Concepts

### Value Objects

`ValueObject<TValue>` in `Avalon.Common` wraps primitives like `AccountId` and `WorldId` for strong typing. They serialize to their underlying primitive via custom `System.Text.Json` converters and appear as scalars in OpenAPI through a custom schema transformer. See → [ValueObject — OpenAPI Integration](docs/valueobject-openapi.md)

### OpenAPI & Scalar UI

In Development (or with `Application:ApiDocs:Enabled`; production serves neither, #803) the API exposes an interactive Scalar UI at `/scalar` and raw schema at `/openapi/v1.json`, built on `Microsoft.AspNetCore.OpenApi`. A custom schema transformer produces clean scalar definitions for value object types. See → [ValueObject — OpenAPI Integration](docs/valueobject-openapi.md)

### Authentication & Security

JWT issuance and validation, MFA (Otp.NET) with Redis-backed ephemeral secrets, BCrypt password hashing, refresh tokens, and session tracking in `AuthDb`. See → [Security — Session Management](docs/security-session-management.md)

### Networking

Custom TCP layer inside TLS (`Avalon.Hosting/Networking`: `ServerBase`, `Connection`) with Protobuf-net serialization and reflection-based packet handler registration. Auth and World servers share packet abstractions via `Avalon.Network.Packets`. See → [Networking — Packet Protocol](docs/networking-packet-protocol.md)

### Caching & Pub/Sub

Redis (via `IReplicatedCache`) holds the login budgets, MFA state, launcher codes, game tickets and join tickets, world readiness and presence, and carries cross-service pub/sub events. See → [Redis Cache Keys](docs/redis-cache-keys.md)

### Persistence

Postgres via Npgsql EF Core with three distinct DbContexts (`AuthDbContext`, `CharacterDbContext`, `WorldDbContext`) for separation of concerns and independent scaling. Design-time factories enable `dotnet ef` without a running host.

### Telemetry & Logging

Serilog for structured logging; OpenTelemetry instrumentation covers HTTP, EF Core, Redis, and runtime metrics. See → [Configuration Reference](docs/configuration-reference.md)

### World Simulation

`WorldServer` hosted service runs the tick loop at ~60 Hz. Each `MapInstance` manages entities, spell queues, creature AI, and state broadcast. See → [Spell System](docs/spell-system.md) · [Creature System](docs/creature-system.md) · [Architecture — Startup Flow](docs/architecture-startup-flow.md)

### Scripting & Extensibility

`Avalon.World.Scripts.Abstractions` isolates contracts for externally defined gameplay logic. Future dynamic loading planned via `PluginFramework`.

## Running Locally

Prerequisites: .NET 10 SDK, Docker, and a clone with its submodule (`git clone --recurse-submodules`). The whole
path, from a fresh clone to the game client in a world, with what each step sets up and what to do when one fails, is
in [Development setup: from clone to client in world](docs/development-setup.md#from-clone-to-client-in-world). In
short:

1. Once per machine, trust the ASP.NET Core development certificate. The API serves https with it, and the game
   client talks to the API only over https, trusting what the machine trusts:
   ```bash
   dotnet dev-certs https --trust
   ```
2. Start everything, either with the Aspire AppHost, in one command (it runs Redis and Postgres as containers on 6379
   and 5432, so stop the docker compose ones first):
   ```bash
   dotnet run --project src/Server/Avalon
   ```
   or by hand. `setup` makes, once, what nothing commits: the API's signing keys, the world's three local TLS
   certificates (into the gitignored `certificates/local/`) and the API's and the world server's user-secrets for them:
   ```bash
   docker compose up -d redis postgres
   dotnet run --project tools/Avalon.LocalDev -- setup
   dotnet run --project src/Server/Avalon.Api            # first: it migrates the auth database
   dotnet run --project src/Server/Avalon.Server.Auth
   dotnet run --project src/Server/Avalon.Server.World
   ```
   Optionally add Redis Insight for a GUI over Redis: `docker compose -f docker-compose.yml -f docker-compose.tools.yml up -d`.
3. Sign in as the seeded `ADMIN` account (password `123`), the one account that may enter world 1, the Development
   world; in Development the API grants it the license admission needs. `check` walks the client's REST chain up to a
   join ticket and prints the worlds; `login` hands the client a game ticket:
   ```bash
   dotnet run --project tools/Avalon.LocalDev -- check
   dotnet run --project tools/Avalon.LocalDev -- login --launch <the client's build>/runtime.exe
   ```
   The client's `netconfig.local.json` for a local run is in the same document.

The API answers on `https://localhost:7166` and `http://localhost:5210`; in Development (or with
`Application:ApiDocs:Enabled`) its Scalar UI is at `http://localhost:5210/scalar` and the raw schema at
`/openapi/v1.json`. It reaches every world listed under `Database:Worlds`; see
[Configuration Reference](docs/configuration-reference.md#rest-api-worlds).

Optional: email change (`POST /account/email/change`) answers 501 until the API has an email sender
(`Application:Email:Sender`, default `None`). In Development you can turn on the pickup sender, which writes each
email as an `.eml` file into `Application:Email:PickupDirectory` (default: an `avalon-mail` folder under your local
application data folder, readable only by you on Linux and macOS) instead of sending it. The API refuses it in any
other environment. See [REST API Email](docs/configuration-reference.md#rest-api-email).
```bash
dotnet user-secrets set "Application:Email:Sender" "Pickup" --project src/Server/Avalon.Api
dotnet user-secrets set "Application:Email:From" "noreply@avalon.monster" --project src/Server/Avalon.Api
```

Outside Development, the API's keys come from environment variables, and the Helm chart reads them from a Kubernetes
Secret ([REST API signing key](docs/development-setup.md#rest-api-signing-key), which also covers rotation).

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

The World and Character contexts read their design-time connection string only from the environment (`Database__World__ConnectionString`, `Database__Characters__ConnectionString`) or from that database project's user-secrets, and refuse without it. `migrations add` never connects, so a placeholder that points nowhere is enough:

```bash
Database__World__ConnectionString="Host=127.0.0.1;Port=1;Database=design_time_only" \
  dotnet ef migrations add <Name> --project src/Server/Avalon.Database.World \
  --startup-project src/Server/Avalon.Api --context WorldDbContext
```

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
| [Networking — Packet Protocol](docs/networking-packet-protocol.md) | Header fields, auth lifecycle, world admission, Redis patterns, failure modes |
| [Networking — Graceful Shutdown](docs/networking-graceful-shutdown.md) | Connection lifecycle, `SDisconnectPacket` schema, shutdown sequences |
| [Security — Session Management](docs/security-session-management.md) | Auth flow, world entry through join tickets, MFA, REST API authentication |
| [Architecture — Startup Flow](docs/architecture-startup-flow.md) | Bootstrap sequence for API, Auth Server, and World Server |
| [Map Generation](docs/map-generation.md) | Chunk authoring, town + procedural pipelines, end-to-end Unity → bake → playable, troubleshooting |
| [Instanced Map System](docs/instanced-maps.md) | Instance lifecycle: town routing, normal map re-entry, expiry, transitions, logout |
| [ValueObject — OpenAPI Integration](docs/valueobject-openapi.md) | Schema transformer pattern, registration, and shape transformation |
| [Configuration Reference](docs/configuration-reference.md) | All `appsettings.json` keys, validation rules, environment override guidance |
| [Redis Cache Keys](docs/redis-cache-keys.md) | All Redis key patterns and pub/sub channels: purpose, TTL, writer/consumer |
| [Spell System](docs/spell-system.md) | Spell lifecycle, power cost deduction, AoE targeting, creature spell support |
| [Creature System](docs/creature-system.md) | Creature lifecycle, AI scripting, XP rewards, respawn/remove timers |
| [Character Login Flow](docs/character-login-flow.md) | World admission → spawn sequence, inventory on login, instance IDs, movement |
| [Architecture Decisions](docs/architecture-decisions.md) | ADRs: World/Auth DB decoupling, chat command handler pattern, specializations |

Pending work is tracked in [GitHub Issues](https://github.com/WoozChucky/Avalon.Server/issues).

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
