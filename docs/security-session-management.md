# Security — Session & Authentication Management

This document covers the authentication pipeline, world session security, and MFA flow for the Avalon server.

Related TODOs: [TODO-006](../TODO.md#todo-006), [TODO-007](../TODO.md#todo-007), [TODO-008](../TODO.md#todo-008), [TODO-013](../TODO.md#todo-013)

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

## World Key Security (TODO-006)

### Problem

`CWorldSelectHandler` previously used `new Random().NextBytes(worldKey)`. `System.Random` is seeded from the system clock and its output is predictable. Two connections that hit the handler within the same tick could receive correlated keys.

### Fix

```csharp
// BEFORE (insecure):
var worldKey = new byte[32];
new Random().NextBytes(worldKey);

// AFTER (secure):
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

## Duplicate World Session Guard (TODO-008)

### Problem

Nothing prevents an account from calling `CWorldSelectHandler` twice before the first key is consumed by the World server. This could create two in-flight world keys for the same account.

### Proposed Guards

**Option A — Redis atomic check:**

```
SETNX account:{id}:inWorld 1 EX 300
```

If `SETNX` returns `0` (key exists), reject the request. When the World server accepts the connection (key consumed), either clear the flag or let it expire. On character logout, World server deletes the key.

**Option B — Check existing keys:**

Before issuing, scan `world:{worldId}:keys:*` for any key whose value matches the account ID. Reject if found.

Option A is preferred (atomic, O(1)).

### Flow with Guard

```
CWorldSelectHandler.ExecuteAsync
  1. Check SETNX account:{id}:inWorld 1 EX 300
     └─ Returns 0 → send error and return
  2. Generate world key (CSPRNG)
  3. SET world:{worldId}:keys:{key} {accountId} EX 300
  4. Publish world:{worldId}:select
  5. Send SWorldSelectPacket to client
```

---

## `Avalon` Bearer Token Validation (TODO-007)

### Current State

`AvalonAuthenticationHandler.HandleAuthenticateAsync` extracts the token but does not validate it, returning success with a hardcoded `("test", "value")` claim.

### Intended Purpose

This handler is for server-internal management operations carried under the `Authorization: Avalon <token>` scheme — separate from the standard `Bearer` JWT scheme used by regular API consumers.

### Implementation

1. Add `SharedSecret` (string) to `AvalonAuthenticationSchemeOptions`.
2. Populate from `IConfiguration["Avalon:SharedSecret"]` at registration. The secret must come from environment variables or a secrets manager — never from source-controlled `appsettings.json`.
3. Use constant-time comparison:

```csharp
bool valid = CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(token),
    Encoding.UTF8.GetBytes(Options.SharedSecret));

if (!valid)
    return AuthenticateResult.Fail("Invalid Avalon token");
```

4. Replace the `("test", "value")` claim with a meaningful role claim, e.g. `("role", "InternalAdmin")`.

### Security Requirements

- `SharedSecret` is at least 32 characters (entropy ≥ 192 bits).
- Timing-safe comparison prevents oracle attacks.
- Secret must **not** appear in application logs.

---

## MFA Flow Re-enablement (TODO-013)

### Dependencies Already Wired

- `IMFAHashService` — injected into `CAuthHandler`, generates an ephemeral TOTP challenge hash.
- `Otp.NET` — TOTP library present in dependencies.
- Redis — used to store the ephemeral hash with a short TTL.

### Re-enable Steps

1. Uncomment the MFA block in `CAuthHandler`:

```csharp
var mfa = await _mfaSetupRepository.FindByAccountIdAsync(account.Id!.Value);
if (mfa is { Status: Status.Confirmed })
{
    var mfaHash = await _mfaHashService.GenerateHashAsync(account);
    ctx.Connection.Send(SAuthResultPacket.Create(null, mfaHash, AuthResult.MFA_REQUIRED,
        ctx.Connection.CryptoSession.Encrypt));
    return;
}
```

2. Ensure `IMFASetupRepository` is injected (was `_databaseManager.Auth.MFASetup` in the old code).
3. Implement or verify `CMFAVerifyHandler` exists to handle the second factor.

### MFA Packet Flow

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

### Security Notes

- Ephemeral hash TTL: 5 minutes.
- MFA hash must not be reused (delete from Redis after first verify attempt).
- Failed MFA attempts should increment a counter and lock after N failures.

---

## Test Coverage Matrix

| Scenario                                             | TODO  | Expected Result              |
|------------------------------------------------------|-------|------------------------------|
| World key from `RandomNumberGenerator`               | 006   | Unpredictable 32-byte key    |
| Two rapid world-selects for the same account         | 008   | Second request rejected      |
| Valid `Avalon` token                                 | 007   | `AuthenticateResult.Success` |
| Invalid `Avalon` token                               | 007   | `AuthenticateResult.Fail`    |
| Account with confirmed MFA                           | 013   | `MFA_REQUIRED` response      |
| Account without MFA                                  | 013   | `OK` + world list            |
| Correct TOTP submitted                               | 013   | Auth success                 |
| Incorrect TOTP submitted                             | 013   | Auth fail                    |
| Expired MFA hash                                     | 013   | Auth fail                    |
