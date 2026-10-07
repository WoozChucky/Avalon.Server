# Development setup

Every command for building, testing, running and publishing the servers, the EF Core design-time rules, the chunk generator, and the REST API signing key. [CLAUDE.md](../CLAUDE.md) keeps the short list.

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
# The REST API: the host level (contract, route ownership, each service's host), the shared hosting, and each
# service's own tests (see docs/api-services.md)
dotnet test tests/Avalon.Api.UnitTests
dotnet test tests/Avalon.Api.Hosting.UnitTests
dotnet test tests/Avalon.Api.Identity.UnitTests
dotnet test tests/Avalon.Api.Worlds.UnitTests
dotnet test tests/Avalon.Api.Commerce.UnitTests
dotnet test tests/Avalon.Api.Distribution.UnitTests

# Run a specific test class or method
dotnet test tests/Avalon.Server.Auth.UnitTests --filter "FullyQualifiedName~CAuthHandlerShould"

# Start infrastructure (Redis + Postgres)
docker compose up -d redis postgres

# Run individual servers. The API runs all four of its services on http://localhost:5210 unless
# Application:Services names some (docs/api-services.md), e.g. one alone:
#   Application__Services__0=worlds dotnet run --project src/Server/Avalon.Api
dotnet run --project src/Server/Avalon.Api
dotnet run --project src/Server/Avalon.Server.Auth
dotnet run --project src/Server/Avalon.Server.World

# Read-only smoke check of a deployed API, every route group (the script's header lists its options; its expected
# statuses are a configured deployment's, so a local API without a build store or worlds 2 and 3 fails some lines)
BASE=https://avalon.example/api tools/api-smoke/smoke.sh

# Publish (Release)
dotnet publish src/Server/Avalon.Server.World/Avalon.Server.World.csproj -c Release
dotnet publish src/Server/Avalon.Server.Auth/Avalon.Server.Auth.csproj -c Release
dotnet publish src/Server/Avalon.Api/Avalon.Api.csproj -c Release
dotnet publish src/Server/Avalon.Balance.Service/Avalon.Balance.Service.csproj -c Release -p:SourceRevisionId=$(git rev-parse HEAD)

# Benchmarks
dotnet run -c Release --project tools/Avalon.Benchmarking

# Regenerate the generated forest chunk pieces (Maps/Chunks). Validates and bakes every piece in a temporary copy
# of Maps/ first and writes nothing if any fails; writes only Maps/Chunks (never chunk-pools.json or
# chunk-groups.json, which are edited by hand) and lists the forest_* files there it did not generate.
# ForestPiecesShould fails when the committed files differ from a fresh run. --maps names another Maps directory.
dotnet run --project tools/Avalon.ChunkGen -- forest [--maps <dir>]

# Regenerate Glimmerdell's four town squares (town_sw/se/nw/ne_01) from tools/Avalon.ChunkGen/TownPieces.cs: the
# approved layout as data, each building and prop a shape with a usemtl line the client colours. Same staging,
# validation and bake as the forest (the four squares baked together); TownPiecesShould fails when the committed
# files differ from a fresh run or a piece breaks the layout rules. The four older hand-authored town_* chunks
# are listed, never touched.
dotnet run --project tools/Avalon.ChunkGen -- town [--maps <dir>]

# Add a migration (EF design-time)
# Avalon.Api is the startup project by design (early-development convenience).
# Auth migrations are applied automatically when an API process running identity starts (by default every
# process runs it; a process without it waits for them), and by the auth server; each world server applies
# its own World and Characters migrations.
dotnet ef migrations add <Name> \
  --project src/Server/Avalon.Database.Auth \
  --startup-project src/Server/Avalon.Api \
  --context AuthDbContext
# The World and Character design-time factories read only Database:World:ConnectionString /
# Database:Characters:ConnectionString, from the environment or from that database project's
# user-secrets (Avalon.Database.World and Avalon.Database.Character each have a UserSecretsId), never
# from the API's appsettings files, which list Database:Worlds instead (#523). Without the string they
# refuse: "set Database__World__ConnectionString (or user-secrets) to run dotnet ef against a database".
# migrations add and has-pending-model-changes build the model and never connect, but still need the
# variable; pass a placeholder that points nowhere:
Database__World__ConnectionString="Host=127.0.0.1;Port=1;Database=design_time_only" \
  dotnet ef migrations add <Name> --project src/Server/Avalon.Database.World \
  --startup-project src/Server/Avalon.Api --context WorldDbContext
