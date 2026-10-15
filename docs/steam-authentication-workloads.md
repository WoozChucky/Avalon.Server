# Game server admission configuration

The game admission API accepts an opaque game context for world listing and issues a server-specific join ticket only while its source-bound game license remains current. Store contexts also require current verified identity evidence. Tickets last at most 30 seconds, are bound to the selected world and context, and may be redeemed by one authenticated server/connection. Exact retries recover the original receipt; a pending receipt cannot admit gameplay until the durable save barriers are advanced.

## Shared licensing and rollout

Steam ownership and stored Avalon grants use the same `GameLicenses`, `ExternalIdentities` and
`LicenseObservations` model. Provider identifiers are registered strings. Contexts bind the trusted
application key, license ID and authority revision; a role, linked identity or historical ownership
observation cannot grant gameplay. Authorization lasts at most five minutes, bounded further by
license expiry, store identity validity and absolute context expiry. Revocation invalidates the
revision, including identical cached handoff/refresh/join retries. An unavailable provider or
database cannot extend authorization or substitute another source.

The Avalon route redeems the existing one-use launcher handoff and keeps that account and launcher
refresh family. An active `avalon` stored grant authorizes it without a Steam SDK/API call. Otherwise
the context stays `PendingLicense`; `Check again` refreshes that same context, allowing trusted
fulfillment to add its first grant without replaying the spent handoff. Revoked bound grants require
a fresh context. Steam main `2499460` retains account/world-role checks; Playtest `2514590` remains
restricted to PTR world 3 without permanently promoting the account's role.

Coordinate the updated server/client cutover; older store HTTP formats are not supported. The generated Auth migrations are
`20261005143248_SharedGameLicenses` and `20261005151122_SharedStoreProvenance`. They preserve historical
observations and label existing store account/consolidation provenance `steam`; they do not create
licenses from historical ownership. Review and apply them through the normal user-controlled
rollout before testing. API startup applies migrations, so do not launch this branch against
production merely to inspect it. Old cached contexts missing the shared binding require sign-in;
fresh Steam verification establishes bounded evidence. Retire `/client/auth/attempts` and
`/client/auth/store/steam`; every route uses `provider-attempts`, followed by common `store/proof` or
native `handoffs/redeem`. Launcher stdin handoff is preserved. New automatic store accounts still receive the Player role, which is
not a license or general development-world access grant.

Later live acceptance requires explicit test grants and a coordinated server rollout: verify the
launcher enters the same account's worlds, missing license blocks admission, `Check again` detects
fulfillment, revocation blocks retries and heartbeat renewal, provider outages add no time, and
Playtest remains PTR-only. Unit tests use fixtures; this slice creates no live grants, applies no
production migrations and performs no deployment or Steam upload.

Adding a store requires registered identity/license adapters, trusted application configuration and
client SDK/proof packaging, not another license table. Generic `provider-attempts` and `store/proof`
endpoints are the only store wire format; there are no legacy Steam wrappers. Epic SDK/ownership verification and
Stripe checkout/webhooks remain deferred. Stripe fulfillment will create `avalon` stored grants;
Stripe is not a game identity provider.

## Deployment bindings

Configure one workload for each served world under `Application:GameWorkloads:Servers`. Each entry supplies `ServerId`, `WorldId`, `TlsServerName`, `TlsCertificateSha256`, and `ClientCertificateSha256`. Digests are 64 hexadecimal characters: SHA-256 of the complete DER leaf certificate, not the certificate thumbprint property. `TlsServerName` must match the world server certificate's DNS/IP identity. The world host/port and existing access, maintenance, database availability, readiness and version information come from the world repository.

The world process holds the private key for its client certificate. Its leaf must be current and have the TLS client authentication EKU (`1.3.6.1.5.5.7.3.2`). The API maps its pinned leaf to exactly one server ID. Never distribute a workload private key or publisher key to the game client.

