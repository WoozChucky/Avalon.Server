# Architecture — Startup Flow

Bootstrap sequence for each server component.

## API

`Avalon.Api`'s `Program.cs` is `AvalonApiHost.RunAsync(args, [.. ApiServices.All])`: one host for the API services
`Application:Services` selects, all four when it is unset ([API services](api-services.md)).

1. `AvalonApiHost.CreateBuilder`: the configuration sources (`ApiConfiguration.Sources`), the selected services, each
   service's builder settings (identity: the game workload listener), Serilog then the service defaults (with the
   `avalon.api.services` resource attribute), CORS, the selected services' controllers only (camelCase JSON, no
   value-object converter), the OpenAPI document, the token validation (`JwtKeys.Create`, which stops startup on an
   unusable key or a private key in a process without identity, #801; a throwaway key when only generating the OpenAPI
   document), the shared hosting for the services' needs, then each service's own registrations
2. Build, then the one pipeline (`ApiPipeline`) and its endpoints: `/health`, `/alive`, `/openapi/v1.json` and Scalar
   at `/scalar` (in Development, or with `Application:ApiDocs:Enabled`, #803), the controllers
3. Log the services it runs and the key ids it accepts tokens under (with a warning while the ignored HS256 key,
   `Application:Authentication:IssuerSigningKey`, is still set, #801); then, unless `AVALON_OPENAPI_GENERATION_ONLY` is
   set, `ApiStartup` (the options, `Database:Worlds`, the services' checks, identity's game-auth host key among them,
   the auth schema, migrated by identity and awaited by any other process, and the reachability of the world databases
   the process reads, handing any unreachable one to `WorldDatabaseRecheck`), each service's `StartAsync` (identity's
   grants the seeded `ADMIN` a license, in Development only), and the Redis connection when a service needs it
4. Run; commerce's reconciliation worker and `WorldDatabaseRecheck` are hosted services

## World Server

1. `AvalonHostBuilder.CreateHostAsync` — sets working directory, core services and JSON options.
2. `ConfigureOpenTelemetry` registers world telemetry.
3. Register `WorldServer`, `WorldMaintenanceReconciler`, `WorldReadyHeartbeatService` and
   `PresenceSnapshotService`, plus the world's specialized services.
4. `WorldStartup.PrepareAsync` validates options, including `Hosting:Security` and `World:Admission`, builds the
   packet decoders and server serializers, and builds the world's admission backstop. It migrates the World and
   Characters databases, then seeds the map catalog. The world reads the auth database (its world row and maintenance)
   but never migrates it: identity must migrate it first, so Aspire waits for the API to be healthy.
5. Connect Redis.
6. `WorldServer.ExecuteAsync` loads scripts and the world, subscribes to the Redis disconnect channel, registers the
   connection listener and starts the tick thread. It opens the TCP listener with `StartListening` only after that
   work succeeds. A failed world load or a stop during loading leaves the port shut.
