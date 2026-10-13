# Networking — Packet Protocol

Custom TCP layer with Protobuf-net serialization. Every client↔server message wraps a `NetworkPacket` (header + payload).

## Header Fields (`NetworkPacketHeader`)

| Field | Type | Purpose |
|---|---|---|
| `Type` | `NetworkPacketType` | Semantic opcode / message type (e.g., `CAuthPacket` → `CMSG_AUTH`) |
| `Flags` | `NetworkPacketFlags` | Bitmask: Encryption, Compression, Reserved — enables fast checks without parsing payload |
| `Protocol` | `NetworkProtocol` | Logical channel grouping (Authentication, World, Social, Character) |
| `Version` | `int` | Protocol version for backward compatibility |

The header is a struct (`NetworkPacketHeader`), read by hand from each frame by `InboundPacketFrame.ParseFrame`, so
neither a received frame nor a sent packet allocates a header (#875); the wire is unchanged.

Transport: TCP inside TLS, one connection per phase (Auth, then World). The auth server wraps every accepted socket in
TLS 1.2 with its certificate, the world server in TLS 1.2 or 1.3 with its own (both `Hosting:Security:CertificatePath`),
and a client pins the world's leaf named in its join reply. Ordering guaranteed by TCP.  
Encryption: inside TLS, session crypto negotiated via ephemeral public key exchange during handshake stages; on the
world server the per-packet session layer is a per-world setting, `Network:PacketEncryption`, off by default (#875).  
Size calculation uses fixed field lengths; header marshaled first enabling preallocation.

## Sending a packet

A server packet's `Create` (`S*Packet.Create`) returns an `OutboundPacket`: its header, and its message encoded, plain,
into a pooled `PayloadSegment` by `PacketEncoder` (#875). `IConnection.Send` takes the packet's payload reference. The
connection's outbox (a world connection's send thread, the auth server's outbox as its drain task writes) seals the
payload of a packet flagged Encrypted only when the outbox seals (`IOutbox.Seals`): always on the auth server, and on
a world connection only with `Network:PacketEncryption`. Otherwise the payload goes plain inside TLS and the header
loses the Encrypted flag, so the client opens nothing. The outbox frames the packet with `PacketEnvelope`:
`[varint length][NetworkPacket{1: header, 2: payload}]`, written by hand, byte for byte what protobuf-net writes for a
`NetworkPacket`, then releases the segment to its pool: no `byte[]` per packet.

A broadcast whose bytes are the same for every recipient (a hit, a cast, a death, a heal, loot spawned or despawned,
instance and party chat, a party member's status, a maintenance warning) is encoded once, for the first recipient that
hears it, and each recipient takes a reference of its own (`OutboundPacket.Share`); the broadcaster releases its own
after the loop (#875). Each send thread copies the payload into its own connection's burst, sealing it there when its
connection seals, and releases its reference as it frames it, or at once when its connection refuses the packet; the
segment is never sealed in place, and it returns to its pool after the last reference. World-state packets
(`SInstanceStateAdd/Update/Remove`, the aura list and updates) stay per recipient: each recipient hears its own set of
units, and they are sent inside the per-recipient state pass. An aura update's bytes depend only on its unit, so sharing
it needs a per-unit cache across that pass (#898).

Nor a message object per packet. `Create` fills the calling thread's instance of its message type
(`PacketEncoder.Scratch<T>()`) and encodes it before it returns, so one instance per thread serves every packet of that
type. A resetter compiled once per type, at the world server's startup (`PacketEncoder.PrepareServerPackets`, before
the port opens), sets every field back to a new message's value on every take and again once the message is written,
so no value of one packet reaches the next or stays referenced after it. A collection comes back null (protobuf-net
writes a null repeated field as it writes an empty one), so a factory assigns each collection it sends; a nested
message the template creates is shared by every thread, so a factory assigns it whole and never writes into it. A
member the reset cannot assign (a read-only field, a property without a setter) makes the type refuse to start.
`PacketEncoderScratchShould` dirties every member of every server packet and checks that the next take comes back as a
new message, and runs every factory and checks that a new thread's scratch still encodes as a new message does.

The client-to-server packets' `C*Packet.Create`, which only clients send (the load-test tool, the benchmarks and the
tests), still build a `NetworkPacket`, sealed as it is created.

## Auth Phase Lifecycle

1. `CRequestServerInfoPacket` — client sends its version; `SServerInfoPacket` answers with the server's version and
   public key, or refuses a client below `Application:MinClientVersion` and closes.
2. `CClientInfoPacket` — client sends its ephemeral public key.
3. `SHandshakePacket` — server returns handshake data (server challenge).
4. `CHandshakePacket` — client proves possession / echoes handshake data.
5. `SHandshakeResultPacket` — indicates success and signals encryption activation.
6. `CAuthPacket` — credentials (username + password; server BCrypt verifies).
7. `SAuthResultPacket` — status (`SUCCESS`, `LOCKED`, `MFA_REQUIRED`, etc.) and `AccountId` on success; with
   `MFA_REQUIRED`, an MFA hash that `CMFAVerifyPacket` sends back with the code.
8. `CWorldListPacket` — client requests accessible world list.
9. `SWorldListPacket` — worlds filtered by account access level, each with its derived status.

The auth phase ends there: the TCP world select and world-key handoff were retired with the TLS join-ticket admission,
and their opcodes are unassigned. [Auth server login flow](auth-server.md) has the rules of each step.

## World Admission

10. The client gets a join ticket from the REST API (`POST /game/join-tickets`, against its game context), bound to one
    world, character and world server, for at most 30 seconds.
11. Client opens a TCP connection to that world server and completes TLS.
12. `CGameAdmissionPacket(JoinTicket, PublicKey)` — clear text inside TLS: the ticket and a new ephemeral public key.
13. The world server redeems the ticket and activates the session through the API's internal admission routes, off the
    tick, then initializes the session crypto. The key exchange runs for every connection, whatever the world's mode, so
    a client that always seals keeps working (retiring it is #878).
14. `SGameAdmissionPacket(PublicKey, Result, PacketEncryption)` — `Accepted` with the server's public key and the
    world's mode (field 3, `Network:PacketEncryption`, #875): true, the client seals every gameplay packet it sends;
    false, TLS alone and it sends plain. The client seals exactly as told. Otherwise `InvalidRequest`,
    `AuthorizationRequired` or `ServiceUnavailable` (`UnsupportedClient` is declared beside them; this server refuses
    an old client at step 15 instead), `PacketEncryption` false, and the connection is closed.
15. `CWorldHandshakePacket(Version)` / `SWorldHandshakePacket` — sealed only when the world seals; the client's version
    must be at least 0.3.0 and the world's `MinVersion`, or the connection is closed.
16. Subsequent packets (character list, selection, movement, chat) proceed under the admitted session. A connection
    keeps the mode it was created and admitted under for its life; a change of the setting needs a restart.

Strictness of the mode (#875). With `Network:PacketEncryption` on, a plain packet the world runs (the version
handshake, and every packet a session filter names but the pong) closes the connection as a protocol violation, at
arrival, before any filter is asked (`WorldConnection.OnReceive`). Admission and pong are plain by design and accepted
plain. An opcode no world filter takes is dropped as before, whatever its flags. With the flag off, plain and sealed
are both accepted.

Compatibility of the mode (#875). A server opens a client packet by its header, sealed or not; a client opens a server
packet by its header too.

| Client | Server | Client to server | Server to client |
|---|---|---|---|
| before #875 | flag off | the client still seals; the server opens it by its header | plain; the client follows the header |
| before #875 | flag on | sealed, as before | sealed, as before |
| current | flag off | plain | plain |
| current | flag on | sealed | sealed |
| current | before #875 (field absent, reads false) | plain; that server decides by the header | sealed; the client follows the header |

## Time Sync: `SMSG_PING` and `CMSG_PONG`

The world server's tick queues a ping for every connection in its list once every 600 ticks (about ten seconds, each
connection on the tick its place in the list gives it), whether or not the connection has been admitted yet, and one
more right after a connection's version handshake. The client answers each ping with one pong. From the exchange the
server measures the connection's round trip (`IWorldConnection.RoundTripTime` in ticks, `Latency` in whole
milliseconds) and the offset between the two clocks, and tells the client both on the next ping. The character row
and the REST API carry `Latency` as of character select (`CharacterSelectHandler` copies it once): often the
handshake ping's reading, or 0. Both packets are plain inside TLS, whatever `Network:PacketEncryption` says. Every
time is in UTC ticks (100 ns, counted from year 1).

| Packet | Field | Meaning |
|---|---|---|
| `SPingPacket` | `ServerTimestamp` (1) | Server time as the connection's send thread wrote the ping (#875). It also names the ping |
| | `ClientTimestamp` (2) | The `ClientReceivedTimestamp` of the last pong the server accepted |
| | `Rtt` (3) | The last round trip the server measured |
| | `Offset` (4) | Server time minus client time, from that same exchange |
| `CPongPacket` | `LastServerTimestamp` (1) | The `ServerTimestamp` of the ping it answers, echoed exactly |
| | `ClientReceivedTimestamp` (2) | When the client received the ping, on its own clock |
| | `ClientSentTimestamp` (3) | When the client sent the pong. Still on the wire; the server does not read it |

**The server measures the round trip alone (#892).** Before, the round trip was computed from the echo and the client's
receive and send times, so a client could report any round trip it liked, zero and negative ones included. Now:

- The send thread records the stamp of each ping as it writes it, before the ping can reach the client. It keeps the
  stamps of the last four pings it wrote (`ConnectionSender.PingStampCapacity`), each until a pong answers it. A
  connection has one ping unanswered at a time, two when the handshake's ping sits beside a periodic one, so a stamp
  stays answerable for thirty seconds or more.
- **One pong per ping.** Each ping written grants the connection one pong credit, never more than four at once. A pong
  takes a credit as the read loop receives it, whatever it echoes, and a pong that finds none is dropped there
  (`WorldConnection.OnReceive`), before it is queued for the tick. The check comes after the admission check, so a pong
  sent before admission is dropped as it always was and spends nothing: the credits of the pings written before
  admission are still there after it, up to the four.
- A pong that got in is accepted only when its `LastServerTimestamp` equals one of the recorded stamps. The stamp is
  then spent. The echo plays no other part.
- `round trip = the pong's arrival - the ping's stamp`. Both are the server's own readings of its own clock: the stamp
  taken at the write, and the arrival taken by the read loop as it reads the pong off the socket, not when the tick
  handles it. `Latency` is the round trip in whole milliseconds.
- `offset = the ping's stamp + round trip / 2 - ClientReceivedTimestamp`. That is the one term of the client's left. It
  is the client's own clock by nature, and a false value moves only that client's own idea of server time.
- The next ping carries `Rtt`, `Offset` and `ClientTimestamp` from the last accepted pong. All three are 0, as are
  `Latency` and `RoundTripTime`, until the first one.

**What a client must do.** Echo `ServerTimestamp` unchanged, answer each ping once, and send no other pong: a pong
that echoes anything else uses up that ping's one answer, and the true echo sent after it is dropped. Answer as it
reads the ping: the time between receiving a ping and sending its pong is part of the round trip (it used to be
subtracted), and half of it lands in `Offset`. Fill `ClientReceivedTimestamp` from the clock it applies `Offset` to.
After a stall in which pings went unanswered, answer only the newest ones, at most four, and drop the rest: the server
remembers the last four stamps and lets at most four pongs in, whatever they echo, so an answer to an older ping
takes the place of an answer to a newer one.

**What the server drops and ignores.** In every case below `Latency`, `RoundTripTime` and the offset stay as they
were, the next ping repeats the old `Rtt` and `Offset`, and the connection stays open. No metric counts any of them.

- *Dropped as it arrives*: a pong beyond one per ping written (a second answer, a burst, a pong before any ping). It is
  never queued and no handler runs. Logged at Trace only (`Avalon.World.WorldConnection`), since a client can send
  them at any rate. This also catches a client that stalls past four pings and then answers them all, oldest first:
  its first pongs spend the four credits on pings the record has forgotten, and the answers to its newest pings are
  dropped. With five unanswered, the newest ping's answer is the one dropped and the reading left is the one before
  it, stretched by the stall; with eight, all four pongs let in answer forgotten pings and nothing is measured. The
  next ping measures again.
- *Let in, then ignored by the handler*: a pong whose echo is the stamp of no unanswered ping (a forged or guessed
  one, the echo of a ping already answered, or of a ping that four later pings have pushed out of the record). Logged
  at Debug, at most once per ping written.
- *Let in, matched, then ignored*: a pong whose arrival reads earlier than the stamp it echoes. Either the echo was
  sent ahead of its ping (a guess that hit) or the server's clock was set back in between. The stamp is spent, nothing
  is measured, and the round trip is never negative. Logged at Debug.

**What the measurement does not prove: `Latency` is advisory, in both directions.**

- *Guessing the stamp.* The echo is a reading of the server's clock, not a secret. A client sees every earlier
  `ServerTimestamp` and the pings follow the tick's deadlines, so it can predict the next stamp to within about 0.1 to
  2 ms: 1,000 to 20,000 candidate values at 100 ns. A pong carrying the right one, arriving just after the ping is
  written and before the client could have read it, reads a round trip shorter than the client's own. The credit
  leaves it one guess per ping against those thousands of candidates (up to four at once after letting pings go
  unanswered), where it used to have as many as it could send. That lowers the odds and does not close guessing. A
  ping identifier the client cannot predict would close it, and is a wire change.
- *Answering late* raises the reading. How long a stamp stays answerable follows from the cadence alone (until four
  later pings are written, some forty seconds at one ping per ten seconds). Nothing in the code caps the round trip.
- *The reading has no age.* A client that stops answering keeps its last reading, and one that never answers reads 0.

Code that would trust latency (lag compensation, cheat heuristics, matchmaking) needs more than this. An age for the
reading and a ping identifier the client cannot predict would close staleness and guessing, and no more. A client could
still raise its reading at will, so a use that a high reading benefits also needs a bound on readings. And what would
be left is only a floor: the reading would be no lower than the real round trip to the connection's endpoint, which is
whatever terminates its TLS (a proxy or a relay as well as a player's machine), not to the player.

## Redis Usage in Flow

- `auth:account:{accountId}:mfa` and `auth:mfa:hash:{sha256}` — the MFA step's state.
- `game-auth:{environment}:join-ticket:{digest}` — a join ticket, stored by its digest, redeemed once.
- `auth:accounts:online` / `world:accounts:disconnect` — pub/sub channels for cross-component presence coordination.

See [Redis Cache Keys](redis-cache-keys.md) for the full key reference.

## Failure Modes & Safeguards

- Invalid handshake data → connection closed (avoid resource waste).
- Wrong key size → rejected prior to crypto init.
- A malformed, expired, spent or foreign join ticket → `SGameAdmissionPacket` refusal and the connection closed; a
  world connection that is not admitted within 15 seconds is closed.
- Multiple logins for same account → previously connected session force-disconnected via pub/sub event.

## Open Defect — a `DateTime` loses its kind

A `DateTime` sent over the protocol arrives without knowing which zone it is in, and this is a
defect in the server rather than a property of the wire format a client has to live with.

protobuf-net encodes a `DateTime` as `bcl.DateTime`, which has a `kind` field, and writes only
the value and the scale. The loss is on the writing side alone: nothing populates the field, but
the reader honours it when it is there — hand protobuf-net a `bcl.DateTime` carrying `kind = 1`
and it returns a `DateTimeKind.Utc` timestamp, `kind = 2` and it returns a local one. So a
timestamp written as `DateTimeKind.Utc` comes back from the **server's own deserializer** as
`DateTimeKind.Unspecified`, and any code that then treats it as local — `ToLocalTime`, a
comparison against `DateTime.Now`, a format without an offset — shifts it silently by the host's
offset. Nothing raises.

Affected today: the two chat timestamps, `SChatMessagePacket.DateTime` and its client
counterpart. A client cannot recover the zone either and has to be told out of band.

Either end of a fix is viable and both are protocol changes. Populating the field is the smaller
one, and it needs no reader work at all: `RuntimeTypeModel.Default.IncludeDateTimeKind = true`,
set before the model first serializes a `DateTime`, writes `kind` and the round trip then keeps
it. That adds two bytes to every `DateTime` on the wire, so it is a wire change and the corpus
moves with it. The other end is to carry the timestamp as an explicit UTC tick count or a
`google.protobuf.Timestamp` and drop `bcl.DateTime` for these members. Until one is taken, treat
every received `DateTime` as UTC by convention and never call `ToLocalTime` on one.

`WireLimitsShould.Not_Carry_The_Kind_Of_A_DateTime` holds the current behaviour with the bytes
in it, so a fix will turn that test red rather than pass unnoticed.

## Security Considerations

- Single-use join tickets mitigate replay: a ticket lives at most 30 seconds, is bound to one world server, and is
  redeemed once, by that server, over its mutually authenticated workload connection.
- Separation of the auth and world sessions limits the blast radius of a compromised session token.
- Public key re-exchange on world join prevents key reuse across phases.
- Auth failures are budgeted per source and per username ([auth server](auth-server.md)).

## Extensibility

- **Add a new packet type:** define Protobuf contract, assign `NetworkPacketType` enum value, implement handler (`IAuthPacketHandler<T>` or `IWorldPacketHandler<T>`), register via reflection scan (attribute-driven).
- **Version evolution:** introduce parallel handlers keyed off `Header.Version` while keeping backward compatibility.
- **Optional compression:** introduce via `Flags` without breaking existing decoding.

## Sequence Diagram

```mermaid
sequenceDiagram
    participant Client
    participant Auth as Auth Server
    participant Api as REST API (identity)
    participant World as World Server

    Client->>Auth: TLS connect
    Client->>Auth: CRequestServerInfo(version)
    Auth-->>Client: SServerInfo(version, publicKey)
    Client->>Auth: CClientInfo(publicKey)
    Auth-->>Client: SHandshake(handshakeData)
    Client->>Auth: CHandshake(handshakeData)
    Auth-->>Client: SHandshakeResult(success)
    Client->>Auth: CAuth(username,password)
    Auth-->>Client: SAuthResult(SUCCESS, accountId)
    Client->>Auth: CWorldList()
    Auth-->>Client: SWorldList(worlds[])
    Client->>Api: POST /game/join-tickets (game context, world, character)
    Api-->>Client: join ticket, world host, TLS pin
    Client->>World: TLS connect
    Client->>World: CGameAdmission(joinTicket, publicKey)
    World->>Api: POST /internal/game/join-tickets/redeem, sessions/activate (mTLS)
    Api-->>World: account, session, fencing token, lease
    World-->>Client: SGameAdmission(serverPublicKey)
    Client->>World: CWorldHandshake(version)
    World-->>Client: SWorldHandshake
    Client->>World: (Character/Gameplay packets...)
```

Flow summary:
1. Secure ephemeral key negotiation (Auth)
2. Credentials verification & session marking
3. World discovery with access filtering
4. One-time join ticket issuance (REST game admission)
5. World server redemption + second crypto establishment
6. Transition to gameplay channel
