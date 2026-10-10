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

The internal endpoint ignores player/launcher JWTs and server IDs in headers or request bodies. Its authentication policy is separate from account role authorization. The pinned leaf is the deployment trust anchor, so private leaves can be used without installing a machine-wide trust root. TLS verifies private-key possession; the API also verifies pin, validity and EKU on every request.

Kestrel listener configuration follows [Microsoft's endpoint documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0).

## Session activation and durable save authority

Redeeming a join ticket only reserves a pending session. The world must call `POST /internal/game/sessions/activate` over the workload mTLS connection and receive an `active` reply before accepting gameplay. Activation first advances the prior world's save barrier, then the target world's barrier, then activates the Auth-DB head and target guard. A failure leaves a resumable pending transition; retries retain the same session and fencing token.

The activation, heartbeat (`POST /internal/game/sessions/heartbeat`) and end (`POST /internal/game/sessions/end`) bodies contain canonical string `AccountId`, GUID `GameSessionId` and canonical positive string `FencingToken`. The workload identity comes from the TLS certificate. No request body or player JWT chooses a server identity.

Heartbeat every 15 seconds and stop gameplay by the granted `LeaseUntil`, even when a renewal request or response is lost. Renewal is capped at 45 seconds and the current ownership/proof/context deadlines. Both the Auth-DB session and Character-DB guard must renew before an active reply is returned. An expired guard cannot be resurrected by a heartbeat. A password lock on the account is not a refusal (#882): it guards the password steps only, so a guesser cannot end a session; a ban, a deactivation or a consolidation is, and a ban or deactivation also moves the account's session epoch. A heartbeat or activation that meets a database outage while checking the session's game context (its account or license unreadable) is answered 503 `SERVICE_UNAVAILABLE`, never `SESSION_REVOKED`: the world keeps its current lease, asks again, and still stops at `LeaseUntil` if the outage outlasts it. The workload rate limit is separate from player limits (`Application:RateLimiting:WorkloadPermitsPerMinute`, default 16384); size it for four heartbeat requests per minute per concurrent player plus admission and cleanup traffic.

At load, bind the character entity to the immutable account/session/fence admitted by the world. Snapshots retain that authority through the save queue and despawn; replacement connections cannot relabel an old entity. Character saves acquire durable account guards inside their transaction, validate current ownership and lease expiry after lock acquisition, and recheck the deadline before commit. Multi-character transactions acquire account guards in ascending account order and fail atomically if any writer is stale.

Flush authoritative state before ending the session. End blocks the matching Character-DB guard before ending the Auth-DB head and remains available after context revocation. An old end request cannot block or end a replacement session. Do not use the presence/online flag as gameplay authority.


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
existing encrypted version handshake. Minimum supported gameplay client version is 0.2.0. Retired
TCP handoff, world-select and world-key opcodes 0x200F, 0x201B/0x301B and 0x201C/0x301C are unassigned.
The launcher handoff issuer and inherited-stdin contract remain; updated clients redeem the handoff
through `/client/auth` for a restricted context and separately request licensed world admission.
Roll out API, world, schema and supported client together; do not deploy this intermediate backend
branch while clients still use the retired protocol.

Provider calls, redemption, heartbeat and character persistence run outside the simulation tick.
Connection identity, role, session and fence bind once from admission. Every queued gameplay packet
is checked again at dispatch. A renewal outage grants no additional time: the world stops mutations
five seconds before the lease deadline to flush final state, then ends the session. Character
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
