# Multi-world REST API

A deployment runs any number of world servers, one per row of the auth `Worlds` table, and every world has its own
world database (content: templates, maps, chunks, quests) and characters database; only the auth database and Redis
are shared (#523). The API serves all of them: world content and characters are the worlds service's
(`Avalon.Api.Worlds`), and identity reads each world's Characters database for game admission and account
consolidation ([API services](api-services.md)). World content and characters live under `/world/{worldId}/...`:
`WorldRouteMiddleware` decides, before authorization, whether this API can serve that world to this caller and selects
it for the request, and the ordinary repositories then open that world's databases. Views that span worlds (an
account's characters, the world list, live presence) read each world by name. Adding a world is configuration only.
The world servers are unchanged: each serves one world from its own `Database:World` and `Database:Characters`, and
each migrates its own databases; the API never migrates them, and each API process checks the world databases it
reads once, at startup.

## Where the code lives

The world database plumbing is shared hosting, under `src/Server/Avalon.Api.Hosting/Worlds/` (namespace
`Avalon.Api.Hosting.Worlds`), so every service that reads a world uses the same code; the world controllers, previews
and services are the worlds service's, under `src/Server/Avalon.Api.Worlds/`. Paths below are under `src/Server/`.

| Type | File | Role |
|---|---|---|
| `WorldDatabaseSettings` | `Avalon.Api.Hosting/Worlds/WorldDatabaseSettings.cs` | Parses `Database:Worlds`, for the parts a process reads. `TryParseWorldId` is the one parse of a world id, for the configuration keys and the route alike |
| `WorldDatabaseParts` | `Avalon.Api.Hosting/Worlds/WorldDatabaseParts.cs` | Which of a world's databases a process reads: both for worlds, only the Characters database for identity, none for commerce and distribution (a process reads the union for its services) |
| `ConfiguredWorld`, `WorldDatabaseStatus` | `Avalon.Api.Hosting/Worlds/ConfiguredWorld.cs`, `WorldDatabaseStatus.cs` | One configured world: its id, the connection strings the process reads and its status (`Available`, `Unavailable`). A class, not a record, and its `ToString` prints only the id and status, so no generated member can print a connection string |
| `IWorldDatabases`, `WorldDatabases` | `Avalon.Api.Hosting/Worlds/IWorldDatabases.cs`, `WorldDatabases.cs` | The configured worlds in id order. `IsAvailable(world)` is the one test of "this API process can serve that world" |
| `ApiDatabaseMigrator` | `Avalon.Api.Hosting/Worlds/ApiDatabaseMigrator.cs` | Startup: migrates the auth database for the process that owns its schema (identity), and checks the world databases the process reads |
| `WorldRouteMiddleware` | `Avalon.Api.Hosting/Worlds/WorldRouteMiddleware.cs` | Selects the request's world on `[WorldScoped]` and `[PublicWorldScoped]` endpoints; in the pipeline only where a running service declares world routes (worlds) |
| `WorldScopedAttribute`, `PublicWorldScopedAttribute` | `Avalon.Api.Hosting/Worlds/WorldScopedAttribute.cs`, `PublicWorldScopedAttribute.cs` | The controller markers the middleware acts on (route value `worldId`) |
| `ICurrentWorld`, `CurrentWorld` | `Avalon.Api.Hosting/Worlds/CurrentWorld.cs` | Scoped: the request's world id and its auth row's name. Selected at most once; a second `Select` throws |
| `CurrentWorldDbContextFactory<TContext>` | `Avalon.Api.Hosting/Worlds/CurrentWorldDbContextFactory.cs` | The API's `IDbContextFactory<WorldDbContext>` and `IDbContextFactory<CharacterDbContext>`: opens the request's world, throws when none is selected |
| `IWorldDbContextFactory`, `ConfiguredWorldDbContextFactory` | `Avalon.Api.Hosting/Worlds/WorldDbContextFactory.cs` | Opens a named world's contexts (the caller disposes them). `ForWorld`/`ForCharacters` fix a factory to one world |
| `IWorldRepositories`, `WorldRepositories` | `Avalon.Api.Hosting/Worlds/WorldRepositories.cs` | Repositories over one named world, for cross-world work; each call builds a fresh repository |
| `WorldDatabaseRegistration` | `Avalon.Api.Hosting/Worlds/WorldDatabaseRegistration.cs` | `AddWorldDatabases`, called by `AddApiHosting` (`Avalon.Api.Hosting/ApiHostingRegistration.cs`) with the parts the running services read |
| `PublicCaller`, `PublicWorlds` | `Avalon.Api.Hosting/Worlds/PublicCaller.cs`, `PublicWorlds.cs` | Who a public request comes from; which worlds it may read, and the default one |
| `PublicController`, `PublicWorldController`, `PublicPreviewController` | `Avalon.Api.Worlds/Controllers/` | Item and ability tooltips, the public world list, link previews |
| `LinkPreviewPage`, `LinkPreviewText`, `PreviewConfiguration`, `PublicSiteSettings` | `Avalon.Api.Worlds/Previews/` | Link preview HTML, its one-line description and its settings |
| `AccountCharactersService`, `WorldService`, `ObservabilityService` | `Avalon.Api.Worlds/Services/` | The cross-world views |
| `ApiStartup` | `Avalon.Api.Hosting/ApiStartup.cs` | The startup order (validation, world parse, startup checks, auth schema, world checks) |

The repositories themselves are the shared ones (`AddWorldRepositories`, `AddCharacterRepositories`), registered as
singletons as in every host. Only their context factories differ in the API, which is what makes them per world; see
[DbContext Lifetime](dbcontext-lifetime.md) for why a repository opens a context per call.

## Rules and invariants

- **A world id has one canonical form**: ASCII digits, 1 to 65535, no sign, no leading zero, no whitespace
  (`WorldDatabaseSettings.TryParseWorldId`). `01` and `1` would be two configuration keys and two routes naming one
  world, so `01` is refused everywhere, never read as world 1.
- **A world's id agrees in three places**: its auth `Worlds` row, the API's `Database:Worlds:<id>` key (whose strings
  must point at the databases that world server uses), and that world server's `Game:WorldId` (Helm
  `server.game.worldId`). The Redis keys that join the API to a world are named by it: `world:{id}:presence`,
  `presence:world:{id}:character:{characterId}`, `world:{id}:ready`, `world:{id}:scripts`, `world:{id}:reload` and
  `world:{id}:reload:result`.
