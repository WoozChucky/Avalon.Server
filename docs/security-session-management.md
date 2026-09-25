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
- Recovery codes (`MFARecoveryCodes`): three per account, each 80 bits from `ISecureRandom`, shown as
  16 Crockford base32 characters (`XXXX-XXXX-XXXX-XXXX`). They are generated at confirm and returned
  only in that response; the `MfaSetups` row stores only the SHA-256 of each canonical code (upper
  case, no separators). Reset hashes each input and compares with `CryptographicOperations.FixedTimeEquals`,
  and a successful reset deletes the row, which consumes the codes. A stored value that is not a
  32-byte hash, such as a plaintext code written before #464, never verifies.
- Failed logins increment `account.FailedLogins` and lock the account at the configured threshold.

---

## REST API Authentication

`Avalon.Api` accepts two credentials. The CLAUDE.md "REST API Auth" section has the full rules.

- **Access JWT:** sent as `Authorization: Bearer <jwt>` or in the `AVToken` cookie. `JwtUtils` mints it at
  login, MFA verify and refresh. The bearer handler checks the signature (HS256 only), issuer, audience and
  lifetime (`AccessTokenLifetimeMinutes`, default 15, plus `ClockSkewInMinutes`). An expired JWT gets a
  401, and the client renews it with `POST /account/refresh`.
- **Personal access token (PAT):** sent as `Authorization: Avalon avp_...`. `AvalonAuthenticationHandler`
  (`src/Server/Avalon.Api/Authentication/AV/`) reads it, looks up the token by its SHA-256 hash, and refuses
  it if it is revoked or expired.

Neither credential is trusted on its own (#451, #480). On every request both go through
`AccountAccessCheck`. It reloads the account and refuses a missing or non-Active one with 401. The request
then carries only the credential's roles masked by the account's current `AccessLevel`, so a demotion or
ban takes effect on the next request.

### JWT Signing Key

The JWTs are signed and validated with an HMAC-SHA256 key, `Application:Authentication:IssuerSigningKey`.
No key is committed (#482). In development it comes from `dotnet user-secrets`; everywhere else it comes
from the environment variable `Application__Authentication__IssuerSigningKey` (the Helm chart's
`authentication.issuerSigningKey`).

`JwtSigningKey.Create` runs when `AddAuth` registers authentication, so the API refuses to start, with an
error naming the setting, when the key is:
- missing;
- under 32 bytes in UTF-8;
- the value that used to be committed to `appsettings.json`, which is public.

The key never appears in logs. Changing it invalidates every access token already issued; clients get a 401
and refresh. Setup commands: README "Running Locally", CONTRIBUTING "Local Setup".

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
