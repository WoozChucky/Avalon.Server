# Security Review — Network Transport & Session Crypto

**Date:** 2026-09-04
**Scope:** Auth and World transport security, the ECDH handshake, and the AES-GCM session layer.
**Repos read:** `Avalon.Server` (this repo) and the Unity client at `C:\dev\3D` — both ends matter here, and two of the findings are only visible when you read them together.
**Status:** Findings only. No code changed.

## Summary

The session layer is built correctly in its parts — ephemeral ECDH on P-256, AES-GCM, a challenge-response to confirm the key. What is missing is **authentication of the peer's public key**, on both channels, for two different reasons. The result is that both channels resist a passive eavesdropper and neither resists an active one.

There is also one non-security bug in the same code path that is almost certainly already causing rare, unexplained connection failures.

| # | Severity | Finding |
|---|---|---|
| 1 | **Critical** | The client accepts any server certificate, so Auth's TLS provides no authentication |
| 2 | **High** | The World channel has no transport security and its ECDH is unauthenticated |
| 3 | **High** | The session key is the raw ECDH x-coordinate: variable length, and no KDF |
| 4 | Medium | One key in both directions, with random 96-bit nonces |
| 5 | Low | No explicit validation of the peer's public key |
| 6 | Low | `SecureRandom` seeded with a timestamp; a dead nonce field |
| 7 | Info | The custom crypto is redundant on Auth and load-bearing on World |

---

## 1. Critical — the client accepts any server certificate

**Where:** `C:\dev\3D\Assets\Scripts\Network\Core\Connection\AuthConnection.cs:36-41`

```csharp
var ssl = new SslStream(new NetworkStream(socket, ownsSocket: false),
    leaveInnerStreamOpen: false,
    userCertificateValidationCallback: (_, _, _, _) => true);   // accepts anything

await ssl.AuthenticateAsClientAsync(host, new X509Certificate2Collection { cert },
    SslProtocols.Tls12, checkCertificateRevocation: true);
```

The server side is correct — `AuthConnection.cs:78-82` wraps the socket in `SslStream` and authenticates with a real certificate. The intent to pin is also clearly present on the client: `cert-public.pem` is read from `StreamingAssets` immediately above.

But the pinned certificate is passed as the **client certificate collection** — the mutual-TLS slot, which this server never requests (`clientCertificateRequired: false`) — while the **server's** certificate is accepted unconditionally by the callback.

**Consequence.** An active attacker in path presents any self-signed certificate. The client accepts it, completes TLS with the attacker, and the attacker opens its own TLS session to the real server. The custom ECDH inside does not save this: the attacker performs one ECDH with the client and another with the server, and relays. The 32-byte challenge is decrypted with one key and re-encrypted with the other, so it verifies perfectly at both ends.

`CAuthPacket` then delivers **the username and password in plaintext** to the attacker, along with read/write access to the whole session.

**Fix.** Validate in the callback against the pinned certificate rather than passing it as a client certificate — compare the presented certificate's public key or thumbprint to `cert-public.pem` and return false on mismatch. Two lines. Do this before anything else in this document; it is the single change with the largest security return.

## 2. High — the World channel has no transport security

**Where:** `src/Server/Avalon.World/WorldConnection.cs:169`

```csharp
Task.FromResult(new PacketStream(new NetworkStream(client.Client, true)));
```

and on the client, `Assets/Scripts/Network/Core/Connection/WorldConnection.cs` does **not** override `CreateStreamAsync`, so it inherits the plain `NetworkStream` from `Connection.cs:111`. Both ends confirmed.

So World runs the custom ECDH + AES-GCM with no TLS underneath and nothing authenticating the server's ephemeral key. The same relay attack as finding 1 applies, with nothing to defeat even in principle.

**Consequence.** Lower than finding 1 — credentials do not cross this channel — but still severe: session hijack, plus read and write access to all gameplay traffic (movement, chat, inventory, commands). An attacker can inject packets as the player.

**Fix.** Either terminate the World channel in TLS as Auth does, or move to a transport with authenticated encryption built in (see *Transport*, below). If neither is near-term, pin a long-term server key and have the server sign its ephemeral public key with it, binding the handshake transcript.