Game admission and this listener are the REST API's identity service (`Avalon.Api.Identity`, [API services](api-services.md)): a process without identity maps no `/internal/game/*` route, and the chart renders the listener's settings only into a release that runs identity. In a process that runs identity, `/internal/game/*` exists on this listener only: a request for it that arrives on any other port, the public one behind the ingress included, is answered 404 before it is authenticated, judged by the port the connection was accepted on (`GameInternalRoutes`). Configure `Kestrel:Endpoints:GameInternal:Url` as a direct HTTPS listener, with its API server certificate supplied through the usual Kestrel certificate configuration/secret mount. It requires a TLS client certificate during the handshake. Keep this port reachable by world servers through direct TLS or TLS passthrough. Also configure the public API listener explicitly: named Kestrel endpoints replace `ASPNETCORE_URLS`. Preserve its existing HTTPS/ingress setup. A TLS-terminating proxy and forwarded certificate headers cannot authenticate a workload.

Example structure (replace every placeholder through deployment configuration):

```json
{
  "Application": {
    "GameWorkloads": {
      "Servers": [
        {
          "ServerId": "world-1",
          "WorldId": 1,
          "TlsServerName": "world-1.example.test",
          "TlsCertificateSha256": "<64 hex characters from the world TLS leaf>",
          "ClientCertificateSha256": "<64 hex characters from the world's client-auth leaf>"
        }
      ]
    }
  },
  "Kestrel": {
    "Endpoints": {
      "Public": { "Url": "http://0.0.0.0:8080" },
      "GameInternal": {
        "Url": "https://0.0.0.0:9443",
        "Certificate": { "Path": "/run/secrets/api-workload.pfx" }
      }
    }
  }
}
```

