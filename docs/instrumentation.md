# Instrumentation

To monitor the performance of the server, you can use the `dotnet-counters` tool, which is part of the .NET Core diagnostics suite.
This tool allows you to collect performance metrics from your application in real-time.

```bash
dotnet tool install --global dotnet-counters
dotnet-counters ps
dotnet-counters monitor -p <PID> System.Runtime world-server
dotnet-counters collect -p <PID> --refresh-interval 1 --format csv -o idle.csv System.Runtime
```

## World metrics for load testing

The world server publishes these on the `world-server` meter. Through OTLP into Prometheus a dot becomes an underscore, the unit becomes a suffix (`us` becomes `_microseconds`, `ms` becomes `_milliseconds`; a `{packets}` annotation adds nothing), a counter gains `_total`, and a histogram is read through its `_bucket`, `_sum` and `_count` series.

They reach Prometheus once per export interval: 60 s by default, 10 s on the load-test world (the world chart's `otel.metricExportIntervalMs`, [configuration reference](configuration-reference.md#world-metrics-export)). The [load-test ramp](load-testing.md#the-limits) judges each step on these series.

### Tick and instance time

Every tick histogram is in microseconds (`us`). The SDK's default buckets end at 10 ms, so every slower tick landed in `+Inf` and a p95 or p99 could never read above 10 ms. These buckets reach a one-second stall and keep one frame at 60 Hz (16667 µs) as an edge, so "over one frame" is exact (`WorldHistograms.TickMicroseconds`):

`250, 500, 1000, 2000, 4000, 8000, 12000, 16667, 25000, 33333, 50000, 100000, 250000, 1000000`

| Metric | Prometheus | Measures |
|---|---|---|
| `world.tick.duration` | `world_tick_duration_microseconds_bucket` | One whole world tick |
| `world.update.duration` | `world_update_duration_microseconds_bucket` | The world update pass of a tick |
| `world.session_update.duration` | `world_session_update_duration_microseconds_bucket` | The session update pass of a tick |
| `world.instance.update.duration` | `world_instance_update_duration_microseconds_bucket` | One instance's update, tagged `map.type` (`map_type` in Prometheus) |
| `world.post_update.duration` | `world_post_update_duration_microseconds_bucket` | One stage after the world update, tagged `stage` (below) |
| `world.tick.deadline_overshoot` | `world_tick_deadline_overshoot_microseconds_bucket` | How far past its deadline the tick loop woke; signed, an early wake is negative |

The world update is followed by work no other histogram covers, about two thirds of a median tick in the first capacity ramp (#875). `world.post_update.duration` times each stage of it, in tick order, under its `stage` tag:

| `stage` | What it times |
|---|---|
| `quests` | `QuestFlusher` over every connection: quest counts, log, updates, lines and markers |
| `inventory` | `InventoryUpdateFlusher` over every connection |
| `sheet` | `CharacterSheetFlusher` over every connection |
| `ability_amounts` | `AbilityAmountsFlusher` over every connection |
| `party_status` | `PartyService.FlushMemberStatus` |
| `presence` | The admin view's presence capture |
| `pings` | The time-sync pings due this tick |
| `outbox` | `NetworkSendScheduler.SignalAll`: one wake-up per send thread with work (a thread with nothing queued sleeps on), never more than the thread count whatever the player count; tens of µs, about as much as the wake-ups cost on the host ([the send path](benchmarks.md#the-send-path-875)) (#875) |
| `continuations` | `FlushContinuations` over every connection: the off-tick results handed back to the tick |

Since #875 the tick neither seals nor writes: the `outbox` stage only wakes the send threads, which frame (and, with `Network:PacketEncryption` on, seal) and write each connection's packets off the tick (their cost is under [Send threads and slow clients](#send-threads-and-slow-clients)). Before that change the stage framed, sealed and wrote every connection's packets on the tick, so compare it across the change with that work moved, not removed.

A stage is usually a small part of a tick, so these buckets start lower (`WorldHistograms.StageMicroseconds`): `25, 50, 100, 250, 500, 1000, 2000, 4000, 8000, 12000, 16667, 25000, 33333, 50000, 100000, 250000, 1000000`. The stages together are the tick's time after the world update. Each stage's mean time per tick: `sum by (stage) (rate(world_post_update_duration_microseconds_sum[1m])) / sum by (stage) (rate(world_post_update_duration_microseconds_count[1m]))`.

The deadline overshoot has its own buckets (`WorldHistograms.OvershootMicroseconds`): it is mostly within the timer's precision, so they split early from late around 0 and stay fine below 1 ms before reaching the same one-second stall:

`-1000, -250, -50, 0, 50, 100, 250, 500, 1000, 2000, 4000, 8000, 16667, 33333, 100000, 1000000`

### Send threads and slow clients

Since #875 the tick only queues packets; the send threads frame (and, with `Network:PacketEncryption` on, seal) and write them, and nothing is dropped.

| Metric | Prometheus | Measures |
|---|---|---|
| `network.out.slow_kicks` | `network_out_slow_kicks_total` | Connections closed as too slow, tagged `reason`: `bytes` (past `Network:MaxPendingBytes`, sent `SDisconnect(SlowConnection)`) or `stall` (a write pending past `Network:MaxWriteStall`, closed without a notice) |
| `network.out.pending_bytes` | `network_out_pending_bytes_bucket` | Per send pass, the most bytes any one connection it visited had queued or being written |
| `network.send.pass.duration` | `network_send_pass_duration_microseconds_bucket` | One send thread's pass, tagged `thread`; the `_sum` rate is the threads' busy time |
| `network.send.bytes` | `network_send_bytes_total` | Bytes handed to the sockets, counted once per send pass |
| `network.send.burst_packets` | `network_send_burst_packets_bucket` | Packets per write |
| `network.send.thread_faults` | `network_send_thread_faults_total` | Failures that escaped a send pass, tagged `thread` (the thread goes on); any non-zero value is a bug |

The send threads' busy time, in cores: `sum(rate(network_send_pass_duration_microseconds_sum[1m])) / 1e6`.

### Receive backlog

`world.receive_queue.depth` (`{packets}`, Prometheus `world_receive_queue_depth`) is an observable gauge of the packets received and not yet dispatched by the tick. It reports two series, tagged `stat`: `total`, the sum over every connection, and `max`, the deepest single connection. It is read on the metrics exporter's thread over the connection snapshot, never on the tick. A `total` that keeps climbing means the tick no longer keeps up with what clients send; a high `max` with a low `total` points at one client. Since #927 it is the difference of two counts each connection keeps (packets queued by its read loop, packets taken out by the tick), the figure the cap below is checked against, and reading it no longer walks the queues. A connection's depth never passes `Network:MaxReceiveQueueDepth` and can equal it (a burst of exactly the cap is accepted). The gauge reads the tick's count first and the read loop's second, so a reading is never negative and never below the real depth, but it can be above it by the packets the tick took between the two reads, when the exporter's thread is descheduled there: `max` reads up to the cap, and past it only by that.

| Metric | Prometheus | Measures |
|---|---|---|
| `network.in.backlog_kicks` | `network_in_backlog_kicks_total` | Connections closed with `Network:MaxReceiveQueueDepth` packets received and waiting for the tick (#927): one more arrived, was not queued, and the connection was closed without a notice. No tags. Each also writes one Warning (`Avalon.World.WorldConnection`) naming the connection, its account and the opcode |

Any count is worth a look: a client sending faster than the tick takes (150 packets a pass per connection, `WorldConnection.MaxPacketsPerUpdate`), a tick stalled for as long as the cap is deep (about 34 s of input at the default), or a connection whose queue is wedged ([packet handlers](packet-handlers.md)). The counter does not see a flood the tick keeps up with, nor packets dropped as they arrive.

### Failed handshakes

| Metric | Prometheus | Measures |
|---|---|---|
| `network.in.handshake_failures` | `network_in_handshake_failures_total` | Connections dropped before their TLS handshake completed (#897), tagged `reason`: `failed` (the handshake ended in an authentication failure) or `timeout` (the peer did not finish it within the world's five seconds; the auth server's handshake has no timeout, #938). On the world's meter (`world-server`) and on the auth server's (`auth-server`), each counting its own connections |

No TLS is needed to cause one: `failed` is a peer that speaks something else to the port (an HTTP probe, a port scan),
and `timeout` one that connects and says nothing. Each writes one Debug line (the connection and its address) and no
exception; each was an Error, "Failed to establish connection", with a stack trace. `reason` takes those two values
and no other.

`failed` is every handshake that ended in an authentication failure, whichever end caused it: the server cannot tell a
peer that does not speak TLS from a client it could not agree a protocol version or a certificate with. So it has a
server-side cause too. A rate of `failed` that follows client launches rather than scans, with no logins or
admissions behind it, is the server's TLS setup (its certificate, its protocol versions) and not its peers; the line
that would say why is no longer written, so reproduce it with a client.

Not counted: a peer that closes its socket during the handshake (that was, and is, one Debug line as the peer going
away), and a handshake cut short by the server stopping (one Debug line; it was an Error).

### Refused frames

| Metric | Prometheus | Measures |
|---|---|---|
| `network.in.frame_kicks` | `network_in_frame_kicks_total` | Connections closed for a frame that declared more than `Hosting:MaxInboundFrameSize`, or whose length prefix was malformed ([packet protocol](networking-packet-protocol.md#the-inbound-frame-length-limit)). No tags. On the world's meter (`world-server`) and on the auth server's (`auth-server`), each counting its own connections |

A client never sends such a frame, and any peer that completes TLS can, before it is admitted or logged in: a count is
a peer that is not a client, or a client newer than the limit allows. Each also writes one Debug line (the connection,
its address and the declared length), and nothing above Debug.

### Protocol violations

| Metric | Prometheus | Measures |
|---|---|---|
| `network.in.protocol_kicks` | `network_in_protocol_kicks_total` | Connections closed for a frame the peer got wrong (#897, [packet handlers](packet-handlers.md)), tagged `reason`: `malformed_frame` (not a `NetworkPacket`), `sealed_before_key` (flagged `Encrypted` before the session had keys), `seal_not_authentic` (flagged `Encrypted`, and does not authenticate or is shorter than a nonce and a tag) or `undecodable_payload` (not an encoding of its opcode's packet). On the world's meter (`world-server`) and on the auth server's (`auth-server`), each counting its own connections |

`reason` takes those four values and no other: nothing a peer sends becomes a tag. Each count is one connection closed
and one Warning (the connection, its account once it has one, its address, how many packets it had delivered and, but
for `malformed_frame`, the opcode), without the exception. Each
was an Error with a stack trace, but a payload cut short, which was reported at Debug as the peer having closed the
connection. A world's close for a packet that arrives unsealed while it seals
(`Network:PacketEncryption`) is not counted here; it writes its own Warning. Nor is an opcode the server has no packet
type for, which leaves the connection open.

What each reason can mean, and who can cause it. None is only ever the peer's doing: the line and the count say
what arrived, not whose fault it is, and the packets the connection had delivered (in the Warning) tell a peer that
never was a client from a session that worked until then.

| `reason` | Who can cause it | The peer's causes | The server's own |
|---|---|---|---|
| `malformed_frame` | Any peer that completes TLS, on either server, before admission or login | A peer that speaks something else inside TLS; a client that frames a packet another way | A frame the hand-written header parser (`InboundPacketFrame.ParseFrame`) refuses and should not |
| `sealed_before_key` | Any peer that completes TLS, on either server | A peer that flags a frame `Encrypted` without a key exchange; a client that seals before it has the server's answer to its exchange | A server that had not keyed the session when a correct client's sealed packet arrived: the auth server leaves a connection open and unkeyed after a client-info packet whose public key it refuses, and a world keys the session on the tick, after the admission it waits for off the tick |
| `seal_not_authentic` | On the auth server, any peer after a key exchange, which needs no login. On a world, only a connection admitted as an account: before admission its session has no key, and a sealed frame is `sealed_before_key` | A frame altered or made up; a sealed frame shorter than a nonce and a tag; a client that derives another key | A server that derives another key than its clients do: the two cannot be told apart from one connection, and it is every client, at its first sealed packet ([session crypto](crypto-v1-derivation.md)) |
| `undecodable_payload` | Any peer that completes TLS, with a plain frame; with a sealed one, whoever can seal one that opens: on the auth server any peer after a key exchange, on a world an admitted account | A peer that is no client; a client built against another wire schema; a payload nested more than 32 deep | A contract changed so that current clients no longer match it; a packet class whose own code throws one of the five types the decode is classified by ([packet handlers](packet-handlers.md)). A contract that cannot be built at all stops the server from starting instead |

A rate of `seal_not_authentic` or `undecodable_payload` that follows logins or world entries rather than scans, on
connections with few packets delivered, is the two ends disagreeing, and the change to look at may be the server's.

**The byte counters are approximate.** `network.in.bytes` and `network.out.bytes`, on both meters, and each
connection's byte rates (`network.in.bytes.rate`, `network.out.bytes.rate`) count a packet as its payload plus a
constant 10 bytes for the header (`NetworkPacketHeader.Size`), and a sealed packet's nonce and tag with its payload.
The constant is not the header's length on the wire. A frame puts 10 to 16 bytes around its payload: the header
itself (5 bytes plain, 7 sealed or flagged `ClearText`), the frame's length prefix (1 to 3), and the tag and length
in front of the header and of the payload (4 to 6). So a plain packet whose frame is under 128 bytes is counted
exactly, and a sealed or a longer one up to 6 bytes short; TLS's records are counted nowhere. Read them as the
application's bytes, at most 6 bytes a packet under what is framed, not as bytes on the wire. What they count is
deliberately left alone (#883): dashboards read them, and a change would move every series. For exact bytes use
`network.send.bytes`, which counts what a world's send threads hand to their sockets (frames as written, before TLS);
nothing counts received bytes exactly.

`network.in.packets` and `network.in.bytes`, on both meters, count what the read loop decoded. A frame whose opcode
the server has no packet type for is counted in both, though nothing is decoded for it. A frame that arrives
while its connection is closing is read and discarded undecoded
([graceful shutdown](networking-graceful-shutdown.md#what-a-closing-connection-reads)): it adds its length to
`network.in.bytes` and nothing to `network.in.packets`.

### Refused portal entries

| Metric | Prometheus | Measures |
|---|---|---|
| `world.portal_entry.refusals` | `world_portal_entry_refusals_total` | Portal entries (`CMSG_ENTER_MAP`) the world refused (#883, [instanced maps](instanced-maps.md#entermaphandler-flow)), tagged `reason`. On the world's meter (`world-server`) |

`EnterMapHandler` counts one for every request it refuses, where it refuses it. `reason` takes these values and no
other: nothing a client sends, and no character, account or map id, becomes a tag. Like every series on the world's
meter it carries no world of its own: the world is the label the collector gives the pod's series.

| `reason` | The client is sent | Refused because |
|---|---|---|
| `dead` | nothing | the character is dead as the request arrives, or has died by the time its instance is ready |
| `move_in_progress` | `MoveInProgress` | another move is under way: a return to town, a teleport, a respawn, or an earlier portal entry that has not ended |
| `unknown_map` | `MapNotFound` | no map has the id asked for |
| `no_instance` | `MapNotFound` | the character is in no live instance: the server's own fault, also logged at Error |
| `no_portal` | `MapNotFound` | the map the character is on has no portal to the one asked for |
| `not_near_portal` | `NotNearPortal` | the character is outside the portal's radius, by the server's position of it |
| `level_too_low`, `level_too_high` | `LevelTooLow`, `LevelTooHigh` | the character's level is outside the map's band |
| `instance_released` | `MapNotFound` | on arrival: the instance was released before the arrival, twice in a row |
| `not_in_party` | `MapNotFound` | on arrival: the character is no longer in the party the instance is for |
| `instance_full` | `InstanceFull` | on arrival: the party's instance is full |

A client asks for a portal it stands on, of a map it can see, so in ordinary play the series is close to flat. Each of
these refusals writes a Debug line at most, which a world does not write at the level it ships with, so the count is
what shows them:

- **A rate of `not_near_portal`** is clients and the server disagreeing about where characters stand (the server's
  position of a character has stopped following the client's, as it once did after a map transition), or a client
  asking for portals it is not at. The first follows players; the second follows a few connections.
- **`move_in_progress`** is a second request while the first is under way: a client that asks twice, and nothing
  more than that at a low rate.
- **`unknown_map` and `no_portal`** are requests no client's interface makes.
- **`no_instance` and `instance_released`** are the server's own: each also writes an Error or a Warning.

Not counted: a portal entry whose instance could not be built (no reply; logged at Error), one whose character left
the connection before it arrived, and the refusals of the other moves: an item's teleport (`MapTeleport`) answers
through its own code, and a return to town refuses nothing by a reply.

### Packet handlers

Every handler run records `avalon.packet.handler.duration` (ms, tagged `avalon.packet.type` and `avalon.outcome`) and, when it throws, `avalon.packet.handler.errors`. A packet of a high-rate type (`Hosting:Telemetry:NoSpanPacketTypes`, default `CMSG_PLAYER_INPUT` and `CMSG_PONG`) opens no span and no log scope (#875): at 60 inputs a second per player those cost the tick several objects per packet (248 B with a scope-reading provider). The default types' handlers write `{ConnectionId}` and `{PacketType}` into their own lines (`HighRatePacketLog`); a type added to the list loses its handlers' log scope with nothing in its place. Every other packet opens its span and a log scope with `PacketType`, `ConnectionId`, `AccountId` and `CharacterId`.

### Character saves

`world.character.save.duration` (`ms`, Prometheus `world_character_save_duration_milliseconds_bucket`) times one save batch with buckets `5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000`. It is tagged:

- `outcome`: `committed` or `failed`. A cancelled despawn save counts as `failed`.
- `kind`: `despawn` for the save of a character leaving the world, `periodic` for every other save (the periodic save, instance and map entry, and saves of several characters at once).

The span starts once the batch stops waiting behind an earlier save of the same character, so the queue is not in it, and covers the despawn row preparation and the repository write ([inventory and saves](inventory-and-saves.md)).

## Correlated tick phase traces

For bounded, opt-in visibility, tracking and replication phase records correlated with individual slow ticks, see [tick phase traces](tick-profiling.md).