## 3. High — the session key is the raw ECDH x-coordinate

**Where:** `src/Shared/Avalon.Common/Cryptography/AsymmetricCipher.cs` (`CalculateSharedSecret`) and `IAvalonCryptoSession.cs` (`Initialize`)

```csharp
var secret = agreement.CalculateAgreement(otherPublicKey);
var sharedSecret = secret.ToByteArrayUnsigned();   // BigInteger -> bytes, leading zeros stripped
...
_sessionKey = AsymmetricCipher.CalculateSharedSecret(_ownKeyPair, _otherEndPublicKey);   // used directly as the AES key
```

Two problems in one line.

**(a) The key is variable length, and this is a live availability bug.** `ToByteArrayUnsigned()` strips leading zero bytes. The P-256 shared x-coordinate has a zero high byte roughly **1 time in 256**, giving a 31-byte key; 1 in 65536 gives 30 bytes, and so on. AES accepts only 16, 24 or 32 bytes, so those connections fail at the first encrypt with a key-size exception rather than a clean error.

If rare unexplained handshake failures have been observed at roughly a 0.4% rate, this is the cause.

**(b) There is no KDF.** The raw x-coordinate is used directly as key material. It is not uniformly distributed — it is a valid curve x-coordinate, roughly half the 256-bit space — and standard practice is to run the ECDH output through a KDF. Not known to be directly exploitable here, but it fails any external review and it is what makes (a) possible.

**Fix.** Both close with one change: derive the key with HKDF-SHA256 over the shared secret, with a salt and a context string. That yields exactly 32 uniform bytes every time, and removes the length hazard by construction.

## 4. Medium — one key in both directions, with random nonces

**Where:** `IAvalonCryptoSession.cs`, `Encrypt`

Client and server both encrypt with the same `_sessionKey`, each drawing a random 96-bit nonce per message. GCM nonce reuse under a shared key is catastrophic — it exposes the authentication subkey and permits forgery of arbitrary messages — and because both directions share the key, the birthday bound applies to their *combined* message count.

At realistic volumes this is not close to a practical break. It is flagged because the fix is free once finding 3 is addressed: derive **separate keys per direction** with distinct HKDF `info` labels, and use a **counter** nonce per direction. That removes the birthday bound entirely rather than staying comfortably inside it. This is what TLS does.

## 5. Low — no explicit public key validation

**Where:** `AsymmetricCipher.GetPublicKeyFromBytes`

The peer's key arrives as DER-encoded `SubjectPublicKeyInfo` and is parsed with `PublicKeyFactory.CreateKey`, which means the *curve* is chosen by the peer. Nothing in this codebase checks that the point lies on the expected curve, is not the identity, and is in the correct subgroup — the classic preconditions for avoiding invalid-curve and small-subgroup attacks.

Modern BouncyCastle validates domain parameters inside `ECDHBasicAgreement` and rejects points that do not decode onto the named curve, so this is **probably** not exploitable as written. That is the point: the safety rests entirely on undocumented library behaviour that a version bump could change, and nothing local asserts it.

The existing length check (`packet.PublicKey.Length != ServerCrypto.GetValidKeySize()`, `CClientInfoHandler.cs`) is a weak proxy — it compares against the server's own DER length rather than validating the key.

**Fix.** Assert the curve is the expected one and validate the point explicitly before use.

## 6. Low — RNG seeding and a dead field

**Where:** `IAvalonCryptoSession.cs`, constructor

```csharp
_secureRandom.SetSeed(DateTime.UtcNow.Ticks);
var tempBytes = new byte[16];
_secureRandom.NextBytes(tempBytes);   // discarded
_secureRandom.NextBytes(_nonce);      // never used; Encrypt allocates its own nonce
```

`SecureRandom.SetSeed` **mixes** additional material rather than replacing the seed, so this is not currently a weakness. It is flagged as a trap: it reads like the timestamp is the seed, and a later "cleanup" that made that true would be catastrophic. The `_nonce` field and `tempBytes` are dead — `Encrypt` generates a fresh local nonce each call.

