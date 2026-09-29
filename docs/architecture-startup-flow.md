# Architecture — Startup Flow

Bootstrap sequence for each server component.

## API

1. Build `WebApplicationBuilder`
2. Bind `ApplicationConfig`
3. Register JSON options + converters (`ValueObjectJsonConverterFactory` + `JsonStringEnumConverter`)
4. Configure OpenAPI + schema transformations (ValueObject → scalar)
5. Add Auth and Infrastructure services (databases, Redis, workers)
6. Build / apply EF migrations / start workers / connect Redis
7. Expose OpenAPI (`MapOpenApi` + Scalar UI at `/scalar`)

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
