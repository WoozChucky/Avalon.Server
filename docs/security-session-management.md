# Security — Session & Authentication Management

This document covers the authentication pipeline, how world entry is secured, and the MFA flow for the Avalon server.

---

## Authentication Flow

```
Game Client                Auth Server                          Redis
    |                          |                                  |
    | CRequestServerInfoPacket |                                  |
    |------------------------->| version check                    |
    | SServerInfoPacket        |                                  |
    | (version + public key)   |                                  |
    |<-------------------------|                                  |
    | CClientInfoPacket        |                                  |
    | (client public key)      |                                  |
    |------------------------->| session crypto                   |
    | SHandshakePacket         |                                  |
    |<-------------------------|                                  |
    | CHandshakePacket         |                                  |
    |------------------------->| handshake data checked           |
    | SHandshakeResultPacket   |                                  |
    |<-------------------------|                                  |
    | CAuthPacket              |                                  |
    | (user + password)        |                                  |
    |------------------------->| source and username budgets      |
    |                          |--------------------------------->|
    |                          | BCrypt verify, status            |
    |                          | [MFA confirmed?]                 |
    |                          | --- yes: ephemeral hash          |
    |                          |--------------------------------->|
    | SAuthResultPacket        |                                  |
    | (MFA_REQUIRED + hash)    |                                  |
    |<-------------------------|                                  |
    | CMFAVerifyPacket         |                                  |
    | (hash + code)            |                                  |
    |------------------------->| attempt count, TOTP, spend hash  |
    |                          |--------------------------------->|
    | SAuthResultPacket        |                                  |
    | (SUCCESS + account id)   |                                  |
    |<-------------------------|                                  |
    | CWorldListPacket         |                                  |
    |------------------------->| PostLoginGuard, readiness        |
    | SWorldListPacket         |                                  |
    |<-------------------------|                                  |
```

An account without confirmed MFA gets `SUCCESS` straight from `CAuthPacket`. The auth server's part ends there: world
entry is the REST game admission, below.

---

## World Entry

The auth server no longer issues world keys. The TCP world select, the world-key exchange and the `SETNX` duplicate
session mutex were replaced by the TLS join-ticket admission of the REST API's identity service, which also replaced
their guarantees:

- **One-time, short-lived entry.** A join ticket (`POST /game/join-tickets`) is a random token kept in Redis only as
  its digest, bound to the game context, the world, the character and the world server, valid for at most 30 seconds
  and redeemed once, by the world server it names, over that server's mutually authenticated workload connection.
- **One session per account.** Redeeming a ticket reserves the account's game session with a fencing token; a live
  session elsewhere refuses a new ticket (`ActiveGameSession`) unless the client confirms the takeover, and every
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

