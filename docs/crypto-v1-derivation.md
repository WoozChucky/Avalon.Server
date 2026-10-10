# Session crypto v1 — key derivation

**Status:** implemented on the server. Wire-breaking against any client built before it.
**Vectors:** `schema/crypto/session-v1.txt`
**Addresses:** findings 3, 4, 5 and 6 of `docs/security-review-network-crypto.md`.

Both ends of a connection derive the same two keys independently and never compare them. A
derivation that disagrees therefore fails as a packet that will not open — on a live connection,
at the first sealed packet, with nothing on the wire saying which end is wrong. That is what the
vector file exists to prevent, and it is why it is the part of this change a client author should
read first.

## The derivation

```
secret = the ECDH P-256 shared x-coordinate, at a FIXED 32 bytes, leading zeros kept
salt   = clientPublicKeyDer || serverPublicKeyDer
c2s    = HKDF-SHA256(ikm: secret, salt: salt, info: "avalon/v1 c2s", 32 bytes)
s2c    = HKDF-SHA256(ikm: secret, salt: salt, info: "avalon/v1 s2c", 32 bytes)
```

The client seals with `c2s` and opens with `s2c`. The server does the reverse.

`info` is ASCII with no terminator — `61 76 61 6c 6f 6e 2f 76 31 20 63 32 73` and
`61 76 61 6c 6f 6e 2f 76 31 20 73 32 63`. Standard RFC 5869 HKDF: extract then expand, one
output block, no truncation question at 32 bytes out of SHA-256.

Both public keys are DER-encoded `SubjectPublicKeyInfo` with the **named** `prime256v1` OID —
91 bytes, beginning `30 59 30 13 06 07 2a 86 48 ce 3d 02 01 06 08 2a 86 48 ce 3d 03 01 07 03 42
00 04` and then the uncompressed point. The explicit-parameters encoding is a different length
and the handshake's own length check rejects it.

### Why the salt is ordered that way

The salt is the client's key first and the server's second, **at both ends**, rather than each
end putting its own first. Ordering by ownership is the obvious thing to write and it is wrong:
the two ends would build two different salts for one exchange and agree on nothing, while every
single-process test still passed. Ordering by role means the salt is a property of the exchange
rather than of the observer, and each end only has to know which one it is.

Including the keys at all is what binds the derivation to this exchange. The same two peers
reconnecting derive different keys, and a relay that substitutes a public key derives keys its
own exchange cannot match — which is not by itself authentication (findings 1 and 2 remain), but
it does mean a key cannot be carried across exchanges.

**Both key pairs are per-connection**, and on the server that is a requirement of the counter
nonce rather than a nicety — see below. A client must not treat the server's key as an identity:
it is different on every connection, nothing signs it, and nothing pins it. Authenticating the
server is findings 1 and 2, and is not addressed here.

### Why the secret is fixed-width

A P-256 x-coordinate is a 256-bit number, so about one in 256 has a zero top byte. The minimal
`BigInteger` encoding drops it and yields 31 bytes. Before this change that was the AES key
directly, so the connection failed at its first sealed packet; it is still a divergence now,
because HKDF over 31 bytes is a different key. `tests/Avalon.Shared.UnitTests/Cryptography/SharedSecretShould.cs` measures the
rate at 17 in 5000. mbedTLS's `mbedtls_ecdh_calc_secret` already zero-pads to the curve size —
a client must **not** strip those zeros.

The second exchange in the vector file is exactly this case, and is the only one an
implementation that trims gets wrong.

## Nonces

A 96-bit big-endian counter from zero, one per direction, incremented per sealed packet. The
two counters cannot collide because the two directions are keyed differently — which is the
whole reason a counter is affordable here.

### What a counter costs, and what pays for it

Random 96-bit nonces were collision-safe whatever the peer did. A counter is not: every session
starts at zero, so **two sessions that agree the same key seal their first packets under the same
key and the same nonce**, which recovers the GCM authentication subkey for anyone listening and is
invisible at both ends.

That is not hypothetical, and while the server's key pair was per-process it took nothing but a
client reusing its own key pair across two connections. Both halves of the salt would be
identical, so the secret, both derived keys and both counters would be too. It would have been a
security property the server could neither verify nor enforce, resting on a sentence in this
document.

So the server now agrees every connection on a key pair of its own (`Connection`, not
`ServerBase`), which closes it unilaterally: the salt and the secret differ per connection
whatever the client does. Cost is one P-256 keygen per accepted connection — **0.23 ms**, measured
in Release, against well under a microsecond per sealed 256-byte packet and against a TLS handshake
on the same path.
It is per connection, not per packet.