# The Characters context is the same with Database__Characters__ConnectionString and
# --project src/Server/Avalon.Database.Character --context CharacterDbContext.
# Commands that connect (database update, migrations list) take the real string the same way. Verify
# only against a throwaway Postgres, with --connection "$CONN" and the variable both set; nothing falls
# back to a local file any more, so a missing variable is a refusal, not a connection to port 5432.
# The Auth factory is unchanged: it still reads Database:Auth from the working directory's
# appsettings.Design.json, then appsettings.json, then the environment.
# A seed migration that points existing rows at rows it also inserts (a foreign key to a new
# row) must be reordered by hand: EF emits the UpdateData calls before the InsertData ones.
# Only Postgres catches this; the SQLite unit tests and CI never run the migration.
# Balance tables (ClassLevelStats, ClassStatFactors, CombatFormula, CreatureBaseStats, CreatureRarityModifiers,
# CreatureTemplates, AbilityTemplates, ItemTemplates, CharacterCreateInfos, VendorStocks, AuraTemplates,
# AuraStatModifiers) change only through
# HasData plus a generated migration, never through a raw migrationBuilder.Sql data edit: the balance simulator
# (tools/Avalon.Balance) reads HasData as the seed. Existing raw SQL fix-ups of existing rows stay as they are.
# ModelDriftShould (Avalon.Database.UnitTests) fails when HasData changed without a migration.
```

Target framework: **.NET 10**. Docker compose credentials default to password `123`.

**PR titles follow Conventional Commits and every PR has a player note** (`.github/workflows/pr-check.yml`, which runs `WoozChucky/avalon-release-notes/pr-check`): the title is `type(scope)!: summary (#123)`, with type one of `feat`, `fix`, `docs`, `chore`, `refactor`, `test`, `ci`, `perf`, `build`, `style`, `revert`; the scope optional, lowercase `[a-z0-9-]+`, commas between several (`(api,world)`); `!` optional, for a breaking change; the summary starting with any character but a space and not ending with a period; an optional trailing ` (#123)` or ` (#123, #456)`. Example: `fix(security): keep secrets out of Redis key names and exception messages (#535)`. The description needs a line `Player note: <one sentence>` written like a patch note: third person, starting with what changed, never "I" or "we" (the check refuses first-person notes), e.g. `Player note: Fixed an issue where heals could raise health above the maximum.`; for internal work, `Player note: No gameplay changes: faster builds.` A note can link an item or ability with `[item:14]` or `[ability:210]` (optionally `[item:14@3]`); the release fills in its name and the changelog shows its tooltip. It is shown on the public changelog, and `gh pr create --body` skips the PR template, so write the line into the body yourself. The rules live in the `avalon-release-notes` repository.

### REST API signing key

`Avalon.Api` will not start without a JWT signing key, and none is committed (#482). Every API process needs it, whichever services it runs ([API services](api-services.md)): identity signs access tokens with it and every service validates them with it, until #801 moves the signing to an ES256 key pair. The setting is `Application:Authentication:IssuerSigningKey` (environment variable `Application__Authentication__IssuerSigningKey`). `JwtSigningKey.Create` refuses a key that is missing, has leading or trailing whitespace, is under 32 bytes in UTF-8, or is on `JwtSigningKey.BlockedKeyHashes` (the SHA-256 of the one that used to sit in `appsettings.json`, public now), and the error names the setting. `AddApiAuthentication` (`Avalon.Api.Hosting`) registers the resulting `SymmetricSecurityKey` as a singleton, which identity's `JwtUtils` signs with and every service's bearer handler validates with; identity's game-auth cryptography derives its keys from it too. The services are one project, so one user-secret serves every process. Set it once per machine:

```bash
# Local runs (dotnet run, or the Aspire AppHost in src/Server/Avalon): Development loads user-secrets.
# Each line generates a key and stores it; run one from the repo root.
dotnet user-secrets set "Application:Authentication:IssuerSigningKey" "$(openssl rand -base64 48)" --project src/Server/Avalon.Api   # bash
$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); dotnet user-secrets set "Application:Authentication:IssuerSigningKey" ([Convert]::ToBase64String($b)) --project src/Server/Avalon.Api   # PowerShell 5.1 or 7

# Containers and every non-Development host: the environment. Keep the key out of shell history:
# put it in a file (api.env is gitignored) holding the line Application__Authentication__IssuerSigningKey=<key> ...
docker run --env-file ./api.env ...
# ... or name the variable without a value, so docker passes it through from the current environment.
docker run -e Application__Authentication__IssuerSigningKey ...

# Helm: the chart reads every secret through a Kubernetes Secret (secretKeyRef), never a plain env value.
# Either name a Secret you manage (preferred; keys listed in values.yaml; leave the chart's own secret
# values empty, or it refuses to render) ...
helm install ... --set existingSecret=avalon-api-secrets
# ... or let the chart create it, passing the key from a file (a trailing newline is trimmed).
# Without one of the two, the chart refuses to render.
helm install ... --set-file authentication.issuerSigningKey=./jwt.key
```

Rotating the key: with a chart-managed Secret, `helm upgrade` with the new file restarts the pods (a checksum annotation). With `existingSecret`, the chart cannot see the change, so after updating the Secret run `kubectl rollout restart deployment/<release>-avalon-api` (the chart's fullname; the chart renders a Deployment). Once the services run in releases of their own, every release that runs one holds the key: restart each, so they validate with the key identity signs with.

`docker-compose.yml` runs only Redis and Postgres, so it needs no key. EF design-time commands (`dotnet ef migrations ...` with `--startup-project src/Server/Avalon.Api`) need no key either: they build each context through its `IDesignTimeDbContextFactory` and never run the API host's registrations. Tests never read one: `ApiTestHost` (`tests/Avalon.Api.Testing`) and the other API tests make their own key in code. Changing the key invalidates every access token already issued; clients get a 401 and refresh.
