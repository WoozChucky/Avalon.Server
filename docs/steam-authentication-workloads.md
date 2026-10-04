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