`ServerBase.Crypto` is gone rather than left unused, because a process-wide key pair within reach
is how this would come back.

The side benefit, which was the original reason to want it: both halves of the exchange are now
ephemeral, so recorded traffic is not retroactively readable from either end's stored keys. That
holds only while **both** ends are ephemeral — a client that reuses its key pair is safe from the
nonce problem but gives that property up, so a client should generate a fresh pair per connection
too.

The cipher will not catch a lapse here. The platform `AesGcm` the session seals with has no
repeated-nonce guard at all, and one per cipher instance would be silent across two sessions anyway,
since each session owns its own.

**The nonce stays on the wire.** The format is unchanged:

```
[12-byte nonce][ciphertext][16-byte tag]
```

Nothing in the packet format, the `.proto` or the opcode table changes. Transmitting a value the
receiver could compute looks redundant, and buys two things: decryption reads the nonce that
arrived, so a peer never tracks the sender's count and a packet still opens when it is not the
one expected next; and a future change to how a sender picks its nonce needs no receiver change.
Twelve bytes per packet is the price.

**No replay or ordering check is performed.** A receiver accepts any nonce that authenticates,
so a replayed packet decrypts. That is a deliberate omission, not an oversight: the channel is a
reliable ordered stream today and rejecting out-of-order nonces would be a behaviour change
larger than this one. If it is wanted, it belongs with the transport decision in finding 2.

