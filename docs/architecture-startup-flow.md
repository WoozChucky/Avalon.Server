# Architecture — Startup Flow

Bootstrap sequence for each server component.

## API

1. Build `WebApplicationBuilder`
2. Bind `ApplicationConfig`
3. Register JSON options + converters (`ValueObjectJsonConverterFactory` + `JsonStringEnumConverter`)
4. Configure OpenAPI + schema transformations (ValueObject → scalar)
5. Add Auth, Infrastructure, AutoMapper profiles
6. Build / apply EF migrations / start workers / connect Redis
7. Expose OpenAPI (`MapOpenApi` + Scalar UI at `/scalar`)

## Auth Server & World Server

1. `AvalonHostBuilder.CreateHostAsync` — sets working directory, core services, JSON options
2. `ConfigureOpenTelemetry`
3. Register `HostedService` (`AuthServer` / `WorldServer`) + specialized services
4. Migrate respective databases
5. Connect Redis
6. Run hosted loop
