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

The client sees the same measurement as `world.tick.duration` (#1016): every time-sync ping carries `TickMicros`, the mean of that duration over the ticks that ended in the last completed window of about one second, for the whole world (`WorldServer.MeanTickMicros`; 0 until the first window closes). It adds no clock read and no allocation to the tick, only a sum and a count, with one division a window (`TickWindow`), and the same windows give `world.tick.rate`: their ticks over their length ([the time sync](networking-packet-protocol.md#time-sync-smsg_ping-and-cmsg_pong)).

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

### Silent connections

| Metric | Prometheus | Measures |
|---|---|---|
| `network.in.silent_kicks` | `network_in_silent_kicks_total` | Admitted connections closed for answering no time-sync ping for 60 seconds, counted from the admission or the last answer (#1034, the liveness rule, [packet protocol](networking-packet-protocol.md#time-sync-smsg_ping-and-cmsg_pong)): closed without a notice through the drain a disconnect takes (session end, save, lease released). No tags. On the world's meter (`world-server`) |

A client answers every ping in every state, so a count is a client that died or hung without its socket closing, a
peer holding a session open without speaking (or trickling a TLS record), or a network fault that cut many clients off
at once (TCP keepalive closes those too, within about 90 s, and the rule usually gets there first). Each close is also
an Information line (`Avalon.World.WorldConnection`) naming the connection, its account and how long it was silent, at
most one every ten seconds for the whole world, with the number held back since the last.

### Session ends not sent

| Metric | Prometheus | Measures |
|---|---|---|
| `world.session_ends.skipped` | `world_session_ends_skipped_total` | Game session ends a drain did not send to identity (#1051), decided as each reached the front of its lane (`WorldDatabaseWork.SessionEnds`), tagged `reason`: `expired` (the lease had run out more than `WorldServer.SessionEndSkew`, 5 s, ago on both the wall and the monotonic clock, and every renewal asked for was applied, so the session holds nothing an end would release) or `stopping` (the host's stop timeout had passed; a stop that leaves any writes one Warning with the count). Each such session runs out with its lease ([game server admission](steam-authentication-workloads.md#session-activation-and-durable-save-authority)) |

`expired` counts rise when identity answers ends slower than sessions end; `stopping` means a stop gave up on identity.

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
| `network.in.protocol_kicks` | `network_in_protocol_kicks_total` | Connections closed for a frame the peer got wrong (#897, [packet handlers](packet-handlers.md)), tagged `reason`: `malformed_frame` (not a `NetworkPacket`), `sealed_before_key` (flagged `Encrypted` before the session had keys), `seal_not_authentic` (flagged `Encrypted`, and does not authenticate or is shorter than a nonce and a tag), `undecodable_payload` (not an encoding of its opcode's packet), `frame_timeout` (a frame started and not complete within `Hosting:InboundFrameTimeoutSeconds`, #938) or, on a world only, `bad_public_key` (a world that seals was sent an admission whose public key is the right length and not a P-256 point; counted by the admission, not the read loop, #878). On the world's meter (`world-server`) and on the auth server's (`auth-server`), each counting its own connections |

`reason` takes those values and no other: nothing a peer sends becomes a tag. Each count is one connection closed
and one Warning (the connection, its account once it has one, its address, how many packets it had delivered and, but
for `malformed_frame` and `frame_timeout`, the opcode), without the exception. Each
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
| `sealed_before_key` | Any peer that completes TLS, on either server | A peer that flags a frame `Encrypted` without a key exchange; a client that seals before it has the server's answer to its exchange; any client that seals on a world with `Network:PacketEncryption` off, which runs no exchange (#878), before admission or after | A server that had not keyed the session when a correct client's sealed packet arrived: the auth server leaves a connection open and unkeyed after a client-info packet whose public key it refuses, and a world keys the session on the tick, after the admission it waits for off the tick |
| `seal_not_authentic` | On the auth server, any peer after a key exchange, which needs no login. On a world that seals, only a connection admitted as an account: before admission its session has no key, and a sealed frame is `sealed_before_key` | A frame altered or made up; a sealed frame shorter than a nonce and a tag; a client that derives another key | A server that derives another key than its clients do: the two cannot be told apart from one connection, and it is every client, at its first sealed packet ([session crypto](crypto-v1-derivation.md)) |
| `undecodable_payload` | Any peer that completes TLS, with a plain frame; with a sealed one, whoever can seal one that opens: on the auth server any peer after a key exchange, on a world that seals an admitted account | A peer that is no client; a client built against another wire schema; a payload nested more than 32 deep | A contract changed so that current clients no longer match it; a packet class whose own code throws one of the five types the decode is classified by ([packet handlers](packet-handlers.md)). A contract that cannot be built at all stops the server from starting instead |
| `bad_public_key` | On a world that seals, a client holding a join ticket it can redeem: the key is found to be no point only once the ticket is spent and the session keyed, off the tick | A client whose key encoding or curve arithmetic is wrong; a peer that holds a ticket and is no client | A server whose key validation (`AsymmetricCipher.GetPublicKeyFromBytes`) refuses keys that are good. Anything else that fails while the session is keyed from a key that parses is the server's, and logged at Error with its exception |

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

Not counted: a portal entry whose instance could not be built (no reply; logged at Error, or, for a build abandoned at
its deadline, a system line and a Warning, counted under `world.instance.build_timeouts`), one whose character left
the connection before it arrived, and the refusals of the other moves: an item's teleport (`MapTeleport`) answers
through its own code, and a return to town refuses nothing by a reply.

### Instance build timeouts

| Metric | Prometheus | Measures |
|---|---|---|
| `world.instance.build_timeouts` | `world_instance_build_timeouts_total` | Instance builds abandoned at their 30 s deadline (#969, `InstanceRegistry.BuildDeadline`, [instanced maps](instanced-maps.md#build-deadline)), every request waiting on one answered with the failure. Tagged `map.type` (`Town` or `Normal`; a party's instance is `Normal`). On the world's meter (`world-server`) |
| `world.instance.builds_abandoned_running` | `world_instance_builds_abandoned_running` | Observable gauge (`{builds}`): builds abandoned at their deadline whose bake has not ended yet. Nothing cancels a bake (owner decision), so each still holds a thread-pool thread or a bake's memory; under load abandonments can stack. Up by one at each abandonment, down by one when its late outcome arrives (an instance discarded or a failure), read on the exporter's thread. No tags. On the world's meter (`world-server`) |

A server fault, never a player's: a build that hangs (a navmesh bake, #924, or the thread pool it shares, #960). Any
count is worth an alert, and a `builds_abandoned_running` that keeps climbing is bakes that never end, piling up. Each is counted once, on the tick, and writes one Error (`Avalon.World.Instances.InstanceRegistry`)
naming the build's kind, map and owner, how many requests were waiting and how many abandoned builds are still running
(that one included); a build that ends after it was abandoned
writes a Warning naming the instance it discarded, or its failure, and how long it took.

### Work queue refusals and unserved admissions

| Metric | Prometheus | Measures |
|---|---|---|
| `world.work.refusals` | `world_work_refusals_total` | Operations a full work queue (`WorldDatabaseWork`) refused (#1050), counted in the queue as it refuses them, tagged `lane`. On the world's meter (`world-server`) |
| `world.admission.failures` | `world_admission_failures_total` | Admissions (`CMSG_GAME_ADMISSION`) whose redemption threw, or was cancelled, once started, answered `ServiceUnavailable` and closed (#1050). No tags. On the world's meter |
| `world.admission.source_refusals` | `world_admission_source_refusals_total` | Admissions refused before their ticket was redeemed because their source was over its budget (#1052), answered `ServiceUnavailable` and closed. Tagged `reason`: `in_flight` (`World:Admission:MaxInFlightPerSource`, or its IPv6 /56's `MaxInFlightPerIPv6Block`) or `rate` (`MaxPerSourcePerMinute`, or its IPv6 /56's `MaxPerIPv6BlockPerMinute`); no address becomes a tag. On the world's meter |
| `world.admission.backstop_refusals` | `world_admission_backstop_refusals_total` | Admissions the world's backstop refused (#967): the world was over its `Game:PlayerLimit` plus margin and the account not staff, answered `WorldFull` and its session ended as `backstop`. Should stay 0: any value above it means identity's place ledger was wrong or lost, or identity's stored limit is above the world's `Game:PlayerLimit` ([the backstop](world-player-limit.md#the-backstop)); each refusal also writes one Error, at most once per 10 s. No tags. On the world's meter |

`lane` takes a fixed set of values, nothing a client sends; each names one queue, and each queue has its own capacity:

| `lane` | The queue | A refusal means | The world answers | Logged |
|---|---|---|---|---|
| `admission` | `WorldDatabaseWork.Admission`: 128 redemptions outstanding, 32 of them running | An admission never started, so it never reached the identity service | `ServiceUnavailable` and the close, at once, on the receive path that delivered the admission, as the refusals before a ticket is spent are: nothing reaches the tick | One Warning (`Avalon.World.WorldConnection`), without an exception, at most once per 10 s for the world, with how many more were refused since the last |
| `heartbeat` | `WorldDatabaseWork.Heartbeat`: 8,192 outstanding, 128 running | A live player's lease heartbeat not sent; the next is tried 15 s later, and three in a row end the lease ([game server admission](steam-authentication-workloads.md)) | Nothing | Nothing. Any count is worth a look: the lane is sized above any player count a world carries |
| `select` | The character select's own queue, the one the container hands it (256 outstanding, 64 running, the database lane's capacity): every read and the one write of the select chain, and the world entry check before it | A step of the select, or its entry check: the select is cancelled and the connection closed at once as busy, `SelectTimeout` with "The world is busy; try again", the stalled-select sweep's answer, which it got 15 s later before. An entry check that ran and failed (it threw, or took more than 5 s) still closes with `Maintenance` (#821): a verdict could not be read, so entry fails closed | One Warning (`Avalon.World.Handlers.CharacterSelectHandler`), without an exception, under the same bound, shared with the final spawn's |
| `database` | `WorldDatabaseWork.ThreadPool`: 256 outstanding, 64 running: character create and delete, and the world entry check before the final spawn | The final spawn's entry check: the connection is closed as busy, as a select is. A create or a delete: its continuation is dropped | The final spawn's busy close as the select's, under the logger of what released it (the load report's handler or the tick); a dropped continuation one Warning (`Avalon.World.WorldConnection`), without an exception, under the same bound |
| `session_ends` | `WorldDatabaseWork.SessionEnds` (#1051): 16 running, the rest queued without limit | Never counted: the lane refuses nothing, since a refused end would hold the account until its lease runs out. The ends it does not send are counted in `world.session_ends.skipped` ([above](#session-ends-not-sent)) | Nothing | Nothing |

`admission` refusals are expected when many clients reconnect at once (a world restart, an identity service restart):
each such client is told to try later. A rate that lasts outside such a storm is a world whose redemptions are slow
(the identity service, or the path to it). A refusal costs no exception and no task of its own: the queue hands back
one faulted task, made once, which callers recognise by reference (`WorldDatabaseWork.IsRefusal`); its shared
exception's stack names whichever caller rethrew it last, so nothing logs it. Before #1050 each refused admission, and
each refused select step, was an Error with its stack ("Continuation faulted"), 3,231 in a minute on the load-test
world; the admission's client got no answer until the 15-second admission timeout closed it, and the select's waited
15 s for the sweep. The heartbeats shared the admission lane until then.

`world.admission.failures` is a defect: the admission client turns every transport, timeout and parsing failure into
an answer of its own. It is counted on the thread pool, where the redemption ended, so a client that went away before
its answer is counted too; the answer goes out on the tick. Each also writes one Error with the exception, at most
once per 10 s for the world. Any other tick continuation whose task faulted stays an Error with its exception
("Continuation faulted"), under the same bound; a continuation whose work a full queue refused is a Warning without
it, as above. Every 10 s bound is timed on the monotonic clock, so a wall clock set back cannot mute a line.

`world.admission.source_refusals` with `reason` `in_flight` is one source holding `World:Admission:MaxInFlightPerSource`
admissions (16 by default) whose redemptions have not ended, or one IPv6 /56 holding `MaxInFlightPerIPv6Block` (32);
with `rate`, one source that started `MaxPerSourcePerMinute` (120) in the last minute, or one /56 that started
`MaxPerIPv6BlockPerMinute` (480). Either is a flood of join
tickets from one address, or many clients behind one address (carrier-grade NAT) reconnecting at once while
redemptions are slow ([configuration](configuration-reference.md#world-admission-budget)): after a restart, refusals
from addresses that are not floods mean the defaults are too low. Each also writes one Warning
(`Avalon.World.WorldConnection`), without an exception, at most once per 10 s for the world, naming the source of the
one it logs as it is counted (an IPv4 address, an IPv6 /64, or the /56 its block cap counts, as the world's other
connection lines name the address), the limit it reached, and how many more were refused since the last line, from
any source. During a
load-test ramp a count here means the bot PC is not exempt (`World:Admission:ExemptSources`). A refusal here never
reaches the admission lane, so it is not also counted under `world.work.refusals`.

Not counted: an admission the identity service answered, whatever it answered (its own "try later" included), and
one refused before its ticket was spent (`InvalidRequest`).

### Packet handlers

Every handler run records `avalon.packet.handler.duration` (ms, tagged `avalon.packet.type` only, whether the handler succeeded or threw) and, when it throws, `avalon.packet.handler.errors` (tagged `avalon.packet.type`), which is how failures are counted; the span, where there is one, carries `avalon.outcome` (`ok` or `error`). A packet of a high-rate type (`Hosting:Telemetry:NoSpanPacketTypes`, default `CMSG_PLAYER_INPUT` and `CMSG_PONG`) opens no span and no log scope (#875): at 60 inputs a second per player those cost the tick several objects per packet (248 B with a scope-reading provider). The default types' handlers write `{ConnectionId}` and `{PacketType}` into their own lines (`HighRatePacketLog`); a type added to the list loses its handlers' log scope with nothing in its place. Every other packet opens its span and a log scope with `PacketType`, `ConnectionId`, `AccountId` and `CharacterId`.

What the duration costs per packet with the SDK listening, measured for #977: about 190 to 290 ns per input on a developer machine while it also carried `avalon.outcome`, almost all of it the SDK finding the series of a measurement with two tags (one tag costs about 36 ns, none about 13). The owner dropped the outcome tag from the duration (#977), which brought a dispatched input to about 80 ns; before it, the histogram had an `ok` and an `error` series per type, merged since. The sent-packet counters (`network.out.packets`, `network.out.bytes`) cost about 20 ns per packet sent; the ways to make them cheaper are a design choice, recorded in [benchmarks](benchmarks.md#per-connection-work-every-tick--connectiontickbenchmarkscs-questflushbenchmarkscs-packetmetricsbenchmarkscs-977).

### Script load contexts

`avalon.world.scripts.load_contexts` (`{contexts}`, Prometheus `avalon_world_scripts_load_contexts`) is an observable gauge of the hot-reload script load contexts still in memory (#930), one per compile of the hot-reload scripts ([world simulation](world-simulation.md)). It reports two series, tagged `state`: `current`, the contexts not unloaded (the generation in use, and for a moment a compile not yet handed over), and `unloaded`, the replaced ones the runtime has not collected yet. It is read on the metrics exporter's thread, counting weak references under their own lock, never on the tick. `current` is 0 before the first hot reload and 1 after it. `unloaded` rises at each reload and falls back once the old generation's scripts stop running and a collection runs; one that never falls is a generation something still holds, which the world-simulation page lists the usual holders of.

### Character saves

`world.character.save.duration` (`ms`, Prometheus `world_character_save_duration_milliseconds_bucket`) times one save batch with buckets `5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000`. It is tagged:

- `outcome`: `committed` or `failed`. A cancelled despawn save counts as `failed`.
- `kind`: `despawn` for the save of a character leaving the world, `periodic` for every other save (the periodic save, instance and map entry, and saves of several characters at once).

The span starts once the batch stops waiting behind an earlier save of the same character, so the queue is not in it, and covers the despawn row preparation and the repository write ([inventory and saves](inventory-and-saves.md)).

## World places and the queue (identity)

Identity's metrics of each world's places and line (#967, [world player limit](world-player-limit.md#metrics)), on the
API's meter (`avalon-api`), each tagged `world`. The gauges are sampled every 15 s for each configured world by
identity's `WorldPlaceGauges`, which sweeps what expired as it does; a world whose sample fails is left out until one
succeeds.

| Metric | Prometheus | Meaning |
|---|---|---|
| `avalon.world.places.taken` | `avalon_world_places_taken` | Gauge: places taken (in the world, at character select, holding a ticket or a turn, held after a drop). Not `avalon.world.players.online`, which counts characters in the world |
| `avalon.world.places.held` | `avalon_world_places_held` | Gauge: places held after a dropped connection |
| `avalon.world.queue.length` | `avalon_world_queue_length` | Gauge: accounts waiting in the line |
| `avalon.world.places.limit` | `avalon_world_places_limit` | Gauge: the stored player limit, only for a world that has one |
| `avalon.world.queue.outcomes` | `avalon_world_queue_outcomes_total` | Counter, tag `reason`: `entered`, `cancelled`, `timed_out`, `turn_expired`, `refused_at_cap` |
| `avalon.world.queue.wait` | `avalon_world_queue_wait_seconds_bucket` | Histogram: seconds in the line of those who entered |

On a world with a limit, `places.taken` at the limit with `queue.length` above zero is a full world working as meant;
`outcomes{reason="timed_out"}` rising fast means waiters stop checking in (a client that does not poll), and
`refused_at_cap` that the line is at `Application:WorldPlaces:QueueCapacity`. The world's own
`world.admission.backstop_refusals` is above, beside the world's admission counters.

## Correlated tick phase traces

For bounded, opt-in visibility, tracking and replication phase records correlated with individual slow ticks, see [tick phase traces](tick-profiling.md).
