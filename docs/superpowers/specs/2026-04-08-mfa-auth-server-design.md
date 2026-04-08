# MFA Auth Server — Design Spec

**Date:** 2026-04-08
**Status:** Approved
**Closes:** TODO-013

## Goal

Re-enable MFA verification in the TCP auth login flow and give game clients full MFA lifecycle capability (setup, confirm, reset) via the Auth server TCP connection — parity with the existing REST API. Extract shared MFA business logic from `Avalon.Api` into `Avalon.Infrastructure` so both servers consume a single implementation.

---

## Background

`MFAService` (TOTP business logic: setup/confirm/verify/reset) currently lives only in `Avalon.Api`. `IMFAHashService` (Redis ephemeral hash management) already lives in `Avalon.Infrastructure` and is already injected into `CAuthHandler`, but the MFA check is commented out (TODO-013). The Auth TCP connection is long-lived (stays open until player exits), making it the correct host for all MFA operations for game clients.

A secondary fix is included: `MFAHashService.GetAccountIdAsync` currently performs an O(N) Redis `KEYS` glob scan. This is replaced with an O(1) reverse-lookup key.

---

## Section 1 — Shared Code Extraction (`Avalon.Infrastructure`)

### Project reference

`Avalon.Infrastructure.csproj` adds a `<ProjectReference>` to `Avalon.Database.Auth`. Both `Avalon.Api` and `Avalon.Server.Auth` already reference `Avalon.Infrastructure`, so this single addition makes `IMFAService` + `MFAService` available to both servers with no further project reference changes.

### Files moved

| From | To |
|---|---|
| `Avalon.Api/Services/MFAService.cs` | `Avalon.Infrastructure/Services/MFAService.cs` |
| `Avalon.Api/Services/IMFAService.cs` | `Avalon.Infrastructure/Services/IMFAService.cs` |

`Avalon.Api` removes its local copies and registers the shared versions. No behaviour changes to the API.

### `IMFAService` interface (updated)

The interface moves to `Avalon.Infrastructure`. The return types are updated from REST-oriented DTOs (`SetupMFAResponse`, etc. in `Avalon.Api/Contract/`) to leaner result records that live in `Avalon.Infrastructure/Services/`:

```csharp
public interface IMFAService
{
    Task<MFASetupResult> SetupMFAAsync(AccountId accountId);
    Task<MFAConfirmResult> ConfirmMFAAsync(AccountId accountId, string code);
    Task<MFAVerifyResult> VerifyMFAAsync(string hash, string code);
    Task<MFAResetResult> ResetMFAAsync(AccountId accountId, string r1, string r2, string r3);
}

// Result records (new, in Avalon.Infrastructure/Services/MFAResults.cs):
public record MFASetupResult(bool Success, string? OtpUri, MFAOperationResult Status);
public record MFAConfirmResult(bool Success, string[]? RecoveryCodes, MFAOperationResult Status);
public record MFAVerifyResult(bool Success, AccountId? AccountId);
public record MFAResetResult(bool Success, MFAOperationResult Status);
```

`MFAVerifyResult` intentionally does NOT issue a JWT — the API issues the token after calling the service; the TCP handler marks the connection as authenticated. `Avalon.Api/MFAController` is updated to call `VerifyMFAAsync` and map the result to its existing `AuthenticateResponse` DTO.

The existing REST DTOs in `Avalon.Api/Contract/` remain as-is for the API layer; they are now populated from the shared result records instead of being returned directly from the service.

### Redis reverse-lookup fix (`MFAHashService`)

**Problem:** `GetAccountIdAsync(string hash)` scans `auth:account:*:mfa` with `KEYS` — O(N) across all accounts.

**Fix:** On hash generation, write a second key:
- `auth:mfa:hash:{hashValue}` → `accountId` (same 2-minute TTL)

`GetAccountIdAsync` becomes a single `GET auth:mfa:hash:{hash}` — O(1).
`CleanupHash` deletes both the forward key (`auth:account:{accountId}:mfa`) and the reverse key (`auth:mfa:hash:{hash}`).

New `CacheKeys` entry:
```csharp
public static string MfaReverseHash(string hash) => $"auth:mfa:hash:{hash}";
```

---

## Section 2 — New TCP Packets

All packets use `NetworkProtocol.Authentication`. New `NetworkPacketType` enum values added for each.

### Client → Server

| Packet | Fields | When |
|---|---|---|
| `CMFAVerifyPacket` | `string MfaHash`, `string Code` | During login, after `MFA_REQUIRED` |
| `CMFASetupPacket` | _(none)_ | After authenticated, to initiate setup |
| `CMFAConfirmPacket` | `string Code` | After setup, to confirm with TOTP |
| `CMFAResetPacket` | `string RecoveryCode1`, `string RecoveryCode2`, `string RecoveryCode3` | To disable/reset MFA |

### Server → Client

| Packet | Fields | In response to |
|---|---|---|
| `SMFASetupPacket` | `string OtpUri`, `MFAOperationResult Result` | `CMFASetupPacket` |
| `SMFAConfirmPacket` | `string[] RecoveryCodes`, `MFAOperationResult Result` | `CMFAConfirmPacket` |
| `SMFAResetPacket` | `MFAOperationResult Result` | `CMFAResetPacket` |

`CMFAVerifyPacket` reuses the existing `SAuthResultPacket` as its response — no new response packet.

### New enum values

