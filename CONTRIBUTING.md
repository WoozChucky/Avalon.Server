# Contributing to Avalon.Server

Thank you for your interest in contributing. This document covers how to set up a development environment, the conventions used in this codebase, and the process for submitting changes.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Local Setup](#local-setup)
- [Project Structure](#project-structure)
- [Development Workflow](#development-workflow)
- [Coding Conventions](#coding-conventions)
- [Testing](#testing)
- [Submitting a Pull Request](#submitting-a-pull-request)
- [Reporting Issues](#reporting-issues)

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (for infrastructure services)
- A PostgreSQL client (optional, for direct DB inspection)

---

## Zero-Config Dev Environment

The repository is intentionally set up so that cloning and running is enough to get started — no manual
configuration required. Specifically:

- **`appsettings.json` files** contain hardcoded local-dev credentials (Postgres password `123`, Redis password
  `123`, JWT signing key, etc.). These are development-only defaults, safe to use locally, and deliberately
  committed so contributors can run the project immediately.
- **`certs/cert-tcp.pfx`** is a pre-generated self-signed TLS certificate (password `avalon`) used by the Auth
  TCP server. It is committed for the same reason — so no manual cert generation is needed.
- **`docker-compose.yml`** uses matching credentials so the infra spins up in sync with the app config.

> None of these values are intended for production. For any real deployment, override all secrets via environment
> variables or a secrets manager.

---

## Local Setup

1. **Clone with submodules** (the `vendor/DotRecast` navmesh library is a git submodule):
   ```bash
   git clone --recurse-submodules https://github.com/<org>/Avalon.Server.git
   cd Avalon.Server
   ```

2. **Start infrastructure** (Redis + PostgreSQL):
   ```bash
   docker compose up -d
   ```
   Optionally include Redis Insight:
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.tools.yml up -d
   ```

3. **Restore and build:**
   ```bash
   dotnet restore
   dotnet build --no-restore
   ```

4. **Run the servers** (separate terminals):
   ```bash
   dotnet run --project src/Server/Avalon.Api
   dotnet run --project src/Server/Avalon.Server.Auth
   dotnet run --project src/Server/Avalon.Server.World
   ```

5. **API docs** are served at `https://localhost:<port>/scalar`.

---

## Project Structure

| Area | Path | Notes |
|---|---|---|
| REST API | `src/Server/Avalon.Api` | ASP.NET Core, JWT, OpenAPI |
| Auth Server | `src/Server/Avalon.Server.Auth` | TCP login, MFA, world-key issuance |
| World Server | `src/Server/Avalon.Server.World` | 60 Hz simulation loop |
| Core world logic | `src/Server/Avalon.World` | Maps, entities, spells, AI scripts |
| Shared libraries | `src/Shared/` | Domain, networking, config, metrics |
| Database projects | `src/Server/Avalon.Database.*` | EF Core contexts + migrations |
| Infrastructure | `src/Server/Avalon.Infrastructure` | Redis wrapper, MFA, cache keys |
| Tools | `tools/` | Migration CLI, benchmarks |
| Tests | `tests/` | Unit tests per component |

For a full architectural walkthrough see `README.md` and the documents under `docs/`.

---

## Development Workflow

### Adding a `ValueObject<T>`

1. Create a class deriving from `ValueObject<TPrimitive>` in `src/Shared/Avalon.Common`.
2. Add validation rules in the constructor or factory method.
3. OpenAPI schema and JSON serialization are handled automatically — no extra registration needed.

### Adding a packet handler

1. Define the packet contract in `src/Shared/Avalon.Network.Packets` and add a `NetworkPacketType` enum value.
2. For **Auth**: implement `IAuthPacketHandler<TPacket>` in `src/Server/Avalon.Server.Auth/Handlers/`.
3. For **World** (server layer): implement `IWorldPacketHandler<TPacket>` in `src/Server/Avalon.Server.World/Handlers/` and decorate with `[PacketHandler(NetworkPacketType.X)]`.
4. For **World** (core layer): implement `WorldPacketHandler<TPacket>` in `src/Server/Avalon.World/Handlers/`.

See [networking-packet-protocol.md](networking-packet-protocol.md) for the full protocol reference.

### Adding a world script

1. Define the contract in `src/Server/Avalon.World.Scripts.Abstractions`.
2. Implement in `src/Server/Avalon.World.Scripts`.
3. Register via the DI extension method in `Avalon.World`'s `ServiceExtensions`.

### Adding a chat command

1. Implement `ICommand` in `src/Server/Avalon.World/Chat/`.
2. Register it as `services.AddSingleton<ICommand, YourCommand>()` in `ServiceExtensions.AddWorldServices`.

### Adding a database migration

`Avalon.Api` is used as the EF design-time startup project. This is intentional for the current early stage of
development; migration execution will be decoupled to the deployment/hosting solution as the project matures.

```bash
dotnet ef migrations add <Name> \
  --project src/Server/Avalon.Database.Auth \
  --startup-project src/Server/Avalon.Api \
  --context AuthDbContext
```

Replace `Auth` / `AuthDbContext` with `Character` / `CharacterDbContext` or `World` / `WorldDbContext` as needed.
Convenience `add-migration.ps1` scripts in each `Avalon.Database.*` project wrap this command.

Migrations are applied automatically when `Avalon.Api` starts — no separate apply step is needed in development.

---

## Coding Conventions

- **Framework:** .NET 10 / C#. Follow standard C# naming conventions.
- **Value objects:** Wrap primitive IDs in `ValueObject<T>` (see `src/Shared/Avalon.Common`). Do not pass raw `int`/`long` IDs across layer boundaries.
- **No raw strings for Redis keys:** all key patterns live in `CacheKeys` in `src/Server/Avalon.Infrastructure`.
- **Public abstractions:** if a type is consumed by more than one project, it belongs in a `*.Public` or `*.Abstractions` project, not in the implementation project.
- **No direct cross-context DB access:** each `DbContext` (`Auth`, `Character`, `World`) is owned by its server. Cross-context data exchange goes through Redis or a service interface.
- **Comments:** only where the logic is non-obvious. Avoid restating what the code already says.
- **Security:** never embed credentials, keys, or connection strings in source. Use `appsettings.Development.json` (gitignored) or environment variables.

---

## Testing

```bash
# Run all tests
dotnet test --no-build

# Run a single project
dotnet test tests/Avalon.Server.Auth.UnitTests

# Run a specific class
dotnet test tests/Avalon.Server.Auth.UnitTests --filter "FullyQualifiedName~CAuthHandlerShould"
```

- Framework: **xUnit** + **NSubstitute**.
- Test files are named `<Subject>Should.cs`; methods follow `Should_<verb>_<condition>`.
- No real infrastructure in unit tests — substitute all external dependencies.
- New behaviour should be accompanied by tests. Bug fixes should include a regression test where practical.

---

## Submitting a Pull Request

1. Fork the repository and create a branch from `main`:
   ```bash
   git checkout -b feat/my-feature
   ```

2. Make your changes, following the conventions above.

3. Ensure all tests pass and the solution builds in Release:
   ```bash
   dotnet build -c Release
   dotnet test --no-build
   ```

4. Push your branch and open a PR against `main`.

5. In the PR description, explain:
   - **What** changed and **why**.
   - Any design trade-offs or alternatives you considered.
   - Steps to test the change manually (if applicable).

6. Keep PRs focused. If you have unrelated improvements, open separate PRs.

---

## Reporting Issues

- Use [GitHub Issues](../../issues) to report bugs or request features.
- For bugs, include the component (API / Auth Server / World Server), steps to reproduce, expected vs. actual behaviour, and relevant log output.
- Check `TODO.md` before opening a feature request — it may already be tracked there.

---

## License

By contributing you agree that your contributions will be licensed under the [MIT License](LICENSE).
