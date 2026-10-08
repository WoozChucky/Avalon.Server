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

## Almost Zero-Config Dev Environment

Almost everything a local run needs is committed. What is not, because a committed copy would be a working credential
for anyone, is made on your machine by one command (the Aspire AppHost, or `tools/Avalon.LocalDev setup` for a plain
`dotnet run`; step 4 of [Local Setup](#local-setup)). Specifically:

- **`appsettings.json` files** contain hardcoded local-dev credentials (Postgres password `123`, Redis password
  `123`, etc.). These are development-only defaults, safe to use locally, and deliberately
  committed so contributors can run the project immediately. The API's `appsettings.Development.json` adds world 1's
  local databases and placeholder store settings (Steam's test app id 480 and a dummy publisher key).
- **`certs/cert-tcp.pfx`** is a pre-generated self-signed TLS certificate (password `avalon`) used by the Auth
  TCP server. It is committed for the same reason — so no manual cert generation is needed.
- **`docker-compose.yml`** uses matching credentials so the infra spins up in sync with the app config.
- **Not committed, made locally:** the REST API's signing keys (a committed key lets anyone forge a token for any
  account, so `Avalon.Api` refuses to start without one, #482), and the world's three private TLS certificates (the
  world server's own, its client certificate for the API's game workload listener, and that listener's), with each
  side's pin of the other. The AppHost makes them on every run; `setup` makes them once into the gitignored
  `certificates/local/` and writes them to the API's and the world server's `dotnet user-secrets`, outside the
  repository.
- **A license:** entering a world needs one, and only a purchase writes one, so in Development the API grants it to
  the seeded `ADMIN` account (password `123`) at startup.

> None of these values are intended for production. For any real deployment, override all secrets via environment
> variables or a secrets manager.

---

## Local Setup

The full path, from clone to the game client in a world, with what each step sets up and what to do when one fails,
is [Development setup: from clone to client in world](docs/development-setup.md#from-clone-to-client-in-world).

1. **Clone with submodules** (the `vendor/DotRecast` navmesh library is a git submodule):
   ```bash
   git clone --recurse-submodules https://github.com/<org>/Avalon.Server.git
   cd Avalon.Server
   ```

2. **Trust the ASP.NET Core development certificate**, once per machine. The API serves https with it, and the game
   client talks to the API only over https:
   ```bash
   dotnet dev-certs https --trust
   ```

3. **Restore and build:**
   ```bash
   dotnet restore
   dotnet build --no-restore
   ```

4. **Run everything**, in one command with the Aspire AppHost (it starts Redis and Postgres as containers on 6379
   and 5432, so stop the docker compose ones first, and makes every key and certificate on each run):
   ```bash
   dotnet run --project src/Server/Avalon
   ```
   or by hand, in separate terminals, the API first (it migrates the auth database, which the world server reads but
   never migrates). `setup` runs once, and again whenever you want new certificates:
   ```bash
   docker compose up -d redis postgres
   dotnet run --project tools/Avalon.LocalDev -- setup
   dotnet run --project src/Server/Avalon.Api
   dotnet run --project src/Server/Avalon.Server.Auth
   dotnet run --project src/Server/Avalon.Server.World
   ```
   Optionally include Redis Insight: `docker compose -f docker-compose.yml -f docker-compose.tools.yml up -d`.
   Without `setup`, `Avalon.Api` exits at startup with `System.InvalidOperationException:
   Application:Authentication:SigningKey is not set`, and the world server with "A world TLS certificate is
   required". The keys can also be set by hand: see
   [docs/development-setup.md](docs/development-setup.md#rest-api-signing-key).

5. **Check it, and start the client:**
   ```bash
   dotnet run --project tools/Avalon.LocalDev -- check     # the client's REST chain up to a join ticket, for ADMIN/123
   dotnet run --project tools/Avalon.LocalDev -- login --launch <the client's build>/runtime.exe
   ```

6. **API docs** (Scalar) are served in Development at `http://localhost:5210/scalar`.

---

## Project Structure

| Area | Path | Notes |
|---|---|---|
| REST API | `src/Server/Avalon.Api` | ASP.NET Core, JWT, OpenAPI |
| Auth Server | `src/Server/Avalon.Server.Auth` | TCP login, MFA, world list |
| World Server | `src/Server/Avalon.Server.World` | 60 Hz simulation loop |
| Core world logic | `src/Server/Avalon.World` | Maps, entities, spells, AI scripts |
| Shared libraries | `src/Shared/` | Domain, networking, config, metrics |
| Database projects | `src/Server/Avalon.Database.*` | EF Core contexts + migrations |
| Infrastructure | `src/Server/Avalon.Infrastructure` | Redis wrapper, MFA, cache keys |
| Tools | `tools/` | Benchmarks, exporters, map generation, local runs (`Avalon.LocalDev`) |
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
3. For **World**, the connection layer (admission and the version handshake): implement the generic `IWorldPacketHandler<TPacket>` in `src/Server/Avalon.Server.World/Handlers/`.
4. For **World**, the game layer: implement `WorldPacketHandler<TPacket>` in `src/Server/Avalon.World/Handlers/`, decorate it with `[PacketHandler(NetworkPacketType.X)]`, and give the opcode a session filter entry (`MapSessionFilter` or `WorldSessionFilter`).

See [packet handlers](https://github.com/WoozChucky/Avalon.Server/blob/main/docs/packet-handlers.md) for the full rules.

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

Migrations are applied automatically, with no separate apply step in development: the auth database's when `Avalon.Api`
(or the auth server) starts, each world's World and Characters databases' when its world server starts.

---

## Coding Conventions

- **Framework:** .NET 10 / C#. Follow the coding standard in `docs/coding-standard.md`, which every build enforces.
- **Value objects:** Wrap primitive IDs in `ValueObject<T>` (see `src/Shared/Avalon.Common`). Do not pass raw `int`/`long` IDs across layer boundaries.
- **No raw strings for Redis keys:** all key patterns live in `CacheKeys` in `src/Server/Avalon.Infrastructure`.
- **Public abstractions:** if a type is consumed by more than one project, it belongs in a `*.Public` or `*.Abstractions` project, not in the implementation project.
- **No direct cross-context DB access:** each `DbContext` (`Auth`, `Character`, `World`) is owned by its server. Cross-context data exchange goes through Redis or a service interface.
- **Comments:** only where the logic is non-obvious. Avoid restating what the code already says.
- **Security:** never embed credentials, keys, or connection strings in source. Use `dotnet user-secrets` or environment variables.

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

4. Push your branch and open a PR against `main`. Its title must follow Conventional Commits, which CI
   checks: `type(scope)!: summary (#123)`, for example
   `fix(security): keep secrets out of Redis key names and exception messages (#535)`. The type is one of
   `feat`, `fix`, `docs`, `chore`, `refactor`, `test`, `ci`, `perf`, `build`, `style` or `revert`; the
   scope (lowercase, commas between several), the `!` for a breaking change and the issue reference are
   optional; the summary has no closing period. The full rule is in
   [avalon-release-notes](https://github.com/WoozChucky/avalon-release-notes).

   The description also needs a line `Player note: <one plain sentence>` saying what a player would notice
   (for internal work, what it means, e.g. "Faster builds; nothing changes in the game"). CI checks it, and it
   is shown on the public changelog.

5. In the PR description, explain:
   - **What** changed and **why**.
   - Any design trade-offs or alternatives you considered.
   - Steps to test the change manually (if applicable).

6. Keep PRs focused. If you have unrelated improvements, open separate PRs.

---

## Reporting Issues

- Use [GitHub Issues](https://github.com/WoozChucky/Avalon.Server/issues) to report bugs or request features.
- For bugs, include the component (API / Auth Server / World Server), steps to reproduce, expected vs. actual behaviour, and relevant log output.
- Search the existing issues before opening a feature request — it may already be tracked.

---

## License

By contributing you agree that your contributions will be licensed under the [MIT License](https://github.com/WoozChucky/Avalon.Server/blob/main/LICENSE).
