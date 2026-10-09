# API services

The REST API is one binary that runs as up to four services (#794): `identity`, `worlds`, `commerce` and
`distribution`. Each service is a library. The host, `Avalon.Api`, composes the services a process is told to run
(`Application:Services`), so one process can run all four, as `Avalon.Api` did before the split, or a deployment can
run one service per process, each with only its own settings, secrets, database access, routes and rate limiter.

!!! note "Production runs the services apart"
    Since the rollout (#802, 2026-10-08) the homelab runs one release per service: `avalon-api` runs identity, beside
    `avalon-api-worlds`, `avalon-api-commerce` and `avalon-api-distribution`, and the `avalon-api-routes` release
    sends each request to its service (see [Rollout](#rollout)). Access tokens are ES256 (#801): only identity holds
    the private key that mints them, and the other services hold the public keys alone. Each release has its own
    Secret and database connection strings; identity, worlds and commerce sign in to Redis as their own ACL users,
    and NetworkPolicies admit only the peers each port needs (#803).

## The services

| Service | Library | Owns | Needs |
|---|---|---|---|
| `identity` | `Avalon.Api.Identity` | Accounts and credentials, MFA, personal access tokens, web and launcher sessions and the tokens they are issued (the only service that mints them), store and Steam sign-in, account links and consolidation, game contexts, join tickets, game admission with its workload listener and session fences, email verification and change, push devices, [load-test accounts](#load-test-accounts) | Redis; the Characters database of every world; it owns the auth schema and migrates it |
| `worlds` | `Avalon.Api.Worlds` | The world registry and maintenance, world content and [live template editing](live-template-editing.md), characters, the views across worlds (`/character`, presence), public tooltips and link previews, the balance workbench's proxy | Redis; the World and Characters databases of every world; the world routes (`/world/{worldId}/...`) |
| `commerce` | `Avalon.Api.Commerce` | Checkout, purchase and licence status, the admin purchase tools, payment notifications and the reconciliation worker ([Commerce](commerce-configuration.md)) | Redis; no world database |
| `distribution` | `Avalon.Api.Distribution` | The launcher installer and its update feed, releases, the changelog, channels and their manifests, read from the build store | No Redis and no world database |

Every service reads the auth database: each request's credential is checked against its account there. Each
descriptor (`IdentityApi.Service`, `WorldsApi.Service`, `CommerceApi.Service`, `DistributionApi.Service`) states
these needs as an `ApiServiceNeeds`, and a process provides the union of the needs of the services it runs.

The cuts follow state: each service owns its tables, its secrets and its external providers (Steam, Resend and web
push for identity, the balance service for worlds, Stripe for commerce, the S3 build store for distribution), and
nothing that shares state tightly was separated. Game admission stays with identity, because join tickets, game contexts, the
session fences and the game-auth cryptography share identity's records and key. Launcher sessions stay with web
accounts (refresh families, minting), Steam web linking stays with game links and consolidation, and the public
tooltips, previews, observability and the world registry stay with world content (one multi-world plumbing, the same
data). There is no schema per service, no gateway process and no distributed rate limiter, and the services never
call each other.

## Projects

| Project | Role |
|---|---|
| `src/Server/Avalon.Api` | The host. `Program.cs` is one statement, `AvalonApiHost.RunAsync(args, [.. ApiServices.All])`; `ApiServices.All` lists the four in the host's order (identity, worlds, commerce, distribution). Also the appsettings, the user-secrets id, the Dockerfile, the `avalon-api` Helm chart with the route manifest, the build of the OpenAPI document, and the EF design-time startup project |
| `src/Server/Avalon.Api.Hosting` | What every service runs on: `AvalonApiHost`, `IApiService`, `ApiServiceNeeds`, `ApiServiceSelection`, the one pipeline (`ApiPipeline`), startup (`ApiStartup`, `AuthSchemaGate`, `IApiStartupCheck`), token validation (`ApiAuthentication`: the JWT bearer with `JwtAccountRevalidation`, the personal access token scheme, `AccountAccessCheck`, the role policies), the request rate limiter, forwarded headers, the exception middleware with its `IExceptionProblemMapper` registry, `BaseController`, the OpenAPI conventions, the world database plumbing (`Avalon.Api.Hosting.Worlds`, see [Multi-world API](api-worlds.md)) and `RouteTable` |
| `src/Server/Avalon.Api.Contract` | The REST contract: DTOs, contract enums, paginate filters, validation attributes and mapping extensions. Its namespaces (`Avalon.Api.Contract*`) are those the types had before the split, so the document's schema names did not change |
| `src/Server/Avalon.Api.Identity` | The identity service |
| `src/Server/Avalon.Api.Worlds` | The worlds service, its `Templates`, `Previews` and `Balance` included |
| `src/Server/Avalon.Api.Commerce` | The commerce service |
| `src/Server/Avalon.Api.Distribution` | The distribution service |

A service library references Hosting, the contract, the databases, Infrastructure, the shared libraries and its own
packages, never another service library (`ApiServiceBoundariesShould`); Hosting and the contract reference no service.
Only the host brings the four together.

| Test project | Holds |
|---|---|
| `tests/Avalon.Api.Testing` | Support, not a test project: `ApiTestHost` (an in-memory host built with `AvalonApiHost.CreateBuilder` and the real pipeline, for a list of services), the SQLite auth and world databases, login and password helpers |
| `tests/Avalon.Api.Hosting.UnitTests` | Authentication, middleware, startup, world plumbing and route manifest tests of the shared hosting |
| `tests/Avalon.Api.Identity.UnitTests`, `.Worlds.UnitTests`, `.Commerce.UnitTests`, `.Distribution.UnitTests` | Each service's own tests |
| `tests/Avalon.Api.UnitTests` | The host level: the contract, route ownership and reachability, authentication across services, each service's real host, the library boundaries (see [Guards](#guards)) |

## How a process runs its services

### `Application:Services`

- **Absent**: the process runs all four. This is the default everywhere: local development, the docs build that
  generates the OpenAPI document, and production today.
- **Present**: a list (`Application__Services__0=worlds`, `Application__Services__1=commerce`) or a single name
  (`Application__Services=worlds`), compared without regard to case. The process runs the services it names, in the
  host's order whatever the order of the list.
- **Refused**, naming the setting, while the host is built: an empty list, or a name the host has no service for
  (`ApiServiceSelection`).

### What a process registers and maps

- **Controllers**: only those of the running services' assemblies. The host clears the application parts MVC
  discovers and adds back the services' own, so a service the process does not run has no endpoint at all; its
  assembly is loaded for its descriptor only.
- **Shared hosting** (`AddApiHosting`), for the union of the needs: the auth database (always), Redis
  (`Application:Cache`, validated at startup and connected before the process serves) when any service needs it, the
  world databases for the parts the services read (`WorldDatabaseParts`: identity reads only each world's Characters
  database, worlds both), the forwarded headers and the rate limiter. Token validation (`AddApiAuthentication`) is
  shared too.
- **The services' own registrations** (`IApiService.AddServices`), in the host's order, after the shared ones: their
  options and checks, services, schemes and policies, and their exception mappers (`IExceptionProblemMapper`, asked by
  the exception middleware before its shared mappings).

### The pipeline

Every process runs one pipeline (`ApiPipeline.Use`, pinned by `PipelineOrderShould`), in this order: the developer
exception page (Development only), the exception handler, request logging, forwarded headers, routing, CORS, the
services' hooks before authentication (identity: the game workload routes kept to their listener, then the Steam
OpenID callback), authentication, the services' hooks after authentication (identity: the game workload
authentication), the rate limiter, the world routes (`WorldRouteMiddleware`, only when a running service declares
world routes, which only worlds does), authorization, and the endpoints: `/health`, `/alive`, `/openapi/v1.json` and
Scalar at `/scalar` where the API docs are served (see [Health, OpenAPI and Scalar](#health-openapi-and-scalar)), and
the controllers.

### Startup

`AvalonApiHost.RunAsync` builds the host, logs `Running the API services <names>` (for example
`Running the API services identity,worlds,commerce,distribution`), then each service's startup lines, and runs
`ApiStartup.ValidateAndMigrateAsync`:

1. The options validations (`IStartupValidator`), `Database:Auth:ConnectionString` among them.
2. `Database:Worlds`, parsed for the parts the process reads; a process that reads no world database has none to
   read and needs no `Database:Worlds`.
3. The services' startup checks (`IApiStartupCheck`; identity checks the store settings and the Steam web link's
   URLs), still before any database call.
4. The auth schema (see below).
5. Each configured world's databases, only the parts the process reads, checked for reachability: a world that does
   not answer is unavailable until a recheck in the background reaches it ([Multi-world API](api-worlds.md#startup-and-availability)).

Then each service's `StartAsync` (only identity has one: in Development, and only there, it grants the seeded `ADMIN`
account the base game's license, `DevelopmentLicenseGrant`; see
[Development setup](development-setup.md#from-clone-to-client-in-world)), then the Redis connection when a service
needs Redis. With
`AVALON_OPENAPI_GENERATION_ONLY=true` (the docs build only) all of this is skipped and the process can describe the
API but not serve it.

The OpenTelemetry resource of the process carries `avalon.api.services` with the same comma-separated names, so the
traces and metrics of one deployment say which services it ran.

### The auth schema

Identity owns the auth database's schema: a process that runs identity migrates it at startup, as `Avalon.Api`
always did (the TCP auth server migrates it too, and EF's migration lock serialises the two). A process without
identity never migrates: it waits until the auth database has no migration its build knows of pending, checking every
5 seconds, and fails, naming `Application:Startup:AuthSchemaWaitSeconds` (default 300, whole seconds, at least 1),
once the wait is spent (`AuthSchemaGate`). So a worlds, commerce or distribution process started before identity's
release never serves against an older schema than its build expects. No API process migrates a world's databases;
each world server migrates its own.

## Authentication across services

- **Identity mints, every service validates.** Access JWTs, refresh tokens and personal access tokens are issued only
  by identity. Every service validates a JWT or a personal access token itself and reloads its account from the auth
  database on every request (status, the access mask, the credentials version), exactly as before the split
  ([REST API authentication](api-authentication.md)). No service asks another whether a caller is signed in.
- **Only identity can sign (#801).** Access tokens are ES256: identity signs them with a private key under a key id
  (`Application:Authentication:SigningKey`, `SigningKeyId`), and every service checks them with the public keys it is
  given by key id (`Application:Authentication:ValidationKeys`), which are not secret. A process that does not run
  identity refuses to start while it holds the private key, and one with no public key refuses too. HS256 is refused
  whatever is configured ([REST API authentication](api-authentication.md)).
- **Identity's own keys**: the game-auth cryptography (proofs, replay receipts, the Steam OpenID state) derives its
  keys from `Application:GameAuth:HostKey`, read by identity alone, which refuses to start without it. It holds the
  value the HS256 key of before #801 held, so what was protected before stays readable.
- **Rate limits are per process.** The request rate limiter is in memory, so each process counts the requests it
  serves: a caller spreading requests over services gets one budget per process. The budgets that matter for
  security (logins, MFA codes, registration, email sends, checkout) are in Redis and shared by every process.

## The route manifest

`src/Server/Avalon.Api/Helm/avalon-api/files/routes.json`, inside the chart so Helm can read it, says which service
owns each path:

```json
{
  "version": 1,
  "default": "identity",
  "internal": ["/internal"],
  "services": {
    "identity": ["/account", "/mfa", "/pat", "/notification", "/client/auth", "/game", "/admin/load-test"],
    "worlds": ["/world", "/character", "/observability", "/public", "/balance"],
    "commerce": ["/account/game-license", "/account/purchases", "/admin/purchases", "/payments/notifications"],
    "distribution": ["/client/launcher", "/client/releases", "/client/changelog", "/client/channels"]
  }
}
```

- A rule `/x` matches `/x` and every path under it, ignoring case; the longest matching rule decides; a path no rule
  matches belongs to the default service, identity. So `/account/purchases/checkout` is commerce's,
  `/account/links/steam/callback` identity's and `/client/channels/ptr/manifest` distribution's. Identity's
  `/admin/load-test` would reach it by default too; it is listed so that a later `/admin` rule for another service
  cannot take it.
- `internal` lists the paths served only inside the cluster (identity's game workload routes); no public route names
  them.
- A rule is written one way only: lower-case segments of letters, digits and `-`, each after a `/`, with no `/` at the
  end. `RouteTable` (`Avalon.Api.Hosting/Routing`) reads the manifest and refuses anything else, an unknown member, a
  default that names no service and a rule listed twice included (`RouteTableShould`).
- Its readers are the chart (the service names it accepts in `services`, and the routes mode below), the host tests
  (`RouteOwnershipShould`, `EveryRouteReachableShould`) and the smoke check's coverage test (`SmokeCoverageShould`).

Every endpoint has exactly one owner. The table that decides it is `RouteOwnershipShould`'s: 62 endpoints for
identity (4 of them the internal game workload routes), 48 for worlds, 8 for commerce and 6 for distribution, plus the
Steam OpenID callback (`/account/links/steam/callback`, answered by identity's middleware rather than an endpoint).
Every process also maps `/health` and `/alive`, and the API docs where they are served, which through the ingress
reach identity.

## How a request reaches its service

### Today

In production one `avalon-api` process runs all four services: the website's and the admin site's ingresses send
every `/api/...` request to its Service on port 8080, stripping `/api`, so it sees the paths it maps. Locally the
same process answers everything on `http://localhost:5210`. Nothing routes by service yet, and no consumer has had to
change: the paths, the base URLs and the published OpenAPI document are what they were before the split.

### Routes mode

A release of the same chart with `services: []` and `routes.enabled: true` runs no pods. It renders one Traefik
`IngressRoute` that sends every path under `routes.pathPrefix` on `routes.hosts` to the Service of the API service
that owns it, by the manifest:

| Route | Priority | Goes to |
|---|---|---|
| The default: the prefix itself and every path under it | 10000 | `routes.backends.identity` |
| A one-segment rule, such as `/world` | 10010 | The rule's service's backend |
| A two-segment rule, such as `/account/purchases` | 10020 | The rule's service's backend |
| A rollout override (`routes.overrides`) | 20000 + 10 per segment | The override's backend |

Each route matches the hosts in `routes.hosts` and a path regular expression: with the prefix `/api`, the default is
`^/api(/|$)` and a rule `/x` is `^/api/(?i)x(/|$)`. A rule's priority is 10000 plus 10 per segment, so a deeper rule
outranks a shallower one and every rule sits above the default and above any Ingress router. `(?i)` keeps matching
case-insensitive after the prefix, as ASP.NET routing is, and `(/|$)` keeps `/api/world` from matching `/api/worlds`.
Every route runs through `routes.middlewares` (the one that strips the prefix) to the Service
`routes.backends.<service>` names, on `service.port`. The default rule sends every `/api` path no rule names to
identity, which answers 404, so nothing under `/api` falls through to a website.

`routes.overrides` (`[{path, service, backend}]`) sends one path to another Service ahead of every rule, which is how
the rollout moves a route group world by world and channel by channel. The chart refuses an override whose path is not
written as the manifest writes rules, is listed twice, is internal, belongs to another service by the manifest, or has
another service's rule under it; it also refuses a routes release without hosts or without a backend for each of the
four services, and `routes.enabled` in a release that runs services (which would remove its pods).

### The game workload routes

`/internal/game/*` (join-ticket redemption and the session activate, heartbeat and end calls) is identity's, on its
game workload listener only (`Kestrel:Endpoints:GameInternal`, port 9443 in the chart, mTLS with a pinned client
leaf). A request for `/internal` or a path under it that arrives on any other port is answered 404 before it is
authenticated, judged by the port the connection was accepted on, never by a header (`GameInternalRoutes`,
`InternalEndpointsShould`). The world servers call identity's port 9443 directly
([game server admission](steam-authentication-workloads.md)); no ingress route names `/internal`. When the services
run apart, identity keeps the release and Service name `avalon-api`: the world servers call
`avalon-api.avalon.svc:9443` and pin the TLS leaf issued for that name.

### Health, OpenAPI and Scalar

Every process maps `/health` (readiness) and `/alive` (liveness), never rate limited. The API docs, `/openapi/v1.json`
and Scalar at `/scalar`, describing only the services the process runs, are served only in Development or where
`Application:ApiDocs:Enabled` turns them on (#803, `ApiDocs`), so production serves none; the chart never sets it. The
published document ([API reference](api-reference.md)) does not depend on them: the docs build generates it from a
process that runs all four (`AVALON_OPENAPI_GENERATION_ONLY`), so it is the whole contract, the one the
Avalon.Dashboard client is generated from (locally, from `http://localhost:5210/openapi/v1.json` in Development).

## Configuration per service

A process reads the settings of the services it runs and nothing else is required of it. Every service reads the auth
connection string, the token validation settings (the public keys among them; only identity reads the private key and
the game-auth host key), the forwarded headers, `Application:ApiDocs` and the general
rate limits; identity, worlds and commerce read `Application:Cache`; the world connection strings are read by worlds
(both) and identity (Characters); the rest belongs to one service each (identity's store, Steam, workload, email and
notification settings, worlds' templates, map assets, public site and balance settings, `Application:Commerce`,
`Application:Distribution`), except that commerce also reads the store's `Environment` and `SteamIdentityPrefix`,
binding them without identity's validator. The table of every setting by service is in the
[Configuration Reference](configuration-reference.md#rest-api-services).

## Deploying with the chart

One image (`ghcr.io/woozchucky/avalon-server/api`) and one chart (`avalon-api`) serve every mode; the release,
nightly and registry workflows did not change. The chart's values that decide the mode:

- **`services`** (left out by default): the services the release runs, rendered as `Application__Services__<n>`.
  Left out, nothing is rendered and the process runs all four, which is how the homelab release renders today:
  `ci/test.sh` pins that its values render exactly what the chart rendered before #794, with the ES256 keys of #801 in
  place of the HS256 key (`ci/homelab-render.txt`).
  An empty list is accepted only in routes mode; an unknown name, or one listed twice, refuses to render.
- **Values per service**: a service's values reach the pod only when the release runs it, so a release per service
  holds only its own settings and Secret references. Identity's are `authentication.signingKeyId` and the Secret
  references of its private key and game-auth host key (#801), `storeAuthentication` (with the publisher key's
  Secret reference), `email`, `notification`, `steamWebLink`, `gameAdmission` (port 9443 on the container and the
  Service, the certificate volume, the world bindings), `rateLimiting.clientAuthPermitsPerMinute` and `loadTest`
  ([load-test accounts](#load-test-accounts)); worlds' are
  `templates`, `mapAssets`, `publicWorldId`, `publicSiteUrl` and `balance`; commerce's `commerce`; distribution's
  `distribution`. `storeAuthentication.environment` and `steamIdentityPrefix` reach identity and commerce, `cache`
  every service but distribution, and the world connection strings the services that read them (characters for
  identity and worlds, world for worlds). Every release reads `existingSecret`, `database.auth`,
  `authentication.validationKeys` (the public keys, plain values, required where identity does not run),
  `forwardedHeaders`, the other `rateLimiting` values (`exemptSources` among them), `otel`, `environment` and
  `resources`. The chart renders no
  HS256 key and refuses the removed `authentication.legacyIssuerSigningKey` and `authentication.issuerSigningKey`
  ([REST API signing key](development-setup.md#rest-api-signing-key)).
- **Secret keys per service**, in a chart-managed Secret or the one `existingSecret` names (none holds the old
  `jwt-signing-key` any more):

    | Service | Keys |
    |---|---|
    | identity | `database-auth-connection-string`, `jwt-signing-private-key`, `game-auth-host-key`, `cache-password`, `notification-private-key`, `database-characters-<id>-connection-string` per world; plus the Secrets `storeAuthentication`, `gameAdmission` and (with Resend) `email` name |
    | worlds | `database-auth-connection-string`, `cache-password`, `balance-shared-secret`, `database-world-<id>-connection-string` and `database-characters-<id>-connection-string` per world |
    | commerce | `database-auth-connection-string`, `cache-password`; plus the Stripe keys in the Secret `commerce.existingSecret` names |
    | distribution | `database-auth-connection-string`, `distribution-secret-key` |

- **Redis user per service (#803)**: `cache.username` signs a release in as its own Redis ACL user
  ([Redis users per API service](redis-cache-keys.md#redis-users-per-api-service)); left out, it signs in as the
  default user. With `existingSecret`, `cache.passwordKey` names the key holding that user's password (default
  `cache-password`), so a release can switch to its user while the default user's password stays under
  `cache-password` until the switch is done. A named key is required, not optional.
- **`startup.authSchemaWaitSeconds`** (default 300): a release without identity renders it as
  `Application__Startup__AuthSchemaWaitSeconds`, with a `startupProbe` on `/alive` (every 5 s) that allows that wait
  and a minute more before the liveness probe takes over. A release with identity has neither.
- **`routes`**: the routes mode above (`enabled`, `hosts`, `pathPrefix` default `/api`, `middlewares`, `backends`,
  `overrides`).
- **`networkPolicy`** (off by default, on in the homelab, #803): on, the API port admits only `networkPolicy.ingressController` (and
  `networkPolicy.publicSite` where worlds runs, for the link-preview bots the public site forwards), and the game
  admission port, where identity runs, only `networkPolicy.worldServers`. The chart refuses to enable it without the
  peers a port needs, since a rule with none would admit every source.

The chart's notes (`src/Server/Avalon.Api/Helm/avalon-api/templates/NOTES.txt`) name the services a release runs, or, for a routes release, where each service's requests
go. `ci/test.sh` has a case for each mode (see [Guards](#guards)).

## Load-test accounts

Identity serves `POST` and `DELETE /admin/load-test/accounts` (`LoadTestAccountsController`,
`LoadTestAccountService`), which create and remove runs of bot accounts for load tests. The controller is always
mapped; while `Application:LoadTest:Enabled` is false
([Configuration Reference](configuration-reference.md#rest-api-load-test-accounts)) an admin's request to either
action gets the standard Not Found response, before its body is even validated (authorization still runs first: 401
without a credential, 403 for a caller who is not an admin). Enabled, both need:

- the `Admin` policy (Admin or Console);
- an access token: a personal access token is refused with 403, as these accounts can enter PTR worlds;
- the admin's own `currentPassword` in the body, checked through `IReauthentication` as for `POST /pat/admin`: a
  wrong one is a failed login in every respect, answered 401.

**Create**: `POST { runId?, count, password, currentPassword }` answers 201 `{ runId, accounts }`.
- `count` is 1 to 1,000 (400 otherwise). Existing load-test accounts plus `count` above
  `Application:LoadTest:MaxAccounts` (default 5,000) is 409.
- `runId` is three ASCII letters, upper-cased, generated when absent. A run id is used once: one a license already
  names, or whose usernames are taken, is 409.
- `password` is the bots' shared password, under the registration rule (at least 8 characters, measured trimmed),
  hashed once for the run; it is never echoed or logged.
- The run's accounts are named `LT` + the run id + a 7-letter index (`LTABCAAAAAAA`, `LTABCAAAAAAB`, ...; 12
  letters, so each name is also a valid character name), with no email, `Player | PTR` and credentials version 0.
  Each holds an Avalon stored grant for the base game in the configured store environment under the reference
  `loadtest:<runId>:<accountId>`, which is what marks it as a load-test account. Accounts and grants are inserted in
  one auth transaction, directly, so no per-source registration budget is spent. The cap can be passed by two
  creates running at once (accepted for an admin-only, password-confirmed action).

**Delete**: `DELETE ?run=<id>` removes one run, `DELETE ?all=true` every run; the body is `{ currentPassword }`, and
the reply 200 `{ deleted, skipped }`.
- Exactly one of `run` and `all`, each given once, is checked before the password: neither, both, a repeated one,
  `all` with any value but `true`, or an empty or blank `run` is 400, so a command built from an unset variable never
  deletes every run.
- An account is deleted when it holds an Avalon license under a load-test reference (of the run, when one is named)
  and nothing a run never gives: no other license, purchase or refund, store identity, store creation or
  consolidation, email, or role beyond `Player | PTR`. One that holds the run's license and any of those is kept and
  named in `skipped`.
- Nothing is deleted (409) while any selected account is online: a game session not ended whose lease runs, or a
  gameplay fence not blocked whose lease runs, in any configured world. A character's `Online` flag counts only beside
  one of those, so a flag a crashed world left set stops blocking once its leases expire (within a minute). Nor
  (409) while a configured world's characters database is unavailable, since that world's bot characters would be
  left behind. Stop the bots before deleting.
- The order: in every configured world's characters database, the accounts' characters (their rows cascade) and
  gameplay fences; then one auth transaction, which locks the accounts' rows one at a time in ascending id order (so
  two deletes over the same accounts wait for each other rather than deadlock), selects them again (keeping any that
  gained something of a person's meanwhile), and deletes the license observations and holds that point at their
  licenses, the licenses and the accounts (everything else cascades); then, once that has committed and whether or
  not the caller still waits, a world disconnect for each account deleted, and the world sweep again, best-effort,
  for a character a bot still running created meanwhile. `deleted` is the number of accounts that transaction
  removed; one a concurrent delete removed is in neither field.
- A 409 after the world sweep means a bot entered a game meanwhile: the run's characters are gone but its accounts
  are not. Stop the bots and send the same request again: a delete is re-runnable, and finishes what one before it
  left.

Each create and delete logs one Information line (the admin's id, the run or `all`, the counts). `ExemptSources`
([Configuration Reference](configuration-reference.md#exempt-sources)) is what lets the bots sign in from one load
machine without the per-source limits; the create itself spends none.

## Rollout

The rollout was #802, completed on 2026-10-08 in the steps below (from the design of #794); production now runs the
state step 6 describes, with the other services' keys gone from `avalon-api`'s Secret and the ingresses' `/api` paths
removed. The steps stay here as the way back:

1. **Signing first** (#801, in the code): identity signs with an ES256 private key, the other services hold the public
   keys, and one release accepted both algorithms, so no session was lost; the release after it refuses HS256.
2. **The split-capable release**: `avalon-api` keeps running all four services, now naming them in `services`, and
   behaves as before.
3. **Routes in place**: a routes release (`avalon-api-routes`) takes over `/api` with every backend still
   `avalon-api`, so nothing changes destination; the ingresses' own `/api` paths stay underneath as the fallback.
4. **Dark launch**: `avalon-api-worlds`, `avalon-api-commerce` and `avalon-api-distribution` start beside it, each
   with its own Secret, take no public traffic, and are checked with the smoke check through
   `kubectl port-forward`.
5. **Moves, one values change each**, through `routes.overrides`: the development world and channel first (`/world/1`,
   `/public/world/1`, the admin-only `/balance` and `/observability`; `/client/channels/dev`), then the PTR world and
   channel (`/world/3`, `/public/world/3`, `/client/channels/ptr`), then everything else by pointing
   `routes.backends.worlds` and `routes.backends.distribution` at the new Services (the public site's link-preview
   upstream with them), then commerce.
6. **Shrink**: `avalon-api` runs `services: [identity]`, at a quiet time, since it restarts the process that renews
   game sessions; later the other services' keys leave its Secret and the ingresses lose their `/api` paths.

With the shrink done, a service moves back into `avalon-api` by adding it to that release's `services`, putting its
Secret keys back in `avalon-api`'s Secret and pointing its `routes.backends` entry at `avalon-api`. The homelab
repository holds the values.

## Local development

`dotnet run --project src/Server/Avalon.Api`, or the Aspire AppHost's `api` resource, runs all four services on
`http://localhost:5210`, the address the Dashboard's development servers proxy to. The AppHost makes the signing key
pair itself; a plain `dotnet run` reads the keys from user-secrets, which serve every process, since the services are
one project ([Development setup](development-setup.md#rest-api-signing-key)). In Development the API docs are served.

To run one service, name it. Without identity the process must not hold the private key, so blank the user-secret on
its command line (it keeps the public key):

```bash
dotnet run --project src/Server/Avalon.Api -- --Application:Services:0=worlds --Application:Authentication:SigningKey=
```

It then maps only that service's routes (any other path answers 404). Without identity it waits for the auth schema,
which identity, or the auth server, migrates.

## The smoke check

`tools/api-smoke/smoke.sh` sends each line of `tools/api-smoke/requests.tsv` as a GET and compares the status with the
line's expected statuses, to show after a deploy or a route move that each route group reaches a service that answers
it:

```bash
BASE=https://avalon.example/api AVALON_SMOKE_PAT=avp_... AVALON_SMOKE_GROUPS=worlds,distribution tools/api-smoke/smoke.sh
BASE=http://127.0.0.1:18080 tools/api-smoke/smoke.sh   # after kubectl port-forward svc/<release> 18080:8080
```

- **Groups** are the four services; `AVALON_SMOKE_GROUPS` picks some (every group when unset). Each request is in the
  group of the service the manifest sends it to, and every manifest rule has a request (`SmokeCoverageShould`), so a
  run reaches every route group. A 405 on a POST-only route, or the 403 only identity gives, shows that the service
  mapping the route answered; any other service would answer 404.
- **`AVALON_SMOKE_PAT`**: an Admin personal access token for the lines marked `pat`, reaching curl on its standard
  input, never its command line; without it those lines are skipped.
- It sends GET only, follows no redirect, and prints each request's group, method, path and status, never a response
  body (channel manifests carry presigned URLs) or the token. It exits 0 when every status matched, 1 when one did not
  (`000` is no answer), 2 on bad input.

## Adding a route

1. Put the action in the library of the service that owns its path by the manifest. A new controller in a service's
   assembly is mapped only in the processes that run that service.
2. Add the endpoint to `RouteOwnershipShould`'s table, which fails until someone decides the owner. A route parameter
   with a constraint `RouteSamples` has no sample value for fails there too: give it one.
3. A path that falls under no rule of its service (a new first segment for a service other than identity, say) needs a
   rule in `files/routes.json`, written in the manifest's form, and a smoke request in `requests.tsv` whose path that
   rule decides (`SmokeCoverageShould`). The routes release renders the rule with the next chart.

## Adding a service

1. A library `src/Server/Avalon.Api.<Name>` that references `Avalon.Api.Hosting`, `Avalon.Api.Contract` and what it
   needs, never another service library, with one descriptor implementing `IApiService`: its `Name` (as
   `Application:Services` names it), its `ControllerAssembly`, its `Needs`, `AddServices`, and the hooks it needs
   (`ConfigureBuilder` for a listener of its own, the two middleware hooks, `LogStartup`, `StartAsync`). Its
   exceptions get an `IExceptionProblemMapper`; a check that must not run during OpenAPI generation is an
   `IApiStartupCheck`.
2. The host references it and lists it in `ApiServices.All`, in the order it should run.
3. Its rules go under its name in `files/routes.json`; the chart accepts the name in `services` from there, and a
   routes release then needs `routes.backends.<name>`. Its values and Secret keys render in `templates/deployment.yaml`
   and `templates/secret.yaml` only when the release runs it, with a case in `ci/test.sh`.
4. The host tests iterate `ApiServices.All`, so its real host, its routes and its token acceptance are checked once its
   endpoints are in `RouteOwnershipShould`'s table; that test also lists the manifest's services in order. Add its smoke
   requests under its name.

## Guards

| Test | Project | Fails when |
|---|---|---|
| `RouteOwnershipShould` | `Avalon.Api.UnitTests` | An endpoint has no owner in the table, a service's process maps another's endpoint or misses one of its own, the manifest sends an endpoint elsewhere, a manifest rule decides nothing, or the API docs are mapped outside Development without their setting, or not mapped in it (#803) |
| `EveryRouteReachableShould` | `Avalon.Api.UnitTests` | An endpoint's sample request, as the ingress delivers it, is not routed in its owner's process, or is routed in another's |
| `SmokeCoverageShould` | `Avalon.Api.UnitTests` | A manifest rule has no smoke request, or a request is in another service's group |
| `CrossServiceAuthenticationShould` | `Avalon.Api.UnitTests` | A token identity minted is refused by another service, holding only the public key, or a token signed with another key under the same key id or past its lifetime is accepted there |
| `JwtSigningKeyShould` | `Avalon.Api.Identity.UnitTests` | A process starts with a key it cannot use, without a key to validate with, or holding identity's private key without running identity (#801) |
| `ApiHostGraphShould` | `Avalon.Api.UnitTests` | A service's real host, run alone, does not build under the container validation, maps another service's controllers, or cannot build one of its own |
| `ApiServiceBoundariesShould` | `Avalon.Api.UnitTests` | A service library references another, directly or through a project, or Hosting or the contract references a service |
| `MonolithCompositionShould` | `Avalon.Api.UnitTests` | The all-in-one process differs from `Avalon.Api` before the split: its exception mappers, the game servers' rate-limit partition, the Steam callback's query kept out of the request log, the startup checks, the store settings bound once, the auth schema owned, the middleware order |
| `ApiServiceSelectionShould`, `PipelineOrderShould`, `AuthSchemaGateShould`, `WorldDatabasePartsShould`, `RouteTableShould`, `OpenApiOrderShould` | `Avalon.Api.Hosting.UnitTests` | The selection rules, the pipeline order, the schema gate, the world parts a process reads, the manifest reader, or the ordinal order of a document's paths, tags and schemas change |
| `InternalEndpointsShould` | `Avalon.Api.Identity.UnitTests` | `/internal/game/*` answers on a port other than the workload listener's |
| `ci/test.sh` | the chart | The homelab values render differently from before the split with the ES256 keys (#801) in place of the HS256 key, identity's private key or game-auth host key is not required, a removed HS256 value renders, a service alone renders a setting or Secret key that is not its own, the routes release's rules, priorities or refusals change, the NetworkPolicy renders without its peers, `rateLimiting.exemptSources` misses a release, or `loadTest` reaches a release without identity |