- **There is no fallback world.** A repository used where no world is selected throws `InvalidOperationException`
  (`CurrentWorldDbContextFactory.NoWorldSelected`: "No world selected for this request: the world and characters
  databases are reached only under /world/{worldId}/, or through IWorldDbContextFactory for a named world."), which
  the exception middleware answers as a 500. Nothing reads a default world. (#523 proposed keeping the single
  `Database:World` pair as a fallback; the implementation dropped it, and the API reads no singular `Database:World`
  or `Database:Characters`.)
- **A route names a world exactly where it reads one.** Every route containing `{worldId` is on a `[WorldScoped]`
  or `[PublicWorldScoped]` controller, and no other route is (`WorldScopedEndpointsShould`). A route naming a world
  without the marker would read no world through no check; a marker on a route without one would select nothing.
- **Non-disclosure.** An unknown world, a world this API is not configured for and a world the caller may not enter
  all get the same empty 404, and the 404 always comes before any 503, so a restricted world is never revealed, not
  even while it is unavailable. The auth row is read before the configuration is asked, so an unconfigured world
  costs the same lookup as a restricted one.
- **The world rule is a mask test**, `AccessLevels.ForWorld(world.AccessLevelRequired).Allows(callerLevel)`, the same
  rule the auth server applies to its world list and the game admission to its world list and join tickets. Never compare levels with `>=`:
  `AccountAccessLevel` is `[Flags]`, and PTR (32) and Tournament (16) are numerically above Admin (4).
- **Character ids are unique only within one world** (each world has its own characters database). Anything that
  keys a character, in the API, Redis or a client, keys it by `(worldId, id)`, never by `id` alone (#556).
- **Connection strings never reach a log or a message.** A configuration refusal names the setting, never its value;
  a failure reading a world is logged with the world id and the exception type only, because a driver's message can
  carry hosts and ports.
- **The API never migrates a world's databases.** Each world server alone migrates its own World and Characters
  databases at its start (`WorldStartup.PrepareAsync`; homelab spec 2026-09-28-release-channels-design, D2). Worlds
  on different release channels can therefore run different schemas, while the API reads every world with the one EF
  model it was built with. Columns a world has and the model does not map are ignored (pinned for the creature
  columns #745 dropped, `TemplateEditShould.Read_and_save_a_creature_in_a_world_that_still_has_the_dropped_columns`);
  a column the model maps that a world's database lacks fails every query that reads it. Keep world schema changes
  readable by the API across the releases the worlds run.

## How a request reaches its world

The pipeline every API process runs (`ApiPipeline`) has `UseRouting`, CORS, `UseAuthentication`, the services' hooks
after it (in a process running identity, the game workload authentication), the request rate limiter
(`UseApiRateLimiting`, #561), then `WorldRouteMiddleware`, then `UseAuthorization`. The world middleware is added only
in a process that runs a service with world routes, which only worlds has. The world check sits behind the rate
limiter, so a flood of refused world requests is limited too, and before authorization, so an unknown world is a 404
whatever the endpoint's role policy.

On a `[WorldScoped]` endpoint the middleware does, in order:

1. **An endpoint that allows anonymous callers** answers the same empty 404. Authorization would let an anonymous
   caller through to an action with no world selected, so this fails closed. No world endpoint allows anonymous
   callers (`WorldScopedEndpointsShould.Let_no_anonymous_caller_onto_a_world_endpoint`).
2. **It authenticates** through the default policy's schemes (the JWT bearer and the `Avalon` personal access token),
   so a personal access token is checked exactly like a JWT. The handlers cache their result, so the account is still
   loaded once per request. A caller that is not authenticated passes on, and authorization answers 401 as
   everywhere.
3. **A world id not in canonical form** is an empty 404, with nothing looked up. The routes use `{worldId:int}`, so a
   segment that is not an integer matches no endpoint at all and is the router's 404.
4. **The auth row is read.** No row, the caller fails the world rule, or the id is not under `Database:Worlds`: the
   same empty 404 each time.
5. **A world whose databases failed the startup check** answers 503 ProblemDetails (`Type` `ServiceUnavailable`,
   `Title` "World unavailable", `Detail` "World {id} is unavailable").
6. **Otherwise** it selects the world on the scoped `CurrentWorld` (the id and the auth row's name) and the endpoint's
   role policy decides the rest (403 for a role the endpoint does not admit).

A `[PublicWorldScoped]` endpoint goes through steps 3 to 6 with the caller's level from `PublicCaller` (see
[Public routes](#public-routes)). A request on any other endpoint passes straight through.

Behind the selection, `CurrentWorldDbContextFactory<T>` reads `ICurrentWorld` from the request's services through
`IHttpContextAccessor` and opens that world's context through `IWorldDbContextFactory`, so concurrent requests on
different worlds each read their own (`CurrentWorldContextsShould.Keep_concurrent_requests_in_their_own_worlds`).
The per-world contexts follow the rule every context follows for sensitive data logging (#558): on only when the host
environment is Development, never turned on by configuration, and off for every context of a process that runs
commerce with checkout enabled (the commerce registration post-configures `DatabaseConfiguration`).

## Startup and availability

`ApiStartup.ValidateAndMigrateAsync` runs, in order:

1. `IStartupValidator.Validate()`: the options validations, `Database:Auth:ConnectionString` among them
   (`ValidateDatabasesOnStart(DatabaseConnections.Auth)`), and, where worlds runs, `Application:Templates`.
2. In a process that reads a world database, it resolves `WorldDatabases`, which parses `Database:Worlds` for the
   parts the process reads and stops startup, naming the setting and never its value, for no world, an id not in
   canonical form, a world missing a string the process reads, or a blank string. A process running worlds needs both
   strings of every world and one running identity without worlds only the Characters strings; a string the process
   does not read is ignored, and a process running only commerce or distribution reads no `Database:Worlds` at all.
3. The services' startup checks (`IApiStartupCheck`; identity's are the store authentication and the Steam web link).
4. The auth schema (`AuthSchemaGate`): a process running identity migrates the auth database, and a failure stops it;
   any other waits until no migration its build knows of is pending ([API services](api-services.md#the-auth-schema)).
5. `ApiDatabaseMigrator.CheckWorldsAsync`: for each configured world in id order, its World database is checked with
   `Database.CanConnectAsync` where the process reads it, and its Characters database where the process reads it and
   the World database answered (or was not read). A world that cannot be reached, or whose check throws, is marked
   `Unavailable` and logged at Error with its id (and the exception's type, when one was thrown). Every world's status
   is then logged at Information.

`Database:Worlds` is deliberately not an options validation: OpenAPI generation starts the host with no world
configured (`AVALON_OPENAPI_GENERATION_ONLY=true`) and skips `ApiStartup`, and `WorldDatabases` is built only when
first resolved, so generating the document needs no world.

What follows from it:

- **The status is fixed for the life of the process.** There is no retry: an unavailable world answers 503 until the
  API restarts, even once its databases come back. A world whose databases fail later is not marked; its requests
  fail as database errors, which the exception middleware answers as 503 (`DbException`), and it stays `Available`.
- **Each unreachable world adds the driver's connect timeout to startup.**
- **A database that does not exist yet counts as unreachable.** On a fresh install the world server creates its
  databases at its first start; an API started before that (the Aspire AppHost starts the API first, and the auth and
  world servers wait for it) answers 503 for that world until it is restarted.

## Routes

World-scoped routes (`[WorldScoped]`). They, and every other route on this page, are the worlds service's. Character
reads admit the owner or a game master (`CharacterReadHandler`), character writes the owner or an admin
(`CharacterWriteHandler`).

| Route | Verbs | Policy | Notes |
|---|---|---|---|
| `/world/{worldId}/character/paginate` | GET | GameMaster | |
| `/world/{worldId}/character/{id}` | GET, PATCH | Player | Owner, or game master (read) or admin (write) |
| `/world/{worldId}/character/{id}/inventory`, `/abilities`, `/stats`, `/quests`, `/auras` | GET | Player | Owner or game master |
| `/world/{worldId}/map-template`, `/{id}` | GET | Player | |
| `/world/{worldId}/map-template/{id}/preview-layout` | GET | GameMaster | Runs the procedural generator against that world's data; see [Map Generation](map-generation.md) |
| `/world/{worldId}/map-template/chunk-asset/{*filename}` | GET | GameMaster | Whitelisted against the world's `ChunkTemplate.GeometryFile` values; the bytes come from the API's own copy of `Maps/` (`Application:MapAssets:ChunkAssetRoot`), not from the world |
| `/world/{worldId}/item-template`, `/ability-template`, `/creature-template`, `/aura-template` (and `/{id}`) | GET | Player | Each row carries `version` and `editable`; see [Live Template Editing](live-template-editing.md) |
| `/world/{worldId}/item-template/{id}` (and ability, creature, aura) | PUT | Admin | [Live Template Editing](live-template-editing.md) |
| `/world/{worldId}/quest-template`, `/{id}` | GET | GameMaster | Read-only |
| `/world/{worldId}/observability/character/{id}` | GET | GameMaster | One character's live presence, from that world's keys only |
| `/world/{worldId}/scripts` | GET | GameMaster | The script names the world has published (`world:{id}:scripts`); see [Live Template Editing](live-template-editing.md) |

The map, item, ability, creature, aura and quest template lists are paged with `page` (below 1 reads as 1) and
`pageSize` (outside 1 to 50 reads as 50). In the OpenAPI document `worldId` is a required path parameter
(`WorldRoutesOpenApiShould.Mark_worldId_a_required_path_parameter` pins it route by route).

Routes that span worlds, or read only the auth database, take no `worldId`: `GET /character`, `GET /world`,
`GET /world/{id}` (the auth world lookup, which shares the prefix but is not world-scoped), the world create, update
and maintenance endpoints, `GET /observability/online` and `GET /observability/instance/{instanceId}`.
`WorldRoutesOpenApiShould.Leave_no_world_content_route_outside_a_world` fails if a world content route reappears
outside `/world/{worldId}/`.

## Public routes

`[PublicWorldScoped]` is the anonymous-friendly variant, for the public site's item and ability tooltips:

| Route | Answer |
|---|---|
| `GET /public/world` | `{ defaultWorldId, worlds: [{ id, name }] }`: the configured, available worlds the caller may read, in id order. `Cache-Control: private, no-cache`, since the list depends on the caller |
| `GET /public/world/{worldId}/item/{id}` | `PublicItemDto` |
| `GET /public/world/{worldId}/ability/{id}` | `PublicAbilityDto` |

- **The caller** (`PublicCaller.AccessLevelAsync`) is authenticated through the default policy: a signed-in caller
  reads with its own access flags plus Player, and an anonymous caller, or one whose token is not valid, counts as a
  Player. So anyone reads a world every player may enter, and only a caller who may enter a restricted world reads
  it. A world the caller may not read is the same empty 404, before any 503, through the same selection as above.
- **Caching:** an answer from a world every player may enter is `Cache-Control: public, max-age=300`; one from a
  world only this caller may enter is `private, max-age=300` (the middleware stores which in
  `HttpContext.Items[PublicWorldScopedAttribute.OpenToEveryoneItem]`).
- **The default world** (`PublicWorlds.DefaultOf`) is `Application:PublicWorldId` when the caller may read it, else
  the first readable world, else none.
- **GET only:** every `[PublicWorldScoped]` endpoint is a single-verb GET under `public/world/`
  (`WorldScopedEndpointsShould.Keep_public_world_endpoints_apart_from_player_world_endpoints`).
- **Rate limiting:** the public endpoints carry no authorization requirement, so the request rate limiter does not
  look up a personal access token there: such a request counts against its source address's anonymous budget, and
  `PublicCaller` authenticates the token afterwards, outside the limiter's failed-lookup budget. A JWT, which
  `UseAuthentication` validates on every request, counts against its account as anywhere else.

### Link previews (#308)

`PublicPreviewController` serves `GET /public/preview/item/{id}` and `GET /public/preview/ability/{id}`: a small
HTML page with no scripts, for the link-preview bots of chat services, which run no JavaScript (the public site's
nginx sends those bots here). The world is `?world=N`, or else the default world `GET /public/world` would pick for
this caller. The route names no world, so the controller applies the selection order itself, through the shared
`PublicWorlds`: a world id not in canonical form, a world the caller may not read or that is not configured, or a
missing template is a 404 page; a 503 page only for a readable world whose databases failed. It then selects the world
on `CurrentWorld` itself, so the ordinary repositories read it.

- **The page** (`LinkPreviewPage.Render`, `text/html; charset=utf-8`): `<title>`, `description`, `og:site_name` (when
  `Application:Previews:SiteName` is set), `og:type` `website`, `og:title`, `og:description`, `og:url` (only when
  `Application:PublicSiteUrl` is set), `theme-color` (only for a valid `#RRGGBB` colour), `twitter:card` `summary`, and
  a body with one link. Every value from game data or configuration is HTML-encoded.
- **The link** is `<PublicSiteUrl>/item/{id}` (or `/ability/{id}`), with `?world=N` only when the request named a
  world. With no `PublicSiteUrl`, `og:url` is left out and the link is relative.
- **The description** (`LinkPreviewText.Describe`) reads like the site's tooltips: for an item its rarity, slot,
  subclass, damage, armour, other stats, required level, allowed classes and item power; for an ability its classes,
  cost, cast time, cooldown and amount line (templates have no description or flavour text). Parts are joined by
  ` · `, collapsed to one line and capped at 200 characters with an ellipsis.
- **Colours:** an item's `theme-color` is its rarity's colour, an ability's `AbilityColour`. A missing or malformed
  colour leaves `theme-color` out; it never fails the request.
- **Not-found and unavailable pages** are titled "Not found · {SiteName}" and "Unavailable · {SiteName}" (or just
  "Not found" and "Unavailable").
- **Caching:** `public, max-age=300` only when the world is one every player may enter and the page does not depend
  on the caller (a named world, or the default for a caller with Player rights only); otherwise
  `private, max-age=300`.

## Cross-world views

These read each world by name, through `IWorldRepositories` or Redis, and never let one world's failure fail the
whole answer.

- **`GET /character`** (`AccountCharactersController`, `AccountCharactersService`) returns
  `{ characters, unavailableWorlds }`. It walks the auth `Worlds` rows in id order and skips a world the caller may not
  enter (it appears nowhere) and one this API is not configured for. A world that is unavailable, or whose read
  throws, goes into `unavailableWorlds` (a failed read is logged with the world id and exception type); the call still
  succeeds with the other worlds' characters. Each `CharacterDto` carries `worldId` and `worldName`.
- **`GET /world`** lists only the worlds the caller may enter (paging and the total count cover only those, #452),
  each with `configured` (under `Database:Worlds`) and `available` (configured, and its databases answered at
  startup), beside the derived status. `GET /world/{id}` answers 404 for a world the caller may not enter, the same as
  for a missing one.
- **Observability** (`ObservabilityService`) is cross-world because presence lives in Redis: each world server writes
  `world:{id}:presence` and `presence:world:{id}:character:{characterId}`. `GET /observability/online` and
  `GET /observability/instance/{instanceId}` read every world the caller may enter (instance ids are GUIDs from
  `Guid.NewGuid()` in `MapInstance`, so they cannot collide across worlds). A snapshot with an unknown schema version,
  or naming another world than its key, is ignored. Template names and the layout-staleness check are read from each
  presence's own world, with the per-request name memo and the 30-second pool cache keyed by world (pool 3 of one
  world is not pool 3 of another); a world this API does not serve or that is unavailable, or a failed lookup, names
  the template `#id` and reports the layout not stale.
- **One character's presence** is `GET /world/{worldId}/observability/character/{id}` (`WorldObservabilityController`),
  behind the world check. It reads only that world's index key and snapshot and treats an index entry naming another
  world as absent. Its 404 means "not in this world now", not "no such character".
- **Game sessions** (identity's) also read worlds by name, through their Characters databases:
  `GameServerAllocator` offers no destination on a world this process marked unavailable (in a process running
  identity without worlds, one whose Characters database did not answer at startup); `GameSessionFenceService` writes
  gameplay fences into the named world's characters database (`IWorldRepositories.GameplayFences`); and
  `AccountConsolidationService` runs across every configured world,
  refusing with `WorldConfigIncomplete` while any auth `Worlds` row is missing from `Database:Worlds` and with
  `WorldUnavailable` while any configured world is unavailable. It includes configured worlds whose auth row is gone,
  so a retired world stays configured while it still holds characters.

## Configuration

### `Database:Worlds`

| Key | Description |
|---|---|
| `Database:Worlds:<id>:World:ConnectionString` | World `<id>`'s content database |
| `Database:Worlds:<id>:Characters:ConnectionString` | World `<id>`'s characters database |

`<id>` is the world's id in the auth `Worlds` table, in canonical form. The environment form is
`Database__Worlds__2__World__ConnectionString`. A process reads only the strings of the databases its services read
(`WorldDatabaseParts`): both for worlds, the Characters string for identity, none for commerce or distribution.

```json
"Database": {
  "Auth": { "ConnectionString": "…" },
  "Worlds": {
    "1": { "World": { "ConnectionString": "…" }, "Characters": { "ConnectionString": "…" } },
    "2": { "World": { "ConnectionString": "…" }, "Characters": { "ConnectionString": "…" } }
  }
}
```

The API's `appsettings.json` lists no world, so the published image ships none and every deployment states its own.
`appsettings.Development.json` holds world 1 (the local `world` and `characters` databases), and the Aspire AppHost
(`src/Server/Avalon/Program.cs`) sets the same pair through the environment.

### Helm (`avalon-api` chart)

A `worlds` map keyed by world id; every connection string reaches the pod through the Secret (`secretKeyRef`), never
as a plain value. A release renders the strings of the parts its services read, as above; one that runs only commerce
or distribution ignores `worlds`, and the checks below apply to the parts a release renders.

- **Chart-managed Secret** (no `existingSecret`): give `worlds.<id>.world.connectionString` and
  `worlds.<id>.characters.connectionString`, from files (`--set-file`; a trailing newline is trimmed). The Secret holds
  them under `database-world-<id>-connection-string` and `database-characters-<id>-connection-string`, and a changed
  string changes the checksum annotation, which restarts the pods.
- **`existingSecret`**: give no strings. The Secret holds each world's two keys under those default names, or under
  the names in `worlds.<id>.worldKey` / `worlds.<id>.charactersKey` (trimmed; blank means the default), which are
  accepted only with `existingSecret`. A world with default names is still listed, for example
  `--set worlds.2.worldKey=`.
- **The chart refuses to render** with no world; a world id that is not `^[1-9][0-9]{0,4}$` up to 65535; with a
  chart-managed Secret, a world missing either string or naming a key; with `existingSecret`, any inline string; a
  key that is not a valid Secret key name (letters, digits, `-`, `_`, `.`); a key two settings would read (two worlds,
  a world's own two keys, or one of the chart's keys `jwt-signing-private-key`, `game-auth-host-key`,
  `database-auth-connection-string`, `cache-password`, `notification-private-key` and `distribution-secret-key`); and
  the removed `database.world` / `database.characters` values. `src/Server/Avalon.Api/Helm/avalon-api/ci/test.sh` pins
  each refusal.

### Public routes and previews

| Key | Default | Description |
|---|---|---|
| `Application:PublicWorldId` | none (`null`) | The default world of `GET /public/world` and of the link previews. Helm `publicWorldId`, rendered only when set |
| `Application:PublicSiteUrl` | none (blank) | The public site's base URL, e.g. `https://avalon.example`: an absolute http or https URL with no query or fragment, trailing slash dropped (`PublicSiteSettings.Create`); anything else stops startup, naming the setting. Helm `publicSiteUrl`; the AppHost sets `http://localhost:5173` |
| `Application:Previews:SiteName` | `Avalon` | `og:site_name` and the not-found title; left out when empty |
| `Application:Previews:AbilityColour` | `#BC8A4E` | An ability's `theme-color` |
| `Application:Previews:RarityColours:<Rarity>` | Junk `#988F81`, Common `#E9E2D8`, Uncommon `#34D399`, Rare `#38BDF8`, Epic `#C084FC`, Legendary `#FBBF24` | An item's `theme-color` by rarity name (any case) |

The preview colours mirror the Dashboard's `rarity.ts` and the public site's primary accent; change them together.

## Extension points

- **A new world-scoped controller**: route `world/{worldId:int}/...`, mark it `[WorldScoped]`, never allow anonymous
  callers, and read through the ordinary repositories (or `ICurrentWorld` for the id). The pairing test fails if the
  marker and the route disagree.
- **A new public world-scoped endpoint**: GET only, under `public/world/{worldId:int}/`, on a `[PublicWorldScoped]`
  controller, setting `Cache-Control` from `OpenToEveryoneItem` as `PublicController` does.
- **A route that names no world but must read one** (as the link previews do) applies the same order itself (parse,
  auth row, rule, configured, available; one 404 for the first four) and then selects the world on the scoped
  `CurrentWorld`, so the ordinary repositories follow it.
- **A view across worlds**: walk `IWorldDatabases.All` or the auth rows, skip a world the caller may not enter,
  check `IsAvailable`, read through `IWorldRepositories` (add a member there when a new repository is needed) or
  `IWorldDbContextFactory`, and contain each world's failure: log the world id and exception type, report the world
  as unavailable, never fail the whole view.
- **Adding a world**: an auth `Worlds` row, a `Database:Worlds:<id>` pair (Helm `worlds.<id>`) pointing at that world
  server's databases, and the same id as the world server's `Game:WorldId`; then restart the API processes that read
  worlds (those running worlds or identity), which read the configuration and check the databases only at startup.

## Tests

The shared plumbing's tests are in `tests/Avalon.Api.Hosting.UnitTests`, the worlds service's in
`tests/Avalon.Api.Worlds.UnitTests`, and the host-level ones in `tests/Avalon.Api.UnitTests`.

| Test | Project | Guards |
|---|---|---|
| `Worlds/WorldDatabaseSettingsShould` | Hosting | Parsing, canonical ids, refusals, connection strings kept out of `ToString`, statuses |
| `Worlds/WorldDatabasePartsShould` | Hosting | A process reads, requires and opens only the world databases its services need |
| `Hosting/ApiStartupValidationShould` | Hosting | `Database:Worlds` refused at startup before any database call, the options checks passing without a world, one world's failed check leaving the others available |
| `Worlds/ApiDatabaseMigratorShould` | Worlds | Auth migrated and worlds only checked, a failed world marked and logged by type, the 503 for it |
| `Hosting/ApiHostGraphShould` | Host | Each service's real host opens the request's world through the per-request context factories |
| `Worlds/CurrentWorldContextsShould` | Hosting | The selected world's database, refusal with no world or no request, concurrent requests, named-world reads |
| `Worlds/WorldContextSensitiveLoggingShould` | Hosting | Sensitive data logging follows the host environment for every world |
| `Worlds/WorldRouteShould` | Worlds | The middleware's order: 404 cases alike, auth row before configuration, 404 before 503, 401, personal access tokens, a mid-request database failure as 503 |
| `Worlds/WorldScopedEndpointsShould` | Worlds | Marker and route agree; no anonymous world endpoint; public endpoints GET only |
| `Worlds/WorldScopedDataShould` | Worlds | The same route reads each world's own rows |
| `Worlds/WorldRoutesOpenApiShould` | Worlds | `worldId` a required path parameter; no world content outside a world; cross-world routes unscoped |
| `Worlds/PublicRouteShould`, `Worlds/PublicWorldListShould`, `Worlds/PublicRoutesOpenApiShould` | Worlds | Public tooltips, caching, the default world |
| `Worlds/LinkPreviewShould` | Worlds | Preview tags, encoding, the 200-character cap, `og:url`, colours, world selection |
| `Worlds/WorldObservabilityRouteShould`, `Services/ObservabilityServiceShould` | Worlds | Presence per world (#556) and the cross-world views |
| `Services/AccountCharactersServiceShould` | Worlds | `GET /character` across worlds with `unavailableWorlds` |
| `Worlds/QuestTemplateRouteShould`, `Worlds/WorldScriptCatalogRouteShould` | Worlds | Two world-scoped routes behind the world check |
