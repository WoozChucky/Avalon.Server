# Security — Session & Authentication Management

This document covers the authentication pipeline, how world entry is secured, and the MFA flow for the Avalon server.

---

## Sign-in

Identity (`Avalon.Api.Identity`) handles sign-in over HTTPS: password login and MFA, launcher credentials, and store proofs. The TCP auth server was removed in #1097. A game obtains a context through `/client/auth`, requests a join ticket, and presents it to the world over pinned TLS. See [World entry](api-authentication.md#world-entry) for the client sequence.

---

## World Entry

The retired TCP world select, world-key exchange and `SETNX` duplicate-session mutex were replaced by the TLS join-ticket admission of the REST API's identity service, which also replaced
their guarantees:

- **One-time, short-lived entry.** A join ticket (`POST /game/join-tickets`) is a random token kept in Redis only as
  its digest, bound to the game context, the world, the character and the world server, valid for at most 30 seconds
  and redeemed once, by the world server it names, over that server's mutually authenticated workload connection.
- **One session per account.** Redeeming a ticket reserves the account's game session with a fencing token; a live
  session elsewhere, active or still pending, refuses a new ticket (`ActiveGameSession`) unless a join confirms the
  takeover; a reconnect replaces only its own context's active session, never a pending one (a world whose admission
  failed ends the pending session it reserved, #1013), and every
  character save checks the fence, so a replaced session can no longer write.
