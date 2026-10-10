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
| `outbox` | `FlushOutbox` over every connection: each connection's queued packets framed and written to its stream |
| `continuations` | `FlushContinuations` over every connection: the off-tick results handed back to the tick |

A stage is usually a small part of a tick, so these buckets start lower (`WorldHistograms.StageMicroseconds`): `25, 50, 100, 250, 500, 1000, 2000, 4000, 8000, 12000, 16667, 25000, 33333, 50000, 100000, 250000, 1000000`. The stages together are the tick's time after the world update. Each stage's mean time per tick: `sum by (stage) (rate(world_post_update_duration_microseconds_sum[1m])) / sum by (stage) (rate(world_post_update_duration_microseconds_count[1m]))`.

The deadline overshoot has its own buckets (`WorldHistograms.OvershootMicroseconds`): it is mostly within the timer's precision, so they split early from late around 0 and stay fine below 1 ms before reaching the same one-second stall:

`-1000, -250, -50, 0, 50, 100, 250, 500, 1000, 2000, 4000, 8000, 16667, 33333, 100000, 1000000`

### Dropped packets

`network.out.dropped` (`{packets}`, Prometheus `network_out_dropped_total`) counts the packets a full outbox evicted, oldest first, to take a newer one; the outbox holds `Hosting:SendBufferCapacity` packets ([configuration reference](configuration-reference.md)). It is tagged with the packet type under `avalon.packet.type` (`avalon_packet_type` in Prometheus), the same key the packet-dispatch telemetry uses. Only these capacity evictions are counted: a packet refused because the outbox is closing is not a drop. Any non-zero rate means a client is being sent more than its connection drains, and what it missed is gone.

### Receive backlog

`world.receive_queue.depth` (`{packets}`, Prometheus `world_receive_queue_depth`) is an observable gauge of the packets received and not yet dispatched by the tick. It reports two series, tagged `stat`: `total`, the sum over every connection, and `max`, the deepest single connection. It is read on the metrics exporter's thread over the connection snapshot, never on the tick. A `total` that keeps climbing means the tick no longer keeps up with what clients send; a high `max` with a low `total` points at one client.

### Packet handlers

Every handler run records `avalon.packet.handler.duration` (ms, tagged `avalon.packet.type` and `avalon.outcome`) and, when it throws, `avalon.packet.handler.errors`. A packet of a high-rate type (`Hosting:Telemetry:NoSpanPacketTypes`, default `CMSG_PLAYER_INPUT` and `CMSG_PONG`) opens no span and no log scope (#875): at 60 inputs a second per player those cost the tick several objects per packet (248 B with a scope-reading provider). The default types' handlers write `{ConnectionId}` and `{PacketType}` into their own lines (`HighRatePacketLog`); a type added to the list loses its handlers' log scope with nothing in its place. Every other packet opens its span and a log scope with `PacketType`, `ConnectionId`, `AccountId` and `CharacterId`.

### Character saves

`world.character.save.duration` (`ms`, Prometheus `world_character_save_duration_milliseconds_bucket`) times one save batch with buckets `5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000`. It is tagged:

- `outcome`: `committed` or `failed`. A cancelled despawn save counts as `failed`.
- `kind`: `despawn` for the save of a character leaving the world, `periodic` for every other save (the periodic save, instance and map entry, and saves of several characters at once).

The span starts once the batch stops waiting behind an earlier save of the same character, so the queue is not in it, and covers the despawn row preparation and the repository write ([inventory and saves](inventory-and-saves.md)).
