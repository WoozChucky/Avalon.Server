# Development setup

Every command for building, testing, running and publishing the servers, the EF Core design-time rules, the chunk generator, and the REST API signing key. `CLAUDE.md`, at the repository root, keeps the short list.

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

# A local run, from clone to client in world: see "From clone to client in world" below. Once per machine:
dotnet dev-certs https --trust

# Everything at once (Aspire AppHost): containers, keys, certificates and every server, in order
dotnet run --project src/Server/Avalon

# Or by hand. Infrastructure (Redis + Postgres), then once the local settings (signing keys, the world's TLS
# certificates, user-secrets for the API and the world server), then the servers, the API first
docker compose up -d redis postgres
dotnet run --project tools/Avalon.LocalDev -- setup
# The API runs all four of its services, on https://localhost:7166 and http://localhost:5210 once setup has run,
# unless Application:Services names some (docs/api-services.md), e.g. one alone (without identity it must not hold
# the private signing key, so blank the user-secret; see "REST API signing key" below):
#   dotnet run --project src/Server/Avalon.Api -- --Application:Services:0=worlds --Application:Authentication:SigningKey=
dotnet run --project src/Server/Avalon.Api
dotnet run --project src/Server/Avalon.Server.Auth
dotnet run --project src/Server/Avalon.Server.World

# A game ticket for the client (ADMIN/123 by default), or the client's REST chain up to a join ticket
dotnet run --project tools/Avalon.LocalDev -- login --launch <path to the client's runtime.exe>
dotnet run --project tools/Avalon.LocalDev -- check

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

