# Configuration Reference

This document covers all configuration keys for the Avalon server.

---

## Overview

Avalon uses strongly-typed configuration classes bound from `appsettings.json` (or environment variables / secrets manager in production) via `IOptions<T>`. Configuration classes reside in `src/Shared/Avalon.Configuration`.

---

## Configuration Classes

| Class                     | Namespace                    | Bound from                   |
|---------------------------|------------------------------|------------------------------|
| `DatabaseConfiguration`   | `Avalon.Configuration`       | `ConnectionStrings:*`        |
| `CacheConfiguration`      | `Avalon.Configuration`       | `Cache:*`                    |
| `AuthenticationConfig`    | `Avalon.Configuration`       | `Authentication:*`           |
| `HostingConfiguration`    | `Avalon.Configuration`       | `Hosting:*`                  |
| `AuthConfiguration`       | `Avalon.Server.Auth.Configuration` | `Application:*`        |
| `GameConfiguration`       | `Avalon.World.Configuration` | `Game:*`                     |

---

## Auth Server Configuration (`AuthConfiguration`)

Section in `appsettings.json`: `"Application"`

| Key                         | Type   | Default   | Description                                    |
|-----------------------------|--------|-----------|------------------------------------------------|
| `MinClientVersion`          | string | `"0.0.1"` | Minimum client version accepted during handshake |
| `ServerVersion`             | string | `"1.0.0"` | Server version sent in `SServerInfoPacket` to clients |
| `MaxFailedLoginAttempts`    | int    | `5`       | Consecutive failed logins before account lock  |
| `Issuer`                    | string | `"Avalon"` | Issuer name embedded in MFA OTP URIs           |

```json
"Application": {
  "MinClientVersion": "0.0.1",
  "ServerVersion": "1.0.0",
  "MaxFailedLoginAttempts": 5,
  "Issuer": "Avalon"
}
```

**Validation rules:**
- `MinClientVersion`, `ServerVersion`: required, must match `^\d+\.\d+\.\d+$` (SemVer).
- `MaxFailedLoginAttempts`: minimum `1`.
- `Issuer`: required, non-empty.

---

## Hosting Configuration (`HostingConfiguration`)

Section in `appsettings.json`: `"Hosting"`

| Key                     | Type   | Default  | Description                                             |
|-------------------------|--------|----------|---------------------------------------------------------|
| `Host`                  | string | `"0.0.0.0"` | Bind address                                         |
| `Port`                  | int    | `21000`  | TCP listen port                                         |
| `PacketReaderBufferSize`| int    | `4096`   | Internal read-buffer size in bytes for `PacketReader`   |

```json
"Hosting": {
  "Host": "0.0.0.0",
  "Port": 21000,
  "PacketReaderBufferSize": 4096,
  "Security": {
    "CertificatePath": "cert-tcp.pfx",
    "CertificatePassword": "avalon"
  }
}
```

**Validation rules:**
- `PacketReaderBufferSize`: minimum `512`, maximum `65535`.

---

## Cache Configuration (`CacheConfiguration`)

Section in `appsettings.json`: `"Cache"`

| Key        | Type   | Description                           |
|------------|--------|---------------------------------------|
| `Host`     | string | Redis endpoint, e.g. `"localhost:6379"` |
| `Password` | string | Redis AUTH password                   |

```json
"Cache": {
  "Host": "localhost:6379",
  "Password": "your-redis-password"
}
```

---

## Avalon Internal Authentication

Section: environment variable or secrets manager (**never committed to source control**)

| Key                   | Type   | Default       | Description                                               |
|-----------------------|--------|---------------|-----------------------------------------------------------|
| `Avalon:SharedSecret` | string | _(required)_  | Shared secret for `Authorization: Avalon <token>` scheme  |

> **Warning:** This key must **never** appear in `appsettings.json`. Use environment variables or a secrets manager (Azure Key Vault, AWS Secrets Manager, dotnet user-secrets) in all environments.

```bash
# Environment variable:
Avalon__SharedSecret=<minimum-32-char-random-value>
```

---

## Applying Configuration at Startup

All configuration classes use:

1. A property with a data annotation (`[Required]`, `[Range(...)]`, `[RegularExpression(...)]`).
2. `services.AddOptions<TConfig>().BindConfiguration(section).ValidateDataAnnotations().ValidateOnStart()` in the appropriate DI extension method.
3. `IOptions<TConfig>` injection in the consuming class.

Example for `AuthConfiguration`:

```csharp
// In IServiceCollection extension:
services.AddOptions<AuthConfiguration>()
    .BindConfiguration("Application")
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

Use `appsettings.{Environment}.json` (e.g. `appsettings.Production.json`) to override defaults per environment without changing the base file. Sensitive values (database passwords, `SharedSecret`) must come from environment variables or a secrets manager, not files.

```bash
# Environment variable override syntax (.NET):
Application__MinClientVersion=1.5.0
Application__MaxFailedLoginAttempts=10
Avalon__SharedSecret=<from-vault>
```

---

## Startup Validation

All config classes opt into startup validation to fail fast on misconfiguration:

```csharp
.ValidateDataAnnotations()
.ValidateOnStart()
```

This causes the application to throw an `OptionsValidationException` at startup rather than at runtime when the missing/invalid value is first accessed.