- **Current access.** Issue and redemption both re-read the account (Active, not being consolidated, at the context's
  credentials version), the world's access level and maintenance cutoff, and the world's readiness. A password lock is
  not asked: it guards the password steps only (#882), so a guesser cannot keep a player out of the game.
- **Named refusals, after the proof.** An account that may not play is answered 403 `ACCOUNT_BANNED`,
  `ACCOUNT_DEACTIVATED` or `ACCOUNT_CONSOLIDATING` only once the caller's proof is current and the account still at
  its credentials version; a stale or missing proof keeps the generic answer (#882,
  [REST API authentication](api-authentication.md)).
- **A ban ends the game.** It raises the account's session epoch in its own transaction, which voids every game
  context and session of the account for good, and the servers close its connections with `DisconnectReason.Banned`
  or `Deactivated` (`world:accounts:status`).

[World entry](api-authentication.md#world-entry) has the client's steps, and
[game server admission](steam-authentication-workloads.md) the session lease, heartbeat and save fencing.

---

## MFA Flow

MFA uses TOTP via `Otp.NET`. After a valid password for an account with confirmed MFA, identity issues a two-minute login hash; `POST /mfa/verify` submits that hash and a code to finish sign-in.

| REST endpoint | Purpose |
|---|---|
| `POST /mfa/verify` | Complete login with a TOTP code and login hash |
| `POST /mfa/setup` | Check the current password and return an OTP URI |
| `POST /mfa/confirm` | Confirm setup with the first TOTP code and return recovery codes |
| `POST /mfa/reset` | Remove MFA using recovery codes; return 204 |

### Security Notes

- The two-minute login hash has one winner, and each TOTP step is accepted only once. The attempt budgets, replay refunds and race ordering are specified in [Login budgets and account rules](api-authentication.md#login-budgets-and-account-rules).
- Resetting MFA with the recovery codes deletes the row and revokes every refresh token and personal
  access token the account holds, in one transaction (`MfaSetupRepository.ResetConfirmedAsync`, #483),
  then publishes the account on `world:accounts:disconnect` (best-effort), through
  `MFAService.ResetMFAAsync`. The REST reset (`POST /mfa/reset`) returns 204 and enrols nothing: the
  recovery codes are not the password, so enrolling again is `POST /mfa/setup`, which asks for it.
- Recovery codes (`MFARecoveryCodes`): three per account, each 80 bits from `ISecureRandom`, shown as
  16 Crockford base32 characters (`XXXX-XXXX-XXXX-XXXX`). They are generated at confirm and returned
  only in that response; the `MfaSetups` row stores only the SHA-256 of each canonical code (upper
  case, no separators). Reset hashes each input and compares with `CryptographicOperations.FixedTimeEquals`,
  and a successful reset deletes the row, which consumes the codes. A stored value that is not a
  32-byte hash, such as a plaintext code written before #464, never verifies.
- Failed logins and codes are counted and locked by the shared login policy described under
  [Login budgets and account rules](api-authentication.md#login-budgets-and-account-rules).

---

## REST API Authentication

Every API service accepts two credentials, which only identity issues ([API services](api-services.md)).
[REST API authentication](api-authentication.md) has the full rules.

- **Access JWT:** sent as `Authorization: Bearer <jwt>` or in the `AVToken` cookie. `JwtUtils` mints it at
  login, MFA verify and refresh. The bearer handler checks the signature (ES256 only), issuer, audience and
  lifetime (`AccessTokenLifetimeMinutes`, default 15, plus `ClockSkewInMinutes`). An expired JWT gets a
  401, and the client renews it with `POST /account/refresh`.
- **Personal access token (PAT):** sent as `Authorization: Avalon avp_...`. `AvalonAuthenticationHandler`
  (`src/Server/Avalon.Api.Hosting/Authentication/AV/`, in every API service) reads it, looks up the token by
  its SHA-256 hash, and refuses it if it is revoked or expired.

Neither credential is trusted on its own (#451, #480). On every request both go through
`AccountAccessCheck`. It reloads the account and refuses a missing or non-Active one with 401. The request
then carries only the credential's roles masked by the account's current `AccessLevel`, so a demotion or
ban takes effect on the next request.

### Login policy

Identity uses `PasswordLoginPolicy` and `MfaLoginPolicy` in `Avalon.Infrastructure.Login`. Their Redis source and username budgets, lock races, MFA consumption, forwarded-address handling and startup limits are documented in [Login budgets and account rules](api-authentication.md#login-budgets-and-account-rules). Identity replicas share those budgets. `Accounts.Online` and `OnlineSessionId` remain in the schema, but the retired TCP connection lifecycle no longer maintains them; game admission uses session leases and save fences.

### Re-authentication for sensitive actions

`IReauthentication.RequireCurrentPasswordAsync` (`Avalon.Api.Identity/Services/Reauthentication.cs`) runs the
password step against the signed-in account, so a wrong current password is a failed login in every respect:
both budgets, the row's count, and the lock in the last slot. It guards:

- `POST /account/password` (`currentPassword`, as before);
- `POST /mfa/setup` (was `GET`), body `SetupMFARequest { currentPassword }` (#478);
- `POST /pat` and `POST /pat/admin`, a `currentPassword` field on the request body (#483). The admin route
  takes the admin's own password: it can mint for any account, the admin's included.

A missing or wrong password is 401 `Invalid current password`; a spent budget or locked account is 429
`LOCKED`. A right one gives back only its own slots: no login completed, so no count is cleared.

### Revocation

- A password change writes the new verifier by column and revokes every refresh token and every personal
  access token in the same transaction (#483), then publishes the account on `world:accounts:disconnect`.
- An MFA reset with the recovery codes does the same (see [MFA Flow](#security-notes)).
- An admin's MFA removal and a ban already did (#475, #480). A ban or a deactivation also raises the session epoch,
  voiding every game context and session, and publishes on `world:accounts:status` instead (#882).
- The publish is best-effort: the change is committed, so a Redis failure is logged and the call succeeds. When the status notice on `world:accounts:status` cannot be published, the bare id goes on `world:accounts:disconnect` instead, so connections still close (#882).

Access JWTs are revalidated against the account on every request. A credentials-version change or inactive status therefore refuses an already issued JWT at its next use; see [REST API authentication](api-authentication.md).

### Account writes

The API never writes back an account row it read (#478, #484). A login and an MFA
verify write `TryRecordApiLoginAsync`, a password change `AccountRepository.SetPasswordAsync`, an email
change `SetConfirmedEmailAsync`, a role change `SetAccessLevelAsync`: each an `ExecuteUpdate` of its own columns, so a
ban or a lock written between the read and the write survives.

### JWT Signing Key

The access JWTs are signed with ES256 only (#801): identity signs them with an EC P-256 private key,
`Application:Authentication:SigningKey`, under a key id, `SigningKeyId`; every API service checks them with the public
key their key id names, `Application:Authentication:ValidationKeys:<key id>`, so a service without identity cannot
mint one. No private key is committed (#482). In development they come from `dotnet user-secrets` (or the Aspire
AppHost, which makes a key pair per run); everywhere else from the environment (`Application__Authentication__SigningKey`
and the others), which the Helm chart fills from a Kubernetes Secret rather than a plain value in the pod spec, the
private key only where identity runs.

`JwtKeys.Create` runs while every API process is built, so the API refuses to start, with an error naming the
setting, when:
- a process running identity has no private key, one that does not parse, one on another curve than P-256, or no key id;
- a process that does not run identity holds the private key;
- a public key does not parse, holds a private key, or is listed under identity's key id without being its own;
- a process has no key to validate with.

A token is checked only against the public key its key id names, and only when it is ES256 (`JwtKeys.Resolve`): an
HS256 token, a token naming an unknown key id, an HMAC keyed with a public key, or one with no signature finds none.
HS256 is refused whatever is configured: the HS256 key of before #801, `Application:Authentication:IssuerSigningKey`,
is ignored, and a process that still has it logs one warning at startup naming it. Identity's game-auth host key,
`Application:GameAuth:HostKey` (in a deployment from before #801, that key's value), is required: identity refuses to
start without it, or when it has leading or trailing whitespace, is under 32 bytes in UTF-8, or is the value once
committed to `appsettings.json`, which is public.

No key appears in logs. A rotation lists the new public key everywhere before identity signs with it, so no token is
refused; swapping the key without that refuses the tokens signed with the old one, and clients get a 401 and refresh.
Setup and rotation: [Development setup](development-setup.md#rest-api-signing-key).

## Test Coverage

| Scenario                                             | Expected Result              |
|------------------------------------------------------|------------------------------|
| Account with confirmed MFA                           | `AuthenticationResponseStatus.RequiresMFA` response      |
| Account without MFA                                  | Access and refresh credentials issued                    |
| Correct TOTP submitted                               | Auth success                 |
| Incorrect TOTP submitted                             | Auth fail                    |
| Expired MFA hash                                     | Auth fail                    |
| REST login on a locked account                       | 429 `LOCKED`, password not checked (`RestLoginPolicyShould`) |
| N failed logins across identity replicas           | Account locked at the one threshold (`RestLoginPolicyShould`) |
| Parallel REST guesses at one username                | At most `MaxFailedLoginAttempts` BCrypt verifies (`RestLoginPolicyShould`) |
| Unknown vs known username over REST                  | Same verify, same failure write, same answer (`RestLoginPolicyShould`) |
| REST MFA codes past the hash's or username's limit   | Hash deleted / 429 `LOCKED` (`RestMfaVerifyShould`) |
| Two parallel verifies of one hash                    | One wins (`RestMfaCodeShould`) |
| MFA setup or PAT mint without the current password   | 401, nothing issued (`SensitiveActionReauthenticationShould`) |
| PAT minted before a password change or an MFA reset  | Refused afterwards (`CredentialRevocationShould`) |
| Ban and lock landing during an API account write     | Both survive (`ApiWriteRaceShould`) |
| Banned, deactivated or consolidating account on a game route | 403 naming it after a current proof; generic before it or with a stale one (`AvalonGameAuthorizationShould`) |
| Ban lifted while a game context is live              | The context stays void: the ban moved the epoch (`AccountStatusChangeShould`) |
| Password lock while a player is in the world         | Reservation, activation and heartbeat still renew the session (`GameSessionFenceServiceShould`, `GameSessionRenewalShould`) |
| X-Forwarded-For from a trusted / untrusted peer      | Source is the client / the peer, warned once a minute (`ForwardedHeadersShould`) |
| Replayed right MFA code                              | Refused, hash kept, not counted (`TotpReplayShould`, `RestMfaVerifyShould`) |
| Locked account at login                              | One dummy BCrypt verify, as an unknown username (`RestLoginPolicyShould`) |
| Caller with no peer address                          | 400 on budget-spending endpoints (`AddressLessCallerShould`) |
