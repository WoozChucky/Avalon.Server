# Architecture — Startup Flow

Bootstrap sequence for each server component.

## API

`Avalon.Api`'s `Program.cs` is `AvalonApiHost.RunAsync(args, [.. ApiServices.All])`: one host for the API services
`Application:Services` selects, all four when it is unset ([API services](api-services.md)).

1. `AvalonApiHost.CreateBuilder`: the configuration sources (`ApiConfiguration.Sources`), the selected services, each
   service's builder settings (identity: the game workload listener), Serilog then the service defaults (with the
   `avalon.api.services` resource attribute), CORS, the selected services' controllers only (camelCase JSON, no
   value-object converter), the OpenAPI document, the token validation, the shared hosting for the services' needs,
   then each service's own registrations
2. Build, then the one pipeline (`ApiPipeline`) and its endpoints: `/health`, `/alive`, `/openapi/v1.json`, Scalar at
   `/scalar`, the controllers
3. Log the services it runs; then, unless `AVALON_OPENAPI_GENERATION_ONLY` is set, `ApiStartup` (the options,
   `Database:Worlds`, the services' checks, the auth schema, migrated by identity and awaited by any other process, and
   the reachability of the world databases the process reads), each service's `StartAsync`, and the Redis connection
   when a service needs it
4. Run; commerce's reconciliation worker is a hosted service

## Auth Server & World Server

1. `AvalonHostBuilder.CreateHostAsync` — sets working directory, core services, JSON options
2. `ConfigureOpenTelemetry`
3. Register `HostedService` (`AuthServer` / `WorldServer`) + specialized services
4. Migrate respective databases
5. Connect Redis
6. Run hosted loop. The Auth server opens its TCP port at host start (`ServerBase.StartAsync`). The
   World server opens it only at the end of `WorldServer.ExecuteAsync` (#665), once scripts and the
   world are loaded, the Redis disconnect channel is subscribed, the connection listener is
   registered and the tick thread is running (`ListenOnStart` is false, and `StartListening` is
   called there). A load that fails ends the start with the port never opened; a stop during the
   load leaves it shut.
