# Game server admission configuration

The game admission API accepts an opaque game context for world listing and issues a server-specific join ticket only while Steam ownership and identity proof remain current. Tickets last at most 30 seconds, are bound to the selected world and context, and may be redeemed by one authenticated server/connection. Exact retries recover the original receipt; a pending receipt cannot admit gameplay until the durable save barriers are advanced.

## Deployment bindings

Configure one workload for each served world under `Application:GameWorkloads:Servers`. Each entry supplies `ServerId`, `WorldId`, `TlsServerName`, `TlsCertificateSha256`, and `ClientCertificateSha256`. Digests are 64 hexadecimal characters: SHA-256 of the complete DER leaf certificate, not the certificate thumbprint property. `TlsServerName` must match the world server certificate's DNS/IP identity. The world host/port and existing access, maintenance, database availability, readiness and version information come from the world repository.

The world process holds the private key for its client certificate. Its leaf must be current and have the TLS client authentication EKU (`1.3.6.1.5.5.7.3.2`). The API maps its pinned leaf to exactly one server ID. Never distribute a workload private key or publisher key to the game client.

Configure `Kestrel:Endpoints:GameInternal:Url` as a direct HTTPS listener, with its API server certificate supplied through the usual Kestrel certificate configuration/secret mount. It requires a TLS client certificate during the handshake. Keep this port reachable by world servers through direct TLS or TLS passthrough. Also configure the public API listener explicitly: named Kestrel endpoints replace `ASPNETCORE_URLS`. Preserve its existing HTTPS/ingress setup. A TLS-terminating proxy and forwarded certificate headers cannot authenticate a workload.

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

Use a secret configuration provider for certificate passwords. Certificate pins and server assignments are a startup snapshot; deploy new assignments and restart the API for rotation. An empty workload assignment grants no world allocation or internal authentication. It is not a bypass or an optional authentication mode.

The internal endpoint ignores player/launcher JWTs and server IDs in headers or request bodies. Its authentication policy is separate from account role authorization. The pinned leaf is the deployment trust anchor, so private leaves can be used without installing a machine-wide trust root. TLS verifies private-key possession; the API also verifies pin, validity and EKU on every request.

Kestrel listener configuration follows [Microsoft's endpoint documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0).

## Session activation and durable save authority

Redeeming a join ticket only reserves a pending session. The world must call `POST /internal/game/sessions/activate` over the workload mTLS connection and receive an `active` reply before accepting gameplay. Activation first advances the prior world's save barrier, then the target world's barrier, then activates the Auth-DB head and target guard. A failure leaves a resumable pending transition; retries retain the same session and fencing token.

The activation, heartbeat (`POST /internal/game/sessions/heartbeat`) and end (`POST /internal/game/sessions/end`) bodies contain canonical string `AccountId`, GUID `GameSessionId` and canonical positive string `FencingToken`. The workload identity comes from the TLS certificate. No request body or player JWT chooses a server identity.

Heartbeat every 15 seconds and stop gameplay by the granted `LeaseUntil`, even when a renewal request or response is lost. Renewal is capped at 45 seconds and the current ownership/proof/context deadlines. Both the Auth-DB session and Character-DB guard must renew before an active reply is returned. An expired guard cannot be resurrected by a heartbeat. The workload rate limit is separate from player limits (`Application:RateLimiting:WorkloadPermitsPerMinute`, default 16384); size it for four heartbeat requests per minute per concurrent player plus admission and cleanup traffic.

At load, bind the character entity to the immutable account/session/fence admitted by the world. Snapshots retain that authority through the save queue and despawn; replacement connections cannot relabel an old entity. Character saves acquire durable account guards inside their transaction, validate current ownership and lease expiry after lock acquisition, and recheck the deadline before commit. Multi-character transactions acquire account guards in ascending account order and fail atomically if any writer is stale.

Flush authoritative state before ending the session. End blocks the matching Character-DB guard before ending the Auth-DB head and remains available after context revocation. An old end request cannot block or end a replacement session. Do not use the presence/online flag as gameplay authority.


## World transport and coordinated protocol cutover

The world host requires a current TLS leaf with its private key before it listens. Configure
`Hosting:Security:CertificatePath` and its secret password. `World:Admission:ApiUrl` is the fixed
HTTPS origin of the API's GameInternal listener; its server certificate must pass normal TLS
validation. `World:Admission:ServerId` matches the API assignment for `Game:WorldId`.
`World:Admission:ClientCertificatePath` and its secret password supply the pinned client-auth PFX.
Never share the world TLS private key with clients; clients receive only the assigned leaf SHA-256
pin and TLS server name from the authenticated join response.

The world chart requires `server.transport.existingSecret`, `server.admission.apiUrl`, and
`server.admission.serverId`. The transport Secret holds `world-tls.pfx` and `workload.pfx`; optional
`world-tls-password` and `workload-password` keys provide their passwords. PFX files mount read-only
under `/run/avalon-auth`. These are deployment credentials supplied outside source control.

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

The API chart requires `storeAuthentication.existingSecret` (key `steam-publisher-key` by default),
`gameAdmission.tlsExistingSecret` (`tls.pfx`, optional `tls-password`), and
`gameAdmission.bindingsExistingSecret`. `gameAdmission.servers` supplies each `serverId`, `worldId`
and `tlsServerName`; the binding Secret has `<serverId>-tls-sha256` and `<serverId>-client-sha256`.
The chart mounts the API PFX read-only and exposes a separate direct mTLS service port (default 9443).
Publisher keys are only Secret references; there is no plaintext Helm publisher-key setting.
Missing required references/assignments refuse chart rendering.

The sibling homelab repository prepares these settings for worlds 1, 2 and 3, plus the production
Steam OpenID callback and website origin, on `feature/steam-authentication`. Its existing release
versions remain pinned pending coordinated staging. See homelab `docs/avalon.md` for Secret keys,
certificate DNS/trust requirements and cutover instructions. Certificate issuance/private CA trust
and publisher-key injection are external setup gates; this work did not deploy or install trust roots.