**Fix.** Delete all three lines.

## 7. Info — the custom layer is redundant where it is applied, and alone where it matters

Worth stating plainly, because it inverts the intuition:

| Channel | Transport | Custom ECDH + AES-GCM | Authenticated? |
|---|---|---|---|
| Auth | TLS 1.2 with a server certificate | **redundant** — runs inside TLS | No — the client accepts any cert (1) |
| World | Plain TCP | **load-bearing** — the only protection | No — nothing to authenticate with (2) |

On Auth the custom layer costs a second pass of AES-GCM over a handful of login packets, which is irrelevant for performance but is complexity carrying no security. On World it is the only protection there is, and being unauthenticated it stops a passive sniffer and nothing more.

Once finding 1 is fixed, the Auth custom layer can be deleted outright. Once World has authenticated transport, so can that one.

Also noted, not currently exploitable: `AuthConnection.cs:125` returns `true` from the *client-certificate* validation callback. Dead today because client certificates are never requested; it would become a real hole the moment mutual TLS is enabled.

TLS is pinned to `SslProtocols.Tls12` on both ends. TLS 1.3 is available in .NET and is a 1-RTT handshake with better cipher-suite hygiene — worth taking when the callback is fixed.

---

## Transport

The findings above are all fixable in place. Separately, TCP is the wrong transport for the World channel, and that is a latency question rather than a security one: a single dropped packet stalls every state update queued behind it. For 20–30 Hz snapshots that is a visible hitch where an unreliable channel would simply have used the next snapshot.

Two options that solve transport and authenticated encryption together:

**QUIC** — `System.Net.Quic` has been production-supported since .NET 8 (`QuicListener` / `QuicConnection` / `QuicStream`), so it is available here on .NET 10 with no preview flags. TLS 1.3 and ALPN are mandatory, using the same `SslServerAuthenticationOptions` shape already in use. Backed by MsQuic: in-box on Windows 11 / Server 2022, needs `libmsquic` in Linux images, and **unsupported on Windows Server 2019 / Windows 10** — check `QuicListener.IsSupported` rather than assuming. Note the Unity client cannot use it: Unity's .NET Standard 2.1 runtime has no `System.Net.Quic`, so it would need a native MsQuic plugin. A native C++ client links MsQuic directly and the problem disappears.

**GameNetworkingSockets (Valve)** — native C++ with C# bindings, so the *same library* runs on this server and on a C++ client. Curve25519 + AES-GCM with authentication built in, reliable and unreliable channels, lane prioritisation, proven at Steam scale. It solves the crypto and the transport in one dependency.

Recommendation: keep Auth on TLS over TCP — it is request/response, so head-of-line blocking is irrelevant — and move the World channel to one of the two. GameNetworkingSockets is the better fit if the next client is C++, because it is one library on both ends and it is shaped for games. QUIC is the better-standardised, better-tooled choice if betting on a standard is preferred over a vendor library.

## Suggested order

1. **Finding 1** — validate the pinned certificate on the client. Two lines, largest return, independent of everything else.
2. **Finding 3** — HKDF the shared secret. Closes the intermittent failure and the missing KDF together.
3. **Finding 4** — per-direction keys and counter nonces, once HKDF is in.
4. **Findings 5 and 6** — explicit key validation, delete the dead RNG lines.
5. **Finding 2 / Transport** — choose the World transport, then delete the custom layer where TLS or the new transport supersedes it.

Steps 1–4 are small and independent of the transport decision. Do not couple them to it.

## Not verified

- Whether the intermittent-failure rate predicted by finding 3 matches observed reality. That is checkable from server logs: look for AES key-size exceptions during handshake at roughly 1 connection in 256.
- Whether BouncyCastle's current version rejects mismatched domain parameters in every path reachable from `GetPublicKeyFromBytes` (finding 5). The reasoning is that it does; it was not proven by test.
- The `Avalon.Api` HTTPS/JWT surface, rate limiting, the Redis world-key flow beyond reading its documentation, and the World server's gameplay validation. Out of scope for this pass.
- Nothing here was tested against a running server; all findings are from source reading of both ends.