[Auth server: World entry](auth-server.md#world-entry) has the client's steps, and
[game server admission](steam-authentication-workloads.md) the session lease, heartbeat and save fencing.

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

- Ephemeral hash TTL: 2 minutes.
- The hash is single-use, and the delete decides who used it (#478): once a right code's step is
  accepted, it spends the hash with `IMFAHashService.TryConsumeAsync`, and only the caller whose `DEL`
  removed the reverse key goes on. Two verifies sent together with one hash can no longer both win.
- The step is accepted before the hash is spent, so a replayed code (right, but already used) is refused
  without spending the hash, and the owner's own code can still finish the login. A replay, and a right
  code that lost the hash to another verify, are not failed logins: their budget slots come back and the
  row is not counted. A replay also gives back the attempt it counted on the hash, which is safe because
  only a right code can be a replay. A code that lost the hash gives no attempt back: the hash is gone,
  and the account's `:mfa` key may already hold the next login's.
- Each hash allows `MaxFailedMfaAttempts` codes (counted before the code is checked); the last wrong code
  deletes it. Each code also spends its source's and its account's username budgets, and the account's
  lock is checked before the code. TOTP codes are accepted within ±1 step, each step once
  (`LastAcceptedTotpStep`, #471).
- Resetting MFA with the recovery codes deletes the row and revokes every refresh token and personal
  access token the account holds, in one transaction (`MfaSetupRepository.ResetConfirmedAsync`, #483),
  then publishes the account on `world:accounts:disconnect` (best-effort). Both servers share this, in
  `MFAService.ResetMFAAsync`. The REST reset (`POST /mfa/reset`) returns 204 and enrols nothing: the
  recovery codes are not the password, so enrolling again is `POST /mfa/setup`, which asks for it.
- Recovery codes (`MFARecoveryCodes`): three per account, each 80 bits from `ISecureRandom`, shown as
  16 Crockford base32 characters (`XXXX-XXXX-XXXX-XXXX`). They are generated at confirm and returned
  only in that response; the `MfaSetups` row stores only the SHA-256 of each canonical code (upper
  case, no separators). Reset hashes each input and compares with `CryptographicOperations.FixedTimeEquals`,
  and a successful reset deletes the row, which consumes the codes. A stored value that is not a
  32-byte hash, such as a plaintext code written before #464, never verifies.
- Failed logins and codes are counted and locked by the shared login policy described under
  [Login policy](#login-policy-shared-by-both-servers).

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

### Login policy (shared by both servers)

The game client's TCP login and the REST API's login run one policy (#478), in
`Avalon.Infrastructure.Login`: `PasswordLoginPolicy` for the password step, `MfaLoginPolicy` for the code
step, over `SourceBudget`, `UsernameBudget` and `AttemptBudget`. `CAuthHandler`/`CMFAVerifyHandler` and
`AccountService.Authenticate`/`MFAController.VerifyMFA` answer its outcomes in their own protocol.

| Step | Password (`POST /account/authenticate`, `CAuthPacket`) | Code (`POST /mfa/verify`, `CMFAVerifyPacket`) |
|---|---|---|
| 1 | Source budget slot (`auth:source:{source}:failedLogins`) | Source budget slot |
| 2 | Username budget slot (`auth:username:{sha256}:failedLogins`), before the lookup | Hash attempt count; past `MaxFailedMfaAttempts` the hash is deleted |
| 3 | Lookup; an unknown username pays one dummy BCrypt verify | Account lookup, then its username budget slot |
| 4 | Account row lock (`Locked`/`LockedUntil`), before the password; a locked row still pays the dummy verify | Account row lock, before the code |
| 5 | BCrypt verify | TOTP ±1 step, each step once, one winner per hash |
| 6 | Status: Banned/Deactivated refused, only now | Status re-checked |
| 7 | Login written by column, refused if the row locked meanwhile | The same |
| 8 | Completed: source slot back, username count cleared | The same |

Both servers use the **same keys**. The per-username key is the account lock, so a guess over REST and one
over TCP spend one budget, and N failures across both lock the account at the one threshold. The
per-source key is shared too: the budget is "attempts from one address, across every account", and a
guesser who splits its guesses between the two servers must not get the budget twice. The TCP server keys
on the connection's address, the API on `HttpContext.Connection.RemoteIpAddress` after
`UseForwardedHeaders`; both reduce it with `RemoteAddress.SourceOf` (IPv4 address, IPv6 /64).

**Behind a proxy, the API's source is the proxy** unless the proxy is trusted. `UseAvalonForwardedHeaders`
believes `X-Forwarded-For` from loopback and from `Application:ForwardedHeaders:KnownProxies` and
`KnownNetworks` only (see [Configuration Reference](configuration-reference.md#rest-api-forwarded-headers)).
With the ingress missing from those, every REST caller behind it is one source, and ten failures in fifteen
minutes refuse REST logins for everyone until the window ends. So outside Development the API warns at
startup when neither is set, and it logs, at most once a minute, a header sent by an untrusted peer, since
that is either a proxy missing from the list or a caller trying to choose its own source. A trusted network that
is wider than /8 (IPv4) or /32 (IPv6), `0.0.0.0/0` and `::/0` included, is refused at startup. A request with no
peer address has its `X-Forwarded-*` headers removed before they are read, so it cannot name an address
and escape the 400 below.

A caller with no peer address at all (a non-IP transport) is refused with 400 on every endpoint that spends
a source budget. Before, it was given `IPAddress.None`, so all such callers shared one budget.

On REST, a refusal by a budget or a locked row is **429** ProblemDetails with `Detail` `LOCKED`, the same for
every username; the failure in the budget's last slot is answered the same way. A wrong password or code
is the generic 401. The 403 `BANNED`/`DEACTIVATED` answer is given only once the password (or code) is
proved (#480). HTTP cannot answer before writing, as the TCP login does, so an unknown username runs the
same failure statements against an id no account has (`LoginPolicy.NoAccount`): known and unknown usernames
take the same path. A REST login never sets `Online`, which is the game client's session flag.

The limits are configured on both hosts and **must match**: the Auth server's `Application:*` and the
API's `Application:Authentication:*` (`MaxFailedLoginAttempts`, `LockoutDurationMinutes`,
`MaxFailedLoginsPerSource`, `FailedLoginSourceWindowMinutes`, `MaxFailedMfaAttempts`). Each host logs its five at
Information when it starts, so a drift shows by comparing the two lines.

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
- The publish is best-effort: the change is committed, so a Redis failure is logged and the call succeeds.

The access JWT in use is not revoked by any of these; it lives out its `AccessTokenLifetimeMinutes`.

### Account writes

The API never writes back an account row it read (#478, as the Auth server since #484). A login and an MFA
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
| Account with confirmed MFA                           | `MFA_REQUIRED` response      |
| Account without MFA                                  | `SUCCESS`                    |
| Correct TOTP submitted                               | Auth success                 |
| Incorrect TOTP submitted                             | Auth fail                    |
| Expired MFA hash                                     | Auth fail                    |
| REST login on a locked account                       | 429 `LOCKED`, password not checked (`RestLoginPolicyShould`) |
| N failed logins over REST and TCP together           | Account locked at the one threshold (`RestLoginPolicyShould`) |
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
| Replayed right MFA code                              | Refused, hash kept, not counted (`TotpReplayShould`, `CMFAVerifyHandlerShould`, `RestMfaVerifyShould`) |
| Locked account at login                              | One dummy BCrypt verify, as an unknown username (`CAuthHandlerShould`, `RestLoginPolicyShould`) |
| Caller with no peer address                          | 400 on budget-spending endpoints (`AddressLessCallerShould`) |