```csharp
// AuthResult (existing enum) — add:
MFA_FAILED,

// New enum in Avalon.Network.Packets:
public enum MFAOperationResult : ushort
{
    Success,
    AlreadyEnabled,
    InvalidCode,
    NotEnabled,
    Error
}
```

---

## Section 3 — Auth Server Handlers

### Updated: `CAuthHandler`

Re-enable the commented TODO-013 block. Replace `_databaseManager.Auth.MFASetup` with injected `IMfaSetupRepository`. Full updated check:

```csharp
var mfa = await _mfaSetupRepository.FindByAccountIdAsync(account.Id!.Value);
if (mfa is { Status: MfaSetupStatus.Confirmed })
{
    var mfaHash = await _mfaHashService.GenerateHashAsync(account);
    ctx.Connection.Send(SAuthResultPacket.Create(null, mfaHash, AuthResult.MFA_REQUIRED, ...));
    return;
}
```

`IMfaSetupRepository` is added to `CAuthHandler`'s constructor.

### New: `CMFAVerifyHandler`

- Calls `IMFAService.VerifyMFAAsync(packet.MfaHash, packet.Code)`
- If `MFAVerifyResult.Success` is false → `SAuthResultPacket(MFA_FAILED)`
- On success: mark connection authenticated with `result.AccountId`, send `SAuthResultPacket(SUCCESS, accountId)`
- Hash cleanup is handled inside `VerifyMFAAsync` (same as the API flow)

### New: `CMFASetupHandler`

- Guard: connection must be authenticated (same guard used by `CWorldListHandler`)
- If MFA already `Confirmed` → `SMFASetupPacket(AlreadyEnabled)`
- Calls `IMFAService.SetupMFA(accountId)` → `SMFASetupPacket(OtpUri, Success)`

### New: `CMFAConfirmHandler`

- Guard: authenticated connection
- Calls `IMFAService.ConfirmMFA(accountId, packet.Code)`
- Success → `SMFAConfirmPacket(RecoveryCodes, Success)`
- Invalid code → `SMFAConfirmPacket(InvalidCode)`

### New: `CMFAResetHandler`

- Guard: authenticated connection
- Calls `IMFAService.ResetMFA(accountId, r1, r2, r3)`
- Success → `SMFAResetPacket(Success)`
- Invalid recovery codes → `SMFAResetPacket(InvalidCode)`

---

## Section 4 — Tests

All tests use xUnit + NSubstitute. No real Redis or DB.

### `CAuthHandlerShould.cs` (additions)

- `Should_Return_MFA_Required_When_Account_Has_Confirmed_MFA`
- `Should_Return_Success_When_Account_Has_No_MFA`

### `CMFAVerifyHandlerShould.cs` (new)

- `Should_Return_Success_When_Code_Is_Valid`
- `Should_Return_MFA_Failed_When_Hash_Not_Found`
- `Should_Return_MFA_Failed_When_Code_Is_Invalid`

### `CMFASetupHandlerShould.cs` (new)

- `Should_Return_OtpUri_When_Setup_Initiated`
- `Should_Return_AlreadyEnabled_When_MFA_Already_Confirmed`

### `CMFAConfirmHandlerShould.cs` (new)

- `Should_Return_RecoveryCodes_When_Code_Is_Valid`
- `Should_Return_InvalidCode_When_Code_Is_Wrong`

### `CMFAResetHandlerShould.cs` (new)

- `Should_Return_Success_When_Recovery_Codes_Match`
- `Should_Return_InvalidCode_When_Recovery_Codes_Wrong`

### `MFAHashServiceShould.cs` (new, in `Avalon.Shared.UnitTests`)

- `Should_Return_AccountId_Via_Reverse_Lookup`
- `Should_Cleanup_Both_Keys_On_Cleanup`

---

## Flow Diagrams

### Login with MFA

```
CAuthPacket
  └─ account found, BCrypt verified
       └─ MFA Confirmed?
            ├─ Yes → GenerateHash → SAuthResultPacket(MFA_REQUIRED, mfaHash)
            │         └─ CMFAVerifyPacket(mfaHash, totpCode)
            │               ├─ Valid → CleanupHash → SAuthResultPacket(SUCCESS, accountId)
            │               └─ Invalid → SAuthResultPacket(MFA_FAILED)
            └─ No  → SAuthResultPacket(SUCCESS, accountId)
```

### MFA Setup (post-authentication)

```
CMFASetupPacket
  └─ Already Confirmed? → SMFASetupPacket(AlreadyEnabled)
  └─ No → SetupMFA() → SMFASetupPacket(OtpUri, Success)
       └─ CMFAConfirmPacket(totpCode)
            ├─ Valid → SMFAConfirmPacket(RecoveryCodes, Success)
            └─ Invalid → SMFAConfirmPacket(InvalidCode)
```

### MFA Reset

```
CMFAResetPacket(r1, r2, r3)
  ├─ Codes match → SMFAResetPacket(Success)  [status reset to Setup]
  └─ Codes wrong → SMFAResetPacket(InvalidCode)
```

---

## Out of Scope

- MFA rate limiting on failed TOTP attempts (tracked separately)
- Worker service for DB cleanup (Redis TTL handles ephemeral keys; stale `Setup` rows are harmless)
- `Avalon.Server.World` MFA integration (Auth connection covers the full session)
- TTL discrepancy fix between docs (2 min code) and docs/security-session-management.md (5 min) — update docs separately
