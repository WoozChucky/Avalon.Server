# Security — Session & Authentication Management

This document covers the authentication pipeline, world session security, and MFA flow for the Avalon server.

---

## Authentication Flow

```
Game Client          Auth Server                Redis               World Server
    |                    |                         |                     |
    | CRequestServerInfo |                         |                     |
    |------------------>|                         |                     |
    |  SServerInfoPacket |                         |                     |
    | (version + pubkey) |                         |                     |
    |<------------------|                         |                     |
    |                    |                         |                     |
    |   CAuthPacket      |                         |                     |
    | (user + encrypted  |                         |                     |
    |   password)        |                         |                     |
    |------------------>|                         |                     |
    |                    | BCrypt.Verify           |                     |
    |                    |-------+                 |                     |
    |                    |       |                 |                     |
    |                    | [MFA enabled?]          |                     |
    |                    | --- Yes: ephemeral hash |                     |
    |                    |-------->SetAsync(hash)  |                     |
    |  SAuthResultPacket |                         |                     |
    |  (MFA_REQUIRED)    |                         |                     |
    |<------------------|                         |                     |
    |  CMFAVerifyPacket  |                         |                     |
    |------------------>|                         |                     |
    |                    | GetAsync(hash)          |                     |
    |                    |<------------------------|                     |
    |                    | ValidateOTP             |                     |
    |                    |------+                  |                     |
    |  SAuthResultPacket |      |                  |                     |
    |  (OK + world list) |                         |                     |
    |<------------------|                         |                     |
    |                    |                         |                     |
    |  CWorldSelectPacket|                         |                     |
    |------------------>|                         |                     |
    |                    | CSPRNG 32-byte key      |                     |
    |                    | SetAsync(world:key, ...) |                    |
    |                    |----------------------->|                     |
    |                    | Publish(world:select)   |                     |
    |                    |----------------------->|-------------------->|
    |  SWorldSelectPacket|                         |                     |
    |  (world key)       |                         |                     |
    |<------------------|                         |                     |
    |                    |          Game Client connects to World Server |
```

---

## World Key Security

World keys are generated using `RandomNumberGenerator.GetBytes(32)` (OS CSPRNG). The previous `System.Random` implementation has been replaced.

```csharp
byte[] worldKey = RandomNumberGenerator.GetBytes(32); // System.Security.Cryptography
```

`RandomNumberGenerator.GetBytes` is backed by the OS CSPRNG and produces cryptographically unpredictable values regardless of timing.

### Key Lifecycle

| Phase              | Action                                                                      |
|--------------------|-----------------------------------------------------------------------------|
| Issued             | Auth server writes `world:{worldId}:keys:{base64Key}` → `accountId` in Redis, TTL 5 min |
| Consumed           | World server validates key on first connect, deletes the Redis entry         |
| Expired            | TTL expiry automatically invalidates unclaimed keys                          |
| Logout / Crash     | World server publishes `world:accounts:disconnect`; Auth server clears state |

---

## Duplicate World Session Guard

`CWorldSelectHandler` uses a Redis `SETNX` mutex to prevent an account from obtaining two in-flight world keys simultaneously:

```
SETNX account:{id}:inWorld 1 EX 300
```

If `SETNX` returns `0` (key exists), the request is rejected. The flag is cleared when the World server accepts the connection or expires after 5 minutes.

### Flow

```
CWorldSelectHandler.ExecuteAsync
  1. SETNX account:{id}:inWorld 1 EX 300
     └─ Returns 0 → send error and return
  2. Generate world key (CSPRNG)
  3. SET world:{worldId}:keys:{key} {accountId} EX 300
  4. Publish world:{worldId}:select
  5. Send SWorldSelectPacket to client
```

---

## MFA Flow

MFA is implemented using TOTP (Time-based One-Time Passwords) via `Otp.NET`. When an account has confirmed MFA set up, `CAuthHandler` issues an ephemeral hash instead of completing authentication directly.

### Packet Flow

```
Client               Auth Server           Redis
  |  CAuthPacket(user,pass)  |                |
  |------------------------->|                |
  |  SAuthResultPacket       | SetAsync(hash) |
  |    (MFA_REQUIRED, hash)  |--------------->|
  |<-------------------------|                |
  |  CMFAVerifyPacket(totp)  |                |
  |------------------------->|                |
  |                          | GetAsync(hash) |
  |                          |<---------------|
  |                          | ValidateOTP    |
  |  SAuthResultPacket(OK)   |                |
  |<-------------------------|                |
```

### MFA Lifecycle Handlers

| Packet            | Handler               | Purpose                                      |
|-------------------|-----------------------|----------------------------------------------|
| `CMFAVerifyPacket`| `CMFAVerifyHandler`   | Submit TOTP code to complete login           |
| `CMFASetupPacket` | `CMFASetupHandler`    | Initiate MFA setup — returns OTP URI         |
| `CMFAConfirmPacket`| `CMFAConfirmHandler` | Confirm setup with first TOTP code           |
| `CMFAResetPacket` | `CMFAResetHandler`    | Reset MFA using recovery codes               |

### Security Notes

- Ephemeral hash TTL: 5 minutes.
- MFA hash is deleted from Redis after a successful verify (single-use).
- Failed logins increment `account.FailedLogins` and lock the account at the configured threshold.

---

## `Avalon` Bearer Token Validation

`AvalonAuthenticationHandler` (`src/Server/Avalon.Api/Authentication/AV/`) handles the `Authorization: Avalon <token>` scheme used for internal management operations. Token validation is not yet implemented — the handler currently returns success with a placeholder claim.

### Intended Implementation

1. Add `SharedSecret` (string) to `AvalonAuthenticationSchemeOptions`.
2. Populate from environment variable or secrets manager (never from committed config).
3. Use constant-time comparison:

```csharp
bool valid = CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(token),
    Encoding.UTF8.GetBytes(Options.SharedSecret));

if (!valid)
    return AuthenticateResult.Fail("Invalid Avalon token");
```

4. Replace the placeholder claim with a meaningful role claim, e.g. `("role", "InternalAdmin")`.

### Security Requirements

- `SharedSecret` must be at least 32 characters (entropy ≥ 192 bits).
- Timing-safe comparison prevents oracle attacks.
- Secret must **not** appear in application logs.

---

## Test Coverage

| Scenario                                             | Expected Result              |
|------------------------------------------------------|------------------------------|
| World key from `RandomNumberGenerator`               | Unpredictable 32-byte key    |
| Two rapid world-selects for the same account         | Second request rejected      |
| Account with confirmed MFA                           | `MFA_REQUIRED` response      |
| Account without MFA                                  | `OK` + world list            |
| Correct TOTP submitted                               | Auth success                 |
| Incorrect TOTP submitted                             | Auth fail                    |
| Expired MFA hash                                     | Auth fail                    |