`Avalon.Api` will not start without its signing keys, and none is committed (#482, #801). Access tokens are ES256 only: identity signs them with an EC P-256 private key under a key id, and every API service checks them with the public key that key id names, so only a process that runs identity can mint one ([API services](api-services.md)). HS256, which every token before #801 was signed with, is refused whatever is configured; its old key, `Application:Authentication:IssuerSigningKey`, is ignored, and a process that still has it logs one warning at startup naming it.

| Setting | Read by | What it holds |
|---|---|---|
| `Application:Authentication:SigningKey` | identity only | The EC P-256 private key, PKCS#8, as PEM or as the base64 of its DER. A process that does not run identity refuses to start while it is set (an empty value counts as unset) |
| `Application:Authentication:SigningKeyId` | identity | The key id written into each token's header, for example `2026-10`: at most 64 letters, digits, `.`, `_` and `-` |
| `Application:Authentication:ValidationKeys:<key id>` | every service | A public key, the base64 of its DER SubjectPublicKeyInfo (or PEM). Public, not secret. Identity knows its own key without it; a process without identity refuses to start with none |
| `Application:GameAuth:HostKey` | identity | The key identity's game-auth cryptography (proofs, replay receipts, the Steam OpenID state) derives its keys from: a random value of at least 32 bytes. Identity refuses to start without it. A deployment from before #801 gives it the value of its old HS256 key (`Application:Authentication:IssuerSigningKey`), from which those keys were derived then, so what was protected with them stays readable |

`JwtKeys.Create` (`Avalon.Api.Hosting`) builds the keys while the host is built and stops startup, naming the setting, for a missing or unparsable private key, a key on another curve than P-256, a missing or malformed key id, a public key that does not parse or holds a private key, a public key listed under identity's key id that is not its own, and a process with no key to validate with. The game-auth host key keeps the rules of #482 (`GameAuthHostKey.From`, checked before identity serves): set, no leading or trailing whitespace, at least 32 bytes in UTF-8, and not on `GameAuthHostKey.BlockedKeyHashes` (the SHA-256 of the HS256 key that used to sit in `appsettings.json`, public now). A process started with `AVALON_OPENAPI_GENERATION_ONLY=true` (the docs build) makes a throwaway key in memory and needs none.

Make a key pair, the private key as a PKCS#8 PEM file and the public key as the base64 `ValidationKeys` takes:

```bash
# bash (openssl)
openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt -out jwt-es256.pem
openssl pkey -in jwt-es256.pem -pubout -outform DER | base64 -w0
```

```powershell
# PowerShell 7
$k = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
Set-Content -NoNewline jwt-es256.pem $k.ExportPkcs8PrivateKeyPem()
[Convert]::ToBase64String($k.ExportSubjectPublicKeyInfo())
```

**Local runs.** The Aspire AppHost (`src/Server/Avalon`) needs no user-secret of yours: it makes a key pair on every run (a token from an earlier run is refused, and the client refreshes) and keeps a generated game-auth host key in its own user-secrets. A plain `dotnet run --project src/Server/Avalon.Api` (Development loads user-secrets) needs the keys once per machine. `dotnet run --project tools/Avalon.LocalDev -- setup` makes them, under the key id `dev`, when the API's user-secrets hold none, with the rest of a local run's settings ([From clone to client in world](#from-clone-to-client-in-world)); by hand, from the repository root:

```bash
# bash: the private key, its key id, its public key and a game-auth host key
openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt -out jwt-es256.pem
dotnet user-secrets set "Application:Authentication:SigningKey" "$(cat jwt-es256.pem)" --project src/Server/Avalon.Api
dotnet user-secrets set "Application:Authentication:SigningKeyId" "dev" --project src/Server/Avalon.Api
dotnet user-secrets set "Application:Authentication:ValidationKeys:dev" "$(openssl pkey -in jwt-es256.pem -pubout -outform DER | base64 -w0)" --project src/Server/Avalon.Api
dotnet user-secrets set "Application:GameAuth:HostKey" "$(openssl rand -base64 48)" --project src/Server/Avalon.Api
rm jwt-es256.pem
```

```powershell
# PowerShell 7: the same
$k = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
dotnet user-secrets set "Application:Authentication:SigningKey" ([Convert]::ToBase64String($k.ExportPkcs8PrivateKey())) --project src/Server/Avalon.Api
dotnet user-secrets set "Application:Authentication:SigningKeyId" "dev" --project src/Server/Avalon.Api
dotnet user-secrets set "Application:Authentication:ValidationKeys:dev" ([Convert]::ToBase64String($k.ExportSubjectPublicKeyInfo())) --project src/Server/Avalon.Api
dotnet user-secrets set "Application:GameAuth:HostKey" ([Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))) --project src/Server/Avalon.Api
```

A machine that still holds `Application:Authentication:IssuerSigningKey` from before #801 can give `Application:GameAuth:HostKey` that value and remove `IssuerSigningKey`, which nothing reads. The services are one project, so these user-secrets serve every process; one that runs without identity must not hold the private key, so blank it on its command line: `dotnet run --project src/Server/Avalon.Api -- --Application:Services:0=worlds --Application:Authentication:SigningKey=`.

```bash
# Containers and every non-Development host: the environment. Keep the keys out of shell history: put them in a
# file (api.env is gitignored) holding Application__Authentication__SigningKey=<the private key as the base64 of
# its DER, one line: openssl pkcs8 -topk8 -nocrypt -in jwt-es256.pem -outform DER | base64 -w0>,
# Application__Authentication__SigningKeyId=<key id> and Application__GameAuth__HostKey=<key> ...
docker run --env-file ./api.env ...
# ... or name the variables without a value, so docker passes them through from the current environment.
docker run -e Application__Authentication__SigningKey -e Application__GameAuth__HostKey ...

# Helm: the chart reads every secret through a Kubernetes Secret (secretKeyRef), never a plain env value, and the
# key id and the public keys as plain values. Either name a Secret you manage (preferred; it holds
# jwt-signing-private-key and game-auth-host-key, see values.yaml; leave the chart's own secret values empty, or
# it refuses to render) ...
helm install ... --set existingSecret=avalon-api-secrets --set authentication.signingKeyId=2026-10 \
  --set-string 'authentication.validationKeys.2026-10=<public key>'
# ... or let the chart create it, passing the keys from files (a trailing newline is trimmed).
helm install ... --set-file authentication.signingKey=./jwt-es256.pem --set-file gameAuth.hostKey=./host.key ...
# The chart renders no HS256 key, and refuses the removed authentication.legacyIssuerSigningKey and
# authentication.issuerSigningKey values, naming what to remove.
```

**Rotating the signing key** signs nobody out: (1) make a new key pair and add its public key to `ValidationKeys`, under a new key id, in every process (the chart's `authentication.validationKeys`, shared by every release), and deploy; (2) give identity the new `SigningKey` and `SigningKeyId`, and deploy; (3) after 16 minutes, the token lifetime and its clock skew, remove the old public key everywhere and deploy. With a chart-managed Secret, `helm upgrade` restarts the pods (a checksum annotation); with `existingSecret` the chart cannot see a change to the Secret, so run `kubectl rollout restart deployment/<release>-avalon-api` (the chart's fullname) for every release whose keys changed. Swapping the key without those steps refuses every token signed with the old one: clients get a 401 and refresh, since refresh tokens are database rows. Changing `GameAuth:HostKey` voids the game-auth records in flight, which live minutes: pick a quiet moment.

`docker-compose.yml` runs only Redis and Postgres, so it needs no key. EF design-time commands (`dotnet ef migrations ...` with `--startup-project src/Server/Avalon.Api`) need no key either: they build each context through its `IDesignTimeDbContextFactory` and never run the API host's registrations. Tests never read one: `ApiTestHost` (`tests/Avalon.Api.Testing`) and the other API tests make their own keys in code.

## From clone to client in world

The one path from a fresh clone to a character in the world on this machine. Nothing in it is a production setting: every key and certificate it makes is a throwaway, and the license it grants exists only in Development.

**What a local world needs, and why.** The game client signs in over the REST API, which it reaches only over https, and enters a world over TLS with a ticket the API issues ([game admission](steam-authentication-workloads.md)). So beyond Redis, Postgres and the API's signing keys ([above](#rest-api-signing-key)), a local run needs:

- the API's https endpoint, served with the ASP.NET Core development certificate, which the machine must trust: the client checks the API's certificate against the machine's trust store;
- three private TLS leaves for `localhost`, each self-signed (`LocalCertificates`, `src/Server/Avalon/LocalCertificates.cs`): the world server's own (`Hosting:Security`), which the client pins from the join reply; the world's client leaf on the API's game workload listener (`World:Admission:ClientCertificatePath`, client authentication); and that listener's (`Kestrel:Endpoints:GameInternal`, server authentication), which the world pins (`World:Admission:ApiCertificateSha256`);
- the API's assignment of world 1 to that world server (`Application:GameWorkloads:Servers`: server id `world-1`, TLS name `localhost`, the world leaf's and the client leaf's SHA-256), and named Kestrel endpoints: the workload listener on `https://localhost:9443` and, since named endpoints replace the launch profile's URL, the public ones on `https://localhost:7166` and `http://localhost:5210`;
- the store settings identity requires to start: `appsettings.Development.json` holds placeholders (Steam's test app id 480 and a dummy publisher key), unused unless a client signs in through Steam;
- a license: admission needs an active `avalon.base` stored grant, and only a purchase writes one, so in Development identity grants the seeded `ADMIN` account one at startup (`DevelopmentLicenseGrant`, once, in the configured store environment, logged when it grants). No other environment runs it.

`ADMIN` (password `123`, access level Player, GameMaster and Admin) is the one account that can enter world 1, the Development world, which only an Admin may enter. Any other account needs both that access level and a license.

**Once per machine.** The .NET 10 SDK, Docker, a clone with its submodule (`git clone --recurse-submodules`), and the development certificate, trusted:

```bash
dotnet dev-certs https --trust
```

### One command: the Aspire AppHost

```bash
dotnet run --project src/Server/Avalon
```

It starts Redis (`--requirepass 123`) and Postgres (password `123`) as persistent containers on 6379 and 5432 (stop the docker compose ones first: they bind the same ports), makes the signing keys and the three leaves afresh on every run (into `avalon-apphost` in the temp folder), and passes every setting above to the API and the world server as environment variables. The API comes first; the auth server (21000) and the world server (21001) wait until it is healthy, which it is only once it has migrated the auth database: the world reads that database and never migrates it. On a fresh Postgres the API starts before the world's databases exist, so world 1 is unavailable at first; the world server creates them, and the API finds them on its own within about a minute (`WorldDatabaseRecheck`, [Multi-world API](api-worlds.md#startup-and-availability)). The console prints the dashboard's URL. Certificates made per run cost nothing: the client receives the world's pin in each join reply.

### By hand: `dotnet run`

```bash
docker compose up -d redis postgres
dotnet run --project tools/Avalon.LocalDev -- setup   # once; run again to renew the certificates
dotnet run --project src/Server/Avalon.Api            # first: it migrates the auth database
dotnet run --project src/Server/Avalon.Server.Auth
dotnet run --project src/Server/Avalon.Server.World
```

`setup` makes the three leaves into `certificates/local/` (git ignores `certificates/`), each sealed with a random password, and writes to the user-secrets of the API (`src/Server/Avalon.Api`) and of the world server (`src/Server/Avalon.Server.World`, which has a `UserSecretsId` and a Development launch profile for this) every setting above: for the API, its three Kestrel endpoints, the workload assignment of world 1, and its signing keys and game-auth host key when it has none yet; for the world, `Hosting:Security` and `World:Admission`. It keeps any other secret, and the API's existing keys. Development loads user-secrets, so restart the API and the world server after it. It also says when the development certificate is missing or not trusted. Start the world server after the API, or wait: an API that started first finds world 1 once the world server has created its databases, as above.

### Sign in and start the client

The client has no username and password screen: it redeems a game ticket that the launcher hands it on standard input. `tools/Avalon.LocalDev` stands in for the launcher, over the same REST chain: `/account/authenticate`, then `/client/auth/code` with a PKCE challenge and the password again, `/client/auth/token`, and `/client/auth/game-ticket`. The ticket is good for one use, within 60 seconds.

```bash
# Walk the client's chain up to a join ticket, and print the worlds the account may enter (nothing secret)
dotnet run --project tools/Avalon.LocalDev -- check

# Start the client with a fresh ticket on its standard input (--channel avalon, AVALON_GAME_TICKET_STDIN=1)
dotnet run --project tools/Avalon.LocalDev -- login --launch <the client's build>/runtime.exe

# Or pipe it yourself: login writes the ticket alone, one line, to standard output
dotnet run --project tools/Avalon.LocalDev -- login | AVALON_GAME_TICKET_STDIN=1 ./runtime.exe --channel avalon
```

```powershell
$env:AVALON_GAME_TICKET_STDIN = "1"
dotnet run --project tools/Avalon.LocalDev -- login | .\runtime.exe --channel avalon
```

The account is `ADMIN` unless `--user` names another; the password comes from `--password`, then `AVALON_DEV_PASSWORD`, then a prompt. `--api` names another API origin (default `https://localhost:7166`), and `check --world <id>` the world to ask a join ticket for. Each run opens a new launcher session for the account. An account with MFA on is refused: the tool answers no code.

**The client's configuration.** The client reads its servers from `netconfig.local.json` in its project folder (`devproject/`) when that file exists, ahead of the committed `netconfig.json`; the local file is ignored by the client's git. For a local run:

```json
{ "host": "127.0.0.1", "port": 21000,
  "pinSha256": "cf549bcd524330caa3888af69641322f3c1f3c523d4d2768521c891bf2e85d23",
  "apiUrl": "https://localhost:7166",
  "websiteUrl": "https://localhost:5173" }
```

`host`, `port` and `pinSha256` name the auth server: the pin is the SHA-256 of the SubjectPublicKeyInfo of the committed development certificate `certs/cert-tcp.pfx`. `apiUrl` is the API's https origin (an origin, or one with the path `/api`); `websiteUrl` the website the client opens for account pages, https as well (Avalon.Dashboard's local server). The world needs no entry: its address, TLS name and pin come in each join reply.

**When it does not work:**

| Symptom | Cause |
|---|---|
| The API exits naming `Application:StoreAuthentication` | It is not running in Development, so `appsettings.Development.json` is not loaded: use the launch profile (`dotnet run`) or set `ASPNETCORE_ENVIRONMENT=Development` |
| The API exits: no server certificate, the default developer certificate could not be found | `dotnet dev-certs https --trust` |
| The world server exits: "A world TLS certificate is required", or "World admission requires ..." | `setup` has not run, or the world server is not running in Development (its launch profile) |
| `check` or the client cannot reach the API, or the TLS check fails | The API is not up, or the development certificate is not trusted |
| `check`: game context `pending_license` | The account holds no license: only `ADMIN` is granted one, and only in Development |
| `check`: "No world is listed" | The world server is not running or not ready yet (it reports ready once it listens), the API has not found world 1's databases yet (its log says "World 1 is available"), or the account may not enter world 1 |
| The client stops at the world with a pin mismatch | The API and the world server run with leaves from different `setup` runs: restart both |