**An exhausted counter ends the session.** After 2^96 - 1 sealed packets the counter has no next
value; `SessionKeys.TryIncrementNonce`, which the session calls (`IncrementNonce` is its throwing
form), checks for that before it writes anything, so it fails with the counter left where it was
rather than wrapped to zero (#855), and the session that hit it marks itself spent: it never seals again, under nonce zero or any other, and opens nothing either. Not
reachable in practice; what matters is that the failure cannot be followed by a packet sealed
under a nonce the key has already used.

## The cipher

AES-256-GCM is the platform's `System.Security.Cryptography.AesGcm` (#850): one instance per
direction, keyed once in `AvalonCryptoSession.Initialize` from the two derived keys and reused for
every packet. The server seals as it
frames a packet (a world connection's send thread, the auth server's drain task) with `SealInto`, which
writes the nonce, ciphertext and tag straight into the frame and allocates nothing (#875). The nonce
is taken at seal time, so nonce order is the order the frames are written; no receiver checks that
order. `Encrypt`, which the client-to-server packets' `Create` use, allocates only the sealed packet
and writes into it the same way. Opening writes into the caller's buffer and allocates nothing. A
packet shorter than a nonce and a tag, or one that does not authenticate, is refused as a
`CryptographicException` (the platform throws the derived `AuthenticationTagMismatchException` and
clears what it had written). The key agreement and the derivation stay on BouncyCastle, which is
also what sealed the vectors in the file, so the vectors (those recorded before #850) hold the
platform cipher to a second implementation.

The session holds one `AesGcm` per direction and no lock (#875). Seals (`Encrypt`, `Encryptor`,
`SealInto`) must be totally ordered, each happening after the last (one thread, a lock, a queue
hand-off or an await); the same applies to opens (`Decrypt`). On the world server the connection's
send thread seals while its read loop opens; on the auth server the drain task seals while the read
loop opens. A seal and an open may run at once, since each direction has its own cipher and its own
state (the send counter is the sealer's alone). A client must likewise order its seals, and its
opens: the load-test tool takes locks of its own for both, since a bot opens both on its read loop
and where it awaited a reply.

A seal that overlaps another fails closed, in every build: the session takes an in-use flag with a
compare-and-swap before it copies the nonce, and refuses an overlapping seal with an
`InvalidOperationException` before any nonce is copied or the counter moves, so an overlap can never
seal two packets under one nonce. Both send paths already turn a seal that throws into discarding
the burst and closing the connection. Opens are checked only in a Debug build, by an assert on the
same kind of flag. That check is an interlocked operation, which orders opens of its own accord, so
a Debug run that passes does not prove a caller orders its opens.

The two instances live as long as the session, which lives as long as its connection, and are not
disposed: a send thread may still seal for a connection that has just closed, and a disposed cipher
would make that an exception on it. Their native key handles are released, and the key material
destroyed, by the handles' finalizers when the session is collected.

A session seals and opens only once its exchange has completed (#855). `Initialize` runs once: it
claims the session first, so a second call is refused even after a failed first one; it builds
both ciphers, and only then marks the session ready, with a volatile write that publishes the
ciphers with it. An `Initialize` that throws (a peer key that does not parse, say) leaves the session failed
for good, and every later `Encrypt` or `Decrypt` refuses with an `InvalidOperationException` rather
than reaching a missing cipher. `GetPublicKey` and `GetOtherEndPublicKey` refuse the same way
until the exchange has completed; a spent session still returns them, since exhaustion ends what
it may seal, not which exchange it was. The ready check is one volatile read before each packet, so it
adds no lock and no allocation. Exhaustion is written by the one thread sealing, so no seal passes the
check once the counter is spent.

`AesGcm` has no fallback. `ServerBase` refuses to construct, so the auth and world servers refuse to
start, when `AesGcm.IsSupported` is false (on Linux it needs OpenSSL, which the `aspnet:10.0`
images carry), rather than failing at the first handshake. The check is in the host because
`Avalon.Common` targets netstandard2.1, which has no `IsSupported`.

## Roles

`AvalonCryptoSession` takes a `CryptoRole` at construction — there is no default and no
inference. The server's is built in `src/Server/Avalon.Hosting/Networking/Connection.cs`. A
session that guessed its role would complete its handshake and fail to open the first packet it
was sent, so the guess is not available.

## Public key validation

`AsymmetricCipher.GetPublicKeyFromBytes` now rejects a key that is not an EC key, is not on
P-256, is the point at infinity, or is not on the curve, as a `CryptographicException` before
the key reaches the agreement.

Honestly scoped: BouncyCastle does reject a foreign curve on its own, at the agreement, as an
`InvalidOperationException` reading `ECDH public key has wrong domain parameters`. So this is
not a hole being closed. What it changes is that the rejection is local and asserted rather than
resting on undocumented library behaviour a version bump could alter, that it happens at the
parse rather than inside the agreement, and that it is typed as a rejected peer input rather than
as a programming error. Either way the session it was handed to is left failed, never half ready.

## What a client must implement

1. A **fresh** P-256 key pair per connection, exported as named-curve `SubjectPublicKeyInfo` DER
   (91 bytes). The server does the same. Reusing one is no longer a nonce hazard — the server's
   own freshness closes that — but it forfeits forward secrecy.
2. ECDH, **zero-padded to 32 bytes**.
3. HKDF-SHA256 twice, over the salt and the two labels above. `mbedtls_hkdf` with
   `mbedtls_md_info_from_type(MBEDTLS_MD_SHA256)` is exactly this.
4. AES-256-GCM, 16-byte tag, sealing with `c2s` and opening with `s2c`.
5. A 96-bit big-endian send counter from zero, transmitted with each packet; decrypt with the
   nonce that arrived.

Check each of those against `schema/crypto/session-v1.txt` before running against a server. Each
step's output is in the file, so a mismatch names the step rather than presenting as a handshake
that does not complete.

### Reading the vector file

Line-oriented, in the style of `schema/corpus/`. Lines beginning `#` are commentary.

```
exchange ordinary                one frozen exchange; the name is a label
  value clientPrivateScalar 32   a name and a byte count
    30 7b 16 c0 ...              the bytes, lowercase hex, sixteen per line
  ...
  packet c2s 0                   a sealed packet: direction, then the counter it used
    value plaintext 32
    value nonce 12
    value ciphertext 48          ciphertext and tag together
```

Two exchanges are present: `ordinary`, and `leading-zero-secret` whose shared secret begins with
a zero byte. Three packets per direction per exchange, so a counter that does not advance shows;
one of them has an empty plaintext, which seals to a bare tag.

The private scalars are SHA-256 over `avalon/v1 kat <label> <index>`, so the file can be rebuilt
from the strings in it. A client needs only the bytes.

### Vendoring it

The client vendors `schema/` and covers each file with a hash in its own `SCHEMA-MANIFEST`. This
file is not yet in that manifest — adding it is part of the client-side pass, along with the
`-text` marking in the client's `.gitattributes` that the other vendored schema files carry, since
the vectors are compared as bytes and autocrlf would otherwise fail them on a machine where nobody
had touched anything. On this side `schema/crypto/*.txt text eol=lf` does the same job, so
regenerating does not dirty the tree.

## Regenerating

```
dotnet run --project tools/Avalon.Exporter -- crypto
```

The vectors are exported by running the production `AvalonCryptoSession` and `SessionKeys`, not
a second copy of the algorithm, so the file cannot agree with something the server does not do.
`SessionCryptoVectorsShould` holds the checked-in bytes to the implementation, and a change in
what the server derives arrives as a diff someone reads — which for this file means a client
release, so read it that way.