A local run gets every one of these settings, with throwaway self-signed leaves for `localhost`, from the Aspire AppHost or from `tools/Avalon.LocalDev setup` ([Development setup](development-setup.md#from-clone-to-client-in-world)).

Use a secret configuration provider for certificate passwords. Certificate pins and server assignments are a startup snapshot; deploy new assignments and restart the API process that runs identity for rotation. An empty workload assignment grants no world allocation or internal authentication. It is not a bypass or an optional authentication mode.

The internal endpoint ignores player/launcher JWTs and server IDs in headers or request bodies. Its authentication policy is separate from account role authorization. The pinned leaf is the deployment trust anchor, so private leaves can be used without installing a machine-wide trust root. TLS verifies private-key possession; the API also verifies pin, validity and EKU on every request. A client certificate the listener refuses in the handshake is logged as a Warning (`RefusedWorkloadCertificateLog`, #1051: at most one a minute for each configured server's pin, and one a minute shared by every unknown pin, each with the count of those left out since, so junk handshakes cannot hide a real world's refusal) naming why (outside its validity period, without the client authentication usage, or assigned to no game server) and the leaf's SHA-256 pin, the value `ClientCertificateSha256` holds, never the certificate: Kestrel reports the failed handshake at Debug only, which production does not write.

Kestrel listener configuration follows [Microsoft's endpoint documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0).

## Session activation and durable save authority

Redeeming a join ticket only reserves a pending session. The world must call `POST /internal/game/sessions/activate` over the workload mTLS connection and receive an `active` reply before accepting gameplay. Activation first advances the prior world's save barrier, then the target world's barrier, then activates the Auth-DB head and target guard. A failure leaves a resumable pending transition; retries retain the same session and fencing token. Until that pending session's lease (45 seconds from the redemption) runs out, it holds the account: a new join ticket is refused `ACTIVE_GAME_SESSION` (409) unless the player confirms the takeover, which replaces a pending session as it does an active one (#1013); a reconnect ticket replaces only an active one.

**A failed admission ends the session it reserved (#1013).** When admission fails after the redemption (the activation refused or unanswered after its retries, its transport failed, or the world could not trust its answer), the world ends the session the receipt reserved before it replies `ServiceUnavailable`: one `sessions/end` call with the receipt's account, game session id and fencing token (`GameAdmissionClient`), off the tick in the admission's own work, waiting at most 2 seconds on its own timer, so the player's next join is not refused for the rest of that session's lease. It is best-effort: a world that crashed, or an end that failed, leaves the pending session to run out, and a confirmed takeover replaces it meanwhile. A failure before a receipt the world can check (the redemption refused, lost or for another connection) has nothing it may end. The replaced admission cannot come back: its activation is refused because the head no longer holds its session id and fencing token, the target guard refuses its older token once the replacement has advanced it, and a guard it reached only as pending never let it write.

The activation, heartbeat (`POST /internal/game/sessions/heartbeat`) and end (`POST /internal/game/sessions/end`) bodies contain canonical string `AccountId`, GUID `GameSessionId` and canonical positive string `FencingToken`. The workload identity comes from the TLS certificate. No request body or player JWT chooses a server identity.

Heartbeat every 15 seconds and stop gameplay by the granted `LeaseUntil`, even when a renewal request or response is lost. Renewal is capped at 45 seconds and the current ownership/proof/context deadlines. Both the Auth-DB session and Character-DB guard must renew before an active reply is returned. An expired guard cannot be resurrected by a heartbeat. A password lock on the account is not a refusal (#882), neither in the checks nor in the account-row hold the reservation, activation and renewal writes take (`AccountRepository.HoldGameAuthorityAsync`): it guards the password steps only, so a guesser cannot refuse a join or end a session; a ban, a deactivation or a consolidation is, and a ban or deactivation also moves the account's session epoch. A heartbeat or activation that meets a database outage while checking the session's game context (its account or license unreadable) is answered 503 `SERVICE_UNAVAILABLE`, never `SESSION_REVOKED`: the world keeps its current lease, asks again, and still stops at `LeaseUntil` if the outage outlasts it. The workload rate limit is separate from player limits (`Application:RateLimiting:WorkloadPermitsPerMinute`, default 16384); size it for four heartbeat requests per minute per concurrent player plus admission and cleanup traffic.

**Which clock decides a lease's end (#1009).** Three clocks take part: the API's (`TimeProvider`), which works out a session's end and checks the Auth-DB session against it; each world's characters database's (`clock_timestamp()`), which checks the guard and every save against the guard's end; and the world's, which runs the lease it is given (#977). An end worked out on one clock is never refused on another for lying too far ahead. The API asks the target guard for the earlier of 45 seconds on its own clock, the license end and the context deadline; the guard holds the earlier of that and 45 seconds past the database's own time, and answers the end it holds (`GameplayFenceRepository`). Activation activates the Auth-DB session with that end, and activation and heartbeat give the world the earlier of the session's end and the guard's, so the world is never given a lease that outlasts the guard its final save runs under. No clock can lengthen a lease past 45 seconds on any of them; a skewed clock only shortens it, by the skew (an API clock 2 seconds ahead of the database's gives 43-second leases, harmless while heartbeats every 15 seconds renew them). A heartbeat renews the Auth-DB session first, with the API's end, so the session may outlast the world's lease by the skew; that only holds a new join back as long. Before #1009 the guard refused any end more than 45 seconds past its own time, so an API clock ahead of the database's by more than the milliseconds between the two reads refused every activation (`BARRIER_PENDING`, the world's `ServiceUnavailable`) and every guard renewal; Postgres in Docker Desktop drifts by seconds against the Windows host, which made local admission fail at random. Two clock assumptions remain. The five-second save margin assumes the world's clock and the database's agree to well within it: a world behind its database by more than the margin less a save's time has its final save refused by the guard (the safe side: nothing is written without authority). And the world refuses a granted lease that ends more than 45 seconds past its own time (`GameSessionLease`), so a world whose clock runs behind both the API's and the database's by more than an activation's round trip refuses admission, and refuses a renewal, which ends the lease.

At load, bind the character entity to the immutable account/session/fence admitted by the world. Snapshots retain that authority through the save queue and despawn; replacement connections cannot relabel an old entity. Character saves acquire durable account guards inside their transaction, validate current ownership and lease expiry after lock acquisition, and recheck the deadline before commit. Multi-character transactions acquire account guards in ascending account order and fail atomically if any writer is stale.

Flush authoritative state before ending the session. End blocks the matching Character-DB guard before ending the Auth-DB head and remains available after context revocation. An old end request cannot block or end a replacement session. Do not use the presence/online flag as gameplay authority.

**A world sends identity a bounded number of calls at once (#1051).** Admissions run on `WorldDatabaseWork.Admission` (32 at once, 128 waiting, past that refused: the admission answers `ServiceUnavailable`) and heartbeats on their own lane (`WorldDatabaseWork.Heartbeat`, 128 at once, below). Each connection's drain (`WorldServer.DrainGameplayAsync`: a logout, a lease that ran out, a stop) sends its end, after the character's final save, through `WorldDatabaseWork.SessionEnds`: 16 at once (`SessionEndConcurrency`), the rest queued on the world and never refused, since a refused end would hold the account until the lease runs out. A stop drains every connection together, so before #1051 a world with about 1,300 players sent identity about 1,300 ends at once: each opened its own mutual-TLS connection to the workload listener (the world's handler has no connection limit) and waited for the auth and characters database pools (an end is one read and one write on the auth database and a transaction of seven round trips on the characters database's guard); identity answered them in 3.6 s each, against about 5 ms alone, and ran out of memory. At 16 at once, 1,300 ends take well under a second at identity's usual speed. An end waiting for its turn holds the lease and a flag, never the connection. It decides at the front of the lane whether to send at all (`WorldServer.EndOrSkipAsync`, counted in `world.session_ends.skipped`, [instrumentation](instrumentation.md)):

- **Not after the stop's deadline.** A stop gives its ends the host's stop timeout (`World:Shutdown:DrainTime` + `SaveMargin`, 15 s before the pod's grace period ends): past it the end in flight is cancelled and those still waiting are not sent, with one Warning giving their count, so an identity that does not answer cannot hold the stop past the grace period. Every save still comes before its end and is waited for as before.
- **Not for a lease long run out.** A lease that ran out more than 5 s ago (`WorldServer.SessionEndSkew`, the clocks' skew) on both the world's clocks (`GameSessionLease.HasRunOutBy`: its end on the wall clock, and the time granted counted from the grant on the monotonic one, so a wall clock set forward cannot drop an end) is not ended: identity's session and the characters database's guard ran out with it, and an expired session holds nothing an end would release. A join, a reconnect, a takeover, a consolidation and the load-test account delete all read the session's or the guard's lease end, a heartbeat or an activation cannot renew one that ran out, a save is refused by the guard's own end, and the next session's activation advances the guard over it as over an ended one. The exception is a session whose renewals the world cannot account for (`RenewalLedger`): a heartbeat in flight at the drain, or, for the rest of the session, any heartbeat that ended without its renewal applied to the lease (an error, a transport failure or timeout, a reply the lease refused). Identity may have renewed the session past the world's lease, and a request identity still holds may renew it later, after a later heartbeat was applied, so such a session's end is always sent; an end blocks the guard and ends the session, which no later renewal undoes. The skew assumption: a world clock ahead of identity's by more than 5 s, or a characters database clock behind identity's by more (the world's lease is the earlier of identity's session end and the guard's), leaves identity's session live after its end was skipped for the rest of that difference, which only holds a join without a takeover back as long.

Every session not ended runs out with its lease (at most 45 seconds), as after a crash.

A session that never became active ends too (#1013), for the world workload that holds its id and fencing token, as for any end. Its activation may have stopped before the target guard: the guard then still holds an older session (or none) and grants this one nothing, so the end leaves it as it is (the previous session's guard keeps its state) and expires the Auth-DB head only while it is still pending (`GameSessionRepository.TryExpirePendingAsync`), then blocks the guard if an activation still in flight reached it meanwhile, so that activation cannot complete there. If an activation in flight made the head active between the end's read of the guard and its write, the expiry changes nothing and the end falls back to the active order: the guard blocked first, then the head ended (`BARRIER_PENDING` while the guard refuses). An active session's guard always holds its token (activation advances the guard before the head activates), so its end still fails `BARRIER_PENDING` until the guard is blocked. A pending session is not marked `Ended`: it stays `Pending` with its lease end set to a fixed point in the past (the Unix epoch), not the ending replica's time, so no API replica, however far its clock lags, can still activate it; the next reservation keeps the last active session as the ancestor whose world's save barrier it advances, as after a pending reservation that ran out by itself. A reconnect ticket never replaces a pending session, even one that also asks for a takeover: the reservation carries whether it is a reconnect.


## World transport and coordinated protocol cutover

The world host requires a current TLS leaf with its private key before it listens. Configure
`Hosting:Security:CertificatePath` and its secret password. `World:Admission:ApiUrl` is the fixed
HTTPS origin of the API's GameInternal listener. `World:Admission:ApiCertificateSha256` is mandatory:
SHA-256 of the complete DER API server leaf, exactly 64 hexadecimal characters. This explicit leaf
is the private trust anchor; there is no system-CA fallback. TLS still checks the origin hostname,
leaf validity, server-auth EKU and digital-signature key usage. Only chain errors are allowed for the
exact pinned leaf; name mismatch, unavailable certificate, missing usage, wrong pin and expiry fail.
Every request also checks the cached leaf validity, including an already pooled TLS connection. `World:Admission:ServerId` matches the API assignment for `Game:WorldId`.
`World:Admission:ClientCertificatePath` and its secret password supply the pinned client-auth PFX.
Never share the world TLS private key with clients; clients receive only the assigned leaf SHA-256
pin and TLS server name from the authenticated join response.

The world chart requires `server.transport.existingSecret`, `server.admission.apiUrl`, and
`server.admission.serverId`, `server.admission.apiCertificateSecret`, and
`server.admission.apiCertificateKey` (default `api-tls-sha256`). The latter Secret/key supplies the API
leaf pin; no optional reference or unpinned fallback is available. The transport Secret holds `world-tls.pfx` and `workload.pfx`; optional
`world-tls-password` and `workload-password` keys provide their passwords. PFX files mount read-only
under `/run/avalon-auth`. These are deployment credentials; only SOPS-encrypted copies belong in source control.

Updated clients use CGameAdmissionPacket/SGameAdmissionPacket (0x201E/0x301E) inside TLS, then the
version handshake, sealed only on a world with `Network:PacketEncryption` on (a world with it off exchanges no key,
#878). Minimum supported gameplay client version is 0.3.0. Retired
TCP handoff, world-select and world-key opcodes 0x200F, 0x201B/0x301B and 0x201C/0x301C are unassigned.
The launcher handoff issuer and inherited-stdin contract remain; updated clients redeem the handoff
through `/client/auth` for a restricted context and separately request licensed world admission.
Roll out API, world, schema and supported client together; do not deploy this intermediate backend
branch while clients still use the retired protocol.

Provider calls, redemption, heartbeat and character persistence run outside the simulation tick.
Connection identity, role, session and fence bind once from admission. Every queued gameplay packet
is checked again at dispatch. A renewal outage grants no additional time: the world stops mutations
five seconds before the lease deadline to flush final state, then ends the session.

The world checks every connection's lease once per tick, first thing (`WorldConnection.AdvanceGameplayLease`),
against the tick's time: the container's clock read once for every connection (`TickTime`, #977). The lease holds
two due times set when it is granted or renewed, the deadline less the five seconds on the wall clock and the
time granted less the five seconds on the monotonic one, and the check ends gameplay on the first tick at or past
either (`GameSessionLease.DrainDueAt`), so a wall clock set back cannot stretch a lease. The heartbeat is due 15
seconds after the last one by the same time. Heartbeats run on a work lane of their own (`WorldDatabaseWork.Heartbeat`,
#1050): 128 at once and 8,192 outstanding, beside the admissions' 32 and 128. A connection has at most one heartbeat in
flight and only an admitted one has any, so the outstanding bound is above any player count a world carries, and 128 at
once keep up with 5,000 players (about 333 heartbeats a second) while one takes up to about 380 ms. A heartbeat the
lane refuses is not retried before the next 15 seconds, so three refusals in a row end the lease: on the shared lane a
reconnect storm, or a flood of well-formed tickets, that filled it could drain players already in the world.

**Calls to identity per world, and the latency a world can carry (#1050, owner decision).** At its peak a world has
admissions 32 + heartbeats 128 + session ends 16 (`WorldDatabaseWork.SessionEnds`, #1051, above) = 176 requests in
flight to identity's workload listener at most. The workload handler (`WorkloadHttpHandler`) does not cap
connections per server, so each can be its own mTLS connection: a cap would queue requests inside the handler, where
they would spend the five-second transport timeout waiting rather than running. Size identity for that peak per world.
What heartbeats can carry follows from the lane: 128 at once, one per player every 15 s.

| Identity's time per heartbeat, `L` | 5,000 players |
|---|---|
| up to `128 × 15 / players` s (about 384 ms) | every lease renewed on time, every 15 s |
| up to `128 × 40 / players` s (about 1.0 s) | renewals fall behind, each player's every `players × L / 128` s, still inside the lease's 40 s (45 s less the five-second drain) |
| above that | renewals arrive after the drain point: players are drained, a few at a time, until the rest fit |

So at 5,000 players a world keeps its leases while identity answers a heartbeat in about a second; at 2,500, in about
two. A heartbeat that times out (five seconds) keeps the lease as it is and holds its slot for the whole timeout, which
at 5,000 players is past the limit. The dispatch check that follows in the same tick (each pass, each
packet) judges the lease at the time that check was made, not by reading the clock again: a lease the check found
not due is only asked whether it has been revoked since (`GameSessionLease.IsRevoked`, no lock), so a revocation
still takes effect at the next packet. The check has already ended a lease within five seconds of its end, so this
differs from a fresh read only in a tick that runs longer than five seconds, and the tick closes that too: when its
session pass has run past the five seconds since the time was read, it reads the time again and checks every lease
at it before the map pass dispatches anything, so a lease that came due during that pass ends there. What is left is
a single pass longer than five seconds, whose later packets are judged at that pass's start (and the save that
follows is refused by the database's own lease check). The read loop's check of a packet as it arrives reads the clock. Character
creation and its initial children commit atomically under the same durable account guard; select,
update and delete also validate the current owner and lease inside their database transaction.


## API chart and homelab configuration

A release of the API chart that runs identity (one that names no `services` runs all four) requires
`storeAuthentication.existingSecret` (key `steam-publisher-key` by default),
`gameAdmission.tlsExistingSecret` (`tls.pfx`, optional `tls-password`), and
`gameAdmission.bindingsExistingSecret`; a release without identity renders none of them, and no port 9443. `gameAdmission.servers` supplies each `serverId`, `worldId`
and `tlsServerName`; the binding Secret has `<serverId>-tls-sha256` and `<serverId>-client-sha256`.
The chart mounts the API PFX read-only and exposes a separate direct mTLS service port (default 9443).
Publisher keys are only Secret references; there is no plaintext Helm publisher-key setting.
Missing required references/assignments refuse chart rendering.

The sibling homelab repository prepares these settings for worlds 1, 2 and 3, plus the production
Steam OpenID callback and website origin, on `feature/steam-authentication`. Its existing release
versions remain pinned pending coordinated staging. See homelab `docs/avalon.md` for Secret keys,
certificate DNS/trust requirements, rotation and cutover instructions. The authorized follow-up
created SOPS-encrypted publisher-key, seven independent private leaves, PFX passwords and all
matching leaf pins using homelab's existing age recipient. No machine trust roots or live workloads
were changed. Real provider and coordinated staging checks remain required.
