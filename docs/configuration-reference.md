# Configuration Reference

This document covers all configuration keys introduced or planned for the Avalon server, including keys tied to open TODO items.

Related TODOs: [TODO-009](todo.md#todo-009), [TODO-010](todo.md#todo-010), [TODO-011](todo.md#todo-011), [TODO-012](todo.md#todo-012)

---

## Overview

Avalon uses strongly-typed configuration classes bound from `appsettings.json` (or environment variables / secrets manager in production) via `IOptions<T>`. Configuration classes reside in `src/Shared/Avalon.Configuration`.

---

## Existing Configuration Classes

| Class                     | Namespace                    | Bound from                   |
|---------------------------|------------------------------|------------------------------|
| `DatabaseConfiguration`   | `Avalon.Configuration`       | `ConnectionStrings:*`        |
| `CacheConfiguration`      | `Avalon.Configuration`       | `Cache:*`                    |
| `AuthenticationConfig`    | `Avalon.Configuration`       | `Authentication:*`           |
| `ApplicationConfiguration`| `Avalon.Configuration`       | `Application:*`              |
| `AvalonTcpServerConfiguration` | `Avalon.Network.Tcp.Configuration` | `Server:Tcp:*`     |
| `GameConfiguration`       | `Avalon.World.Configuration` | `Game:*`                     |

---

## Planned Keys (from TODOs)

### Auth Configuration (`AuthConfiguration`)

Section in `appsettings.json`: `"Auth"`

| Key                         | Type   | Default | Added by  | Description                                    |
|-----------------------------|--------|---------|-----------|------------------------------------------------|
| `MinClientVersion`          | string | `"0.0.1"` | TODO-010 | Minimum client version accepted during handshake |
| `MaxFailedLoginAttempts`    | int    | `5`     | TODO-012  | Number of consecutive failed logins before account lock |

```json
"Auth": {
  "MinClientVersion": "0.0.1",
  "MaxFailedLoginAttempts": 5
}
```

**Validation rules:**
- `MinClientVersion`: required, non-empty string.
- `MaxFailedLoginAttempts`: minimum `1`, maximum `100`. Use `[Range(1, 100)]` data annotation.

---

### Application Configuration (`ApplicationConfiguration`)

Section in `appsettings.json`: `"Application"`

| Key               | Type   | Default     | Added by  | Description                         |
|-------------------|--------|-------------|-----------|-------------------------------------|
| `ServerVersion`   | uint   | `1000000`   | TODO-011  | Server version sent in `SServerInfoPacket` to clients |

```json
"Application": {
  "ServerVersion": 1000000
}
```

**Alternative approach** — derive `ServerVersion` from `AssemblyInformationalVersionAttribute` at startup:

```csharp
var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "0.0.0";
```

Use whichever approach is more consistent with the release pipeline.

---

### Network Configuration (`AvalonTcpServerConfiguration` extension)

Section in `appsettings.json`: `"Server:Tcp"`

| Key                     | Type | Default | Added by | Description                                             |
|-------------------------|------|---------|----------|---------------------------------------------------------|
| `PacketReaderBufferSize`| int  | `4096`  | TODO-009 | Internal read-buffer size in bytes for `PacketReader`   |

```json
"Server": {
  "Tcp": {
    "ListenPort": 7777,
    "Backlog": 100,
    "Enabled": true,
    "PacketReaderBufferSize": 4096
  }
}
```

**Validation rules:**
- `PacketReaderBufferSize`: minimum `512`, maximum `65535`. Use `[Range(512, 65535)]`.

---

### Avalon Internal Authentication

Section in `appsettings.json` / environment variable (secrets manager recommended):

| Key              | Type   | Default  | Added by | Description                                               |
|------------------|--------|----------|----------|-----------------------------------------------------------|
| `Avalon:SharedSecret` | string | _(required)_ | TODO-007 | Shared secret for `Authorization: Avalon <token>` scheme |

```json
"Avalon": {
  "SharedSecret": "REPLACE_WITH_SECURE_RANDOM_VALUE_MINIMUM_32_CHARS"
}
```

> **Warning:** This key must **never** be committed to source control. Use environment variables or a secrets manager (Azure Key Vault, AWS Secrets Manager, dotnet user-secrets) in all environments.

---

### TCP Client Configuration (`AvalonTcpClientConfiguration` extension)

| Key             | Type   | Default   | Added by | Description                            |
|-----------------|--------|-----------|----------|----------------------------------------|
| `ClientVersion` | string | `"0.0.1"` | TODO-014 | Version string sent to the Auth server |

```json
"Client": {
  "Tcp": {
    "ClientVersion": "0.0.1"
  }
}
```

---

## Applying Configuration at Startup

All changes to configuration classes require:

1. Adding the new property with its data annotation.
2. Calling `services.AddOptions<TConfig>().BindConfiguration(section).ValidateDataAnnotations().ValidateOnStart()` in the appropriate DI extension method.
3. Injecting `IOptions<TConfig>` (or `IOptionsSnapshot<TConfig>` for reloadable config) in the consuming class.

Example for `AuthConfiguration`:

```csharp
// In IServiceCollection extension:
services.AddOptions<AuthConfiguration>()
    .BindConfiguration("Auth")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// In handler:
public class CAuthHandler(IOptions<AuthConfiguration> authConfig, ...)
{
    private readonly AuthConfiguration _authConfig = authConfig.Value;

    // Usage:
    if (account.FailedLogins >= _authConfig.MaxFailedLoginAttempts)
    { ... }
}
```

---

## Environment-Specific Overrides

Use `appsettings.{Environment}.json` (e.g. `appsettings.Production.json`) to override defaults per environment without changing the base file. Sensitive values (`SharedSecret`, database passwords) must come from environment variables or a secrets manager, not files.

```bash
# Environment variable override syntax (.NET):
Auth__MinClientVersion=1.5.0
Auth__MaxFailedLoginAttempts=10
Avalon__SharedSecret=<from-vault>
```

---

## Validating Configuration at Startup

All config classes should opt into startup validation to fail fast on misconfiguration:

```csharp
.ValidateDataAnnotations()
.ValidateOnStart()
```

This causes the application to throw an `OptionsValidationException` at startup rather than at runtime when the missing/invalid value is first accessed.
