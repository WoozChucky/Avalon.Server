# Performance Benchmarks

Benchmarks live in `tools/Avalon.Benchmarking/` and are run with BenchmarkDotNet.

Whole-tick numbers (tick-thread allocations per tick, tick times and GC for fixed world scenarios) come from the
scenario runner instead, and the allocations are gated in CI: see [Scenario baseline](#scenario-baseline) at the end.

```bash
# Run all benchmarks
dotnet run -c Release --project tools/Avalon.Benchmarking

# Run a specific suite
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*TickLoop*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*EntityTracking*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*Serialization*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*SessionCipher*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*ConnectionKeyExchange*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*PacketEncoder*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*PacketSerializationGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*PacketReaderGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*WorldPacketQueueGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*PacketReaderDecryptGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*GetContextPacketGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*CallListenerGc*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*TickThreadGuard*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*ConnectionTick*" --job short
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*ReceiveQueueBenchmarks*" --job short
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*PacketStreamBenchmarks*" --job short
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*TimeSync*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*ConnectionSend*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*AbilityScriptBuild*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*ActiveScriptLookup*"
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*InventoryFlush*" "*ItemRequest*" --job short
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*ArrivalBenchmarks*" --job short
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*AuraScanBenchmarks*" --inProcess --warmupCount 5 --iterationCount 8
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*SocialEventBenchmarks*"

# Process-wide allocations and retained memory across the navmesh tile workers
dotnet run -c Release --project tools/Avalon.Benchmarking -- navmesh-bake 24

# Not BenchmarkDotNet: the send path over real loopback sockets, the tick against the send threads (#875)
dotnet run -c Release --project tools/Avalon.Benchmarking -- outbox-flush 50,200,500,1000 1 1800 memory,tcp,tls both
```

---

## Suites

### Arrival in a map — `Benchmarks/ArrivalBenchmarks.cs` (#883)

What one character's arrival in a map costs the tick, through the code that handles it and up to the last packet it
sends: a `CMSG_ENTER_MAP` through `EnterMapHandler` and its arrival continuation, into the forest and into the town; a
`TownReturn` (a respawn, a scroll, a party's leave countdown); a `CMSG_CHARACTER_SELECTED` through the whole select
chain to the pending spawn; and the five portal entries the handler refuses, each of which a client can send as often
as it likes. No scenario has an arrival in its measured window, so the allocation gate never saw this path.

Three things on it allocated for nothing, until #883:

- **The chunk layout packet's entries, built again for every arrival, in four copies of one block.** A portal entry,
  a return to town, a select and an item's teleport each turned the instance's layout into the packet's entries with
  two queries, a closure each, and an entry per chunk and per portal; the packet's factory then copied both lists.
  An instance keeps the layout it was built from, and the chunk library that names the chunks is loaded once, so the
  entries are now built once, with the instance (`MapInstance.LayoutEntries`, `MapLayoutEntries`: its constructor
  makes them, off the tick, where the instance is built), and the same two lists are written into every packet, from
  one place (`MapArrival`). The packet's thread-local message holds them while it is encoded and is cleared after;
  nothing changes an entry.
- **The map template, found with a query.** `MapTemplates.FirstOrDefault` with a closure, and at two sites a
  `MapTemplateId` made for every template walked; the portal the same way. The map manager now holds the templates
  with a lookup by id (`KeyedRows`, as the item templates and the level rows have; `MapTemplateRows.FindById`), and
  the portal is found by a walk of the instance's portals.
- **A refusal's log line.** Four `LogDebug` calls that made an array of their arguments with Debug off, and a
  diagnostic left at Warning that boxed two positions, two floats and a `Guid` for every request from a character
  away from its portal, and wrote a line for each. They are `[LoggerMessage]` methods at Debug now.

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*ArrivalBenchmarks*" --job short
```

The instances are real: the town as the World server builds it from `Maps/TownLayouts/1.json` (4 chunks, 1 portal)
and a forest off its generator on seed 7 (19 chunks, 1 portal). Everything around the handlers is hand-written and
holds its answers, so a row is the handler and what it sends: the transfer only writes the character's instance id
(`World.TransferPlayer`'s membership change is not here), a save is a task already finished, and a select reads an
empty inventory and no abilities from tasks already finished, so its row is mostly the character it builds. Nothing
the benchmark does to put the character back allocates. The instances of `ArrivalBenchmarks` are built once, so its
rows arrive where others have arrived before; `FirstArrivalBenchmarks`, in the same file and under the same filter,
arrives in a new instance of the same layout on every call (the 1,024 instances of an iteration are built before it,
without creatures), which is what a character's own forest is on every entry outside its re-entry window.

**Allocated per call.** Measured 2026-10-13, BenchmarkDotNet 0.15.8, Windows 11, i9-12900K, SDK 10.0.401 / runtime
10.0.12, concurrent workstation GC. Each column is the same benchmark over one step of the change, in order: the code
as it was (`808eec90`, where the world's templates are the list the map manager held then), then with the lookup by
id, then with the entries built once, then with the log lines. Three runs for the first and the last, two for the two
between; every run of a step read the same, with one exception noted below. The last column is also what the
finished change reads (two more runs, with the entries built in the constructor, the move marked and the refusals
counted), but for the select, whose figure moves with main: the table's select is at `808eec90`. On the base the
change now sits on (`e908a63f`) a select reads 29,978 B as it was (the benchmark over the original code, two runs)
and 29,154 B at the branch's tip (two runs), the same 824 B less; the other rows read the same there.

| | As it was | Templates by id | Entries built once | Log lines |
|---|---:|---:|---:|---:|
| `Portal_entry_into_the_forest` (19 chunks) | 2,216 B | 2,112 B | 504 B | 504 B |
| `Portal_entry_into_the_town` (4 chunks) | 1,352 B | 1,272 B | 504 B | 504 B |
| `Return_to_town` | 1,744 B | 1,648 B | 880 B | 880 B |
| `Character_select` | 30,002 B | 29,946 B | 29,178 B | 29,178 B |
| `Refused_from_a_dead_character` | 56 B | 32 B | 32 B | 0 B |
| `Refused_while_a_move_is_under_way` | 56 B | 32 B | 32 B | 0 B |
| `Refused_for_an_unknown_map` | 160 B | 56 B | 56 B | 0 B |
| `Refused_without_a_portal` | 136 B | 56 B | 56 B | 0 B |
| `Refused_too_far_from_the_portal` | 352 B | 248 B | 248 B | 0 B |

- **The layout cost 1,608 B for 19 chunks and 768 B for 4**, 56 B a chunk on top of 544 B: an entry (40 B) and its
  place in the list the handler built and in the copy the factory made. A portal entry is now 504 B whatever the
  size of the map it leads to, whether or not anyone arrived in the instance before and whether or not the instance
  was built yet when it was asked for (both below); a return to town or a select is 768 B less.
- **A refused portal entry allocates nothing.** It cost 56 to 352 B, and at Warning one written line for every
  request from too far away.
- **The lookups** were 80 to 104 B of a portal entry that got as far as looking a template up, arrived or refused,
  96 B of a return to town and 56 B of a select; and 24 B of every other `CMSG_ENTER_MAP`: the closure the handler's
  two queries shared was made as the handler was entered.
- One of the three runs of the code as it was read `Return_to_town` at 1,808 B, 64 B more: the size of one delegate,
  in code that made two for its template queries, and optimised code keeps some delegates off the heap. Nothing in
  the last column moved between runs.
- **What is left of an arrival** was not taken apart by measurement. By reading: the two continuations' closures (the
  instance request and the save), the instance id written on the row as text, the entry spawn's `Vector3Dto`, and
  the arguments of the Information line each arrival writes.

**The first arrival in an instance costs what any other does: 504 B.** `First_portal_entry_into_a_forest` and
`First_portal_entry_into_the_town` (`FirstArrivalBenchmarks`, two runs) read 504 B each, the figure of the rows
above. The entries are not built by an arrival at all: the instance's constructor builds them, and
`ChunkLayoutInstanceFactory` runs it off the tick, after the layout is generated or read and the navmesh baked.

| Where the entries' bytes go | Before #883 | Now |
|---|---|---|
| Built | by every arrival, on the tick | once, in the instance's constructor, off the tick |
| Allocated | 544 B and 56 B a chunk per arrival: 768 B for the town, 1,608 B for the 19-chunk forest | 272 B and 48 B a chunk per instance: 464 B for the town, 1,184 B for that forest, 1,136 to 1,568 B for forests of 18 to 27 chunks |
| Lifetime | garbage as soon as the packet was encoded | the instance's: freed with it |

The second row's figures are a probe, not a benchmark: an instance without creatures constructed with and without a
chunk library in its container (the least of 200 constructions each, Release), 15,224 B without and the entries more
with, for the town and for forests on seven seeds, taken while an instance could still be built without one. An
instance pays them whether or not anyone ever arrives, and none is built without them: the constructor requires the
chunk library.

Each call of the two rows takes 6.3 to 7.3 us into a forest and 4.2 to 4.3 us into the town, against 1.8 to 2.3 us
and 1.1 to 1.3 us for the rows that arrive in one kept instance: every call reads an instance, its portals and its
entries for the first time, where the kept instance's are in the processor's caches, as a real first arrival finds
them.

**A portal entry marks the move under way** from the moment it is accepted until it ends
([instanced maps](instanced-maps.md#a-portal-entry-is-a-move-under-way)), and counts each refusal
(`world.portal_entry.refusals`, [instrumentation](instrumentation.md#refused-portal-entries)). Both are in the last
figures: an accepted entry whose instance request has already finished is still 504 B, and a refusal still 0 B with
its count in place (one tag, passed as a struct; no benchmark here attaches a listener to the meter).

**An entry whose instance is still being built costs the same: 504 B.** That is the usual entry of a character's
own forest, which starts the build. `Portal_entry_into_a_forest_still_being_built` (`FirstArrivalBenchmarks`) hands
the handler a request that is not finished, completed as the registry completes its own, publishes the build and
runs the arrival once the continuation the connection was handed is ready.

| A portal entry asked for while its instance is being built | Allocated | The arrival runs |
|---|---:|---|
| The request wrapped in a task that cannot fault, with the state to log a failed build (as first written here) | 664 B | once a thread-pool thread has run the wrapper: the first flush after that |
| The same wrapper without state, as `TownReturn` and `MapTeleport` have it (a failed build's exception is not logged) | 624 B | the same |
| Queued to run whatever the request's outcome (`ISettledContinuations`), as it is now | 504 B | in the flush of the tick that published the build |

The arrival must run for a build that failed too, since it is what ends the move, and the connection's ordinary
continuation drops the callback of a task that failed. The first two rows got it run by wrapping the request, which
allocated the wrapper on the tick (160 B and 120 B over the same arrival into an instance already built, two runs
and one) and left the arrival waiting for the thread pool, where the server also bakes navmeshes: the row waits for
its continuation to be ready, and took 8.9 to 24.5 us a call that way on an idle pool. The connection now has a
queue entry the drain runs whatever its task's outcome: one object, as an ordinary continuation is, ready the moment
the registry completes the build's task. The row reads 504 B (two runs), the figure of the same arrival with the
build already finished.

**Time per call**, the mean of each run. The machine was shared with other builds and test runs: for the arrivals
the spread between two runs of the same code is as large as the difference between the columns, so the claim is only
that nothing is slower, for every row: short jobs on a shared machine do not show more. The first run of the code as
it was is BenchmarkDotNet's default job; every other run is `--job short`. The second column is the finished change.

| | As it was, each run | Now, each run |
|---|---:|---:|
| `Portal_entry_into_the_forest` | 2,197 / 5,021 / 2,478 ns | 2,267 / 1,832 ns |
| `Portal_entry_into_the_town` | 1,660 / 2,006 / 1,144 ns | 1,303 / 1,073 ns |
| `Return_to_town` | 1,691 / 1,530 / 1,408 ns | 956 / 1,061 ns |
| `Character_select` | 6,507 / 5,695 / 6,266 ns | 7,505 / 5,396 ns |
| `Refused_from_a_dead_character` | 36.7 / 31.1 / 32.0 ns | 6.6 / 5.9 ns |
| `Refused_while_a_move_is_under_way` | 132.0 / 152.8 / 122.4 ns | 90.9 / 112.9 ns |
| `Refused_for_an_unknown_map` | 224.4 / 180.3 / 169.2 ns | 145.6 / 118.4 ns |
| `Refused_without_a_portal` | 219.7 / 187.9 / 304.1 ns | 155.0 / 118.7 ns |
| `Refused_too_far_from_the_portal` | 259.7 / 250.1 / 433.0 ns | 127.1 / 125.3 ns |

**The packets are what they were.** `MapArrivalShould` arrives in a real town, a real forest and a party's forest
through each of the four paths, twice, and holds the chunk layout each is sent to the layout byte for byte, both from
the entries the instance was built with. It was written first and passed over the four blocks as they were. It also
holds the transition a portal entry, a return to town and a teleport send before the layout to the instance, the
map, its template's name and description and the layout's entry spawn.

**Five other queries over the map templates** became lookups by id with the same change: a logout from a normal map
(two), the respawn town of a logout while dead (two) and the start of an instance's build. The manager's templates
are no longer a `List`, which a query walks without an enumerator, so each would have gained one: 32 B by a probe of
the same call over the keyed rows, 0 B by id.

**The allocation gate does not move.** The gate alone, after the change: `forest-combat` 10,357 B per window in Debug
and in Release, the committed figure, and the three town scenarios 0. No scenario has an arrival in its measured
window, so `perf/scenario-allocations.json` is not regenerated. The scenario harness now registers the chunk library
for the forests it builds through the instance factory, so they are built with their entries as the server's are:
build cost, outside every measured window.

### Inventory update and item requests — `Benchmarks/InventoryFlushBenchmarks.cs`, `Benchmarks/ItemRequestBenchmarks.cs` (#883)

The two inventory paths a client's own actions drive: the tick's `SMSG_INVENTORY_UPDATE` for a connection whose
character's inventory changed (`InventoryUpdateFlusher.Flush`), and a `CMSG_ITEM_MOVE` or `CMSG_ITEM_DESTROY`
through its handler to the one `SMSG_ITEM_RESULT` it is answered with. Until #883 the flusher filtered, ordered and
read the changed slots with a query (`Where`, `OrderBy`, `ThenBy`, `Select`, `ToArray`), the handlers put the slots
a request named in a new list, and the reply to a refusal ran a second query over that list (`Distinct`, two
`Where`, `Select`, `ToArray`). Now each counts its slots, allocates the packet's array at that size and fills it:
the flusher walks the set of changed slots with the set's own enumerator and sorts the array in place by container,
then slot; a handler names its slots in a span on its own stack; the reply drops a slot named twice by looking back
through that span. Nothing is kept between calls, so there is no buffer to share, to clear or to leak from one
connection to the next.

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*InventoryFlush*" "*ItemRequest*" --job short
```

Measured 2026-10-13, BenchmarkDotNet 0.15.8, `--job short`, Windows 11, i9-12900K, SDK 10.0.401 / runtime 10.0.12,
concurrent workstation GC, with other builds and test runs loading the machine. The allocations are exact and the
same in every run. The means move with the load by more than the change is worth: each cell gives the mean of each of
three runs, and no claim is made from them beyond "not slower".

**The flush.** `Queried` is the flush as it was, kept in the benchmark; `Flusher` is `InventoryUpdateFlusher.Flush`.
One invocation marks the Bag slots changed, highest first, and flushes them to a connection that counts the packet
and gives its payload back to the pool; with no slot, only the money changed. Both rows come from the same run, one
after the other, each in its own child process, so a ratio compares two processes under whatever the machine was
doing at the time.

| Changed slots | `Queried` (before), allocated | `Flusher` (after), allocated | `Queried`, mean of each run | `Flusher`, mean of each run | Ratio in each run |
|---:|---:|---:|---:|---:|---:|
| 0, money only | 448 B | 0 B | 334.3 / 199.0 / 199.1 ns | 208.9 / 183.1 / 137.6 ns | 0.64 / 0.94 / 0.69 |
| 1 | 872 B | 120 B | 583.5 / 364.0 / 365.1 ns | 429.7 / 271.5 / 239.8 ns | 0.74 / 0.75 / 0.66 |
| 8 | 1,600 B | 792 B | 2,217 / 1,744 / 1,370 ns | 1,809 / 1,187 / 1,272 ns | 0.82 / 0.69 / 0.94 |
| 30 | 3,960 B | 2,904 B | 6,958 / 4,415 / 6,848 ns | 4,826 / 3,397 / 5,859 ns | 0.69 / 0.77 / 0.86 |

What is left is the packet's own entries, to the byte: the array (24 B and 8 B a slot), an `InventorySlotUpdateDto`
for each slot (32 B) and an `ItemSlotDto` for each slot holding an item (56 B). The query cost 448 B when it had
nothing to walk, on every tick a character's money changed and no slot did, and 752 B, 808 B and 1,056 B on top of
the entries at 1, 8 and 30 slots.

**The quest flusher's bag check.** `QuestFlusher.TouchedTheBag` runs for every connection on every tick, to see
whether a Bag slot changed. It walked the changed slots through the set's interface, which boxed the set's enumerator:
40 B per connection on every tick with any slot change, a cost the finding did not list. It now uses the struct
enumerator on `InventoryClientChanges`. No benchmark or scenario runs the quest flusher, so the figures are from a
probe of the same loop over the built class (Release, best of 7 runs of 20 million calls, loaded machine): with no
changed slot, 0 B and about 1.7 ns before and after; with three slots outside the Bag, 40 B and 30 to 58 ns before,
0 B and about 4 ns after.

**An item request.** The handlers run over an economy that hands back one inventory service kept for the character,
so a row is the handler, the move rules and the reply; production's `CharacterEconomy.InventoryOf` built a service
and its template lookup for every request on top of this, which this change did not touch (104 B, gone since: see
[the gameplay requests](#gameplay-requests-quests-aura-hooks-and-corpses--the-allocation-tests-benchmarksencounterlookupbenchmarkscs-883)). `Accepted_move` moves a
stack between two Bag slots, there and back on alternate calls, and is answered Ok with no slots; `Refused_move`
aims a stack at a full one (TargetFull, both slots reported); `Refused_destroy` names an empty slot (NotFound, that
slot reported). Before is the same benchmark over `507b37d8`, in runs of its own.

| | Before, allocated | After, allocated | Before, mean of each run | After, mean of each run |
|---|---:|---:|---:|---:|
| `Accepted_move` | 96 B | 0 B | 325.5 / 289.8 / 323.4 ns | 281.4 / 245.3 / 316.5 ns |
| `Refused_move` | 1,136 B | 216 B | 877.4 / 571.0 / 888.3 ns | 485.8 / 682.2 / 787.0 ns |
| `Refused_destroy` | 984 B | 64 B | 458.6 / 431.5 / 485.1 ns | 166.5 / 301.0 / 311.7 ns |

An accepted request paid for the list (32 B), its array (40 B) and the closure of the reply's query (24 B, allocated
on entry, though only a refusal runs the query); a refusal paid those 96 B and 824 B for the query itself, 920 B on
top of its entries. What a refusal allocates now is its entries: 216 B for two slots holding items, 64 B for one
empty slot.

`InventoryAllocationShould` pins both paths in the World suite: a money-only flush and an accepted reply at 0 B, and
a flush of changed slots and a refusal at exactly what building their entries costs, measured in the test rather
than written down. It runs in the collection that runs alone in the assembly, and reads the fewest of three windows:
a send takes its payload segment from the process's one pool, and in a whole-suite run a test sending on another
thread took the free segment between two sends, so the next one allocated a new segment and the pin read 200 B over.

**The allocation gate does not move.** `forest-combat` read 11,543 B per window before and after (Release, the gate
alone, once before and twice after), and the three town scenarios 0: the fighters pick nothing up, so no flush with a
change runs in the measured minute, and `perf/scenario-allocations.json` is not regenerated.

### Gameplay requests, quests, aura hooks and corpses — the allocation tests, `Benchmarks/EncounterLookupBenchmarks.cs` (#883)

The per-packet and per-kill allocation sites the performance reviews had confirmed in party, chat, inventory, quest
and aura code, older than the changes that found them. No scenario sends a party packet, a chat message or an item
request, holds a quest or carries a scripted aura, so the [allocation gate](#the-allocation-gate) sees almost none of
this (the one exception is the corpse remover, below). Each site was therefore measured on its own, by an allocation
test over the built classes, read before and after on the same test, and the per-packet and per-kill paths are pinned
by those tests.

Measured 2026-10-13 in a Debug build (Windows 11, i9-12900K, runtime 10.0.12): bytes per call, exact and the same in
every run. The after column reads the same in Release, with dynamic PGO on and off, where every pin passes; the before
column was read in Debug only, and one site in it is Debug's alone (the boxed operands of `HasFlag`, 48 B of every
add).

**What a client can send at will.** Through the handler to the answer sent, over a connection that only counts.

| Path | Before | After | Pinned by |
|---|---:|---:|---|
| A party request (`CMSG_PARTY_INVITE`, `_INVITE_RESPONSE`, `_KICK`, `_PROMOTE`, `_EXPERIENCE_MODE`), refused | 96 B each | 0 B | `GameplayRequestAllocationShould` |
| `CMSG_PARTY_LEAVE`, refused | 64 B | 0 B | the same test |
| A chat message over the sender's limit | 128 B | 0 B | `GameplayRequestAllocationShould` |
| A command nobody has (`/dance with me`) | 240 B | 0 B | the same test |
| A command the caller may not run | 200 B | 0 B | the same test |
| A command that runs, with two arguments | 448 B | 160 B | the same test, against what the command is handed |
| An accepted `CMSG_ITEM_MOVE`, through production's `CharacterEconomy` | 104 B | 0 B | `InventoryAllocationShould` |
| An add into a stack and the removal by template that undoes it | 1,776 B | 0 B | the same test |
| The check that an item fits the Bag | 536 B | 0 B | the same test |
| The check of a Unique item already held | 712 B | 0 B | the same test |

- **Party.** Each of the six handlers passed `PartyReplies.Handle` a lambda that captured the party service and the
  packet: a closure (32 B) and a delegate (64 B) for every party packet; the leave captured only the handler, so its
  delegate alone. `Handle` takes what the request needs as a state argument, a value tuple, and hands it back to a
  static lambda.
- **Chat.** `ChatRateLimiter.TooFast` built its line for every refused message, and the sender over its limit is the
  client that sends the most: the line for each whole second of the 60 s window is written once, when the class
  loads, and never changed. `CommandDispatcher.Dispatch` trimmed every message into a new string and split it into
  an array of strings before it looked the command up, and its failure report was a closure allocated on entry,
  command or not: the command word is now a slice of the message, looked up through the dictionary's span lookup,
  and the context a command is given names the dispatcher and the command where it took a callback. A command that
  runs still gets its context (56 B) and its arguments (an array and a string each), which are its own to keep. The
  message is read as it was (leading slashes dropped, cut at the space character only, empty pieces left out, the
  first piece the command word in any case, nothing quoted): a scratch comparison of the old expression with the new
  code over 3,396,745 messages, every string of up to seven characters from a slash, a space, a tab, a quote, a
  no-break space and three letters, and a million random longer ones, found no difference in which ran or with what
  arguments. One theory in `CommandDispatcherShould` now states the rule; it passes on the old code, where nothing
  pinned leading spaces, repeated slashes or what counts as a separator.
- **The economy.** `CharacterEconomy.InventoryOf` built an inventory service (40 B) and a delegate for its template
  lookup (64 B) for every item request, loot pickup, vendor trade and quest reward. The delegate is made once with
  the economy. The service is made once per character and kept on it (`CharacterEntity.EconomyInventory`, internal,
  with the economy that made it): it holds the character, the lookup and the id allocator and no state of its own,
  the lookup still reads the item templates as they are at each call, and it lives and dies with the character, so
  there is nothing to clear and nothing shared between characters. The wallet and the vendor trade are still made
  per call (the wallet carries the money cap as it is read then).
- **The inventory service.** Its add, its removal by template and its counts ran queries. The add copied the partial
  stacks, ordered by slot, into a list, and then the free slots into another, because the walk writes the Bag; both
  are walks by slot number over the Bag as it is, since a step writes only the slot it stands on and the slots still
  to come are as they were when the walk began. The removal counts what is held, then takes from the highest slot
  down. Slot numbers are unique, so "ordered by slot" and "by slot number" are one order. The room and the owned
  count walk the container's own collection (`CharacterInventoryContainer.Held`), whose enumerator is a struct. Eight
  ways of getting a walk wrong (turned round, the room miscounted, a held copy missed, the first free slot not the
  lowest) were tried against the existing inventory, vendor, loot and quest tests: each failed between 1 and 54.
  The removal by instance id (an item used up, a quest's items taken at turn-in) walked the interface collection
  and boxed an enumerator per container: 72 B with the item in the Bag, 144 B in the Bank or not held, 0 B now.
- **Times.** No benchmark runs these; the review's probe (Release, best of 7, a loaded machine) read an add and a
  removal into a stack at 1,200 to 1,750 ns before and 270 to 500 ns after, the fit check at 300 to 520 ns and 34 to
  76 ns, and a command nobody has at 122 to 163 ns and about 57 ns (about 300 ns and 75 ns at 255 characters). The
  same review ran the old inventory service against the new over 240,000 random operations and the old command
  reading against the new over 1.5 million messages, and the old corpse remover against the new over 3,000 random
  schedules, and found no difference in any.

**Quests.** One character holding three quests, none scripted, over a quest service whose world is no substitute
(`QuestTestWorld.QuietQuests`; a substitute allocates on every call it records).

| Path | Before | After | Pinned by |
|---|---:|---:|---|
| A kill that moves no quest: the credit walk (`CreatureKilled`) | 696 B | 0 B | `QuestAllocationShould` |
| The same kill's quest drop roll: one candidate, the roll made and failed (`RollQuestDrops`) | 448 B | 0 B | the same test |
| A Bag change that changes no count (`RecountCollect`, `HeldInBag`) | 448 B | 0 B | the same test |
| A pickup judged: a quest item still needed (`MayPickUp`) | 560 B | 0 B | the same test |
| A kill that credits one objective | 952 B | 72 B | not pinned: the 72 B is the line it says |
| A kill with a fourth, scripted quest held | 1,112 B | 152 B | not pinned: what the hook is handed |
| A tick's two quest updates (`FlushClient`) | 1,224 B | 336 B | `QuestAllocationShould`, against what the updates carry |

- **The walks.** The kill credit, a conversation and the Bag recount copied the character's quests with `ToList()`
  before walking them, and the copy is there for a reason: the walk settles quests and runs their scripts' hooks,
  which must not move the collection under it. It is still a copy, into a list the quest log keeps for the walk
  (`QuestLog.BeginWalk`, given back with `EndWalk` in a `finally`), one list per walk in progress, as the aura
  system keeps its lists of auras to end, and emptied when the walk ends, so the log keeps no copy between walks.
  The lists are the character's log's, never the service's, which every instance shares. What can change the log
  during a walk: today nothing a hook can reach adds or removes a quest (a quest script is handed loggers and the
  clock, and its one write is to advance its own quest), and a hook cannot start another walk. The walk relies on
  neither: it covers the quests held when it began, each once; one accepted meanwhile is not part of it; one dropped
  meanwhile is still reached and found gone by the checks the walk already made (`AddProgress` and the hook both ask
  the log for the quest again).
- **The hooks.** `RunHook` made a closure on entry for every quest, scripted or not, and each caller a second closure
  and a delegate for the hook's argument. With the boxed enumerator of the stage's objectives, that was 200 B for
  each quest a member held, for each member, on every kill, on top of 72 B a member for the copy and the walk of the
  members (read with none, one, two and three quests held: 72, 296, 496 and 696 B; now 0 B at each). `RunHook` now
  looks for the script first and hands the hook's argument through as state to a static lambda. A script's hook
  still gets a context of its own (it may keep it, and `Advance` judges a kept one by the quest as it is then).
- **The quest drops.** `RollQuestDrops` returned a new list, boxed the enumerators of the eligible members and of the
  drop rows, and made a closure for each candidate drop to find its objective. It fills the kill's own list
  (`KillBuffers.QuestDrops`, emptied with the kill's other lists) and finds the objective by a walk. A creature with
  no quest drop rows still returns before anything else.
- **The Bag.** `HeldInBag` ran `Where` and `Sum` over the Bag once for each Collect objective, on every tick the Bag
  changed; it walks the Bag's own collection. The stage rule (`All`) and the objective lookups (`FirstOrDefault`)
  are walks too. `MayPickUp` took a closure on entry for every pickup, quest item or not.
- **The updates.** `FlushClient` ordered the changed quests with `OrderBy`; the log hands them out by quest id in a
  list of its own, sorted in place (ids are unique, so no tie can reorder them). An update's progress list was built
  by a query over a query (`SelectMany`, `Select`, `ToList`); it is built at its size by a walk of the stages, in the
  same order. What a tick's updates allocate now is the updates: a message and a progress list each, with an entry
  for each objective.

**Tests found by breaking the code**, each for a behaviour no existing test failed on:

| Broken | Fails |
|---|---|
| A walk shares its list with a walk begun inside it | `QuestScriptShould.Credit_a_kill_to_the_quests_held_when_it_happened_whatever_a_hook_does_to_the_log`: the first quest's hook accepts a quest and has the log walked again, from inside the kill's walk |
| The kill's quest drops left in the list for the next kill | `QuestItemDropShould.Drop_only_for_the_member_with_the_objective_and_reserve_it_for_good`, which now kills twice |
| The updates out of quest id order, or an update's objectives out of stage order | `QuestFlusherShould.Send_a_ticks_updates_by_quest_id_each_listing_its_objectives_stage_by_stage` |
| A corpse's wait not counted from its own death | `CreatureCorpseRemoverShould.Count_A_Corpses_Wait_From_Its_Death` |

All four pass on the code as it was. A walk's list left unemptied, the client changes left in the log's sorted list,
a stage judged by its first objective, an objective found that was not asked for and a pickup judged without the Bag
were each already failed by between two and seven tests.

**Aura hooks, stat totals, the stats refresh and corpses.**

| Path | Before | After | Pinned by |
|---|---:|---:|---|
| A scripted aura's hook (apply, stack, tick or removal) | 168 B | 64 B | `AuraScriptHooksShould`, against what its context costs |
| A unit's stat totals rebuilt: three auras of one modifier each | 384 B | 184 B | not pinned: the 184 B is the totals |
| A character's stats refreshed, nothing worn | 208 B | 32 B | not pinned |
| The same with three items worn (65 item templates, 15 class rows) | 1,576 B | 968 B | not pinned |
| A corpse handed to the remover | 32 B | 0 B | `CreatureCorpseRemoverShould` |

- **An aura's hook** allocated its context, a closure and a delegate. The hook, the context and the reason go to the
  script host as one value. The context stays an object of its own for each hook: it is ended when the hook returns,
  so that a script that kept one cannot act through it later, and handing the next hook the same object would give
  such a script its acts back.
- **The stat totals** are rebuilt when one of the unit's auras changed, not when they are read. The query over the
  auras was an iterator, and an enumerator for each aura's modifiers. `AuraStatTotals.Of` now reads the auras from
  the caller's list through a static lambda, and it is the one way to build totals: the simulator builds its units'
  the same way, and the calculator's own tests cover the path the world server runs.
- **The stats refresh** found the class row with `FirstOrDefault` over a lambda on the character and, over the
  current reference data, each worn item's template with another that walked every item template. The row is found
  by a walk by index and a template by the reference data's keyed lookup. What is left is the list of worn templates
  and the calculator's own work on it (an iterator for each item's stats), which is shared with the simulator and
  the API and was not changed.
- **The corpse remover** made a timer object for each kill, as the key of its dictionary. It keeps one count of the
  time its updates have added up (whole milliseconds when this was measured, exact since: the next point) and, for
  each corpse, the count at which its body goes, keyed by the order the corpses were scheduled in. It is the same
  dictionary, added to and removed from exactly as before, so corpses that go on the same tick go in the order they
  always went; a corpse goes on the first update at which the time added since its death reaches its template's, as
  before.
- **The corpse's wait, counted exactly** (a rule fix after the above, not an allocation). Whole milliseconds dropped
  the rest of every tick, in the timer each corpse had and in the count that replaced it: a 60 Hz tick of 16.67 ms
  counted as 16, and a 10 s corpse lay for 625 ticks, 10.4 s. The count is now in `TimeSpan` ticks of 100 ns, all
  a `TimeSpan` holds, and the corpse goes on the first tick at or after 10 s: the 600th or the 601st, since the
  server's frame is a hair under a sixtieth of a second. A dictionary entry is the same size and both paths
  still allocate nothing (the same two pins). Over whole-millisecond deltas and times the two counts agree: a
  scratch comparison of the old class with the new over 5,000 random schedules, 733,586 removals, found the same
  corpses going on the same updates in the same order. `forest-combat`, whose ticks are 16.67 ms, read what it
  had, 71 kills and 10,357 B per window, in Debug and in Release, so the baseline did not move.

**The encounter lookup stays a walk.** `EncounterRegistry.FindEncounterContaining` walks an instance's active
encounters and asks each one's two sets for the unit. `EncounterLookupBenchmarks` compares the walk, for a unit of the
last encounter and for a unit of none, with one dictionary lookup by unit, which is the least an index could cost.
Each encounter is a pack of four and a party of three.

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*EncounterLookup*" --job short
```

BenchmarkDotNet 0.15.8, `--job short`, the same machine, with other builds and test runs loading it: each cell gives
the mean of each of three runs, and the runs disagree by up to a factor of two. Nothing is allocated in any row.

| Encounters | A unit of the last encounter | A unit of none | Indexed |
|---:|---:|---:|---:|
| 1 | 10.2 / 9.5 / 7.4 ns | 10.2 / 11.2 / 9.3 ns | 4.7 / 6.8 / 5.6 ns |
| 2 | 20.0 / 21.5 / 12.4 ns | 25.9 / 27.2 / 15.2 ns | 5.3 / 8.0 / 8.3 ns |
| 4 | 43.8 / 30.4 / 51.3 ns | 51.8 / 38.9 / 52.5 ns | 6.1 / 6.8 / 7.6 ns |
| 8 | 60.8 / 57.3 / 77.1 ns | 60.8 / 53.5 / 87.4 ns | 5.3 / 5.3 / 8.9 ns |
| 32 | 265 / 221 / 442 ns | 319 / 229 / 275 ns | 4.7 / 8.0 / 8.1 ns |

The walk costs about 10 ns for each encounter it passes. The registry is one per instance, and an instance's
encounters are the fights going on in it at once: one for a party on a pack, a few when its members fight apart. The
lookup runs once a tick for each creature that is fighting (its target) and once or twice for each hit. At four
encounters an index would save 25 to 45 ns a lookup, well under a microsecond a tick for an instance in a fight,
and it would have to follow every way an encounter gains or loses a member (a hostile or a player added, removed,
dead, an encounter ended). The figure does not justify it; at 32 encounters in one instance it would, and the
benchmark is kept to ask again.

**The allocation gate.** `forest-combat` read 10,357 B per window, 28 B under its committed 10,385 B, in Debug and in
Release; with the corpse remover alone put back as it was it read 10,385 B again, so the whole difference is the
timer object of the measured minute's 60 kills (32 B each), less 4 B a window for the remover's dictionary, whose
entries are 8 B wider. The baseline was regenerated at `e6b43c97` and is 10,357 B: the runner run that wrote it,
three runs of the Release gate alone (one before the file was written, two after), two of the Debug gate alone, one
of the Release gate with `DOTNET_TieredPGO=0`, and the whole World suite in Debug. The three town scenarios read 0 in
every run. No Linux run was made: read CI's figure from its first run's "Scenario allocation gate" summary.

### Ability script construction — `Benchmarks/AbilityScriptBuildBenchmarks.cs` (#925)

What building one cast's ability script costs, as `InstanceAbilityCastSystem.Build` does it for every cast, a
character's, a creature's or an item's: a copy of the caster's ability, then the script built from
`(ability, caster, aim, arena)`. `Activated_per_cast` is what the cast system did before #925: the ability cloned
whole, its metadata included, and `ActivatorUtilities.CreateInstance`, with its `params` array and the aim boxed.
`Cached_factory` is what it does now: the copy that shares the ability's metadata (`GameAbility.CloneForCast`), and
the `AbilityScriptActivator`, one `ActivatorUtilities.CreateFactory` factory per script type, called with an argument
array and an aim box the cast system keeps. The other rows are parts: each copy alone, and the script built with `new`
over a copy made beforehand. The caster and the arena are substitutes no constructor calls, and nothing fires. A cone
builds like a circle (the same fields); a projectile also keeps the set of units it has hit.

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*AbilityScriptBuild*"
```

Measured 2026-10-13, BenchmarkDotNet 0.15.8 defaults, Windows 11, i9-12900K, SDK 10.0.401 / runtime 10.0.12,
concurrent workstation GC. Three runs, with other builds running on the machine: the allocations are exact and the
same in every run, the means are given for each run and move with the load.

| | Circle, allocated | Circle, mean of each run | Projectile, allocated | Projectile, mean of each run |
|---|---:|---:|---:|---:|
| `Activated_per_cast` (before) | 456 B | 105.1 / 128.0 / 111.4 ns | 552 B | 109.9 / 168.1 / 125.5 ns |
| `Cached_factory` (after) | 224 B | 56.8 / 42.5 / 44.7 ns | 320 B | 52.1 / 62.5 / 53.2 ns |
| `Clone_alone` | 160 B | 16.3 / 19.2 / 16.7 ns | 160 B | 20.5 / 18.3 / 18.2 ns |
| `Cast_copy_alone` | 48 B | 7.0 / 8.5 / 6.2 ns | 48 B | 3.7 / 6.0 / 5.8 ns |
| `Constructor_alone` | 176 B | 14.9 / 17.7 / 17.1 ns | 272 B | 20.1 / 29.1 / 20.0 ns |

The quietest rows have 99.9% half-intervals of about 2 ns before and 1 ns after (circle, third run: 111.4 ± 1.9
against 44.7 ± 0.9 ns; projectile, first run: 109.9 ± 2.1 against 52.1 ± 1.1 ns); the loudest reach 11 ns. Across
the runs a build takes between a third and a little over half the time it took, and 232 B less. `Activated_per_cast` runs the old call over
today's script class, which no longer builds its empty chain list, so it reads 32 B under what a build did cost: 488 B
for a circle or a cone and 584 B for a projectile, measured on `46b38375` on 2026-10-12, where the factory alone, with
the clone and the box as they were, read 98.1 against 57.1 ns and 101.2 against 63.8 ns (99.9% intervals 96.1–100.1
against 55.9–58.2 and 99.2–103.2 against 62.5–65.1 ns), 56 B less.

What a build allocates, by part (the copy and the constructor from the rows above; the constructor's parts measured
one object at a time with `GC.GetAllocatedBytesForCurrentThread`):

| Part | Before #925 | Now, circle or cone | Now, projectile |
|---|---:|---:|---:|
| The `params` array of `CreateInstance` | 56 B | | |
| The aim, boxed for the activation; one box per cast system now | 64 B | | |
| The ability's copy: a `GameAbility` | 48 B | 48 B | 48 B |
| Its `AbilityMetadata`, copied; shared now | 112 B | | |
| The script object | 152 B, a projectile 184 B | 152 B | 184 B |
| Its `ChainedScripts` list, empty; made on first use now | 32 B | | |
| Its `State`, a boxed `SpellState` (the property is an `object`) | 24 B | 24 B | 24 B |
| The projectile's set of units hit | 64 B | | 64 B |
| **A build** | **488 B, a projectile 584 B** | **224 B** | **320 B** |

Firing it adds its guid (an `ObjectGuid`, 24 B); for a script still running once it has prepared, which is every
projectile, the entry the cast system keeps it in (an `ActiveScript`, 48 B); and the second boxed `SpellState` when
it finishes (24 B), a projectile's as well as a circle's or a cone's. These three sizes are computed from the fields,
not measured, and the list covers the cast system and the script's own state, not what a script's effect allocates.
What is left of a build, and why, is in [the cast path](#results--the-cast-path-925-883-2026-10-13).

The factory is found in a table held weakly by script type on every cast; that lookup is in the `Cached_factory`
figure. Making a factory is not: it compiles the constructor call, about 0.5 ms and 10 KB for a script type and about
12 ms and 27 KB for the first in a cold process (per-cast activation paid about 7 ms for the first type and 0.3 to
0.6 ms for a later one, over each type's first two calls), read with a stopwatch around the first builds of each type
in a new process. `ScriptManager.Load` has every ability script's factory made as it registers the type, off the
tick, so no cast pays it; a type that reaches a cast any other way has its factory made by that cast.

### Active script lookup — `Benchmarks/ActiveScriptLookupBenchmarks.cs` (#883)

`InstanceAbilityCastSystem.GetAbility` over 1 to 64 projectiles in flight. The state broadcast asks it for every
projectile each viewer is sent, so one invocation is one viewer's share: every active projectile looked up once.
`Walk_the_list` is the lookup as it was at every size, a walk of the active scripts comparing guids;
`Cast_system_lookup` is the cast system's own since #883: the same walk up to eight scripts
(`InstanceAbilityCastSystem.WalkedUpTo`), and an index by guid above that. `Refill_the_index` is what the first
lookup after a cast or a removal pays once above eight, for every viewer together: the index is emptied by every
change to the active scripts (a removal sweep that throws part-way included), so it never answers with one that left
nor keeps it alive, and filled again from the list.

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*ActiveScriptLookup*"
```

Measured 2026-10-12, BenchmarkDotNet 0.15.8 defaults, Windows 11, i9-12900K, SDK 10.0.401 / runtime 10.0.12,
concurrent workstation GC (the two lookups from one run, the refill from the run before it):

| Active scripts | `Walk_the_list` (before) | `Cast_system_lookup` (after) | Ratio | `Refill_the_index`, once per change | Allocated, all |
|---:|---:|---:|---:|---:|---:|
| 1 | 1.44 ns | 2.45 ns | 1.71 | not indexed | 0 B |
| 4 | 12.6 ns | 15.7 ns | 1.24 | not indexed | 0 B |
| 8 | 44.8 ns | 50.5 ns | 1.14 | not indexed | 0 B |
| 16 | 150.2 ns | 46.1 ns | 0.31 | 76.3 ns | 0 B |
| 64 | 2,368 ns | 190.8 ns | 0.08 | 269.5 ns | 0 B |

At eight and under both walk, and the cast system's costs 0.6 to 1.0 ns more per lookup: a call into the collection
that decides between the walk and the index. The run before read 70.8 ns for the cast system at 8, against 42.0 ns
for the walk, with every other size within 10% of the table. Above eight a lookup costs about 3 ns whatever the
count, where the walk costs 9.4 ns a lookup at 16 and 37 ns at 64. The index would win from about five scripts on
lookups alone; it is used from nine because it also costs a refill on every change and its memory: an instance
allocates it the first time it holds more than eight active scripts, sized for its list (360 B at nine), and again
only when its peak outgrows it (a peak that creeps from 9 to 72 grows it nine times, about 11 KB in all, once per
instance). One party's fight seldom gets there: in `forest-combat` one forest does, once in the measured minute.

### Reference row lookup — `Benchmarks/ReferenceRowLookupBenchmarks.cs` (#883)

What a kill looks up in the reference data: the item template of each drop (`LootRoller.FindItem`), and a level's
experience row (`ExperienceAward.Requirement`: twice for each member the experience is split among, twice more for
each award). The `Walk` cases are the lookups as they were, over the collections production held: the item templates
a read-only view of the repository's list, the level rows the repository's list itself (the item walk is still what
`LootRoller` does for a collection that carries no lookup; the level walk is gone and is copied in the benchmark).
The `Keyed` cases are what production runs since #883 ([the kill path](#results--the-kill-path-890-883-2026-10-13)):
`ItemsPatch.Find` and `ProgressionPatch.FindLevel`, over `KeyedRows` (`Avalon.World/Reload`), which
`StaticData.FindItemTemplate` and `FindLevel` answer from. One invocation looks every row up once, so the mean is
for `Rows` lookups, one at each position of the list. The seed holds 31 item templates and 15 level rows.

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*ReferenceRowLookup*"
```

Measured 2026-10-13, BenchmarkDotNet 0.15.8, a short run (`-j short`: three iterations), Windows 11, i9-12900K,
runtime 10.0.12, concurrent workstation GC, while other work loaded the machine: the error of a mean is 6% to 41%
in the cases above 100 ns and up to several times the mean in the smallest ones.

| Method | Rows | Mean | Per lookup | Allocated |
|---|---:|---:|---:|---:|
| `Walk_item_templates` | 8 | 79.4 ns | 9.9 ns | 0 B |
| `Keyed_item_templates` | 8 | 24.5 ns | 3.1 ns | 0 B |
| `Walk_level_rows` | 8 | 32.1 ns | 4.0 ns | 0 B |
| `Keyed_level_rows` | 8 | 22.0 ns | 2.8 ns | 0 B |
| `Walk_item_templates` | 15 | 270.8 ns | 18.1 ns | 0 B |
| `Keyed_item_templates` | 15 | 36.9 ns | 2.5 ns | 0 B |
| `Walk_level_rows` | 15 | 88.3 ns | 5.9 ns | 0 B |
| `Keyed_level_rows` | 15 | 31.7 ns | 2.1 ns | 0 B |
| `Walk_item_templates` | 32 | 1,033 ns | 32.3 ns | 0 B |
| `Keyed_item_templates` | 32 | 77.6 ns | 2.4 ns | 0 B |
| `Walk_level_rows` | 32 | 434.8 ns | 13.6 ns | 0 B |
| `Keyed_level_rows` | 32 | 69.0 ns | 2.2 ns | 0 B |
| `Walk_item_templates` | 128 | 16,174 ns | 126.4 ns | 0 B |
| `Keyed_item_templates` | 128 | 292.6 ns | 2.3 ns | 0 B |
| `Walk_level_rows` | 128 | 4,994 ns | 39.0 ns | 0 B |
| `Keyed_level_rows` | 128 | 268.6 ns | 2.1 ns | 0 B |

At the seed's sizes a walked item template costs about 32 ns a drop (about 2 ns a row passed: each comparison of two
ids goes through the value object's equality) and a walked level row about 6 ns; keyed, either is 2 to 2.5 ns
whatever the count. The gain today is small, a few tens of nanoseconds a kill, and it grows with every item template
added, where the level rows are bounded by the level cap. The map template's lookup is not benchmarked: it is a
field read where it was a walk of the world's map templates, two today.

### Time sync — `Benchmarks/TimeSyncBenchmarks.cs` (#892)

The ping and pong exchange on an admitted `WorldConnection` with its real `ConnectionSender` and `PongHandler`
(`ReceiveQueueFixture` with a sender), over a stream that discards what it is given. The send thread's pass runs on the
benchmark's thread (a scheduler whose threads never start, as in the scenario runner). A pong is delivered as the read
loop delivers it (`OnReceive`) and handled by the tick's session pass. The fixture's clock writes every ping at one
time and lands its pong 20 ms later. Setup runs one exchange and throws unless the connection then reads that 20 ms
round trip, so the benchmark cannot come to time a pong the server ignores without saying so; it also means every
measured ping carries a round trip, an offset and a client time. Each invocation runs 256 operations.

| Method | What one operation is |
|---|---|
| `PingWritten` | The tick queues a ping (`SendTimeSyncPing`) and one send pass stamps, encodes, frames and writes it. Since #892 the pass also records the stamp and grants a pong credit. No pong is delivered here, so from the fifth operation on the credits are at their cap of four and the grant is a read: this row times the saturated grant only |
| `PingWrittenAndPongHandled` | The same, then the pong that echoes it arrives, takes the credit, is queued, dispatched and accepted: one whole exchange |
| `PingWrittenAndWrongPongHandled` | The same with an echo that is the stamp of no ping: it takes the credit, reaches the handler and is ignored there. Before #892 it was computed like any other pong |
| `SurplusPong` | A pong beyond one per ping, 32 of them between two session passes, which a client can send at any rate. Since #892 it is dropped as it arrives; before, it was queued, dispatched and computed |

Before/after on 2026-10-12, `46b38375` and the change (#892: the server measures the round trip itself, from a record
of the stamps it wrote, and lets one pong in per ping), the same benchmark and fixture on both sides. Five runs per
side, interleaved; each figure is the median of the five runs' means, with their range.

| Method | Before | After | Allocated, both |
|---|---:|---:|---:|
| `PingWritten` | 249.6 ns (247.3 to 256.3) | 254.3 ns (244.5 to 298.9) | 0 B |
| `PingWrittenAndPongHandled` | 395.7 ns (390.3 to 601.0) | 410.5 ns (396.4 to 456.1) | 0 B |
| `PingWrittenAndWrongPongHandled` | 394.6 ns (392.4 to 567.6) | 400.8 ns (396.9 to 435.9) | 0 B |
| `SurplusPong` | 99.8 ns (99.5 to 109.5) | 16.9 ns (16.7 to 17.1) | 0 B |

BenchmarkDotNet 0.15.8, Windows 11, i9-12900K, SDK 10.0.401 / runtime 10.0.12, concurrent workstation GC, affinity
mask 4; one launch, six warmups and fifteen measured iterations per run. Taken after the day's load-test ramp had
ended, on a machine other builds still used: one or two runs of the five on each side were disturbed (the top of each
range; a run's own standard deviation was then 10 to 50 ns against 1 to 3 ns otherwise), which is why the figure is
the median.

The credit count was then moved beside the stamps (below). Three more interleaved runs, the same day, against the
commit before the move, as the range of each side's means: `PingWritten` 247.5 to 254.4 ns before the move and 250.6
to 252.1 after, `PingWrittenAndPongHandled` 405.8 to 412.1 and 404.9 to 410.7, `PingWrittenAndWrongPongHandled` 396.5
to 405.1 and 395.7 to 403.1, `SurplusPong` 16.0 to 17.0 and 16.5 to 17.6. No row moved, and none could: the benchmark
runs on one thread, and the move matters between cores. Other benchmarks may have been running on the machine during
these runs.

- **The ping's write is within the spread** (about 5 ns, 2%, between the medians; the ranges overlap). The stamp goes
  into a four-place ring inside the sender with the one interlocked exchange the last-stamp field took before. The
  credit's grant is a read alone in this row, the credits being at their cap. The grant a client that answers takes,
  a compare-exchange from 0 to 1, is timed only inside the two exchange rows, with the pong's own costs.
- **A whole exchange costs about 15 ns more** (4%): the credit taken at arrival, the scan of the ring and the
  compare-exchange that takes the stamp. A connection has one exchange about every ten seconds, so 1,000 connections
  spend about 1.5 µs a second on it.
- **A pong beyond one per ping costs 17 ns instead of 100 ns, and none of it on the tick.** It used to be queued,
  dispatched and computed by the session pass. Now the read loop drops it after the admission check and one read of
  the credit count, which finds zero and writes nothing; what is left is the arrival path every packet pays (the
  admission and lease checks) and, upstream of this benchmark, reading and decoding the frame.
- **What a flood costs is bounded.** However many pongs a client sends, one per ping written reaches the tick, so the
  tick's work for a connection's pongs is one dispatch per ten seconds, and the Debug line for an unmatched pong is
  written at most as often. The rest cost the connection's own read loop 17 ns each plus the frame's read and decode,
  and nothing is queued for them.
- **Nothing is allocated after decode**, per ping or per pong, on either side. The benchmark reuses one `CPongPacket`.
  In the world every pong frame, a surplus one included, is decoded into a new packet object before `OnReceive` can
  drop it; that allocation is upstream of this benchmark and unchanged by #892. The drop itself is held at 0 B by
  `ReceiveQueueAllocationShould` (one ping, its pong, then 32,000 surplus pongs). The scenario gate does not run this
  path (its connections refuse a time-sync ping) and is unchanged.
- **Where the credit count lies.** The read loop reads it for every pong, so a flood keeps its cache line at the read
  loop's core. It is the last field of the struct that holds the stamps, which the runtime lays out after the sender's
  integers: in the Release build, counted from the object's start, the count is at 280, the group of integers the tick's
  enqueue and the send thread's framing write ends at 215 (`_doomed`, its last), and the object's fields end at 304
  (each 8 more than when this was first measured: #883 added an 8-byte field ahead of them, the time a watch over a
  grown burst buffer began, and a flag in the padding after the bool flags; the send thread writes them only as a
  watch begins or ends, and nothing here moved against anything else).
  However the object falls across 64-byte lines, the count shares none with `_pendingBytes`, `_dirty`,
  `_activeEnqueues`, the bursts' counts or the int flags, so a flood never makes the tick's enqueue or the send thread's
  framing of a packet take a line back. It shares its line with the stamps and the connection's id (seven placements of
  eight each), the pending write's awaiter (three of eight) and the send thread's bool flags (one of eight). What the
  send thread takes back because of a flood is therefore that line once per ping, for the stamp and the grant, and once
  more when a socket write goes pending or ends, in the placements where the awaiter shares it. Before the move the
  count sat among the integers and shared a line with `_doomed` in seven placements of eight, `_activeEnqueues` in six
  and `_dirty` in four. What lies past the object's end, within the count's line in some placements, is the heap's
  choice.
- **Not measured**: the threads together. In the world the send thread writes the stamps and grants credits, the read
  loop takes credits and the tick takes stamps, so that line moves between those cores a few times per exchange; here
  one thread does all three, and no flood runs beside an enqueue on another core. Sockets, TLS and the frame's decode
  are not in it either.

### Connection send — `Benchmarks/ConnectionSendBenchmarks.cs` (#883)

What a send costs the tick on a world connection, which until #883 was recorded nowhere: `Connection.Send` on an
admitted `WorldConnection` with its real `ConnectionSender` (`ReceiveQueueFixture` with a sender), against that
sender's `Enqueue` alone. The difference between the two is what `Send` adds to the enqueue: the size the byte
counters count (`SentSize`), the connection's own two interlocked counters, and the world's `OnQueued`, a virtual call
that adds to the `network.out.bytes` and `network.out.packets` counters. `Send` counts a packet only once the sender
has accepted it.

Each arm sends as the tick does: four packets to the connection with the wake-up deferred, then the send thread's
pass, run on the benchmark's thread (a scheduler whose threads never start) into a stream that discards what it is
given. The pass frames the four packets, and seals them when the connection seals; it is the same work in both arms
and cancels in the difference. The packet is one movement acknowledgement (19 B, flagged Encrypted), encoded once:
each send takes a reference of its own, as a broadcast's recipients do, so no encode is timed. Each invocation runs
256 sends. `Sealed` is `Network:PacketEncryption`. `Metered` subscribes a `MeterListener` to the two counters, as a
world that exports its metrics has; with no listener a counter's `Add` is a check and nothing more. The listener here
only adds to a field, so it is the least a subscribed counter costs, not what the exporter's aggregation costs.

| | Plain | Plain, metered | Sealed | Sealed, metered | Allocated |
|---|---:|---:|---:|---:|---:|
| `SenderEnqueue`, a quiet run, mean | 58.9 ns | 59.9 ns | 279.3 ns | 278.3 ns | 0 B |
| `ConnectionSend`, a quiet run, mean | 65.9 ns | 68.5 ns | 285.3 ns | 305.8 ns | 0 B |
| What `Send` adds | 7.0 ns | 8.7 ns | 6.0 ns | 27.5 ns | |
| `SenderEnqueue`, the burst buffers given back at once, three runs | 58.1 to 61.0 ns | 58.2 to 71.1 ns | 277.5 to 328.2 ns | 279.0 to 284.6 ns | 0 B |
| `ConnectionSend`, the same | 67.2 to 84.3 ns | 71.0 to 74.9 ns | 284.9 to 349.6 ns | 287.6 to 301.3 ns | 0 B |
| `SenderEnqueue`, given back after the delay, three runs | 58.3 to 59.6 ns | 58.8 to 65.0 ns | 277.4 to 285.2 ns | 278.5 to 321.1 ns | 0 B |
| `ConnectionSend`, the same | 67.2 to 68.4 ns | 71.5 to 72.4 ns | 285.2 to 293.6 ns | 288.5 to 294.0 ns | 0 B |

BenchmarkDotNet 0.15.8, in process, Windows 11, i9-12900K, .NET 10.0.12, 2026-10-13. The first three rows are the
default job on `808eec90` with the benchmark added, in a run the machine was quiet for: standard deviations of 0.4 to
2.2 ns in six arms, and 6.8 and 9.7 ns in two of the sealed ones, the last of which is the 27.5 ns. The other four
are `--job short`, three runs a side taking turns, as the range of the runs' means, on a machine that was quiet for
most of them (the top of a range is a disturbed run): with #883's changes to the packet path, which leave `Send` as
it was and add compares to the pass where the burst buffers are swapped
([burst buffers](#results--the-network-path-883-2026-10-13)). First two compares, with a buffer given back as soon as
it looked outgrown; then three, with a buffer given back only after a delay. In their undisturbed runs
the two read the same to within 1 ns in the plain arms and 2 ns in the sealed ones, and the same as the first rows:
the pass's new check costs a connection whose buffers are small nothing that shows.

- **A send costs the tick 6 to 9 ns more than the enqueue it wraps**, plain or sealed: about a tenth of a plain send
  with its share of the pass, and 24 to 36 µs a tick at 4,000 sends. A listener on the counters adds about 2 ns in the
  plain arms. The review that asked for this record measured 3 to 9 ns with a probe of its own (#883).
- **Nothing is allocated**, by the send, the counters or the pass, in any arm.
- **Sealing is the send thread's**: the 220 ns between a plain row and its sealed one is the pass sealing the packet,
  off the tick in a world. On the tick a sealed send differs from a plain one by one branch, in `SentSize`.
- **Not in it**: the encode (the payload is shared), a second thread (the pass runs on the sending thread, so no
  cache line moves between cores, as it does in a world; see [the sender's fields](#the-connection-senders-fields-875-2026-10-10)),
  and the socket. The scenario gate still does not run `Connection.Send` (its connections call the sender's
  `Enqueue`, #883 row 14).

### Idle creature dispatch — `Benchmarks/CreatureDispatchBenchmarks.cs` (#880, #896)

Eleven thousand real `AggroDefendScript` instances, with 0%, 5% or 100% kept in combat. Three out-of-range
characters exercise periodic searches; active creatures keep a nearby, living target and run kitless combat
decisions. Each invocation advances 61 ticks, one full interval of the detector's existing float timer at 60 Hz,
and reports cost per tick for the entire population. Setup warms for 15 seconds; setup and cleanup verify the
active population. This isolates script dispatch: it excludes locomotion integration, abilities, replication,
networking and many-instance locality, and is not a live capacity measurement.

Before/after on 2026-10-12, baseline `516cdf12` and the idle timer shortcut, with the same measured loop:

| Active | Before mean/tick | After mean/tick | Allocated, both |
|---|---:|---:|---:|
| 0% | 248.1 µs | 188.6 µs | 0 B |
| 5% | 273.4 µs | 188.3 µs | 0 B |
| 100% | 846.0 µs | 798.5 µs | 0 B |

BenchmarkDotNet 0.15.8, Windows 11, i9-12900K, SDK 10.0.401 / runtime 10.0.12, concurrent workstation GC,
affinity mask 4; one launch, five warmups and eight measured iterations. The 99.9% intervals are 210.1–286.2 /
172.9–204.3 µs for idle and 245.3–301.5 / 169.2–207.3 µs for 5% active. These runs show lower idle/mixed costs;
the fully active intervals overlap widely (718.0–974.0 / 647.9–949.0 µs), so no active gain is established.
The setup/cleanup population assertion was added after the baseline; the measured loop and population are unchanged.

The separate deterministic `forest-combat` scenario exercises real kits, movement, casts and replication over
four forests and twelve players. One paired run measured mean tick 0.1060 → 0.0864 ms and p99 0.2792 → 0.1869 ms,
with the same 16,244 B per 60-tick window and no collections. It used server GC and a 15-second rehearsal before
600 warmup and 3,600 measured ticks. A separate diagnostic replay preserved every pre-tick state-count total and
all 71 kills: 97.8% of creature states were idle, and 98.4% of searching-detector visits were waiting for their timer.
Those proportions describe this local scenario, not the live 600-bot population. The single full-scenario timing
pair is supporting evidence, not a statistical whole-world or capacity claim. Raw measurements stay outside Git;
the live effect still requires a matched load test after deployment.

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*CreatureDispatchBenchmarks*" --job short --warmupCount 5 --iterationCount 8 --affinity 4
dotnet run -c Release --project tools/Avalon.Scenarios -- --scenario forest-combat --warmup-seconds 15 --measure-ticks 3600
```

#### Results — active and waiting creatures (#979, 2026-10-14)

Since #979 the benchmark binds the instance's own script step (`MapInstance.UpdateCreatureScripts`, private, bound by
name) rather than calling each script from an array, so it measures which creatures the instance visits, the walk
over its creature dictionary included; the population, the mix and the 61 ticks per invocation are as above. Before
(`main` with the step extracted, every creature visited) and after (the scheduler: a waiting creature is in
a search cohort and costs the step nothing until its search is due). `ForestTickBenchmarks` is whole world ticks of one
forest (seed 1, about 200 creatures, its real AI) with three players standing at its entry.

| Row | Before mean/tick | After mean/tick | Allocated, both |
|---|---:|---:|---:|
| 0% active | 169–228 µs | 10–60 µs | 0–1 B |
| 5% active | 221–381 µs | 161–308 µs | 1–2 B |
| 100% active | 818–7,758 µs | 814–7,680 µs | 7–76 B |
| `ForestTickBenchmarks.Tick` | 33.3–34.4 µs | 33.0–34.4 µs | 1 B |

Ranges over three runs each, BenchmarkDotNet 0.15.8 in process (`--inProcess`, from a copy of each Release build),
Windows 11, i9-12900K, SDK 10.0.402 / runtime 10.0.13, concurrent workstation GC, affinity mask 4, five warmups and
eight measured iterations; nothing else ran on the machine, but the rows still vary between runs. The waiting row is the
gain: 11,000 creatures cost the step their searches once a second and nothing on the other 60 ticks. The active rows
are the combat script's own work (550 or 11,000 creatures chasing one target whose locomotion never runs, so they
re-plan every tick), bimodal from run to run before and after, and the same within that noise; an active creature
costs the scheduler one more look at its script after its Update. One forest is level: about 200 creatures were cheap
to visit while they all stayed in cache, which is not the world's case (17,265 creatures at 950 players, about 79 ns a
visit, #974 section 7).

`forest-combat`, five runs each, alternated, the Release scenario runner (ms per tick; one before run, 0.123 mean and
0.270 p99 for everything, was a machine hiccup and is left out): mean 0.073–0.075 before, 0.065–0.079 after; p50
0.069–0.070 before, 0.059–0.068 after; p95 0.103–0.111 before, 0.101–0.136 after; p99 0.137–0.184 before, 0.146–0.206
after. The middle of the distribution is cheaper and the tail overlaps. In the review, three runs each pinned to 16 cores
at high priority: p99 0.125–0.127 ms on `main`, 0.137–0.139 ms on the branch, mean 0.071–0.074 against 0.064–0.068.

The tail is the search tick. Creatures built together search on the same tick, as they did before #979; the ticks in
between no longer visit them, the search tick still runs every search, now over scripts the step has not touched since
the last one, and lets the waiting creatures go and take them back. The review measured it at the profile's scale with a
harness of 80 forests on seeds 1 to 40 (16,682 creatures, a party of three fighting in each, all joined on one tick,
600 warm-up and 3,600 measured ticks, the mean tick of each residue modulo 61, the detector's period at 1/60 s): the
search tick 4.62 and 3.97 ms on `main` against 4.48 and 4.35 ms on the branch, every other tick about 0.5 ms cheaper
(3.22 and 2.84 ms against 2.59 and 2.34), and the whole tick 0.3 to 1.0 ms cheaper at mean and p50 with p95 and p99
equal or lower. Letting a due cohort go in one pass rather than member by member took the search tick's
cost over the other ticks from 1.97–2.04 ms to 1.87 ms in that harness (three alternated runs each; search tick
4.32–4.63 ms before, 4.27–4.34 ms after). The fight is the same fight, kill for kill
(`ForestCombatScenarioShould`: 105 kills, 2 deaths, as on `main`), and the gate reads 14,176 B per window in Release and
14,244 B in Debug for `forest-combat`, 3,904 and 4,008 B for `town-social`, the other scenarios 0, as before: the
scheduler allocates nothing per tick (its roster, bit sets and cohorts grow only as an instance's creatures do).

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*CreatureDispatchBenchmarks*" "*ForestTickBenchmarks*" --inProcess --warmupCount 5 --iterationCount 8 --affinity 4
```

#### Results — a script that needs every tick (#979, 2026-10-14) — `Benchmarks/EveryTickScriptBenchmarks.cs`

Every AI script answers `AiScript.NextUpdateIn` (the owner's decision: no default, every script decides), and the
scheduler reads the answer after each Update of a creature's own script. The cost falls on a script that answers zero:
it used to be run without being asked. Eleven thousand creatures whose script only counts its Updates, per tick, through
the instance's own step, scheduled or walked every tick as before #979 (`UpdateEveryCreatureEveryTick`), three runs each,
the same machine and settings as above:

| Build | Every creature walked | Scheduled | Scheduled, per creature over the walk |
|---|---:|---:|---:|
| Opt-in (`UpdatesWhenDue` false: run, never asked) | 90–92 µs | 138–143 µs | 4.3–4.7 ns |
| `NextUpdateIn` read, then the ownership checks | 91–97 µs | 167–190 µs | 6.6–8.7 ns |
| `NextUpdateIn` read first, the next tick returning at once, `Run` inlined | 89–101 µs | 134–154 µs | 3.9–4.8 ns |

The first answer read after every Update cost about 3–4 ns per such creature per tick: the virtual call, then three
checks that the script still owned its creature, which for the next tick can only end in leaving it as it is. Since the
re-review the answer is read first and the next tick returns at once (four runs): the cost is back to the opt-in's, the
scheduler's own 4–5 ns over the walk (its bit set, the roster read, the call). It allocates nothing. No built-in pays it
where it matters: a waiting `AggroDefendScript` (every forest creature) is asked through its search wait, and a creature
that waits is not visited at all. A world full of custom scripts that need every tick would pay about 4–5 ns a creature
a tick more than before #979: under 0.1 ms a tick at the 17,265 creatures of the 950-player profile, if every one of
them were such a script.

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*EveryTickScriptBenchmarks*" --inProcess --warmupCount 5 --iterationCount 8 --affinity 4
```

### Aura scans — `Benchmarks/AuraScanBenchmarks.cs` (#898)

The aura work of one instance's tick, in a town instance of 50, 100 or 200 characters who all see one another, 10%
or all of them holding Renew (12 s, a heal every 3 s, on characters at full health, so a tick restores nothing and
sends nothing). Every row is per tick of that instance: `Aura_pass` is `AuraSystem.Update` with nothing due (finding
the holders, then each holder's look at its auras); `Update_scan` is the broadcast's aura updates to every character
with no unit changed, which is only the walk for changed units, once per recipient; `Clear_scan` is forgetting every
unit's changes at the end of the broadcast with none changed; `Tick` is whole world ticks with the auras ticking and a
tenth of the holders refreshed every second (each holder every 10 s, every refresh an aura update to every
character). The update scan and the clear are private to `MapInstance` and bound by name, the same before and after.

Before #898 the pass found its holders by walking every character and creature of the instance each tick, and the
broadcast walked every unit once per recipient for changes and once more to clear them: 1,529 of the tick thread's
19,804 samples at 950 players on world 4, about 0.85 ms a tick (#974, section 7). Since #898 each instance keeps an
`AuraRoster` (see [Auras](auras.md)): the pass copies only the holders, and the broadcast walks and clears only the
units that changed.

Before (`b12fd6f3`) and after (`aa844df2`) on 2026-10-14, ranges over two runs each (the pass over six before and
two after, alternated); the machine was shared with other builds and tests, so times are noisy, allocations exact:

| Units | Holding | `Update_scan` before | after | `Clear_scan` before | after | `Aura_pass` before | after |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 50 | 10% | 6.6–12.1 µs | 0.19–0.20 µs | 110–113 ns | 2.0 ns | 236–412 ns | 175–185 ns |
| 50 | 100% | 7.0–11.3 µs | 0.14–0.19 µs | 85–118 ns | 1.7–2.5 ns | 1.35–2.51 µs | 1.39–1.44 µs |
| 100 | 10% | 22.3–26.0 µs | 0.28–0.31 µs | 141–193 ns | 1.9–2.4 ns | 454–837 ns | 282–352 ns |
| 100 | 100% | 38.0–45.5 µs | 0.39–0.42 µs | 214–247 ns | 2.1–2.3 ns | 3.01–5.60 µs | 3.26–4.77 µs |
| 200 | 10% | 121–156 µs | 0.54–0.89 µs | 362–631 ns | 1.9–2.5 ns | 0.94–1.68 µs | 520–576 ns |
| 200 | 100% | 181–207 µs | 0.55–0.69 µs | 535–618 ns | 1.6–1.7 ns | 5.96–9.78 µs | 6.58–7.24 µs |

Every scan row allocates nothing before and after. The update scan was the large one: every recipient looked at every
unit, so it grew with the square of the units in view (about 4 ns a look); it is now a look at the units that changed.
The pass gains where few units hold an aura; where every unit holds one it is level within the noise (the alternated
pairs at 200 units read 6.0–6.4 µs before and 6.6–7.2 µs after), since the holders' own look at their auras is the
cost there, the same before and after. A first version that wrote every kept holder back into its list on every
collection read 8.6–12.2 µs there; it now writes back only once a holder has been dropped.

`Tick` is not conclusive on this machine: the same row varied up to twofold between runs on either side (200 units,
all holding: 1.08–1.34 ms before, 0.76–1.62 ms after), so the scans' share (up to about 0.2 ms at 200 units) is within
its noise. It allocates the same before and after (94 B to 7.5 KB per tick, the aura update packets each refresh
sends to every watcher); since #980 it allocates 0 to 11 B per tick ([shared state and aura
encoding](#results--shared-state-and-aura-encoding-980-2026-10-14)).

BenchmarkDotNet 0.15.8 (in-process toolchain: the generated project's build timed out on the busy machine), Windows 11,
i9-12900K, SDK 10.0.401 / runtime 10.0.12, five warmups and eight measured iterations (the pass alone eight and
fifteen). The scenario gate reads `forest-combat` at 10,361 B per window after #898 in Debug and Release, 4 B over the
committed 10,357 B, within the 256 B floor: the roster's lists growing to their size once during the run (with the
lists made at 64 entries up front it reads 10,357 B). Nothing is allocated per tick.

From #972 on, `Tick` rows include more than the aura work above: the scenario harness sends every packet through the
real `Connection.Send` and the world connection's `OnQueued`, and its tick runs `QuestFlusher.Flush` for every
connection (the fast path: nothing owed). `Tick` timings before and after #972 are not like for like; the pass and
scan rows are unchanged.

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*AuraScanBenchmarks*" --inProcess --warmupCount 5 --iterationCount 8
```

### Nearest-polygon searches — `NavigationMovementBenchmarks.cs`, `NavigationQueryBenchmarks.cs` (#978)

The navigator's queries against the same queries with DotRecast's own full nearest-polygon searches
(`DetourNavigationReference` in `Avalon.World.Testing`, the navigator as it was before #978), on the committed town
navmesh and on forest seed 7's. Each invocation visits 256 queries, results per query; the bake, the input selection
and the check that both give the same answer for every query are setup. `NavigationMovementBenchmarks` is a player's
grounded step, `MoveOnGround`: idle inputs do not move, walking inputs take an unobstructed 4/60 m step, blocked
inputs press into a real wall from where a raycast stopped, mixed inputs are two idle to three walking, and beside
inputs step from half a metre off the mesh's edge, where no polygon is under the player. `NavigationQueryBenchmarks`
is a creature's route (`FindPath` into a reused list) and line of sight (`HasVisibility`) to a ground point up to 20 m
away, and a projectile's step (`RaycastWalkable`, 20 m/s, flying flat at its caster's height over flights of up to
20 m).

Before #978 every one of these searched for the nearest polygon in a 4 x 8 x 4 m box, measuring every polygon in it: a
movement input twice (the raycast's start, and the ground snap's end when the step moved), 1.16 ms a tick at 950 players
on world 4, plus 51 samples zeroing the raycast's 2 KB path buffer (#974, section 7); a route and a line of sight at
both ends; a projectile's step at its start (the 92 samples #974 puts under `ProjectileAbilityScript` are its
`RaycastWalkable`'s search: the script makes no other navmesh query). Since #978 each search looks first in a narrow box
(0.1 m to each side, 0.2 m across, and the climb height plus 0.1 m up and down; `StandingPolygonSearch`) and takes its
first polygon that scores zero, which is the full search's answer; the full box decides only when nothing there scores
zero. Nothing in that argument needs the point to be where someone stands, so it holds at a route's far end and under a
projectile flying flat over a slope; standing decides only how often the narrow box answers. The raycast, the line of
sight and the route's surface moves no longer zero their stack buffers (`[SkipLocalsInit]`; each is only written, then
read no further than written). Nothing is kept between calls.

How often the narrow box answers, from the setups' classification (the same on both maps): the start of every idle,
walking and mixed input, the end of every step that moved, the start of every route, line of sight and projectile
step measured; 113 of 256 blocked starts in the town and 147 of 256 in the forest (a point on a wall's edge that the
polygon test leaves out goes to the full box); no beside input. `town-walk`'s walkers loop over open ground, which is
the walking case.

Measured 2026-10-14 after rebasing on `ba0d327b`, before (`ba0d327b`'s navigator) and after (`eb50dfa5`), alternated
on a mostly idle machine (another agent's work ran during some runs). The step is minimum–median–maximum over three
runs before and five after, the reference column over every run of the step; the queries are minimum–maximum over
three runs each, with three of `442fdef4` for the zeroing alone. Allocations exact:

| Map | Input | Reference | Before | After | Allocated |
|---|---|---:|---:|---:|---:|
| Town | Idle | 598–658–733 ns | 612–623–758 ns | 186–199–326 ns | 0 B |
| Town | Walking | 1,230–1,278–2,277 ns | 1,234–1,256–1,343 ns | 327–532–599 ns | 0 B |
| Town | Mixed | 998–1,066–1,609 ns | 1,024–1,073–1,166 ns | 270–281–492 ns | 0 B |
| Town | Blocked | 827–917–1,092 ns | 871–883–971 ns | 677–800–852 ns | 0 B |
| Town | Beside | 647–661–750 ns | 648–673–957 ns | 805–814–1,057 ns | 0 B |
| Forest | Idle | 368–388–507 ns | 366–374–392 ns | 166–170–209 ns | 0 B |
| Forest | Walking | 741–784–1,042 ns | 738–785–965 ns | 280–290–454 ns | 0 B |
| Forest | Mixed | 556–634–1,030 ns | 609–622–627 ns | 232–238–334 ns | 0 B |
| Forest | Blocked | 388–430–531 ns | 409–418–422 ns | 335–340–348 ns | 0 B |
| Forest | Beside | 449–457–535 ns | 452–454–622 ns | 524–540–1,211 ns | 0 B |

| Map | Query | Before | Narrow search (`442fdef4`) | + no zeroing (`eb50dfa5`) | Allocated |
|---|---|---:|---:|---:|---:|
| Town | Route | 14,356–14,759 ns | 12,858–13,120 ns | 12,812–18,972 ns | 0 B |
| Town | Line of sight | 1,552–2,336 ns | 489–542 ns | 463–510 ns | 0 B |
| Town | Projectile step | 475–558 ns | 186–219 ns | 183–283 ns | 0 B |
| Forest | Route | 13,388–13,892 ns | 12,858–16,557 ns | 12,832–16,800 ns | 0 B |
| Forest | Line of sight | 866–878 ns | 364–450 ns | 354–556 ns | 0 B |
| Forest | Projectile step | 344–372 ns | 156–240 ns | 155–177 ns | 0 B |

By the minima a walking input costs about 0.27 (town) and 0.38 (forest) of what it did, an idle one 0.30 and 0.45;
a line of sight 0.30 and 0.41, a projectile step 0.39 and 0.45. Town walking's median is high because three of its
five runs read 532–599 ns where the other two read 327–339 ns, as did the runs of the same code before the rebase
(342–429 ns); the walking path did not change between them, so the spread is this machine's, not the change's. A route gains about a tenth: its two searches are a small part of following the corridor. An input beside
the mesh costs a sixth to a quarter more (the narrow box finds nothing, then the full box decides), and a blocked one
is level or cheaper; neither is the steady state of a player on the ground. The zeroing is the smaller step: without
it the line of sight's minima are 10 to 26 ns lower (the movement runs before the rebase: 15 to 25 ns lower in eight
of ten rows), about a 2 KB clear, but no single run separates it from the noise. Nothing allocates, before or after.

Same answers: `GroundedMovementShould.Answer_as_the_full_searches_do` compares the navigator's `MoveOnGround`,
`FindGround`, `RaycastWalkable`, `FindPath` and `HasVisibility` with the reference, to the bit, at every polygon's
corners, edges and inside, at the polygon's height, exactly the climb above and below it, just past it and between
floors, for idle, short, long and corner-ending steps, and (routes and sights, from one point in sixteen) to a 4 m
target, a corner and the previous point, on the town (60 of its 313 polygons have a second floor over or under their
centre), the forest (118 of 603) and a two-floor test mesh. It fails when the narrow box breaks a tie the other way,
when the full box is not asked after a miss (either one also with only the route and sight checks left), when
polygons at the point's edge are skipped, or when the search's height is used for a point not over its polygon.
`Reach_every_polygon_within_the_climb_up_and_down` holds the narrow box's height: on a hand-built tile with 1 cm cells,
two polygons 0.79 m apart both score zero and DotRecast answers the far one for one of the two points; a box of 0.1,
0.5 or 0.75 m answers the near one. Baked meshes cannot show it: floors stand at least the agent's 2 m apart and
DotRecast rounds their bounds out to the 0.3 m cell.

BenchmarkDotNet 0.15.8, in-process toolchain, Windows 11, i9-12900K, SDK 10.0.402 / runtime 10.0.13, five warmups
and ten measured iterations. The scenario gate reads main's figures after #978 (rebased on `fb5dfd13`): `forest-combat`
14,292 B per window in Release and 14,360 B in Debug (committed 14,292 B), `town-social` 3,904 B and 4,008 B (committed
3,904 B), the others 0 B; the baseline does not move.

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*NavigationMovementBenchmarks*" --inProcess --warmupCount 5 --iterationCount 10
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*NavigationQueryBenchmarks*" --inProcess --warmupCount 5 --iterationCount 10
```

### Grounded player movement — `NavigationMovementBenchmarks.cs` (#880, #896)

The benchmark below was reworked by #978 (above): its two methods are now the DotRecast reference and
`MoveOnGround`, on the town and a forest. This section keeps #914's figures as measured then.

Compares the existing `RaycastWalkable` + `SampleGroundHeight` composition with `MoveOnGround` over the committed
town navmesh. Each invocation visits 256 positions, with bake and input selection outside timing. Idle inputs have
zero horizontal displacement, blocked inputs press into real town walls, walking inputs take an unobstructed
4/60-metre step, and mixed inputs alternate two idle with three walking inputs. Results are normalized per input.
Setup checks exact output equality for every input. This measures the navigation operation, not the whole input
handler, tick or live bot workload; the separate-query baseline shares the current raycast helper.

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*NavigationMovementBenchmarks*" --job short --warmupCount 5 --iterationCount 8 --affinity 4
```

Measured 2026-10-12, BenchmarkDotNet 0.15.8, Windows 11, i9-12900K, SDK 10.0.401 / runtime 10.0.12,
concurrent workstation GC, one launch, five warmups and eight measured iterations:

| Input | Separate queries | Combined operation | Ratio | Allocated, both |
|---|---:|---:|---:|---:|
| Idle | 1,274.9 ns | 631.1 ns | 0.50 | 0 B |
| Blocked | 1,658.9 ns | 861.7 ns | 0.52 | 0 B |
| Mixed (40% idle) | 1,170.1 ns | 949.7 ns | 0.81 | 0 B |
| Walking | 1,191.0 ns | 1,197.4 ns | 1.01 | 0 B |

Walking's 99.9% confidence intervals overlap (1,183.1–1,199.0 ns and 1,170.4–1,224.3 ns): this run does not
establish a walking gain or regression. Idle's separate-query estimate has wider error (±156.1 ns); the large
reduction remains clear, but its precise percentage is not a capacity forecast. Live tick and GC effects require
the matched load test after deployment.

The combined operation reuses the raycast's starting nearest-polygon result only when the ground query has the
same X/Z and original Y. Moving endpoints retain their independent search, including multi-storey floor selection.
There is no cross-call cache or change to input/ACK cadence. `GroundedMovementShould` checks town walls, relocations,
missing polygons, overlapping floors and slopes; `PlayerInputHandlerShould` checks real-navigator input results.

### Character dirty deltas — `CharacterDeltaBenchmarks.cs` (#880, #896)

Six hundred characters in twenty real town instances (thirty mutually visible characters each), with repeated
position/velocity/yaw assignments every tick. Moving populations are 0%, 60% and 100%, interleaved within each town.
A batch is 42 ticks, six complete broadcast cycles, to avoid sampling different counts of broadcasts. Setup warms
for 15 seconds; the benchmark includes the scenario's inline send/sealing path. It excludes input ACKs, navigation,
combat, database work and real sockets, so it is not a server-capacity measurement or the live mixed load test.

Before/after on 2026-10-12, identical harness on `895956ed` and the character-delta change:

| Moving | Before mean/tick | After mean/tick | Before / after allocated per tick | Before / after bytes per 42 ticks |
|---|---:|---:|---:|---:|
| 0% | 1.425 ms | 0.601 ms | 3 / 1 B | 7,642,860 / 0 |
| 60% | 1.490 ms | 1.053 ms | 0 / 0 B | 7,995,210 / 3,183,498 |
| 100% | 1.313 ms | 1.290 ms | 3 / 3 B | 8,230,110 / 5,217,558 |

BenchmarkDotNet 0.15.8, Windows 11, i9-12900K, SDK 10.0.401 / runtime 10.0.12, concurrent workstation GC,
affinity mask 4; one launch, five warmups, eight measured iterations. The 99.9% intervals are 1.310–1.541 /
0.574–0.629 ms for idle, 1.389–1.591 / 0.955–1.152 ms for mixed and 1.251–1.375 / 1.216–1.364 ms for all moving.
Idle and mixed means fell about 58% and 29%; the all-moving intervals overlap, so no timing improvement is claimed
there. Bytes written (including the scenario's packet framing/sealing) fell 100%, 60% and 37%. Each non-idle batch
still sent 3,600 update packets; the changed idle batch sent none. The small 1–3 B/tick allocation readings are amortized measurements, not evidence of a recurring allocation
change; all four scenario allocation gates pass without raising their baselines.
Raw benchmark logs stay outside the repository.

`dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*CharacterDeltaBenchmarks*" --job short --warmupCount 5 --iterationCount 8 --affinity 4`

### Database context setup — `Database/DbContextFactoryBenchmarks.cs` (#880, #896)

Measures fresh Auth and Character contexts from their actual singleton DI factories, an `AsNoTracking().Take(1)`
query translated with `ToQueryString()`, and disposal. No database connection is opened. Model creation and query
compilation are warmed outside measurement; each case additionally prewarms for 30 seconds to let tiered PGO settle.
This is the cost of local context setup and warm query translation, not database execution or admission latency.

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*DbContextFactoryBenchmarks*" --job short --warmupCount 10 --iterationCount 8
```

Before/after on 2026-10-12, same committed harness against production factories at `7e2d7afd` and the options-reuse change:

| Context | Before mean | After mean | Before allocated | After allocated |
|---|---:|---:|---:|---:|
| Auth | 41.96 us | 30.83 us | 58,790 B | 42,352 B |
| Character | 44.15 us | 29.90 us | 56,933 B | 40,496 B |

BenchmarkDotNet 0.15.8, Windows 11, i9-12900K, SDK 10.0.401 / runtime 10.0.12, concurrent workstation GC, affinity
mask 4. One launch, ten warmups and eight measured iterations per case (seven retained for the Auth baseline after
outlier filtering). The 99.9% timing intervals were Auth 38.80–45.11 us before / 29.78–31.87 us after and Character
40.78–47.52 us before / 26.40–33.41 us after. This run reduced local time by 26.5% / 32.3% and allocations by about
16.4 KB per operation. Raw reports stay outside the repository.

The factories reuse immutable typed options but still construct a fresh context for every operation. Connection
strings and logging remain local to each factory/host; sensitive logging still follows the Development-only
postconfiguration. The API per-world Character factories are outside this change.

Server GC pause and admission measurements require a follow-up controlled load run after CI deployment.

### Connection checks — `ConnectionTickBenchmarks.cs` (#880)

Measures the production readiness sweep with no pending spawn, an admitted connection's lease check before its
heartbeat is due, and the input session filter for an admitted character in a map. Setup supplies a connected TCP
socket, a fixed clock and an active lease; authentication setup and substitutes stay outside the measured paths.
Since #977 its clocks also pay for a read of the system's clock on each call, and it measures a connection's whole
tick outside its handlers ([below](#per-connection-work-every-tick--connectiontickbenchmarkscs-questflushbenchmarkscs-packetmetricsbenchmarkscs-977));
the table here is from before that.
Each invocation performs 300 checks on the same connection, with results normalized per check. This isolates the
recurring allocation and does not model the cache behavior of 300 distinct connections.

Before/after on 2026-10-11, using the same benchmark against `0b8fc1f2` and the #880 fixes:

| Method | Before mean | After mean | Before allocated | After allocated |
|---|---:|---:|---:|---:|
| `ReadinessSweep` | 4.700 ns | 1.193 ns | 48 B | 0 B |
| `LeaseChecks` | 40.081 ns | 34.516 ns | 32 B | 0 B |
| `InputFilters` | 15.775 ns | 16.009 ns | 24 B | 0 B |

BenchmarkDotNet 0.15.8, ShortRun (one launch, three warmups and three measured iterations), Windows 11,
Intel Core i9-12900K, .NET SDK 10.0.401 / runtime 10.0.12, concurrent workstation GC. ShortRun timing confidence
intervals are wide; the result establishes removal of these recurring allocations, not a server latency improvement.
The readiness baseline also includes a fixed enumeration/closure cost per sweep, amortized over 300 entries here.

The fixes defer captured callbacks until a spawn release or heartbeat is due, enumerate the connection snapshot as
a span, and reuse the character's `MapId` while its backing row value is unchanged. A first map read or map change
still creates a wrapper. `ConnectionTickAllocationShould` guards the idle and waiting readiness paths, idle lease
checks and input filtering, including map changes through either the character or its row.

Server GC pauses require a repeat of the controlled k3s load runs after deployment; these local measurements do
not establish a new player-capacity limit or justify GC configuration changes.

---

### Per-connection work every tick — `ConnectionTickBenchmarks.cs`, `QuestFlushBenchmarks.cs`, `PacketMetricsBenchmarks.cs` (#977)

The 950-player profile (#974, section 7, "Outside the batch" and C7) found 0.5 to 0.9 ms of every tick in work done
for every connection whether or not anything had changed: the lease check (521 kernel samples, a UTC clock read per
connection), the quest flush (486, every connection every tick), clock reads in all (673, spread over the lease, the
session pass's `ProcessQueue`, `WorldSessionFilter.CanProcess` and `PacketDispatchTelemetry.Begin`), the per-packet
metrics (`AggregatorStore.Update` 365 inclusive) and `CharacterEntity.InstanceId` (208 exclusive plus `Guid.DecodeByte`
112: the row's text parsed on every read, 124 under `PlayerInputHandler` and 55 under the quest flusher). This branch
changed five of them, one commit each (the per-packet metrics after they were measured and the owner chose, below), and
left one: `CharacterEntity.Update`'s clock reads (up to four per character per tick: `IsInCombat`, regeneration,
casting) need the tick's time handed into the instances, recorded separately:

- **The instance id is kept parsed.** `CharacterEntity.InstanceId` keeps the id it wrote beside the row's text and
  answers from it while the row holds that text; the row is still what is saved, and a row written another way is
  parsed. A portal entry's and a teleport's arrival save wrote the row's text again after the transfer had set it;
  they go through the entity, which leaves it alone, so a transfer formats the id once instead of twice (96 B less
  per portal entry and teleport in production; `ArrivalBenchmarks` still reads 504 B, its transfer writing a text it
  made in advance, so its one format is now the save's step 13's).
- **One clock read per tick.** `WorldServer` reads the container's clock once per tick (`TickTime`: the wall clock and
  the monotonic one) and hands it to every connection's lease check, which keeps it; the tick's passes and each of
  their packets judge the lease at that time instead of reading the clock, and the heartbeat and admission timings use
  it too. Before, a connection's lease check read the clock about six times (the lease's activity and its time left
  each read both clocks, under the lease's lock) and each pass and each packet's filter twice more.
- **The lease is checked against due times.** The grant sets two (the deadline less the 5 s save margin on the wall
  clock, the time granted less the margin on the monotonic one), and the check is two comparisons. Same boundary:
  `WorldServerBarrierTickShould.End_gameplay_on_the_tick_the_lease_comes_within_the_save_margin_of_its_end` pins it on
  each clock, and fails with either comparison made strict or either margin dropped.
- **The tick's dispatch asks a lease its check found not due only whether it was revoked** (`IsRevoked`, a volatile
  read), instead of taking the lease's lock and comparing both clocks again for each pass and each packet. A tick
  whose session pass runs past the 5 s margin reads its time again and checks every lease before the map pass (the
  long-tick guard; the barrier test's "long session pass" row fails without it).
- **The quest flush skips a connection with nothing to flush** (`QuestLog.FlushOwed`, [quests](quests.md)).
- **A handler's duration is recorded under its packet type alone**, without `avalon.outcome` (owner's decision): a
  second tag made the SDK's search for the series cost about four times as much on every packet. Failures are counted
  by `avalon.packet.handler.errors`, and the span keeps its outcome ([instrumentation](instrumentation.md#packet-handlers)).

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter '*ConnectionTickBenchmarks*' '*QuestFlushBenchmarks*' '*PacketMetricsBenchmarks*' --job short
```

`ConnectionTickBenchmarks`' clocks now read the system's clock on every call, as production's does, and answer a fixed
time (before #977 they read nothing, which hid the cost this issue is about). `IdleTicks` is one connection's share
of a tick outside its handlers with nothing received: the lease check, then the session and map passes; `InputTicks`
the same with one input received (the read loop's queueing of it included, which still reads the clock for its
arrival stamp and the filters') and dispatched to a handler that counts it. `QuestFlushBenchmarks` flushes 300
connections through the real `QuestService` over the seeded quests and a real town instance (no quest NPC placed in
it, so markers walk no creature; #973 row 1 grows with them), each character holding quest 1 with its log already
sent; `NoChange` is the common tick, `OneChanged` one character's kill count moved, `AllChanged` every one's.

**Results, per connection, ns.** BenchmarkDotNet 0.15.8 ShortRun, Windows 11, i9-12900K, SDK 10.0.401 / runtime
10.0.12, 2026-10-14. Other builds and tests ran on the machine at the same time, so the timings are noisy: two runs
for each column (three for the last), as a range. Nothing in any row allocates, but `AllChanged`'s update packets.

| | As it was (`1e7d9618`) | Instance id kept | One clock read per tick | Lease due times, quest flag (the branch) |
|---|---:|---:|---:|---:|
| `LeaseChecks` | 184–192 | | 44 | 19–23 |
| `IdleTicks` | 313–315 | | 82–85 | 50–66 |
| `InputTicks` | 714–715 | | 406–439 | 359–404 |
| `InputFilters` (the read loop's, unchanged) | 63–65 | | 53–58 | 53–59 |
| `QuestFlushBenchmarks.NoChange` | 95–101 | 25 | | 3.6–3.7 |
| `QuestFlushBenchmarks.OneChanged` | 91–92 | 24 | | 4.8–5.0 |
| `QuestFlushBenchmarks.AllChanged` (200 B: the update) | 459–571 | 416 | | 253–352 |

After the reviews, on main `c32d3a17` (#983) and two runs each: dispatch asking a lease its check found not due only
whether it was revoked took `IdleTicks` from 48–49 to 25.5 ns and `InputTicks` from 285–291 to 241–242 ns; `LeaseChecks`
stayed at 17–19 ns (its pass-time guard is once per tick, not per connection, and not in these rows).

At 950 connections (the profile's), the lease check, both passes' authorization and the quest flush with nothing
changed come to about (313 + 98) × 950 ≈ 390 µs a tick before and (55 + 4) × 950 ≈ 55 µs after on this machine; the
homelab node ran the same work 1.2 to 1.6 times slower in the profile. A ramp on world 4 decides what it is worth.

**Per-packet metrics: measured, then the outcome tag dropped.** `PacketMetricsBenchmarks` measures, per packet, with
the OpenTelemetry SDK listening (a reader that exports nothing within a run) and without: `DispatchInput` is the
dispatch telemetry around a received input (no span, no scope; its duration was recorded under `avalon.packet.type` and
`avalon.outcome`, and is now under the type alone), `CountSent` the two untagged counters `WorldConnection.OnQueued`
adds for every packet sent; the `Record*` rows record into a histogram shaped like the handler duration with both tags,
with them in the other order, with the type alone and with none. Same runs, with both tags:

| ns per packet | Nothing listening | SDK listening |
|---|---:|---:|
| `DispatchInput` | 39–46 | 188–286 |
| `CountSent` (two counters) | 0.7–1.1 | 20–26 |
| `RecordBothTags` | 0.6 | 149–171 |
| `RecordBothTagsSorted` | 0.6 | 157–159 |
| `RecordTypeTag` | 0.4 | 36 |
| `RecordUntagged` | 0.4–0.5 | 12.6 |

Nearly all of a dispatch's metric cost is the SDK finding the series of a measurement with two tags: about 150 ns,
where one tag costs about 36 and none 13; the tags' order changes nothing. Every way to make it cheaper changes a
series or what is recorded, so the owner chose (#977) among: record the duration of the high-rate types (input, pong)
under the type alone or not at all; drop `avalon.outcome` from the duration (the error counter already counts failures)
or fold it into the type's value; sample the high-rate types; or keep per-type buckets on the tick and publish them
through an observable instrument once per export, which turns the histogram into another kind of series. **Chosen: drop
`avalon.outcome` from the duration, for every type.** The histogram's series are now one per packet type (its `ok` and
`error` series merged; no homelab dashboard or alert read the tag); `avalon.packet.handler.errors`, by type, counts
failures, and a span still carries its outcome. The others were not taken.

| `DispatchInput`, ns per packet | Nothing listening | SDK listening |
|---|---:|---:|
| Both tags (`2c0d2ed4`) | 37.9–40.8 | 189–225 |
| The type alone | 37.5–37.7 | 77.6–83.4 |

`--filter '*PacketMetricsBenchmarks.DispatchInput*' --job short`, four runs a side (the fourth pair taken in turn, one
right after the other), 2026-10-14, same machine and toolchain, with other builds and tests running on it: the ranges
are the runs' means. Nothing allocates, either way. About 120 ns less per input is about 0.11 ms of every tick at 950
players sending one input a tick each, on this machine. The sent-packet counters cost about 20 ns per packet sent and
could be summed per connection and added once per tick with the same series and totals, but `Connection.Send` is
called off the tick too (admission replies), so where and when to add them is a design choice as well. The handler's
duration still reads the clock twice per packet; one read (the end of one dispatch as the start of the next) would put
the loop's own work between packets into each duration.

---

### Social events — `Benchmarks/SocialEventBenchmarks.cs` (#973)

One of each of `town-social`'s events through the real handlers, with the send pass after each: a line said to a town
of 30 (`SaidLine`), a `/w` whisper (`Whisper`), a `/p` party line (`PartyLine`), and a party's turn: its leader leaves,
is invited back by name and accepts (`LeaveInviteAccept`). The chat rate limit is off, so every line is delivered;
setup checks what each event sent. Results: [a social town, quieter](#results--a-social-town-quieter-973-2026-10-15).

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*SocialEventBenchmarks*"
```

### Receive queue — `ReceiveQueueBenchmarks.cs` (#880)

Measures the production `WorldConnection` arrival, session/map filtering and handler dispatch paths. Each operation
enqueues a burst and runs both tick passes on one admitted connection; 256 operations per invocation are normalized
per burst. The fixed clock and non-allocating server fixture keep leases active and avoid measuring substitutes.
Payload construction, deserialization, TLS, socket reads and movement simulation are excluded. The packet object is
reused because this benchmark measures queue ownership, not packet creation. Arrivals and dispatch run sequentially;
the unit tests separately exercise a producer and tick consumer on different threads.

Before/after on 2026-10-11, against `6c279a9b` and the queue fix, same machine and harness:

| Packets per burst | Before mean | After mean | Before allocated | After allocated |
|---:|---:|---:|---:|---:|
| 1 | 181.5 ns | 154.8 ns | 40 B | 0 B |
| 32 | 4.008 us | 3.559 us | 1,280 B | 0 B |
| 150 | 18.751 us | 16.398 us | 5,408 B | 0 B |

BenchmarkDotNet 0.15.8, ShortRun (one launch, three warmups, three measured iterations), i9-12900K, Windows 11,
SDK 10.0.401 / runtime 10.0.12, concurrent workstation GC. Means improved in this local run; ShortRun timing
confidence intervals are wide, particularly for the one-packet baseline. These measurements establish allocation
removal and show no local CPU regression, not a server latency or capacity improvement.

`ConcurrentQueue.TryPeek` preserves the observed segment, preventing dequeues from making its slots reusable. The
tick now dequeues once and holds a head refused by one filter for the other pass. `ReceiveQueueAllocationShould`
guards storage reuse at all three burst sizes. Queue behavior tests cover FIFO, held-head depth and arrival stamps,
authorization revocation/maintenance, character-leave stale packets and the existing filter wedge behavior.

**The depth cap (#927).** A connection may have `Network:MaxReceiveQueueDepth` packets (2,048) waiting; one that arrives
with that many waiting closes it ([packet handlers](packet-handlers.md)). The depth is two counts, each with one writer:
the read loop counts a packet before it queues it and the tick counts it out as it takes it, so the arrival path gains
three reads of its own fields, a compare and a store, and the dispatch path a read and a store. The benchmark's bursts
(at most 150) are emptied every cycle, so the check's other branch, the read of the tick's count, runs once in about a
cap's worth of packets, as it does for a connection the tick keeps up with. The runs below were taken with the cap at
1,024, the default before the owner set 2,048; at 2,048 that branch runs half as often and nothing else differs.

Before/after on 2026-10-13, `814a9105` and the change, the same benchmark both sides. Twelve runs before and eleven
after, interleaved (a twelfth run of the change did not build: a file held by another process); each figure is the
median of the runs' means with their range, then the median of each run's fastest iteration.

| Packets per burst | Before mean | After mean | Before fastest | After fastest | Allocated, both |
|---:|---:|---:|---:|---:|---:|
| 1 | 158.4 ns (153.8 to 213.7) | 158.2 ns (155.4 to 213.5) | 155.0 ns | 156.0 ns | 0 B |
| 32 | 3,551 ns (3,484 to 4,599) | 3,595 ns (3,494 to 4,602) | 3,525 ns | 3,517 ns | 0 B |
| 150 | 16,374 ns (16,184 to 21,678) | 16,398 ns (16,268 to 20,158) | 16,243 ns | 16,226 ns | 0 B |

BenchmarkDotNet 0.15.8, Windows 11, i9-12900K, SDK 10.0.401 / runtime 10.0.12, concurrent workstation GC; one launch,
six warmups and fifteen measured iterations per run, pinned to one performance core (affinity mask 1024 for the first
six pairs, 4096 for the rest). Other builds and benchmarks were using the machine: the last three runs, of both sides,
were about a third slower (the top of every range), and several earlier ones had a spread of 100 to 600 ns at 32
packets against 3 to 60 ns in a quiet run. No row moved by more than the runs differ among themselves. The fastest
iterations, the figure the disturbance touches least, differ by 1 ns at one packet and by less than 0.3% at 32 and
150, there in the change's favour, which is the same noise.

- **Nothing is allocated.** `ReceiveQueueAllocationShould` still reads exactly 0 B at all three burst sizes, and the
  close itself (one generated Warning, one counter increment) happens once per connection.
- **What the benchmark cannot show.** It runs arrivals and the tick on one thread; in the world they are two, and a
  field one writes for every packet costs the other a cache line whenever they share it. So the read loop's side
  (its count, the tick's count as it last read it, and the cap) is a struct with 64 bytes of nothing before and after
  it. Measured on the Release build of the branch's last commit, counted from the object's start: those three fields
  are at 536, 540 and 544, the field before them (`_pendingReceivePacket`) ends at 472 and the one after
  (`CurrentTargetGuid`) starts at 616. However the object falls across 64-byte lines, the line or two they occupy hold
  nothing else, so the read loop's store per packet takes no line from the tick, and the tick's writes take none from
  the check. The tick's count (`_handledPackets`) is at 412, between `_hasPendingReceivePacket` (408), which the tick
  reads for every packet it takes, and `LastInputSeq` (416), which it writes for every input;
  `CurrentPacketArrivedTicks`, which it writes for every packet it dispatches, is at 384. An object starts on a
  multiple of 8, so there are eight placements against the 64-byte lines. In seven the count shares its line with
  `LastInputSeq`; in the eighth (the object at 32 mod 64) the line ends between them and the count shares it with
  `CurrentPacketArrivedTicks` instead. So in every placement the count lies on a line the tick already writes for the
  same input, and counting an input out dirties no other. For a packet that is not an input the tick's write is
  `CurrentPacketArrivedTicks` alone, on the count's line in five placements of eight: in the other three the count
  dirties one line more for that packet. The flags the read loop reads for every packet (`_packetEncryption`,
  `_maintenanceBlocked`, `_tlsAuthenticated`, `_protocolAccepted`, at 432 to 440) are on the count's line in at most five
  placements, each of them one where `LastInputSeq` is on it too: the read loop was already taking that line back
  after each input the tick handled, and the count adds no placement where it must. The closing check the read loop
  makes for every frame reads `_closed` (172), which in every placement shares a line with a counter the read loop
  itself writes for that frame (`PacketReceivedCount` at 176, or `BytesReceivedCount` at 120).
- **The gauge is cheaper.** `world.receive_queue.depth` reads the two counts where it called `ConcurrentQueue.Count`
  on every connection (which can take the queue's cross-segment lock), on the exporter's thread.
- **The connection is about 150 bytes larger** (the padded struct, the tick's count and a reference): about 150 KB at
  1,000 connections.

### Read loop framing — `PacketStreamBenchmarks.cs` (#927)

`PacketStream.EnumerateRawFramesAsync` alone, over 4,096 frames laid back to back in a `MemoryStream`, normalized per
frame: the read into the buffer, the length prefix and the slice handed out. No socket, TLS, parsing or decoding. An
80-byte frame has a one-byte length prefix and a 300-byte one two.

Before/after on 2026-10-13: `0f1d2132` (main) against the branch that gives a frame a maximum length
(`Hosting:MaxInboundFrameSize`). It reads the prefix as up to 35 bits where main read 32 and threw past them, and
compares it with the maximum before anything is sized by it. The benchmark is the same on both sides but for the
argument that carries the maximum (16,384). Six runs a side, interleaved; the median of the runs' means, with their
range.

| Frame | Before | After | Allocated, both |
|---:|---:|---:|---:|
| 80 bytes | 14.67 ns (14.27 to 17.52) | 14.34 ns (14.04 to 18.30) | 0 B |
| 300 bytes | 17.73 ns (17.41 to 25.79) | 18.09 ns (17.96 to 19.66) | 0 B |

BenchmarkDotNet 0.15.8, Windows 11, i9-12900K, SDK 10.0.401 / runtime 10.0.12, concurrent workstation GC; one launch,
six warmups and fifteen measured iterations per run, pinned to one performance core (affinity mask 4096). Other
builds and benchmarks were using the machine: one or two runs a side were disturbed (the top of each range, with a
spread of 2 to 4 ns inside the run against 0.02 to 0.7 ns otherwise).

- **One-byte prefix: no difference.** The ranges cover each other and the medians differ by a third of a nanosecond,
  in the change's favour.
- **Two-byte prefix: about a third of a nanosecond more**, 2%. The undisturbed runs do not overlap (17.41 to 17.83
  before, 17.96 to 18.11 after), so this one is likely real: the prefix is assembled in 64 bits and the frame gets
  its compare. A connection that sends 60 frames a second pays 20 ns a second for it.
- **Nothing is allocated per frame** on either side; the enumerator itself is one object per connection.
- A review of the change measured the same with a harness of its own and saw no cost: 80-byte frames 23.8 to 24.6 ns
  on main against 23.7 to 24.4 on the branch, 300-byte frames 35.3 to 36.6 against 35.2 to 36.5, 0 B.
- The read loop around the framing gained one read of `_closed` per frame (it discards what arrives on a closing
  connection, [graceful shutdown](networking-graceful-shutdown.md#what-a-closing-connection-reads)). No benchmark
  runs that loop, which needs a socket and a stream to authenticate; the field's place is under the receive queue
  section above.
- With #897 the same loop gained one compare per frame, of the decoded packet against null (a frame whose opcode the
  server has no packet type for is dropped there), and catch clauses that cost nothing until a frame is refused.
  It is still not benchmarked as a loop; what it calls per frame is, under
  [the read path's refusals](#results--the-read-paths-refusals-897-2026-10-13).

### Navmesh bake — `navmesh-bake` (#880, #896)

Uses the committed four-chunk town: 1,990 vertices, 3,424 triangles, 49 tiles. Tile rows compare
`keepInterResults=true/false` in alternating AB/BA order with otherwise identical production settings and geometry.
Production rows call the actual asynchronous baker, including OBJ composition/loading and its tile workers.
`GC.GetTotalAllocatedBytes` includes allocations on all managed threads. Forced collections happen outside timing,
first while the complete build result is referenced, then after releasing everything except the finished navmesh.
This isolates retained result graphs; it does not measure peak live memory during baking or natural GC pause tails.

Same environment as the queue benchmark, with the default workstation GC. Matched repeats used the same executable
and dependencies, alternating the saved before/after World assemblies in six fresh processes: three per setting,
24 pairs per process, two warmup bakes per mode. The saved before assembly includes the queue fix; only the navmesh
retention setting changes between these assemblies.

| Measurement | Retain intermediates | Release intermediates |
|---|---:|---:|
| Full tile-build result live memory, mean (144 bakes each) | 7.114 MB | 0.576 MB |
| Final navmesh live memory | about 0.202 MB | about 0.202 MB |
| Production bake mean (72 bakes each) | 40.032 ms | 38.017 ms |
| Production bake median | 39.137 ms | 38.933 ms |
| Production allocated bytes, mean | 29.154 MB | 29.053 MB |

MB is decimal. The full-result graph shrank by 6.538 MB (91.9%). Initial allocations remain essentially unchanged;
the small allocation difference varies with tiered warmup and is not attributed to this change. An initial separate
short before/after run showed a 13.6% production-time increase; it did not reproduce in the alternating repeats.
The matched medians are nearly unchanged, so the result is a memory-lifetime improvement without a reproducible CPU
slowdown, not a bake-time speedup claim. No GC tuning was applied. Server GC/load-test pause effects still need a
controlled deployment comparison with the same generated layouts and schedule.

`NavmeshBakeShould` compares canonical serialized tile data and actual path, raycast and ground queries between
retained, released and production bakes. Canonical tile order removes worker-completion ordering from the comparison.
Mesh/detail data and every geometry parameter remain the same.

---

### Tick Loop — `TickLoopBenchmarks.cs`

Measures per-tick scheduling overhead of the `WorldServer` throttle mechanism.

| Scenario | What it models |
|---|---|
| `Yield_1` | Baseline: one thread-pool hop per tick (equivalent to a `PeriodicTimer` loop) |
| `Yield_5` | Fast tick at low load (~11 ms wait) |
| `Yield_13` | Typical at target load: ~3 ms tick + ~13 ms wait |
| `Yield_20` | Idle server, sub-ms tick (~16 ms wait) |
| `SynchronousPath` | Async state-machine cost only — zero scheduling, the theoretical floor |
| `PeriodicTimer_Tick` | Refactored loop: one `WaitForNextTickAsync` per tick (1 ms period) |

---

### Tracking frame stamps (#880, #896)

`EntityTrackingSystem` uses one dictionary keyed by the stable raw GUID. Each entry retains the original GUID
for notifications and its last-seen frame, replacing the two sets and their repeated hash lookups. An empty dirty
map skips dirty lookups. Unique seen counts retain the no-removal early exit; duplicates, pending callbacks,
reset and frame wrap keep their existing observable behavior. Candidate collection and visibility rules are
unchanged: this saves work per candidate, not the number of candidates scanned per recipient.

Before/after on 2026-10-12, identical harness on `65dda551` and the frame-stamp change:

| Creatures | Dirty | Before mean | After mean | Allocated, both |
|---:|---:|---:|---:|---:|
| 30 | 0% | 0.587 us | 0.337 us | 0 B |
| 30 | 10% | 0.650 us | 0.421 us | 0 B |
| 30 | 100% | 0.706 us | 0.487 us | 0 B |
| 200 | 0% | 3.756 us | 2.110 us | 0 B |
| 200 | 10% | 4.238 us | 2.723 us | 0 B |
| 200 | 100% | 4.610 us | 3.117 us | 0 B |

| Creatures | Replaced | Before mean | After mean | Allocated, both |
|---:|---:|---:|---:|---:|
| 30 | 1% | 0.735 us | 0.369 us | 0 B |
| 30 | 100% | 1.239 us | 0.723 us | 0 B |
| 200 | 1% | 4.003 us | 2.379 us | 0 B |
| 200 | 100% | 8.413 us | 4.712 us | 0 B |

`InterestVisibilityBenchmarks` adds 200-creature views with the actual 60 m entry radius and 10 m removal margin:
all nearby, mostly distant, or all previously tracked at 65 m. Ten percent have dirty fields. It exercises the real
`CharacterCharacterGameState.Update`, with stable inputs and no setup work in the timed operation.

| Layout | Before mean | After mean | Reduction | Allocated, both |
|---|---:|---:|---:|---:|
| Dense | 4.069 us | 2.976 us | 27% | 0 B |
| Sparse | 1.075 us | 0.901 us | 16% | 0 B |
| Margin | 4.653 us | 3.317 us | 29% | 0 B |

The existing 600-character / twenty-instance `CharacterDeltaBenchmarks` includes whole ticks and inline sealing:

| Moving | Before mean/tick | After mean/tick | Before / after allocated per tick |
|---|---:|---:|---:|
| 0% | 0.561 ms | 0.435 ms | 0 / 0 B |
| 60% | 1.190 ms | 0.805 ms | 5 / 2 B |
| 100% | 1.151 ms | 1.104 ms | 16 / 2 B |

Idle whole-tick time fell 22%, with non-overlapping 99.9% intervals (0.538-0.583 / 0.422-0.449 ms).
The moving intervals overlap (60%: 0.844-1.535 / 0.744-0.867 ms; 100%: 1.064-1.239 / 0.958-1.251 ms),
so their lower means do not establish a whole-tick speedup. Wire samples are identical before/after: 0,
3,183,498 and 5,217,558 bytes per 42 ticks; 0, 3,600 and 3,600 packets. Small whole-tick allocation readings
are amortized measurements, not evidence of a recurring allocation change. All four scenario allocation gates
pass against the unchanged baseline.

BenchmarkDotNet 0.15.8, Windows 11, i9-12900K, SDK 10.0.401 / runtime 10.0.12, concurrent workstation GC,
affinity mask 4; one launch, five warmups, eight measured iterations. Tracking and visibility confidence
intervals do not overlap. These are short sequential local runs: some visibility iterations are below the
recommended 100 ms (minimum 58 ms after), and production uses Server GC. They establish local path savings,
not a live capacity increase; the mixed server load and phase trace must be repeated after deployment.
Raw logs stay outside the repository.

`dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*EntityTracking*" "*InterestVisibilityBenchmarks*" "*CharacterDeltaBenchmarks*" --job short --warmupCount 5 --iterationCount 8 --affinity 4`

### Entity Tracking — `EntityTrackingBenchmarks.cs`

Measures the production `CharacterCharacterGameState.Update`: interest filtering plus creature, character and
world-object tracking. All 30 or 200 creatures are visible; the character and world-object inputs are empty.
`EntityTrackingBenchmarks` uses stable membership with prebuilt dirty maps for 0%, 10% or 100% of creatures.
`EntityTrackingChurnBenchmarks` alternates two prebuilt views, replacing 1% (at least one) or 100% of creatures
on each update. Setup primes membership and notification-list capacity and checks the expected notifications.
Input construction and dirty-map population stay outside the measured operation.

Before/after on 2026-10-11, the same corrected harness against `d9a10051` and the tracking early exit:

| Creatures | Dirty | Before mean | After mean | Reduction | Allocated, both |
|---:|---:|---:|---:|---:|---:|
| 30 | 0% | 0.781 us | 0.563 us | 27.8% | 0 B |
| 30 | 10% | 0.820 us | 0.627 us | 23.6% | 0 B |
| 30 | 100% | 0.902 us | 0.683 us | 24.3% | 0 B |
| 200 | 0% | 4.868 us | 3.566 us | 26.7% | 0 B |
| 200 | 10% | 5.223 us | 4.259 us | 18.4% | 0 B |
| 200 | 100% | 5.554 us | 4.420 us | 20.4% | 0 B |

| Creatures | Replaced | Before mean | After mean | Reduction | Allocated, both |
|---:|---:|---:|---:|---:|---:|
| 30 | 1% | 0.745 us | 0.690 us | 7.4% | 0 B |
| 30 | 100% | 1.404 us | 1.261 us | 10.2% | 0 B |
| 200 | 1% | 4.698 us | 4.194 us | 10.7% | 0 B |
| 200 | 100% | 8.425 us | 8.154 us | 3.2% | 0 B |

BenchmarkDotNet 0.15.8, one launch, ten warmups and eight requested measurements, affinity mask 4,
i9-12900K, Windows 11, SDK 10.0.401 / runtime 10.0.12, concurrent workstation GC. Standard outlier filtering
retained seven or eight measurements. These are sequential local runs, not a capacity estimate; production uses
Server GC and varying interest sets. Churn includes the unchanged-membership fast path for the two empty trackers. The 200-creature/full-replacement
confidence intervals overlap; its 3.2% reduction is an observed mean difference, not an established speedup.

The old active cases mutated creatures but passed an empty dirty map and allocated a new dictionary per call;
the corrected cases exercise update notifications and report the production path's allocation cost. Comparing
the old harness's numbers with this table would mix harness changes with the optimization.

After additions and dirty notifications, every seen guid is already tracked. Equal set counts therefore allow
skipping the removal scan when no pending removals remain. Changing membership retains the original scan;
the pending-work check also preserves recovery after a removal callback throws. Replication cadence, visibility
rules and packet contracts are unchanged. Existing reset and same-count replacement coverage remains in place;
tracking tests additionally protect dirty updates, duplicate inputs and pending-removal recovery.

---

### Packet Serialization GC-001 — `PacketSerializationGcBenchmarks.cs`

Before/after allocation comparison for the GC-001 fix: `MemoryStream + ToArray` versus `PacketEncoder.Encode` into a
pooled segment, released after the call (#875).

| Scenario | What it models |
|---|---|
| `Legacy_SmallPacket` | Old pattern — `MemoryStream + Serializer.Serialize + ms.ToArray() + encrypt` on a small packet (one field) — **baseline** |
| `Pooled_SmallPacket` | New pattern — `PacketEncoder.Encode` into a pooled segment on the same packet, released after the call |
| `Legacy_MediumPacket` | Old pattern on `SChatMessagePacket` (two `ulong`s, two `string`s, `DateTime`) |
| `Pooled_MediumPacket` | New pattern on the same medium packet |

Only the legacy path has an encrypt delegate now, an identity (`bytes => bytes`), to isolate serialization cost from
crypto cost; the pooled path does not seal (the send path does, as it frames the packet).

---

### Broadcast State GC-002 — removed

Both shapes it compared are gone. Entity replication no longer places bytes into a buffer by
hand, so there is no rented buffer, no per-entity copy and no slice whose lifetime has to
outlive it; the serializer owns the memory. The results below are kept as a record of what the
old path cost, not as something that can be re-run.

---

### Packet Reader GC-007 — `PacketReaderGcBenchmarks.cs`

Before/after allocation comparison for the GC-007 fix: `MethodInfo.Invoke` with a per-call `new object?[3]` args array and a boxed `ReadOnlyMemory<byte>` struct, versus a cached typed `Func<ReadOnlyMemory<byte>, Packet?>` delegate called directly.

| Scenario | What it models |
|---|---|
| `Legacy_ReflectionInvoke` | Old `PacketReader.Read()` — `MethodInfo.Invoke(null, new object?[] { mem, null, null })` — **baseline** |
| `Delegate_Cached` | New `PacketReader.Read()` — `deserializer(new ReadOnlyMemory<byte>(payload))` |

Both paths deserialize an identical `CCharacterListPacket` payload. The residual allocation in `Delegate_Cached` is the deserialized packet object itself — unavoidable.

---

### World Packet Queue GC-009 — `WorldPacketQueueGcBenchmarks.cs`

This is a historical model comparison, not the current concurrent receive queue. Use `ReceiveQueueBenchmarks` above
for production arrival/filter/dispatch measurements.

Before/after allocation comparison for the GC-009 fix: `class WorldPacket` + `LinkedList<T>`-backed
`LockedQueue` versus `readonly record struct WorldPacket` + `Queue<T>` ring buffer.

| Scenario | What it models |
|---|---|
| `Legacy_ClassQueue` | Old pattern — `new class LegacyWorldPacket` + `LinkedListNode` per packet — **baseline** |
| `Struct_RingBuffer` | New pattern — `readonly record struct` inline in `Queue<T>` ring buffer |

Both paths use a `_ => true` predicate to isolate queue/struct allocation from filter logic.
The `Struct_RingBuffer` queue is reused across iterations so the ring buffer reaches
steady-state capacity after the first iteration; subsequent iterations allocate zero.

---

### Packet Reader Decrypt GC-008 — `PacketReaderDecryptGcBenchmarks.cs`

Before/after allocation comparison for the GC-008 fix: old two-step `Decrypt` (allocates a new
`byte[]` for the decrypted payload, swaps `packet.Payload`) + `Read` versus the new single
`Read(packet, decrypt)` call that rents an `ArrayPool<byte>` buffer, decrypts via spans, and
returns the buffer to the pool within the same call.

| Scenario | What it models |
|---|---|
| `Legacy_DecryptAndRead` | Old pattern — `packet.Payload = decryptFunc(packet.Payload)` (new `byte[]`) then `Read` — **baseline** |
| `Fixed_DecryptAndRead` | New pattern — `Read(packet, decrypt)` with rented buffer, no payload swap |
| `Session_MethodGroupPerPacket` | A real `AvalonCryptoSession` opening a sealed packet, passed as the method group `_server.Decrypt` on every call: `Connection`'s read loop before #854 |
| `Session_CachedDelegate` | The same, through a `DecryptFunc` created once: `Connection`'s read loop since #854 |

In the first two arms the passthrough `DecryptFunc` (`input.CopyTo(output); return input.Length`) isolates allocation
from actual cipher cost. At steady state the `ArrayPool` bucket for this payload size is pre-warmed —
zero net allocation per call for the buffer.

---

### Context Factory Delegate GC-010 — `GetContextPacketGcBenchmarks.cs`

Before/after CPU cost comparison for the GC-010 fix: `Activator.CreateInstance` + `PropertyInfo.SetValue × 2` on a warm cache versus a cached typed `Func<IConnection, Packet?, object>` delegate called directly. Applied to both `WorldServer` and `AuthServer`; benchmark covers the `WorldPacketContext<T>` path.

| Scenario | What it models |
|---|---|
| `Legacy_ActivatorAndSetValue` | Old `GetContextPacket` warm-cache path — `Activator.CreateInstance` + `SetValue × 2` on pre-reflected `PropertyInfo` fields — **baseline** |
| `Delegate_Cached` | New `GetContextPacket` — single `Func<IConnection, Packet?, object>` delegate invocation |

Note: `[MemoryDiagnoser]` will show **equal allocations** on both sides (~32 B). `WorldPacketContext<T>` is a struct that gets boxed to `object` in both paths — the win is CPU speed, not allocation count.

---

### CallListener DIM Dispatch GC-011 — `CallListenerGcBenchmarks.cs`

Before/after allocation comparison for the GC-011 fix: `MethodInfo.Invoke` with a per-call `new object[2]` args array and a boxed `CancellationToken`, versus a single cast to `IPacketHandlerNew` + virtual dispatch. Both paths call the same `CClientInfoHandler` to include handler-internal overhead equally.

| Scenario | What it models |
|---|---|
| `Legacy_ReflectionInvoke` | Old `CallListener` warm-cache path — `MethodInfo.Invoke(handler, new object[] { ctx, token })` — **baseline** |
| `Interface_Dispatch` | New `CallListener` — `((IPacketHandlerNew)handler).ExecuteAsync(ctx, token)` via DIM bridge |

The allocation difference is the eliminated `new object[2]` args array and boxed `CancellationToken` per dispatch.

---

### Serialization — `SerializationBenchmarks.cs`

Measures Protobuf-net packet serialization and deserialization, with and without the AES-GCM
session layer.

| Scenario | What it models |
|---|---|
| `Serialize_NoEncryption` | Serialize `CClientInfoPacket` — no encryption |
| `Serialize_Encrypted` | Serialize `CCharacterListPacket` through the session's `Encryptor`, as every client-to-server packet does (the `Encrypt` method group before #854) |
| `Deserialize_Encrypted` | Deserialize + decrypt + inner-deserialize an encrypted packet |
| `Deserialize_NoEncryption` | Deserialize an unencrypted `NetworkPacket` |

Both sides of the key agreement use P-256, the curve `CryptoManager` and `AvalonCryptoSession`
use in production, so the session key is a 256-bit AES key negotiated the way a live connection
negotiates it.

**Status:** Baseline recorded 2026-09-10. No active refactor in progress.

---

### Session Cipher — `SessionCipherBenchmarks.cs`

Compares the session cipher as the packet pipeline calls it — `AvalonCryptoSession.Encrypt` /
`Decrypt` — against a bare `System.Security.Cryptography.AesGcm` over the same key and the same
nonce + ciphertext + tag layout. Since #850 the session is itself the platform `AesGcm`, keyed once
per direction; before it, the session was BouncyCastle AES-GCM re-keyed per call, and its arms were
named `BouncyCastle_Encrypt` / `BouncyCastle_Decrypt`.

| Scenario | What it models |
|---|---|
| `Session_Encrypt` | Production `AvalonCryptoSession.Encrypt` — counter nonce, one freshly allocated result sealed in place (a client's path) |
| `SessionSealInto` | Production `AvalonCryptoSession.SealInto` — counter nonce, sealed into a caller's frame, nothing allocated (the server's send path, #875) |
| `Session_Decrypt` | Production `AvalonCryptoSession.Decrypt` — caller-supplied output buffer |
| `AesGcm_Encrypt` | Bare platform encrypt into an equivalently allocated result buffer (random nonce) |
| `AesGcm_Decrypt` | Bare platform decrypt into the same caller-supplied output buffer |

`PayloadSize` is parameterised at 64, 256 and 1024 bytes. The key is a real P-256 ECDH agreement
shared by both arms, so the two differ in call shape only — never in key material.

**Status:** Baseline recorded 2026-09-10; re-measured 2026-10-09 before and after #850, and 2026-10-10 before and
after the session lock went (#875). Until #875 both session arms took the session's one lock.

---

### Connection key exchange — `ConnectionKeyExchangeBenchmarks.cs` (#878)

What a connection's key exchange costs, which a world with `Network:PacketEncryption` off no longer pays (#878): until
then every world connection built a P-256 key pair and a session around it as it was created, and keyed the session
at admission, whatever the world's mode. Now only a world that seals, and the auth server, do.

| Scenario | What it models |
|---|---|
| `Construct` | `new ConnectionKeyExchange()`: the key pair (BouncyCastle P-256 keygen, the public key's DER) and the session object, with its own `SecureRandom`. What a connection paid as it was created |
| `ConstructAndKey` | The same, then `Session.Initialize` with a client's public key: the agreement, the two HKDF derivations and the two platform `AesGcm` instances. What admission added |

| Method | Mean | Error | StdDev | Allocated |
|---|---:|---:|---:|---:|
| `Construct` | 63.48 µs | 0.734 µs | 0.573 µs | 61.38 KB |
| `ConstructAndKey` | 202.46 µs | 2.922 µs | 2.440 µs | 238.49 KB |

So a plain world's connection no longer spends about 63 µs and 61 KB of allocation as it is created, nor about 139 µs
and 177 KB more at admission: about 0.2 ms and 238 KB allocated per connection in all.
The key pair is built in the connection's constructor, on the accept path. Until the #878 review the session was keyed
on the tick, in the admission's continuation, so on a world that seals the 139 µs came off a tick's budget once per
world entry, and reconnects after a restart that became ready in one tick added up (thirty, about 4 ms). It is now
keyed off the tick, on the thread the ticket's redemption finished on, before the result is handed to the tick; the
tick publishes the admission and encodes the reply, which carries the key the exchange already holds.

What the connection holds for its life, from a probe outside BenchmarkDotNet (2,000 exchanges held in an array,
`GC.GetTotalMemory(true)` before and after, three rounds, the same machine): **1,839 B** retained per connection once
built, and **1,006 B** more once keyed, **2,846 B** in all. That is the managed heap only: the two `AesGcm` instances
of a keyed session also hold native key handles, which the GC does not count. A plain world's connection keeps none
of it: the base `Connection` and the `WorldConnection` hold null references where the exchange was.

BenchmarkDotNet 0.15.8, DefaultJob, Windows 11, Intel Core i9-12900K, .NET SDK 10.0.401 / runtime 10.0.12, concurrent
workstation GC, 2026-10-14. The per-connection keygen was first measured at 0.23 ms ([session crypto](crypto-v1-derivation.md));
this run's 63 µs is the same operation, with the session object, in a warmed-up process.

---

### Packet encoder — `PacketEncoderBenchmarks.cs`

What a server packet costs on its way out since #875, per packet: the tick encodes it (`Create` fills the thread's
scratch message, `PacketEncoder.Scratch<T>()`, and encodes it into a pooled `PayloadSegment`), and the drain seals and
frames it (`PacketEnvelope.Append` into a burst buffer). The packet is a state update, the most frequent one a world
sends, of one entity and of thirty (`Entities`, a crowded town).

| Scenario | What it models |
|---|---|
| `Encode` | `SInstanceStateUpdatePacket.Create` into a segment, released after the call: the tick's share |
| `EncodeSealFrame` | The same, then sealed in place by a real P-256-keyed session and framed: tick and drain |
| `EncodeFramePlain` | The same, framed without sealing, as for a connection with no sealer (TLS only) |

**Status:** Baseline recorded 2026-10-10 (#875).

### State broadcast encoding — `StateBroadcastEncodingBenchmarks.cs` (#980)

One broadcast's state packets in an instance where every player sees every other player and the same fifteen
creatures (`Players` 1, 2 and 3: a solo player or a party in a forest; 10, 25 and 30: a town, which holds at most 30,
`MaxPlayers`), encoding only: the entities are already described. The updates are a broadcast with every player and
creature moving (a motion-only update has no entry for its own client); the adds are everyone listed everything in
full, each player its own character without its motion. One operation is one broadcast to every player.

| Scenario | What it models |
|---|---|
| `UpdatesBefore`, `AddsBefore` | Each recipient's list encoded whole (`SInstanceStateUpdatePacket.Create`, `SInstanceStateAddPacket.Create`): the encoding before #980 |
| `UpdatesShared`, `AddsShared` | Each recipient's packet put together from entries encoded once per broadcast (`StatePacketAssembler`), whatever the recipient count |
| `UpdatesAfter`, `AddsAfter` | What the instance does since #980: shared from `StatePacketAssembler.MinRecipientsToShare` (2) recipients, whole below |

Results in [shared state and aura encoding](#results--shared-state-and-aura-encoding-980-2026-10-14).

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*StateBroadcastEncodingBenchmarks*" --inProcess
```

---

## Tick Loop — Benchmark Results

### Problem (Before)

`WorldServer.ExecuteAsync` filled the remaining frame budget via a spin/yield loop:

```csharp
while (!stoppingToken.IsCancellationRequested)
{
    await Tick(); // calls Task.Yield() ~13 times per frame to fill the 16.67 ms budget
}
```

At 60 Hz with a ~3 ms tick, `Tick()` called `await Task.Yield()` roughly 13 times per frame.
Each call queues a ThreadPool continuation — the loop had no thread affinity and could resume on
a different CPU core after each yield. At target scale (250 instances × 60 TPS): ~780 ThreadPool
items/s for scheduling alone.

### Fix (After)

Replaced with a single `PeriodicTimer.WaitForNextTickAsync` per tick:

```csharp
using var timer = new PeriodicTimer(MinUpdateInterval);
while (await timer.WaitForNextTickAsync(stoppingToken))
{
    // one thread-pool hop per tick, then Update()
}
```

Scheduling cost dropped from ~13 thread-pool hops per tick to 1.

### Before — spin/yield throttle (2026-04-14)

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8039/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
```

| Method          | Mean          | Error       | StdDev      | Median        | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |--------------:|------------:|------------:|--------------:|-------:|--------:|-------:|----------:|------------:|
| Yield_1         |  1,113.188 ns |  22.2435 ns |  51.1081 ns |  1,094.431 ns |  1.002 |    0.06 | 0.0057 |      96 B |        1.00 |
| Yield_5         |  3,173.693 ns |  62.7428 ns | 142.8971 ns |  3,137.194 ns |  2.857 |    0.18 |      - |      96 B |        1.00 |
| Yield_13        |  8,068.501 ns | 131.7976 ns | 123.2835 ns |  8,105.878 ns |  7.262 |    0.33 |      - |      97 B |        1.01 |
| Yield_20        | 12,271.828 ns | 243.3880 ns | 518.6797 ns | 12,239.030 ns | 11.046 |    0.66 |      - |      97 B |        1.01 |
| SynchronousPath |      3.652 ns |   0.0884 ns |   0.2168 ns |      3.594 ns |  0.003 |    0.00 |      - |         - |        0.00 |

**Key observations:**

- `Yield_13` costs **7.26×** more than `Yield_1` (Ratio = 7.262 vs 1.002) — the production
  throttle burned ~7× the scheduling overhead of a single yield per tick.
- Allocations are essentially identical across all `Yield_*` variants (~96–97 B per tick),
  confirming overhead is pure CPU/scheduling cost, not GC pressure.
- `SynchronousPath` at 3.65 ns (Ratio = 0.003) reveals the async state-machine itself is nearly
  free; all meaningful cost comes from thread-pool hops.
- At 60 Hz, `Yield_13` adds ~484 µs/s of pure scheduling overhead vs ~67 µs/s for `Yield_1` —
  a saving of ~417 µs/s (6.26× reduction) after the refactor.

### After — PeriodicTimer refactor (2026-04-14)

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8039/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
```

| Method             | Mean              | Error           | StdDev          | Ratio      | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------------------:|----------------:|----------------:|-----------:|--------:|-------:|----------:|------------:|
| Yield_1            |        600.474 ns |       3.1183 ns |       2.7643 ns |      1.000 |    0.01 | 0.0057 |      96 B |        1.00 |
| Yield_5            |      1,529.956 ns |      13.1757 ns |      12.3245 ns |      2.548 |    0.02 | 0.0057 |      96 B |        1.00 |
| Yield_13           |      4,119.960 ns |      61.6122 ns |      57.6321 ns |      6.861 |    0.10 |      - |      96 B |        1.00 |
| Yield_20           |      5,943.735 ns |      69.6511 ns |      65.1517 ns |      9.899 |    0.11 |      - |      97 B |        1.01 |
| SynchronousPath    |          3.892 ns |       0.1050 ns |       0.1123 ns |      0.006 |    0.00 |      - |         - |        0.00 |
| PeriodicTimer_Tick | 15,767,664.375 ns | 213,085.0325 ns | 199,319.8717 ns | 26,259.203 |  342.05 |      - |     400 B |        4.17 |

**Key observations:**

- **`PeriodicTimer_Tick` Mean = ~15.8 ms** — dominated by the Windows timer resolution floor
  (~15.625 ms, the OS default granularity). The benchmark uses a 1 ms period but Windows rounds
  up to the next timer interrupt. This is the actual per-tick wall time at 60 Hz.
- **Scheduling overhead = `Yield_1` (~600 ns)** — subtracting the ~15.6 ms sleep from the
  15.8 ms total leaves ~200 µs of overhead; the remaining cost per tick is one thread-pool hop,
  which matches `Yield_1`. The `Ratio = 26,259` reflects the sleep, not scheduling cost.
- **Allocation: 400 B** — includes the `PeriodicTimer` object itself (allocated once per
  benchmark iteration). In production the timer is allocated once at startup and reused across
  all ticks; per-tick allocation is zero.
- **`Yield_13` Ratio stable at ~6.86×** across both runs (Before: 7.26×, After: 6.86×),
  confirming the scheduling overhead reduction is consistent regardless of absolute CPU speed.
- **Production scheduling overhead reduced from `Yield_13` → `Yield_1` per tick**: ~4,120 ns →
  ~600 ns (6.9× reduction). At 60 Hz: ~247 µs/s → ~36 µs/s of pure scheduling cost.
- **Thread affinity preserved** — `WaitForNextTickAsync` suspends once and resumes on the next
  available thread-pool thread with no intermediate hops between tick frames.

---

## Entity Tracking — Benchmark Results

### Problem (Before)

The entity tracking system performed a **full snapshot comparison every tick** for every entity
visible to every client. At the target scale of 250 concurrent map instances, each with up to 200
creatures and ~2 clients, this yielded approximately 60 million field comparisons per second — the
vast majority producing no change (idle entities between AI updates).

Secondary symptom: GC pressure from snapshot object allocations and `byte[] Fields` heap
allocations per changed entity per broadcast.

At 250 instances × 2 clients × 60 TPS: ~473 MB/s of Gen0 allocation — primary driver of tick
jitter.

### Fix (After)

Each mutable entity (`Creature`, `CharacterEntity`, `SpellScript`) accumulates changed fields in a
`_dirtyFields: GameEntityFields` bitmask via property setters. `MapInstance.Update()` snapshots
all dirty bits into a per-frame dictionary before broadcasting. `EntityTrackingSystem.Update()`
skips entities absent from the dirty map entirely, reducing idle-entity cost to a single `HashSet`
lookup.

### Before — full snapshot comparison (2026-04-14)

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8039/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
```

| Method                  | CreatureCount | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------------ |-------------- |----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **Update_AllIdle**          | **50**            |  **2.679 μs** | **0.0347 μs** | **0.0325 μs** |  **1.00** |    **0.02** | **0.2213** |      **-** |   **3.41 KB** |        **1.00** |
| Update_TenPercentActive | 50            |  2.761 μs | 0.0544 μs | 0.0727 μs |  1.03 |    0.03 | 0.2251 |      -  |    3.5 KB |        1.03 |
| Update_AllActive        | 50            |  2.833 μs | 0.0416 μs | 0.0389 μs |  1.06 |    0.02 | 0.2251 |      -  |    3.5 KB |        1.03 |
|                         |               |           |           |           |       |         |        |        |           |             |
| **Update_AllIdle**          | **100**           |  **5.185 μs** | **0.0989 μs** | **0.0925 μs** |  **1.00** |    **0.02** | **0.4730** |      **-** |   **7.31 KB** |        **1.00** |
| Update_TenPercentActive | 100           |  5.323 μs | 0.1049 μs | 0.1364 μs |  1.03 |    0.03 | 0.4807 |      -  |    7.4 KB |        1.01 |
| Update_AllActive        | 100           |  5.661 μs | 0.1100 μs | 0.1267 μs |  1.09 |    0.03 | 0.4807 |      -  |    7.4 KB |        1.01 |
|                         |               |           |           |           |       |         |        |        |           |             |
| **Update_AllIdle**          | **200**           | **10.348 μs** | **0.2031 μs** | **0.3039 μs** |  **1.00** |    **0.04** | **1.0223** |      **-** |  **15.78 KB** |        **1.00** |
| Update_TenPercentActive | 200           | 10.562 μs | 0.2098 μs | 0.3266 μs |  1.02 |    0.04 | 1.0223 | 0.0153 |  15.87 KB |        1.01 |
| Update_AllActive        | 200           | 10.965 μs | 0.2180 μs | 0.2677 μs |  1.06 |    0.04 | 1.0223 | 0.0153 |  15.87 KB |        1.01 |

**Key observations:**

- Scaling is perfectly linear (O(n) per client per tick).
- `AllIdle` and `AllActive` costs are nearly identical (~6% apart at 200 creatures), confirming
  the bottleneck is per-call allocations rather than field comparison logic.
- Every `CharacterCharacterGameState.Update()` call allocates ~15.78 KB at 200 creatures
  (3× `HashSet<ObjectGuid>` + 3× `List<ObjectGuid>` inside `EntityTrackingSystem.Update`).
- At 250 instances × 2 clients × 60 TPS: ~473 MB/s of Gen0 allocation — primary driver of tick
  jitter.

### After — dirty-flag redesign (2026-04-14)

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8039/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.201
  [Host]     : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v3
```

| Method                  | CreatureCount | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------ |-------------- |-----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **Update_AllIdle**          | **50**            |   **877.1 ns** | **16.70 ns** | **15.62 ns** |  **1.00** |    **0.02** | **0.0076** |     **120 B** |        **1.00** |
| Update_TenPercentActive | 50            |   913.7 ns | 17.86 ns | 19.86 ns |  1.04 |    0.03 | 0.0124 |     208 B |        1.73 |
| Update_AllActive        | 50            | 1,069.7 ns | 19.34 ns | 18.09 ns |  1.22 |    0.03 | 0.0114 |     208 B |        1.73 |
|                         |               |            |          |          |       |         |        |           |             |
| **Update_AllIdle**          | **100**           | **1,668.5 ns** | **20.08 ns** | **18.79 ns** |  **1.00** |    **0.02** | **0.0076** |     **120 B** |        **1.00** |
| Update_TenPercentActive | 100           | 1,754.8 ns | 25.92 ns | 21.64 ns |  1.05 |    0.02 | 0.0114 |     208 B |        1.73 |
| Update_AllActive        | 100           | 1,928.0 ns | 26.82 ns | 25.09 ns |  1.16 |    0.02 | 0.0114 |     208 B |        1.73 |
|                         |               |            |          |          |       |         |        |           |             |
| **Update_AllIdle**          | **200**           | **3,322.5 ns** | **64.76 ns** | **74.58 ns** |  **1.00** |    **0.03** | **0.0076** |     **120 B** |        **1.00** |
| Update_TenPercentActive | 200           | 3,293.7 ns | 45.18 ns | 42.26 ns |  0.99 |    0.02 | 0.0114 |     208 B |        1.73 |
| Update_AllActive        | 200           | 3,862.2 ns | 75.20 ns | 70.34 ns |  1.16 |    0.03 | 0.0076 |     208 B |        1.73 |

**Key observations:**

- **3.1× faster across the board** — `AllIdle` at 200 creatures dropped from 10,348 ns to
  3,323 ns.
- **134× less allocation (idle case)** — `AllIdle` at 200 creatures went from 15.78 KB to 120 B.
  The 120 B floor is benchmark harness overhead; the entity tracking path itself allocates nothing
  when no entities are dirty.
- **Idle ≈ Active cost eliminated** — Before, `AllIdle` and `AllActive` were within ~6% because
  both hit the same per-call `HashSet`/`List` allocation cost. Now `AllIdle` is the cheapest
  possible path: a `HashSet` lookup that returns false, nothing else.
- **Active case also improved** — `AllActive` at 200 creatures: 10,965 ns → 3,862 ns (2.8×).
  Even the worst-case (every entity dirty every tick) benefits from removing snapshot comparison
  overhead.
- **At target scale** — 250 instances × 2 clients × 60 TPS: Gen0 allocation drops from
  ~473 MB/s to ~3.6 MB/s (131× reduction), eliminating the primary driver of tick jitter.

---

## Packet Serialization GC-001 — Benchmark Results

### Problem (Before)

Every outbound S-packet `Create()` followed this pattern:

```csharp
using var memoryStream = new MemoryStream();       // alloc 1: MemoryStream + internal byte[] buffer
Serializer.Serialize(memoryStream, p);
var buffer = encryptFunc(memoryStream.ToArray());  // alloc 2: ToArray copy; alloc 3: encrypt result
```

Three heap allocations minimum per outbound packet. The state broadcast sends Add + Update + Remove
packets to every player at ~10 Hz, producing hundreds of short-lived allocations per second under
any real load.

### Fix (After)

Replaced with a `[ThreadStatic]` pooled `IBufferWriter<byte>` (`PooledArrayBufferWriter`) backed by
`ArrayPool<byte>.Shared`. A central `PacketSerializationHelper.Serialize()` helper owns the
thread-local writer and serializes directly into it; the span is passed to `EncryptFunc` with no
intermediate copy.

```csharp
// One call, one allocation (the encrypted payload byte[])
=> PacketSerializationHelper.Serialize(new SXxxPacket { ... }, PacketType, Flags, Protocol, encrypt);
```

### Results — GC-001 fix (2026-04-16)

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8039/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

| Method                    | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **Legacy_SmallPacket**    |  89.06 ns | 2.643 ns | 7.669 ns |  1.01 |    0.15 | 0.0117 |     184 B |        1.00 |
| Pooled_SmallPacket        |  75.00 ns | 1.551 ns | 3.098 ns |  0.85 |    0.11 | 0.0076 |     120 B |        0.65 |
| Legacy_MediumPacket       | 164.23 ns | 2.508 ns | 2.684 ns |  1.86 |    0.23 | 0.0381 |     600 B |        3.26 |
| Pooled_MediumPacket       | 146.02 ns | 2.819 ns | 3.355 ns |  1.66 |    0.20 | 0.0162 |     256 B |        1.39 |

Encrypt delegates are identity copies (`span => span.ToArray()`) in both paths to isolate
serialization cost. The `[ThreadStatic]` writer is pre-warmed in `[GlobalSetup]` so its one-time
allocation does not appear in steady-state measurements.

**Key observations:**

- **Small packet: 184 B → 120 B (35% less allocation, 16% faster)** — `SCharacterCreatedPacket`
  (one enum field). The 64 B saving is the `MemoryStream` object and its internal byte[] buffer,
  which are no longer allocated. The payload byte[] itself is the same cost in both paths.
- **Medium packet: 600 B → 256 B (57% less allocation, 11% faster)** — `SChatMessagePacket`
  (two `ulong`s, two `string`s, `DateTime`, ~70 bytes serialized). The MemoryStream buffer grows
  to hold the larger payload, so the absolute saving scales with packet size.
- **Gen0 rate halved** — Gen0 drops from 0.0117 to 0.0076 (small) and 0.0381 to 0.0162
  (medium). Fewer Gen0 collections means less STW pause time during state broadcasts.
- **Speed improvement is a side effect, not the goal** — the primary win is GC pressure.
  The pooled path is also faster because `ArrayPool` and span operations have better cache
  locality than `MemoryStream`'s internal resize path, but the latency reduction is secondary.
- **At state-broadcast scale** — the hot path sends three packet types (Add, Update, Remove) to
  every player at ~10 Hz. With 100 players each seeing ~20 entities: ~60,000 packets/second.
  Saving ~64–344 B per packet eliminates **3.8–20 MB/s** of Gen0 allocation that was previously
  driving collection pauses on the world server tick loop.

---

## Broadcast State GC-002 — Benchmark Results

**Date:** 2026-04-16
**Runtime:** .NET 10

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8039/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

> These numbers were measured against an entity-replication path that no longer exists. Both
> the benchmark and the buffer plumbing it modelled were removed when entity state became a
> message instead of a hand-placed byte payload.

### Problem (before GC-002)

`BroadcastStateTo` allocated `new byte[bytesWritten]` per visible entity and
`new List<ObjectAdd>()` per player per call. With 20 entities × 60 players at
10 Hz that generated 1,200+ short-lived `byte[]` per broadcast tick.

### Baseline (before fix)

| Method | EntityCount | Mean | Error | StdDev | Ratio | RatioSD | Gen0 | Gen1 | Allocated | Alloc Ratio |
|--------|-------------|------|-------|--------|-------|---------|------|------|-----------|-------------|
| Legacy_BroadcastState | 5 | 501.0 ns | 9.72 ns | 9.09 ns | 1.00 | 0.02 | 0.0811 | - | 1.24 KB | 1.00 |
| | | | | | | | | | | |
| Legacy_BroadcastState | 20 | 1,619.0 ns | 31.01 ns | 41.40 ns | 1.00 | 0.03 | 0.2899 | 0.0019 | 4.45 KB | 1.00 |

**Key observations:**

- **Allocation scales linearly with entity count** — 5 entities: 1.24 KB; 20 entities: 4.45 KB
  (3.59× more entities → 3.59× more allocation). Confirms the dominant cost is `new byte[]`
  per entity, not per-call overhead.
- **Gen1 promotion appears at 20 entities** — `Gen1 = 0.0019` at 20 entities but absent at 5.
  Some allocations survive long enough to be promoted, adding Gen1 collection cost.
- **At target scale** — 20 entities × 60 players × 10 Hz = 12,000 `Legacy_BroadcastState`
  calls/second → **53+ MB/s** of Gen0/Gen1 allocation from this path alone.

### Post-fix results (after GC-002)

| Method | EntityCount | Mean | Error | StdDev | Ratio | RatioSD | Gen0 | Gen1 | Allocated | Alloc Ratio |
|--------|-------------|------|-------|--------|-------|---------|------|------|-----------|-------------|
| Legacy_BroadcastState | 5 | 517.6 ns | 9.76 ns | 8.65 ns | 1.00 | 0.02 | 0.0830 | - | 1,312 B | 1.00 |
| Pooled_BroadcastState | 5 | 459.7 ns | 4.14 ns | 3.45 ns | 0.89 | 0.02 | 0.0439 | - | 696 B | 0.53 |
| | | | | | | | | | | |
| Legacy_BroadcastState | 20 | 1,654.6 ns | 28.60 ns | 29.38 ns | 1.00 | 0.02 | 0.2995 | 0.0019 | 4,712 B | 1.00 |
| Pooled_BroadcastState | 20 | 1,489.1 ns | 28.39 ns | 34.87 ns | 0.90 | 0.03 | 0.1488 | - | 2,344 B | 0.50 |

### Key observations
- `Pooled_BroadcastState` eliminates all per-entity `byte[]` allocations (zero heap alloc per entity).
- `Legacy_BroadcastState` allocates N×(64 B entity array + ObjectAdd object + List growth) per call.
- Allocation reduction scales linearly with entity count — the Pooled path at 5 entities cuts allocation by 47% (1,312 B → 696 B); at 20 entities by 50% (4,712 B → 2,344 B). The residual 696 B / 2,344 B is the single encrypted `NetworkPacket` payload byte[] produced by `s_encrypt` (unavoidable) plus N `ObjectAdd` class instances (one per entity — `ObjectAdd` is a reference type).
- Gen1 promotion eliminated — `Legacy_BroadcastState` at 20 entities shows `Gen1 = 0.0019`; `Pooled_BroadcastState` shows none. The rented buffer and pre-allocated list never escape to Gen1.
- Note: The benchmark only exercises the NewObjects (add) path. The UpdatedObjects path has an identical allocation pattern; at 20 entities across both loops the total Legacy allocation in production is approximately double the measured figure.

---

## Packet Reader GC-007 — Benchmark Results

**Date:** 2026-04-16

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900K 3.20GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

### Problem (before GC-007)

`PacketReader.Read()` deserialised every inbound packet via `MethodInfo.Invoke`:

```csharp
object? payload = p.deserialize.Invoke(null, new object?[] { payloadMemory, null, null });
```

Two heap allocations per call:
1. `new object?[3]` — the reflection invoke args array
2. Boxing of `payloadMemory` (`ReadOnlyMemory<byte>` is a struct → `object?`)

### Fix (after GC-007)

A private static `BuildDeserializer<T>()` helper builds a typed `Func<ReadOnlyMemory<byte>, Packet?>` once per packet type at startup via `MakeGenericMethod`. `Read()` calls the cached delegate directly:

```csharp
return deserializer(new ReadOnlyMemory<byte>(packet.Payload));
```

`new ReadOnlyMemory<byte>(...)` is a stack-allocated struct — no heap allocation. The delegate is shared — no closure per call.

### Results

| Method | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|---|---|---|---|---|---|---|---|
| `Legacy_ReflectionInvoke` | 68.75 ns | 0.796 ns | 0.665 ns | 1.00 | 0.0066 | 104 B | 1.00 |
| `Delegate_Cached` | 50.01 ns | 0.515 ns | 0.456 ns | 0.73 | 0.0015 | 24 B | 0.23 |

### Key observations

- **27% faster** — 68.75 ns → 50.01 ns. Reflection dispatch has measurable overhead even with a pre-closed `MethodInfo`; a direct delegate call is cheaper for the JIT to inline and schedule.
- **77% less allocation** — 104 B → 24 B per `Read()` call. The 80 B eliminated is the `object?[3]` array (~40 B) and the boxed `ReadOnlyMemory<byte>` struct (~40 B). The 24 B residual is the deserialized `CCharacterListPacket` object itself — unavoidable.
- **Gen0 rate reduced 4.4×** — Gen0 drops from 0.0066 to 0.0015 per 1 000 operations. Fewer Gen0 collections means less STW pause time during packet processing.
- **At inbound packet scale** — at 50 players × 10 non-trivial packets/s = 500 `Read()` calls/s, the legacy path allocates ~52 KB/s of short-lived Gen0 objects from this site alone. The delegate path reduces that to ~12 KB/s, a saving of ~40 KB/s.

---

## World Packet Queue GC-009 — Benchmark Results

**Date:** 2026-04-16

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

### Problem (Before)

`WorldConnection.OnReceive` enqueued packets into a `LockedQueue<WorldPacket>` where `WorldPacket` was
a private inner class and `LockedQueue<T>` used a `LinkedList<T>`-backed `Deque<T>`. Two heap
allocations per received inbound packet:

1. `new WorldPacket { ... }` — class instance (~40 B)
2. `LinkedListNode<WorldPacket>` inside `Deque<T>` — one node per enqueued item (~40 B)

Additionally, `ProcessQueue(PacketFilter filter)` created a closure `worldPacket => filter.CanProcess(worldPacket.Type)`
on every call (~6 000/s at 50 players × 60 Hz × 2 passes).

### Fix (After)

- `WorldPacket` inner class → `private readonly record struct WorldPacket(NetworkPacketType Type, Packet? Payload)` — stored inline in the queue array
- `LockedQueue<T>`: `where T : class` removed; `Deque<T>`/`LinkedList<T>` replaced with `Queue<T>` ring buffer (zero allocation at steady state)
- Filter predicates cached as `Func<WorldPacket, bool>` fields on `WorldConnection`, initialized once in the constructor — no per-call closure

### Results — GC-009 fix (2026-04-16)

| Method | Mean | Error | StdDev | Ratio | RatioSD | Gen0 | Allocated | Alloc Ratio |
|--- |---:|---:|---:|---:|---:|---:|---:|---:|
| `Legacy_ClassQueue` | 625.5 ns | 11.71 ns | 10.38 ns | 1.00 | 0.02 | 0.1011 | 1600 B | 1.00 |
| `Struct_RingBuffer` | 547.4 ns | 0.63 ns | 0.56 ns | 0.88 | 0.01 | - | - | 0.00 |

### Key observations

- **100% allocation eliminated** — `Struct_RingBuffer` allocates 0 B per iteration at steady state. The `Queue<T>` ring buffer reaches capacity during BenchmarkDotNet's warm-up phase; subsequent measurement iterations produce zero GC pressure.
- **1600 B → 0 B** — The 1600 B baseline is 20 packets × ~80 B (one `LegacyWorldPacket` class object + one `LinkedListNode<LegacyWorldPacket>` per item on .NET 10 x64).
- **Gen0 eliminated** — `Legacy_ClassQueue` Gen0 = 0.1011 per 1 000 operations; `Struct_RingBuffer` Gen0 = 0. No Gen0 collections from this path.
- **12% faster** — 625.5 ns → 547.4 ns. Better cache locality from the contiguous ring buffer array vs. scattered `LinkedListNode` objects on the heap.
- **At inbound packet scale** — 50 players × 10 packets/s = 500 packets/s queued. Legacy path: ~39 KB/s of Gen0 allocation from this site. Fixed path: 0 KB/s. The closure elimination adds a further ~6 000 delegate objects/s saved (not measured in this benchmark; covered by the filter predicate caching in Task 3).

---

## Packet Reader Decrypt GC-008 — Benchmark Results

**Date:** 2026-04-17

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

### Problem (Before)

`Connection.ExecuteAsync` called `_packetReader.Decrypt(packet, CryptoSession.Decrypt)` followed
by `_packetReader.Read(packet)`. `PacketReader.Decrypt` assigned `packet.Payload = decryptFunc(packet.Payload)`.
Inside `AvalonCryptoSession.Decrypt`, three `byte[]` objects were allocated per call:

1. `data.Take(12).ToArray()` — 12-byte nonce copy (LINQ)
2. `data.Skip(12).ToArray()` — full ciphertext copy (LINQ)
3. `_encryptCipher.DoFinal(ciphertext)` — decrypted output buffer

The Protobuf-allocated `packet.Payload` was then replaced by the DoFinal output.

### Fix (After)

- `IAvalonCryptoSession.Decrypt` → `int Decrypt(ReadOnlySpan<byte>, byte[])`: nonce and ciphertext extracted via span slices (no LINQ); decrypted output written directly into the caller-supplied `byte[]`
- `PacketReader.Decrypt` removed; `Read(packet, DecryptFunc?)` rents one `ArrayPool<byte>` buffer, decrypts into it, deserializes from it, returns the buffer — `packet.Payload` never swapped
- One 12-byte nonce `byte[]` per call remains (unavoidable — `ParametersWithIV` requires `byte[]`)
- One ciphertext `byte[]` per call remains (BouncyCastle 2.6.2 `IBufferedCipher` only provides `byte[]` overloads — no span overloads on `netstandard2.0` or `net6.0`)

### Results — GC-008 fix (2026-04-17)

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
13th Gen Intel Core i7-13850HX 2.10GHz, 1 CPU, 28 logical and 20 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3

| Method                | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| Legacy_DecryptAndRead | 124.5 ns | 2.12 ns | 1.98 ns |  1.00 |    0.02 | 0.0086 |     136 B |        1.00 |
| Fixed_DecryptAndRead  | 132.3 ns | 2.66 ns | 3.07 ns |  1.06 |    0.03 | 0.0050 |      80 B |        0.59 |
```

### Key observations

- **41% allocation reduction**: 136 B → 80 B per call. The eliminated allocation is the `byte[]` that used to be assigned to `packet.Payload` after decryption — it now stays in an `ArrayPool` rented buffer for the duration of deserialization, then is returned to the pool.
- **42% Gen0 reduction**: 0.0086 → 0.0050 GC pressure per call. At 500 encrypted packets/s (50 players × 10 per second) this eliminates ~150 000 Gen0 bytes/s of GC pressure from the decrypted payload swap alone.
- **Residual 80 B**: The deserialized `Packet` object — unavoidable until `NetworkPacket.Payload` is changed to `Memory<byte>` (Approach C, tracked separately).
- **Throughput unchanged**: Mean is 124.5 ns vs 132.3 ns — the small overhead (~6%) is the `ArrayPool.Rent/Return` cost plus `input.CopyTo(output)` in the passthrough; real crypto will dominate this.

### Results — the read loop's decrypt delegate (#854, 2026-10-09)

i9-12900K, Windows 11, .NET 10.0.12, DefaultJob, Release. The two `Session_*` arms are new with #854 and call `Read`
through `IPacketReader`, as `Connection` does; the first two arms, run on `main` (`a892abe7`) and with #854, are
unchanged and are the control.

| Method | Mean | Error | StdDev | Gen0 | Allocated |
|---|---:|---:|---:|---:|---:|
| `Legacy_DecryptAndRead` (main) | 157.8 ns | 5.09 ns | 15.00 ns | 0.0086 | 136 B |
| `Fixed_DecryptAndRead` (main) | 162.1 ns | 5.17 ns | 15.23 ns | 0.0050 | 80 B |
| `Legacy_DecryptAndRead` (#854) | 154.1 ns | 2.99 ns | 7.55 ns | 0.0086 | 136 B |
| `Fixed_DecryptAndRead` (#854) | 153.2 ns | 3.09 ns | 7.39 ns | 0.0050 | 80 B |
| `Session_MethodGroupPerPacket` | 412.8 ns | 8.08 ns | 12.58 ns | 0.0091 | 144 B |
| `Session_CachedDelegate` | 411.7 ns | 8.19 ns | 21.15 ns | 0.0048 | 80 B |

- **The method group cost 64 B per packet, in fully optimised code.** Passed through the interface, the delegate
  escapes and is on the heap: 144 B against 80 B, the 80 B being the deserialized `Packet` and nothing else.
- **No time difference**: creating a small delegate is a few nanoseconds against ~410 ns of open and deserialize.
- **Through the concrete `PacketReader` the JIT hides it.** A first run of the `Session_*` arms called `Read` on the
  concrete type, which .NET 10 inlines; the delegate stayed on the stack and both arms allocated 80 B. That is the
  dependency #854 removes: whether the per-packet delegate cost anything depended on the JIT, not on the code.

### Results — the read path's refusals (#897, 2026-10-13)

What #897 changed in `PacketReader.Read`: an opcode with no packet type returns null without a log call (the read loop
logs it, once per connection), and the open and the decode each sit in a `try` whose catches wrap what the peer got
wrong in a `PacketReadException`. A packet that is in order runs the same code inside two more protected regions.
Outside `Read`, the reader now decodes once with every packet type as it is built, and sets protobuf-net's depth limit
to 32.

i9-12900K, Windows 11, .NET 10.0.12, Release. **Other builds and test runs were loading the machine for every figure
here**, so each is a range, and the comparison to read is the interleaved one: the old body and the new one in one
process, taking turns, under the same load.

**The suite, `--job short`, three runs with the change to `Read` and three without, taking turns.** The ranges
overlap in both directions and say only that nothing large moved; the allocations are exact.

| Method | Before | After | Allocated, before and after |
|---|---:|---:|---:|
| `Legacy_DecryptAndRead` | 133 to 233 ns | 151 to 351 ns | 136 B |
| `Fixed_DecryptAndRead` | 148 to 258 ns | 146 to 318 ns | 80 B |
| `Session_MethodGroupPerPacket` | 422 to 636 ns | 427 to 654 ns | 144 B |
| `Session_CachedDelegate` | 451 to 595 ns | 565 to 623 ns | 80 B |

A last run on the reader as it ends up (every decoder built as it is constructed, the depth limit set): 264.0, 271.8,
574.1 and 530.9 ns in the table's order, inside the ranges above, with the same allocations.

**Interleaved, the old body of `Read` and the new one, copied into one harness** (41 rounds of 200,000 calls an arm,
each round running old then new; two runs; the same `CChatMessagePacket` as the suite, plain and really sealed):

| Packet | Body | Minimum | Median | Allocated |
|---|---|---:|---:|---:|
| plain | old | 120.4 to 126.8 ns | 145.8 to 157.8 ns | 80 B |
| plain | new | 124.0 to 129.4 ns | 147.8 to 153.5 ns | 80 B |
| sealed | old | 371.0 to 379.2 ns | 418.4 to 476.3 ns | 80 B |
| sealed | new | 372.1 to 375.4 ns | 423.8 to 462.4 ns | 80 B |

**A review of the change measured it with a harness of its own**: the old body, the new body and the real
`PacketReader` in one process.

| Packet | | Minimum | 10th percentile | Allocated |
|---|---|---:|---:|---:|
| plain | old body | 120.1 ns | 122.3 ns | 80 B |
| plain | the real reader | 122.9 ns | 125.6 ns | 80 B |
| sealed | old body | 365.1 ns | 367.0 ns | 80 B |
| sealed | the real reader | 361.9 ns | 363.5 ns | 80 B |
| unknown opcode | old body | 28 to 31 ns | | 56 B |
| unknown opcode | the real reader | 4.4 to 5.5 ns | | 0 B |

- **A plain packet: about 3 ns more**, 2 to 3% of a decode, in both harnesses. The decode now runs inside a
  protected region.
- **A sealed packet: no difference** either harness can see, against some 250 ns of opening it.
- **Nothing more is allocated**: 80 B, the decoded packet, before and after.
- **An opcode the server has no packet type for: from about 30 ns and 56 B to about 5 ns and nothing.** The reader no
  longer formats a Warning for each; the read loop writes one per connection and Trace after that.
- **A refused frame costs two exceptions where it cost one** (the decoder's or the session's, and the wrapper), once
  per connection, since the connection closes. It no longer costs the Error line that carried the exception: about
  59,000 characters of it for a payload nested past the decoder's depth.

**The depth limit.** protobuf-net alone, refusing the payload `ConnectionReadLoopShould` uses (600 groups opened
inside one another, in a field the packet does not have), per refused decode, at the model's default depth and at
smaller ones. The review measured the default at 0.46 ms and 132 KB on a quieter machine; the ratio is what holds.

| `TypeModel.MaxDepth` | Time | Allocated | The exception's text |
|---:|---:|---:|---:|
| 512 (protobuf-net's default) | 881 µs | 135.6 KB | 59,096 characters |
| 64 | 76 µs | 17.9 KB | 8,471 |
| 32 (`PacketReader.MaxDecodeDepth`) | 44 µs | 9.4 KB | 4,855 |
| 16 | 28 µs | 5.2 KB | 3,047 |

The deepest packet contract nests 5 and the deepest a server reads nests 2. 32 takes a twentieth of the time and a
fourteenth of the memory of the default; 16 would take a third less again and leave three times the deepest contract
where 32 leaves six.

**Building the decoders at startup.** The reader decodes an empty payload with each packet type as it is built, which
is when protobuf-net builds the type's decoder; before, that was the first packet of the type, on the read loop of the
connection that sent it. In a new process, three runs:

| Server | Packet types read | All of them | The first one |
|---|---:|---:|---:|
| world | 35 | 127 to 166 ms | 72 to 82 ms |
| auth | 10 | 76 to 82 ms | 60 to 66 ms |

The first decode in a process carries protobuf-net's own start (60 to 80 ms); each type after it is a millisecond or
two. That first decode used to be the first packet any client sent: a server's first connection waited 60 to 80 ms on
it. It is startup time now, once, off the tick and before the port opens. What a server sends is built at startup as
well since #883 ([the network path](#results--the-network-path-883-2026-10-13)).

### Results — the opened payload is cleared (#883, 2026-10-13)

`PacketReader.Read` opens a sealed payload into an array rented from the shared pool, and now clears what the open
wrote before the array goes back: on the auth server that is a login or a registration, and the pool hands the array
to whoever rents next. Nothing that rents reads before it writes, so it is a second line. Only the opened length is
cleared (the frame's length when the open failed), at most `Hosting:MaxInboundFrameSize` and tens of bytes for most
packets. `--job short`, four runs before and four after, taking turns, each run's mean, lowest first:

| Method | Before | After | Allocated, both |
|---|---:|---:|---:|
| `Fixed_DecryptAndRead` | 139.8 / 142.3 / 145.7 / 158.2 ns | 136.7 / 137.1 / 146.9 / 156.8 ns | 80 B |
| `Session_MethodGroupPerPacket` | 373.3 / 377.9 / 419.3 / 439.8 ns | 376.4 / 378.9 / 381.0 / 441.8 ns | 144 B |
| `Session_CachedDelegate` | 359.6 / 361.9 / 368.0 / 552.9 ns | 363.8 / 375.1 / 400.7 / 411.7 ns | 80 B |

The high figure or two in each cell is a run another build disturbed (its standard deviation 18 to 45 ns, against 1
to 4 ns in the others). Between the fastest runs the sealed rows differ by 3 and 4 ns, about 1%, which is inside the
spread of the undisturbed runs; the clear of a chat message's 30-odd bytes is too small to tell from it. Nothing more
is allocated. `PacketReaderShould` holds the clear, for a payload that decodes and one that does not.

---

## Context Factory Delegate GC-010 — Benchmark Results

**Date:** 2026-04-17

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900K 3.20GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

### Problem (Before)

`WorldServer.GetContextPacket` (and the identical `AuthServer` path) ran `Activator.CreateInstance` + `PropertyInfo.SetValue × 2` on **every packet dispatch**, even with a warm `(PropertyInfo, PropertyInfo)` cache:

```csharp
object context = Activator.CreateInstance(contextType)!;
packetProp.SetValue(context, packet);
connectionProp.SetValue(context, connection);
return context;
```

The property cache avoided re-calling `GetProperty`, but `Activator.CreateInstance` and two reflection `SetValue` calls still ran unconditionally per dispatch.

### Fix (After)

Replaced the `ConcurrentDictionary<Type, (PropertyInfo, PropertyInfo)>` cache with a `ConcurrentDictionary<Type, Func<IConnection, Packet?, object>>` cache. The delegate is built once per packet type via `BuildContextFactory<TPacket>()` + `MakeGenericMethod`. `GetContextPacket` becomes one dictionary lookup + one delegate call:

```csharp
var factory = _contextFactoryCache.GetOrAdd(packetType, static t =>
    (Func<IConnection, Packet?, object>)s_buildContextMethod.MakeGenericMethod(t).Invoke(null, null)!);
return factory(connection, packet as Packet);
```

The `static` lambda captures no variables — zero closure allocation per call.

### Results

| Method | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|---|---|---|---|---|---|---|---|
| `Legacy_ActivatorAndSetValue` | 19.896 ns | 0.2428 ns | 0.2027 ns | 1.00 | 0.0020 | 32 B | 1.00 |
| `Delegate_Cached` | 5.706 ns | 0.0939 ns | 0.0784 ns | 0.29 | 0.0020 | 32 B | 1.00 |

### Key observations

- **3.49× faster** — 19.896 ns → 5.706 ns (Ratio = 0.29). `Activator.CreateInstance` + `SetValue × 2` have non-trivial reflection overhead even on a warm cache; a direct delegate invocation eliminates all of it.
- **Equal allocations** — both paths allocate exactly 32 B. `WorldPacketContext<T>` is a struct; boxing it to `object` is unavoidable in both paths. This is a **CPU benchmark**, not an allocation benchmark — the spec predicted this outcome and `[MemoryDiagnoser]` confirms it.
- **Gen0 rate unchanged** — `Gen0 = 0.0020` on both paths. The GC pressure is the single boxed struct, identical in both cases.
- **At dispatch scale** — at 50 players × 10 packets/s = 500 `GetContextPacket` calls/s, the legacy path burns ~9.9 µs/s in pure reflection overhead. The delegate path reduces that to ~2.9 µs/s. The absolute saving is modest per-call, but the fix eliminates a reflection barrier that blocked JIT inlining of the construction path entirely.
- **Applied to both servers** — `WorldServer` and `AuthServer` share the identical pattern. Both are fixed; the benchmark covers the `WorldPacketContext<T>` path as representative.

---

## CallListener DIM Dispatch GC-011 — Benchmark Results

**Date:** 2026-04-17

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900K 3.20GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

### Problem (Before)

`ServerBase.CallListener` dispatched every inbound packet via `MethodInfo.Invoke`:

```csharp
await ((Task)handlerCache.ExecuteMethod.Invoke(
    packetHandler,
    new[] { context, _stoppingToken.Token }  // new object[2] per call + CancellationToken boxed
)!).ConfigureAwait(false);
```

Two heap allocations per dispatch:
1. `new object[2]` — the reflection invoke args array
2. Boxing of `_stoppingToken.Token` (`CancellationToken` is a struct → `object`)

### Fix (After)

Added `Task ExecuteAsync(object context, CancellationToken token)` to `IPacketHandlerNew`. Both `IAuthPacketHandler<T>` and `IWorldPacketHandler<T>` provide a DIM that unboxes the context and delegates to the strongly-typed overload. `CallListener` becomes one cast + one virtual dispatch:

```csharp
await ((IPacketHandlerNew)packetHandler).ExecuteAsync(context, _stoppingToken.Token).ConfigureAwait(false);
```

`ExecuteMethod: MethodInfo` removed from `PacketHandlerCache`; `using System.Reflection` removed from `ServerBase.cs`.

### Results

| Method | Mean | Error | StdDev | Ratio | RatioSD | Gen0 | Allocated | Alloc Ratio |
|---|---|---|---|---|---|---|---|---|
| `Legacy_ReflectionInvoke` | 39.90 ns | 0.583 ns | 0.572 ns | 1.00 | 0.02 | 0.0081 | 128 B | 1.00 |
| `Interface_Dispatch` | 24.50 ns | 0.385 ns | 0.360 ns | 0.61 | 0.01 | 0.0041 | 64 B | 0.50 |

### Key observations

- **39% faster** — 39.90 ns → 24.50 ns (Ratio = 0.61). `MethodInfo.Invoke` carries measurable overhead even on a pre-reflected, warm cache; a single virtual dispatch via an interface is significantly cheaper for the JIT to schedule.
- **50% less allocation** — 128 B → 64 B per dispatch. The 64 B eliminated is the `new object[2]` args array and the boxed `CancellationToken` that `MethodInfo.Invoke` required on every call.
- **Gen0 rate halved** — Gen0 drops from 0.0081 to 0.0041 per 1 000 operations. Fewer Gen0 collections means less STW pause time during packet processing.
- **At dispatch scale** — at 50 players × 10 packets/s = 500 `CallListener` dispatches/s, the legacy path allocates ~64 KB/s of short-lived Gen0 objects from this site alone. The DIM path reduces that to ~32 KB/s, a saving of ~32 KB/s.
- **Dead code removed** — `IPacketHandler`/`IPacketRegistry`/`PacketRegistry`/`AvalonTcpClient` (all unreferenced in production) deleted alongside the fix, reducing build surface and eliminating dead maintenance burden.

---

## Serialization — Benchmark Results

**Date:** 2026-09-10

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900K 3.20GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
```

### Results — first baseline (2026-09-10)

| Method | Mean | Error | StdDev | Gen0 | Allocated |
|---|---|---|---|---|---|
| `Serialize_NoEncryption` | 205.5 ns | 1.28 ns | 1.07 ns | 0.0556 | 872 B |
| `Serialize_Encrypted` | 951.8 ns | 17.08 ns | 15.98 ns | 0.1364 | 2,144 B |
| `Deserialize_Encrypted` | 527.0 ns | 10.06 ns | 12.36 ns | 0.1106 | 1,744 B |
| `Deserialize_NoEncryption` | 182.0 ns | 3.03 ns | 2.83 ns | 0.0131 | 208 B |

### Key observations

- **Encryption costs 4.6× on the send path** — 205.5 ns → 951.8 ns, and 872 B → 2,144 B. These
  are end-to-end packet figures, not cipher figures: `CCharacterListPacket.Create` serializes the
  inner packet to a `MemoryStream`, encrypts that, then the outer `NetworkPacket` is serialized
  again. The cipher's own share is isolated in the Session Cipher suite below.
- **Encrypt costs more than decrypt** — 951.8 ns versus 527.0 ns, even though the decrypt scenario
  does strictly more work (outer deserialize, decrypt, inner deserialize). The asymmetry is
  allocation: `AvalonCryptoSession.Decrypt` writes into a caller-supplied buffer, while `Encrypt`
  still copies the input span, takes a fresh `DoFinal` output buffer, and then joins nonce and
  ciphertext through `Concat().ToArray()`. The send path never received the treatment the receive
  path got.
- **The unencrypted rows are the protobuf floor** — 205.5 ns / 872 B to serialize and 182.0 ns /
  208 B to deserialize a small packet. Anything above that on the encrypted rows is the session
  layer, not Protobuf-net.

### Results — #854, before and after (2026-10-09)

Same machine (Windows 11 build 26200.9457, i9-12900K, .NET SDK 10.0.401, .NET 10.0.12, DefaultJob, Release). Before:
`main` at `a892abe7`, `Serialize_Encrypted` passing `_client.Encrypt` as a method group. After: #854, passing
`_client.Encryptor`.

| Method | Before mean | Before allocated | After mean | After allocated |
|---|---:|---:|---:|---:|
| `Serialize_NoEncryption` | 269.2 ns | 872 B | 265.8 ns | 872 B |
| `Serialize_Encrypted` | 441.8 ns | 648 B | 457.0 ns | 584 B |
| `Deserialize_Encrypted` | 324.9 ns | 288 B | 379.7 ns | 288 B |
| `Deserialize_NoEncryption` | 224.5 ns | 208 B | 233.8 ns | 208 B |

- **The send loses its 64 B delegate** (648 → 584 B), even here, in fully optimised code: the method group escapes
  into `Create`, which is not inlined, so it is on the heap. The means move within the runs' spread (StdDev
  10-37 ns); `Deserialize_Encrypted` uses no delegate at all, so its 55 ns is run-to-run noise.

---

## Session Cipher — Benchmark Results

**Date:** 2026-09-10

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i9-12900K 3.20GHz, 1 CPU, 24 logical and 16 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
```

### Results — first baseline (2026-09-10)

| Method | PayloadSize | Mean | Error | StdDev | Gen0 | Gen1 | Allocated |
|---|---|---|---|---|---|---|---|
| `BouncyCastle_Encrypt` | 64 | 776.4 ns | 5.14 ns | 4.56 ns | 0.1125 | - | 1,768 B |
| `BouncyCastle_Decrypt` | 64 | 376.2 ns | 7.14 ns | 8.22 ns | 0.0968 | - | 1,520 B |
| `AesGcm_Encrypt` | 64 | 258.9 ns | 2.89 ns | 2.71 ns | 0.0076 | - | 120 B |
| `AesGcm_Decrypt` | 64 | 195.8 ns | 1.63 ns | 1.27 ns | - | - | - |
| `BouncyCastle_Encrypt` | 256 | 901.1 ns | 17.33 ns | 17.02 ns | 0.1488 | - | 2,344 B |
| `BouncyCastle_Decrypt` | 256 | 456.9 ns | 2.59 ns | 2.03 ns | 0.1087 | - | 1,712 B |
| `AesGcm_Encrypt` | 256 | 277.7 ns | 3.78 ns | 3.35 ns | 0.0196 | - | 312 B |
| `AesGcm_Decrypt` | 256 | 220.9 ns | 2.83 ns | 2.65 ns | - | - | - |
| `BouncyCastle_Encrypt` | 1024 | 1,397.5 ns | 24.96 ns | 24.52 ns | 0.2956 | - | 4,648 B |
| `BouncyCastle_Decrypt` | 1024 | 819.2 ns | 16.30 ns | 20.01 ns | 0.1574 | 0.0010 | 2,480 B |
| `AesGcm_Encrypt` | 1024 | 385.7 ns | 7.19 ns | 6.37 ns | 0.0687 | - | 1,080 B |
| `AesGcm_Decrypt` | 1024 | 283.8 ns | 1.16 ns | 1.08 ns | - | - | - |

### Key observations

- **The platform primitive is 3.0–3.6× faster on encrypt and 1.9–2.9× on decrypt**, at every size
  measured, on identical key material.
- **Allocation is the wider gap.** BouncyCastle allocates 1.7–4.6 KB per encrypt and 1.5–2.5 KB per
  decrypt. The platform arm allocates only the result buffer on encrypt (120–1,080 B) and nothing
  at all on decrypt, because it writes straight into the caller's span.
- **Two independent effects, separable across the three sizes.** A straight-line fit of the encrypt
  means against payload size puts the fixed per-call cost at ~735 ns for BouncyCastle versus
  ~250 ns for the platform, and the marginal cost at ~0.65 ns/byte versus ~0.13 ns/byte. Decrypt
  shows the same shape (~347 ns versus ~190 ns fixed, ~0.46 versus ~0.09 ns/byte). So roughly
  480 ns per call is call shape and the remaining ~5× is cipher throughput.
- **The fixed component is the per-call re-key.** `AvalonCryptoSession` holds one shared
  `IBufferedCipher` and calls `Init(...)` on it for every packet, in both directions, under a
  single lock. Re-keying GCM regenerates the GHASH multiplication tables, which is also where most
  of the per-call allocation goes.
- **At broadcast scale** — 50 connections × 60 Hz state at 256 B is 3,000 encrypts/s. That
  is ~2.7 ms/s of CPU and ~6.7 MB/s of Gen0 on the current path, against ~0.8 ms/s and ~0.9 MB/s
  on the platform primitive: a saving of ~1.9 ms/s and ~5.8 MB/s from this site alone.
- **This measures the current call shape, not BouncyCastle at its best.** A BouncyCastle arm that
  keyed its cipher once and reused it would close the fixed-cost half of the gap. It would not
  close the per-byte half, which is a property of the implementation rather than of how it is
  called.

The table above is the record from before #850, when the session arm was BouncyCastle (named
`BouncyCastle_*`). The runs below are the production figures since.

### Results — #850, before and after (2026-10-09)

Same machine and toolchain as above (Windows 11 build 26200.9457, i9-12900K, .NET SDK 10.0.401,
.NET 10.0.12, BenchmarkDotNet 0.15.8, DefaultJob, Release). Both runs on this date, one on `main` at
`c18f8cd7` and one with #850 applied; the bare `AesGcm_*` arms are unchanged between them and agree
within run-to-run noise, which is the control.

Before — the session on BouncyCastle, re-keyed per call (`main`, `c18f8cd7`):

| Method | PayloadSize | Mean | Error | StdDev | Gen0 | Gen1 | Allocated |
|---|---|---|---|---|---|---|---|
| `BouncyCastle_Encrypt` | 64 | 447.4 ns | 8.97 ns | 24.09 ns | 0.1040 | - | 1,632 B |
| `BouncyCastle_Decrypt` | 64 | 494.3 ns | 12.97 ns | 38.25 ns | 0.0916 | - | 1,440 B |
| `AesGcm_Encrypt` | 64 | 297.4 ns | 5.78 ns | 8.29 ns | 0.0076 | - | 120 B |
| `AesGcm_Decrypt` | 64 | 219.3 ns | 4.31 ns | 4.03 ns | - | - | - |
| `BouncyCastle_Encrypt` | 256 | 614.4 ns | 12.23 ns | 35.49 ns | 0.1402 | - | 2,208 B |
| `BouncyCastle_Decrypt` | 256 | 622.0 ns | 16.18 ns | 47.71 ns | 0.1040 | - | 1,632 B |
| `AesGcm_Encrypt` | 256 | 331.9 ns | 6.62 ns | 11.94 ns | 0.0196 | - | 312 B |
| `AesGcm_Decrypt` | 256 | 250.1 ns | 3.84 ns | 3.60 ns | - | - | - |
| `BouncyCastle_Encrypt` | 1024 | 1,247.5 ns | 29.90 ns | 88.15 ns | 0.2861 | 0.0019 | 4,512 B |
| `BouncyCastle_Decrypt` | 1024 | 1,115.2 ns | 22.17 ns | 37.04 ns | 0.1526 | 0.0010 | 2,400 B |
| `AesGcm_Encrypt` | 1024 | 489.8 ns | 9.73 ns | 17.29 ns | 0.0687 | - | 1,080 B |
| `AesGcm_Decrypt` | 1024 | 336.2 ns | 6.59 ns | 10.83 ns | - | - | - |

After — the session on the platform `AesGcm`, keyed once per direction (#850):

| Method | PayloadSize | Mean | Error | StdDev | Gen0 | Allocated |
|---|---|---|---|---|---|---|
| `Session_Encrypt` | 64 | 257.1 ns | 2.53 ns | 2.25 ns | 0.0076 | 120 B |
| `Session_Decrypt` | 64 | 232.5 ns | 4.53 ns | 5.22 ns | - | - |
| `AesGcm_Encrypt` | 64 | 295.8 ns | 5.88 ns | 11.88 ns | 0.0076 | 120 B |
| `AesGcm_Decrypt` | 64 | 216.5 ns | 2.62 ns | 2.45 ns | - | - |
| `Session_Encrypt` | 256 | 292.9 ns | 5.58 ns | 6.20 ns | 0.0196 | 312 B |
| `Session_Decrypt` | 256 | 257.9 ns | 5.14 ns | 8.00 ns | - | - |
| `AesGcm_Encrypt` | 256 | 323.3 ns | 5.93 ns | 5.26 ns | 0.0196 | 312 B |
| `AesGcm_Decrypt` | 256 | 236.8 ns | 4.50 ns | 3.99 ns | - | - |
| `Session_Encrypt` | 1024 | 423.9 ns | 8.44 ns | 11.83 ns | 0.0687 | 1,080 B |
| `Session_Decrypt` | 1024 | 337.8 ns | 6.76 ns | 13.51 ns | - | - |
| `AesGcm_Encrypt` | 1024 | 450.8 ns | 8.50 ns | 15.33 ns | 0.0687 | 1,080 B |
| `AesGcm_Decrypt` | 1024 | 321.4 ns | 6.42 ns | 11.25 ns | - | - |

- **Sealing allocates the sealed packet and nothing else; opening allocates nothing.** 120, 312 and
  1,080 B are exactly a `byte[]` of nonce + payload + tag at 64, 256 and 1,024 bytes, down from
  1.6–4.5 KB; decrypt goes from 1.4–2.4 KB to zero. `SessionKeyDerivationShould` pins both.
- **1.7–2.9× faster to seal and 2.1–3.3× faster to open**, the gain growing with the payload
  (at 256 B: 614 → 293 ns and 622 → 258 ns). The session now runs within ~20 ns of the bare
  primitive on decrypt (the lock and the length checks). It seals slightly faster than the bare
  arm, because the bare arm draws a random nonce while the session copies its counter.
- **The BouncyCastle "before" ran faster than on 2026-09-10** (447 against 776 ns at 64 B) and
  with a much wider spread (a bimodal decrypt): treat it as one noisy run, not a trend. The
  allocation columns are deterministic and are the solid comparison.
- **At broadcast scale** — 50 connections × 60 Hz × 256 B, 3,000 encrypts/s — sealing now costs
  ~0.9 ms/s of CPU and ~0.9 MB/s of Gen0, against ~1.8 ms/s and ~6.6 MB/s in the before run.

### Results — without the session lock (#875, 2026-10-10)

One thread at a time seals and one opens (the world connection's send thread and its read loop), so the session lost
the lock it took around both. Four runs on one machine, alternating before and after; this machine's clock drifted by
up to 20% between runs, the bare `AesGcm` arms (unchanged code) with it. The pair below is the one whose bare arms
agree within 1%:

| Method | PayloadSize | Before | After | Change |
|---|---:|---:|---:|---:|
| `SessionSealInto` | 64 | 219.8 ns | 207.9 ns | −11.9 ns |
| `Session_Decrypt` | 64 | 216.2 ns | 200.0 ns | −16.2 ns |
| `AesGcm_Encrypt` (control) | 64 | 265.2 ns | 263.5 ns | −1.7 ns |
| `AesGcm_Decrypt` (control) | 64 | 199.2 ns | 196.3 ns | −2.9 ns |
| `SessionSealInto` | 256 | 238.5 ns | 221.5 ns | −17.0 ns |
| `Session_Decrypt` | 256 | 235.4 ns | 227.1 ns | −8.3 ns |
| `AesGcm_Encrypt` (control) | 256 | 287.1 ns | 287.4 ns | +0.3 ns |
| `AesGcm_Decrypt` (control) | 256 | 218.3 ns | 222.1 ns | +3.8 ns |
| `SessionSealInto` | 1024 | 288.6 ns | 282.4 ns | −6.2 ns |
| `Session_Decrypt` | 1024 | 302.9 ns | 283.1 ns | −19.8 ns |

- **About 10–20 ns off each seal and each open**, the cost of an uncontended monitor enter and exit. Measured
  within each run, which the drift does not touch, the open's overhead over the bare primitive fell from 17–21 ns
  (both before runs, 64 and 256 B) to 4–8 ns (both after runs): what is left is the ready check and the length checks.
  The 1,024-byte rows are noisier (a bimodal bare decrypt in the first before run).
- **Nothing allocated, before or after.** At the scale the send path works (a world of 50 connections at 60 Hz, a
  few thousand seals a second per send thread), the saving is tens of microseconds of CPU per second: small, and
  on every packet.

**The seal guard (#875, same day).** The seal path then gained a guard that fails closed in every build: a
compare-and-swap on an in-use flag before the nonce is copied, so an overlapping seal is refused rather than sealed
under a nonce already used. Four more runs, alternating without and with it; the first pair's bare arms agree within
2%:

| Method | PayloadSize | No guard | Guard | Change |
|---|---:|---:|---:|---:|
| `Session_Encrypt` | 64 | 217.2 ns | 223.5 ns | +6.3 ns |
| `SessionSealInto` | 64 | 212.3 ns | 212.6 ns | +0.3 ns |
| `SessionSealInto` | 256 | 216.4 ns | 225.6 ns | +9.2 ns |
| `SessionSealInto` | 1024 | 274.5 ns | 281.7 ns | +7.2 ns |
| `Session_Decrypt` | 64 | 198.2 ns | 199.3 ns | +1.1 ns |
| `AesGcm_Encrypt` (control) | 64 / 256 / 1024 | 265.9 / 282.9 / 387.6 ns | 262.6 / 283.0 / 387.1 ns | ≈0 |

- **0–9 ns a seal**, an uncontended interlocked operation and its release; the open, which keeps only a Debug check,
  is unchanged. Measured against the bare arm in the same run, the seal's lead over it shrank by 4–9 ns.
- **Still under the lock it replaced**: 212.6 / 225.6 / 281.7 ns against 219.8 / 238.5 / 288.6 ns with the lock. The
  second pair of runs drifted mid-run (its 64-byte session rows moved while its bare rows did not) and is not used.

## Packet encoder (#875) — Benchmark Results

### Results — first baseline (2026-10-10)

Windows 11, 12th Gen Intel Core i9-12900K, .NET 10.0.12, BenchmarkDotNet 0.15.8, DefaultJob, Release.

| Method | Entities | Mean | StdDev | Allocated |
|---|---:|---:|---:|---:|
| `Encode` (tick) | 1 | 191.6 ns | 1.76 ns | - |
| `EncodeSealFrame` (tick and drain) | 1 | 461.1 ns | 2.88 ns | - |
| `EncodeFramePlain` (TLS only) | 1 | 218.1 ns | 2.13 ns | - |
| `Encode` (tick) | 30 | 3,261.6 ns | 14.56 ns | - |
| `EncodeSealFrame` (tick and drain) | 30 | 3,687.8 ns | 32.20 ns | - |
| `EncodeFramePlain` (TLS only) | 30 | 3,292.4 ns | 13.19 ns | - |

These rows include the second reset, once the message is written (the scratch lets go of the packet's lists); a run
before it read 216 / 728 / 222 ns at one entity and 3,714 / 3,935 / 3,535 ns at thirty, within this machine's spread.

`SessionCipherBenchmarks` in an earlier run of the same day, the seal the drain does since #875 (Task 2.1's `SealInto`) beside the
`Encrypt` it replaced on the send path:

| Method | PayloadSize | Mean | StdDev | Allocated |
|---|---:|---:|---:|---:|
| `Session_Encrypt` | 64 | 230.2 ns | 1.60 ns | 120 B |
| `SessionSealInto` | 64 | 218.4 ns | 0.99 ns | - |
| `Session_Encrypt` | 256 | 250.0 ns | 2.39 ns | 312 B |
| `SessionSealInto` | 256 | 270.6 ns | 35.80 ns | - |
| `Session_Encrypt` | 1024 | 398.3 ns | 42.15 ns | 1,080 B |
| `SessionSealInto` | 1024 | 289.5 ns | 7.90 ns | - |

- **Nothing is allocated per packet, at either size, on either side.** The message is the thread's scratch
  (`PacketEncoder.Scratch<T>()`), the payload a pooled segment returned by `Release`, the frame written into a reused
  burst buffer, and the seal written in place. Before the scratch, every `Create` allocated its message (24 B for a
  state update, 48 B for a movement acknowledgement), the only allocation left on the send path.
- **Encoding is protobuf-net's own cost**, about 105 ns per entity in view: framing plain adds little (about 25 ns).
  The seal adds about what `SealInto` costs alone: some 270 ns at one entity and 400 ns at thirty (about 1.3 KB).
- `SealInto` matches `Encrypt` in time and drops its array: 120, 312 and 1,080 B per sealed packet, the nonce, the
  payload and the tag.


---

### Tick-thread guard — `TickThreadGuardBenchmarks.cs`

The cost of `TickThreadGuard.AssertOnTick` (#639), compiled into every build and checked only while
`Game:TickThreadGuard` is on. The switch can only be turned on in a process, so the two states are
two classes: `TickThreadGuardOffBenchmarks` refuses to run if the guard is already on, and
`TickThreadGuardOnBenchmarks` turns it on and binds its own thread in its global setup. Both rely on
BenchmarkDotNet's default out-of-process toolchain, which runs every benchmark in a process of its
own; no reset hook was added to production code.

| Scenario | What it models |
|---|---|
| `Baseline_EmptyCall` | An empty method of the guard's shape, called the way the guard is (`x?.Method("...")`) |
| `Guard_Off` / `Guard_OnBound` | The guard itself: off (production default), and on, bound, called on the bound thread |
| `IgnoreList_AddRemove_*` | One ignore and one unignore, a real guarded write: no guard handed over, guard off, guard on |
| `PartyService_Leave_*` | `PartyService.Leave` for a character in no party, the cheapest guarded mutator |

### Results — first baseline (2026-10-02)

Windows 11, 12th Gen Intel Core i9-12900K, .NET 10.0.12, BenchmarkDotNet 0.15.8, DefaultJob, Release.

| Method | Mean | StdDev | Allocated |
|---|---:|---:|---:|
| `Baseline_EmptyCall` (off run) | 0.000 ns | 0.000 ns | - |
| `Guard_Off` | 1.008 ns | 0.065 ns | - |
| `IgnoreList_AddRemove_NoGuard` | 32.85 ns | 0.71 ns | 64 B |
| `IgnoreList_AddRemove_GuardOff` | 26.13 ns | 1.24 ns | 128 B |
| `PartyService_Leave_NoGuard` | 0.550 ns | 0.023 ns | - |
| `PartyService_Leave_GuardOff` | 0.563 ns | 0.025 ns | - |
| `Baseline_EmptyCall` (on run) | 0.016 ns | 0.022 ns | - |
| `Guard_OnBound` | 0.726 ns | 0.005 ns | - |
| `IgnoreList_AddRemove_GuardOn` | 43.63 ns | 5.11 ns | 128 B |
| `PartyService_Leave_GuardOn` | 0.475 ns | 0.064 ns | - |

### Key observations

- **The guard costs about a nanosecond a call, on or off.** Off it is one static volatile read;
  on and bound it adds a volatile read of the bound thread and a compare with the current thread
  id. The empty baseline is inlined away to nothing, so the whole ~1 ns is the guard.
- **In a real write it is lost in the noise.** `PartyService.Leave` measures the same with and
  without it (all three under a nanosecond, below what the harness resolves reliably).
  `IgnoreList` add-and-remove varies more between runs (26-44 ns) than the guard costs.
- **The 64 B against 128 B on `IgnoreList` is the JIT, not the guard.** With no guard handed over,
  .NET 10's escape analysis keeps one of the two short-lived objects (most likely the removal's
  closure) on the stack; the extra guard code changes the inlining and it moves to the heap. With
  `DOTNET_JitObjectStackAllocation=0` both variants allocate 128 B and run in the same time
  (short job: 26.4 ns against 27.5 ns). The guard itself never allocates.
- **The tick runs a few guarded calls every tick** (the party tick, the member-status flush, the
  publish and the expiry pass) and more only on rare requests (transfers, spawns, party requests,
  ignores), so at 60 Hz the guard costs well under a microsecond a second.

---

## The send path (#875)

`OutboxFlush/OutboxFlushHarness.cs`, run as `outbox-flush [connections] [packets] [ticks] [modes] [seal] [wakes]`
(defaults `50,200,500,1000`, 1, 1800, `memory,tcp,tls`, `both` and 1). Not a BenchmarkDotNet suite: like
`crowd-budget`, it is a steady state timed tick by tick.

The first capacity ramp (#875) held about 150 players. At 200, about two thirds of a median tick (some 4.2 ms of
6.2 ms) was spent after the world update, where no histogram looked; `world.post_update.duration` now times each stage
of it ([instrumentation](instrumentation.md#tick-and-instance-time)). Most of it was the outbox stage writing every
connection's packets on the tick. Since #875 the send threads do that, and the tick's outbox stage only wakes them
(`NetworkSendScheduler.SignalAll`). The [scenario baseline](#scenario-baseline) cannot show this split: its connections
write to a stream that only counts bytes, and its tick runs the send passes itself.

For each mode, connection count and seal setting, the harness opens the connections over loopback. The server end sits
behind the world's real `ConnectionSender`, of one started `NetworkSendScheduler` with the default thread count
(`Network:SendThreads`, half the processors, 1 to 8); the client end is read and discarded on the thread pool, as a
peer would. It then ticks at 60 Hz on a thread of its own at the world tick thread's priority (`Highest`; at normal
priority, on Windows, a send thread it wakes can take its core and run its pass inside the outbox stage). Each tick
encodes `packets` movement acknowledgements (a frame of about 70 B, the size of the world's acks and state updates) per
connection and queues them (**enqueue**, which includes the encode and the payload pool's rent), then wakes the send
threads (**outbox stage**). It reports both, mean, p99, p99.9 and max; the wall time of the send threads' passes per
tick and per connection (the sum of `network.send.pass.duration`, read through a `MeterListener`; wall time, not CPU
time, so a pass the OS preempts counts the time it was off its core); and the bytes the tick thread allocated per tick.
`slow-tcp` and `slow-tls` also stall one connection in twenty for 3 s and report the tick's bytes before, during and
after the stall ([a client that stops reading](#a-client-that-stops-reading-875-2026-10-10)).
300 warm-up ticks come first.

| Mode | Stream behind the sender |
|---|---|
| `memory` | The scenario runner's counting stream: no socket, no TLS |
| `tcp` | A plain `NetworkStream` |
| `tls` | An `SslStream` over the socket (TLS 1.2 or 1.3, a self-signed P-256 certificate), as the world serves |
| `receive` | The other direction, off the tick: each client sends one frame per tick, and the server end reads it through `PacketStream.EnumerateRawFramesAsync` and `InboundPacketFrame.ParseFrame`; reports the bytes allocated per frame read |

`seal`: `on` gives every connection its own initialised session, which seals each ack (they are flagged Encrypted);
`off` gives none, so every packet goes plain inside the stream. `wakes` splits the tick's sends into that many slices
with a wake-up after each (default 1, the world's tick): above 1 the send threads drain, and give their payload segments
back to the pool, while the tick goes on encoding, as they do when a write completion or an off-tick send wakes them
during a tick.

The `tls-pool` and `tls-parallel` prototypes are gone: they measured ways to take the old outbox's write off the tick,
which is now how it works.

### World 4 ramps (#875, 2026-10-11)

Four capacity ramps on the homelab's load-test world 4 ([load testing](load-testing.md)), one on each side of the
network layer's changes. Each ran the same command, run `DEW`, from an i9-12900K bot PC:

```bash
dotnet run -c Release --project tools/Avalon.LoadTest -- ramp --run DEW --mix idle=40,walker=20,churner=10,fighter=30 \
  --forest-time 180s --party-size 3 --start 50 --step 50 --hold 90s --max 1000 --dial 10.10.1.17
```

The figures at 200 bots are from the step's judged window. The post-update phase is the sum of the stage means of
`world.post_update.duration` (quests, inventory, sheet, ability amounts, party status, presence, pings, outbox,
continuations).

| Ramp | Server | Capacity | Failed first | Tick p99 at 200 | Outbox stage mean at 200 | Post-update mean at 200 | Send threads busy at 200 (cores) | Pending bytes p99 | Slow kicks |
|---|---|---:|---|---:|---:|---:|---:|---:|---:|
| Before (report `20261010-040445-DEW`) | dev.679: flushed on the tick, always sealed | 350 | tick-p99 23.4 ms at 400 | 16.0 ms | 3,392 µs | 3,477 µs | not exported | not exported | 0 drops |
| Send threads (#894, `20261010-205813-DEW`) | dev.685: always sealed | 250 | gc-stall 19.1 ms at 300 | 10.9 ms | 30 µs | 133 µs | 0.38 | 1,014 B | 0 |
| Flag on (#899, `20261010-234848-DEW`) | dev.686, `Network:PacketEncryption` on | 150 | gc-stall 20.5 ms at 200 | 11.3 / 11.6 ms | 30 µs | 134 µs | 0.37 / 0.35 | 1,014 B | 0 |
| Flag off (#899, `20261011-010327-DEW`) | dev.686, off (TLS alone) | 150 | gc-stall 52.2 ms at 200 | 9.6 / 8.0 ms | 30 µs | 131 µs | 0.34 / 0.35 | 1,014 B | 0 |

Two figures in a cell are the step and its re-hold.

- **The post-update phase.** At 200 players it now costs about 0.13 ms per tick, against 3.5 ms before and the spec's
  target of under 0.5 ms.
  - The outbox stage, which used to write every connection's packets on the tick, is a wake-up of about 30 µs, flat
    with load.
  - The largest stage is now quests (about 56 µs).
- **The session layer.** It costs little: at 200 players the send threads used 0.37 cores with it on and 0.34 to 0.35
  with it off, and the input-ack latency was the same (p95 22 to 30 ms on, 23 to 26 ms off). The tick never seals
  (send threads seal and the read loop opens), so the gap in tick p99 between the two ramps comes from other noise in
  the run, mostly the GC below. It is not a sealing cost.
- **What limits capacity now.** Since the send threads, every ramp has stopped on `gc-stall`, not on tick time, slow
  clients or the network: a gen0 collection that promotes about 21 MB pauses the world for 15 to 52 ms, about once a
  minute (#896).
  - When those pauses land varies from run to run, so capacity read 250, 150 and 150 on servers whose tick costs
    match.
  - The capacity figure says nothing about the network until #896 is fixed.
  - With gc-stall set aside, the send-threads ramp ran 300 players for 11 minutes at a tick p99 of 12.0 ms (report
    `20261010-211500-DEW`).

### Results — the send threads (#875, 2026-10-10)

i9-12900K (24 logical processors). Windows 11 (10.0.26200), .NET 10.0.12, workstation GC, 8 send threads. Linux:
Ubuntu 24.04 in a `dotnet/sdk:10.0` container limited to 4 CPUs (`--cpus 4`, Docker on WSL2), the same binaries, 2
send threads. Release, dynamic PGO on (the default). `tls`, one packet per connection per tick, seal off (the setting
PR 4 makes the default), 1800 measured ticks. Before is `67aa604e` (`TickDrivenOutbox`, its flush timed on the tick, the harness
of that build run with no sealer and at the tick thread's priority); after is this change. Windows: three runs per side,
interleaved, means of the three. Linux: one run of the after build at 1,000 connections only.

| Tick's outbox stage, µs | Before mean | Before p99 | After mean | After p99 |
|---|---:|---:|---:|---:|
| Windows, 200 connections | 3,271 | 5,807 | 30.8 | 62.9 |
| Windows, 1,000 connections | 17,383 | 26,101 | 30.3 | 50.8 |
| Linux, 1,000 connections | not measured | | 44.9 | 71.8 |

| After, the rest of the tick and the send threads | Enqueue mean | p99 | Send threads µs/tick | µs/conn | B/tick on the tick |
|---|---:|---:|---:|---:|---:|
| Windows, 200 | 110.6 | 192.6 | 5,597 | 28.0 | 0 |
| Windows, 1,000 | 462.6 | 631.7 | 24,546 | 24.5 | 0 |
| Linux, 1,000 | 592.8 | 785.2 | 6,429 | 6.4 | 0 |

- **The outbox stage is flat**: about 30 µs on Windows at 200 and at 1,000 connections, where the flush it replaced
  took 3.3 and 17.4 ms (at 1,000 longer than the 16.7 ms tick). What is left is the wake-ups themselves: one
  `ManualResetEventSlim.Set` per send thread (since [the wake-up change](#the-enqueue-and-the-wake-up-875-2026-10-10),
  per send thread with work), about 3 to 4 µs each on Windows (8 threads) and about 20 µs each in the
  WSL2 container (2 threads), whatever the connection count. It is not the "few µs" the design hoped for; a probe of
  the bare primitive (8 waiting threads set at 60 Hz, nothing else running) gave the same: 26 µs median on Windows,
  80 µs on Linux in the container. Waking one thread that wakes the next would leave the tick one wake-up, at the cost
  of the later threads starting later; not done here.
- **Smoke runs over TLS sometimes showed 12 to 88 B per tick on the tick thread** (a run's mean). That is one-off
  growth of the payload pool at a new peak: the world-performance review traced it to 503 segments of 136 B, one
  tick's payloads rented new, in one tick. It does not recur: 0 of about 14,000 ticks in the review's probes allocated.
- **The tick keeps the encode and the queue**: 0.46 µs per connection on Windows at 1,000 (0.55 µs at 200), 0 B
  allocated. The before harness did not time that part. The enqueue alone does cost more than the old outbox's:
  the world-performance review measured it single-threaded on Windows at 18.7 to 21.7 ns before (`TickDrivenOutbox`,
  a bounded channel) against 27.0 to 28.5 ns after (`ConnectionSender`); with the send threads live it is 56 to 115 ns
  ([the sender's fields](#the-connection-senders-fields-875-2026-10-10)). See
  [the enqueue's read of the dirty flag](#the-enqueue-and-the-wake-up-875-2026-10-10) for what was taken back.
- **The send threads carry the write**: about 25 to 28 µs of thread time per connection per tick over TLS on Windows
  loopback, spread over 8 threads (3.1 ms per thread per tick at 1,000), against 17.4 µs per connection on the tick
  before; 6.4 µs on Linux.
- **Not measured**: 50 and 500 connections, seal on, and `memory` and `tcp` in this before-and-after table (the
  measurement was cut to these rows); the Linux before side. The pool and sender sections below have seal-on, `memory`
  and `tls` rows at 1,000 connections, after only.

### The payload pool under the send threads (#875, 2026-10-10)

The tick rents payload segments and the send threads give them back, so each size class's lock is shared between them
(B2's world-performance review measured the tick's rent at 85 to 105 ns, p99 about 235 ns, under a worst-case
micro-benchmark, against 10 to 12 ns for a `ConcurrentQueue`). Three builds of the same tree, all with this harness,
1,000 connections × 4 packets per tick, seal on, Release, dynamic PGO on (the default), three runs each, interleaved:

- **lock**: `aeb6134e`, each release takes the size class's lock;
- **queue**: the same with the `ConcurrentQueue` free lists of `96feac8a`, as a control (it allocates after bursts,
  which is why the pool left it);
- **batched**: the change. A send pass holds back what it gives back (`PayloadSegmentPool.BeginReturnBatch`) and
  returns it with one lock per size class per pass (or per 256 segments of a class), and each size class's stack sits
  on cache lines of its own.

Means of the three runs; enqueue is the tick's encode, rent and queue of 4,000 packets, in µs.

| Windows (8 send threads) | wakes | enqueue mean | enqueue p99 | send threads µs/conn |
|---|---:|---:|---:|---:|
| `memory`, lock | 1 | 876.3 | 1,363.3 | 5.89 |
| `memory`, queue | 1 | 862.0 | 1,228.4 | 3.33 |
| `memory`, batched | 1 | 842.7 | 1,260.0 | 2.01 |
| `memory`, lock | 4 | 1,516.7 | 1,932.2 | 6.66 |
| `memory`, queue | 4 | 909.5 | 1,391.4 | 2.96 |
| `memory`, batched | 4 | 842.6 | 1,192.8 | 2.03 |
| `tls`, lock | 4 | 1,402.5 | 1,716.8 | 27.00 |
| `tls`, queue | 4 | 1,315.4 | 1,645.4 | 26.44 |
| `tls`, batched | 4 | 1,334.8 | 1,652.8 | 27.21 |

| Linux, 4-CPU container (2 send threads) | wakes | enqueue mean | enqueue p99 | send threads µs/conn |
|---|---:|---:|---:|---:|
| `memory`, lock | 1 | 1,278.5 | 1,526.9 | 2.40 |
| `memory`, queue | 1 | 1,302.1 | 1,542.6 | 2.42 |
| `memory`, batched | 1 | 1,219.6 | 1,520.6 | 2.24 |
| `memory`, lock | 4 | 1,467.2 | 1,660.9 | 2.46 |
| `memory`, queue | 4 | 1,263.2 | 1,481.0 | 2.27 |
| `memory`, batched | 4 | 1,192.4 | 1,497.6 | 2.13 |
| `tls`, lock | 4 | 1,570.8 | 1,907.6 | 10.87 |
| `tls`, queue | 4 | 1,477.3 | 1,770.2 | 10.74 |
| `tls`, batched | 4 | 1,490.9 | 1,855.2 | 10.18 |

- **With the world's one wake-up per tick there is little to contend for**: the send threads run after the tick's sends,
  not during them, and the three builds are within a few percent of each other on the tick (`tls` at one wake-up, not
  shown: 1,105.8, 1,133.9 and 1,166.6 µs on Windows, within the runs' spread).
- **When the send threads drain during the tick's sends (4 wake-ups), the lock shows**: on Windows the tick's enqueue
  went from about 860 to 1,517 µs mean (1,932 µs p99) for 4,000 packets, about 160 ns more per packet; on Linux from
  1,220 to 1,467 µs. Batched, it is back at the one-wake-up figure, at or below the queue control on both.
- **The send threads gain most**: in `memory`, where nothing else slows them, eight threads taking one lock per
  packet spent 5.9 to 6.7 µs per connection on Windows; batched, 2.0 µs, below the queue's 3.0 to 3.3.
- Over TLS the write dominates and the builds are within noise.
- The tick-local free list (taking the shared stack in one exchange) was not needed and was not built.
- Allocation: `PayloadSegmentPoolShould` and the scenario gate are unchanged (0 B for the town scenarios, 16,252 B for
  `forest-combat`).

**The cap.** The free stacks never shrank, so a network blip that left a thousand connections each holding up to
`Network:MaxPendingBytes` (512 KiB) could have pinned about 512 MB of segments for good, the 1 MiB ones on the large
object heap. Each size class now keeps at most `PayloadSegmentPool.MaxFreeBytesPerSizeClass`, 4 MiB of segment
capacity, but never fewer than `MinFreeSegmentsPerSmallSizeClass` (2,048) segments for a class up to 4 KiB, where most
packets fall. Past its limit a segment given back is left to the GC.

| Size class | 64 B | 128 B | 256 B | 512 B | 1 KiB | 2 KiB | 4 KiB | 8 KiB | 16 KiB | 32 KiB | 64 KiB | 128 KiB | 256 KiB | 512 KiB | 1 MiB |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Free segments kept | 65,536 | 32,768 | 16,384 | 8,192 | 4,096 | 2,048 | 2,048 | 512 | 256 | 128 | 64 | 32 | 16 | 8 | 4 |

That is 64 MiB of segment capacity over the 15 classes (the 4 KiB class holds 8 MiB), plus about 64 B of object per
segment. What was measured: the harness above, 1,000 connections × 4 movement acks per tick, keeps about 4,000
segments of one small class in flight, far under its limit, and its tick allocated 0 B. Not measured: a steady state
above a class's limit. There, each tick would rent segments the last peak's returns left to the GC, so the tick would
allocate again at every such peak (a recurring re-rent), not only once at a new high.

### The connection sender's fields (#875, 2026-10-10)

Each enqueue makes about four atomic operations, and the fields the tick writes (`_activeEnqueues`, `_dirty`,
`_pendingBytes`) shared cache lines with those the owner send thread writes (`_writeInFlight`, `_writeStarted`, the
burst's byte count). A build with the two groups on cache lines of their own (padded structs) was measured against the
current layout with the harness changed to time the enqueues alone (the packets encoded beforehand, untimed): 1,000
connections × 4 packets, seal on, three interleaved runs each, means in µs for 4,000 enqueues.

| | Windows `memory` | Windows `tls` | Linux `memory` | Linux `tls` |
|---|---:|---:|---:|---:|
| Current layout, 1 wake-up | 225.2 (p99 404.7) | 460.5 (648.5) | 266.1 (346.4) | 510.9 (658.9) |
| Padded, 1 wake-up | 268.6 (438.9) | 477.6 (677.0) | 266.2 (413.1) | 527.5 (673.8) |
| Current layout, 4 wake-ups | 225.8 (360.7) | 528.8 (690.0) | 230.9 (364.6) | 477.3 (625.5) |
| Padded, 4 wake-ups | 244.5 (403.4) | 573.1 (757.3) | 235.5 (378.8) | 521.2 (650.6) |

An enqueue costs about 55 to 130 ns, and padding made it no faster anywhere, so the layout stays. The tick and the
owner rarely touch one connection at the same moment: the owner's pass comes after the tick's sends, and `_dirty` and
`_pendingBytes`, which both write, move between their cores once per tick whatever the layout.

### The enqueue and the wake-up (#875, 2026-10-10)

Two changes after the world-performance review:

- **An enqueue to a connection already listed reads the dirty flag instead of a compare-exchange.** The tick sends
  several packets per connection per tick, and every one after the first used to take the flag's cache line for
  writing. Why no wake-up is lost is in `ConnectionSender.Enqueue`. The pair of atomics around the push
  (`_activeEnqueues`) stays: it is what lets a close know that no enqueue that saw the connection open is still pushing,
  and no cheaper ordering was proven.
- **The tick wakes only the send threads that have work**: a thread whose dirty list is empty is left asleep. Its timed
  wake (100 ms) keeps its stall clock running.

The `outbox-flush` harness, Windows 11, i9-12900K, 8 send threads, Release, seal off. Before is the frame-ahead commit;
after adds these two. Three runs per side, interleaved, means of the three. The enqueue column is the tick's encode,
rent and queue together, in µs per tick.

| | Before | After |
|---|---:|---:|
| `memory`, 1,000 connections × 4 packets: enqueue mean | 868.9 | 868.8 |
| `tls`, 1,000 × 4: enqueue mean | 1,195.5 | 1,133.8 |
| `tls`, 1,000 × 4: outbox stage mean | 29.2 | 28.7 |
| `tls`, 4 connections × 1 packet: outbox stage mean (p99) | 24.1 (63.0) | 14.5 (42.5) |

- The enqueue's change is within the runs' spread here: the column includes the encode, which costs more than the
  atomic it saves.
- The outbox stage falls when some threads have nothing to send: with 4 connections on 8 threads, from 24.1 to 14.5 µs.
  At 1,000 connections every thread has work every tick, so it is unchanged.

### A client that stops reading (#875, 2026-10-10)

While a connection's write was pending, its owner send thread used to leave the queue alone until the write ended. The
tick's packets for it piled up in its `ConcurrentQueue`, whose ring doubles on the enqueuing thread and stays that size
for the connection's life, and each one held a pooled payload segment, so the tick rented new ones. Now the owner frames
what is queued into a second burst buffer while the write is in flight and gives each payload back at once ("frame
ahead"). The next write sends that buffer. The queue holds about one tick's packets and the pool stops growing. What
`Network:MaxPendingBytes` caps is what the connection holds: queued payload bytes plus both burst buffers' payloads.

`outbox-flush 1000 4 900 slow-tls off 1`: 1,000 TLS connections, 4 movement acks each per tick, seal off. One connection
in twenty (50) has 4 KiB socket buffers at both ends, and its reader is paused for 180 ticks (3 s) after the first 180
measured ticks. The figures are the bytes the tick thread allocated per tick (mean, and the largest single tick), before,
during and after the stall. Windows 11, i9-12900K, 8 send threads, Release, dynamic PGO on. Before is `352f18bb`; after
is the change. Three runs per side, interleaved.

| Tick thread, B per tick | Before the stall | During the stall | After the stall | Whole run |
|---|---:|---:|---:|---:|
| Before, run 1 | 0 (largest 0) | 19,018 (largest 441,152) | 32 (largest 17,408) | 3,823 |
| Before, run 2 | 0 (0) | 18,964 (441,152) | 50 (27,200) | 3,823 |
| Before, run 3 | 0 (0) | 18,894 (331,328) | 50 (27,200) | 3,809 |
| After, runs 1 to 3 | 0 (0) | 0 (0) | 0 (0) | 0 |

The rest of the tick did not move: enqueue 1,254 to 1,312 µs mean for 4,000 packets, outbox stage 30.6 to 32.3 µs,
send threads 26.3 to 27.0 µs per connection, on both sides.

### Results — the network path (#883, 2026-10-13)

Low findings of #883 on the packet path: rows 1, 3, 10 to 13 and the send half of row 24. i9-12900K, Windows 11
(10.0.26200), .NET 10.0.12, Release; before is `808eec90`. **Other builds, test runs and benchmarks were using the
machine for every timing here**, so each is a range over several runs; the bytes and the buffer sizes are exact. The
bytes, the buffer sizes and the startup times are from a probe over the built assemblies, a new process for each run.
Every figure is from runtime 10.0.12. The bytes, the buffer sizes and the pool's rents were read again on runtime
10.0.13 (the same assemblies, run on an SDK of its own) and are the same to the byte; no timing was taken again
there.

**A stale packet's drop (row 1).** In-map packets still queued when their character has gone are dropped by the
session pass, on the tick (`WorldConnection.ProcessQueue`); a client causes one with every packet it sends between its
leave and the answer to it. A hundred packets queued, the character taken away, one session pass:

| | Before | After |
|---|---:|---:|
| `CMSG_PLAYER_INPUT`, per packet dropped | 56 B | 0 B |
| `CMSG_CAST_ABILITY`, per packet dropped | 56 B | 0 B |

The 56 B were the log call's argument array and the boxed opcode, allocated before the level was looked at, so with
Debug off as well as on. The line is now a generated log method, at Debug, and at Trace for input, as for a packet
dropped at arrival for its state ([packet handlers](packet-handlers.md)). `ReceiveQueueAllocationShould` holds the
0 B. A pass also takes exactly `WorldConnection.MaxPacketsPerUpdate` packets now, 150, where it took 151.

**Two packets built with queries (row 3).** Each built its list with `Select` and `ToList`; each now fills a list
sized for what it holds, in the same order. Bytes per call:

| | Before | After |
|---|---:|---:|
| `SLootDespawnedPacket.Create`, one drop | 112 B | 64 B |
| `SLootDespawnedPacket.Create`, eight drops | 168 B | 120 B |
| `IgnoreList.ToPacket`, empty | 104 B | 32 B |
| `IgnoreList.ToPacket`, 1 entry | 168 B | 96 B |
| `IgnoreList.ToPacket`, 10 entries | 528 B | 456 B |
| `IgnoreList.ToPacket`, 50 entries | 2,128 B | 2,056 B |

What went is the query's iterator, 48 B and 72 B. What is left is the list the message carries and, for the ignore
list, one object per entry. The first runs once per pickup, for all its recipients; the second at select, at a
character's delete and on `/ignore`.

**Burst buffers (row 10).** A connection's two burst buffers doubled as they filled and never shrank. What one can be
asked to hold is what `Network:MaxPendingBytes` lets a connection queue, 512 KiB of payload, and a frame adds 10 to 16
bytes to its payload, and a seal 28 more: for movement acknowledgements of 19 B that is up to 1 MiB of buffer with the
world sending plain and 2 MiB with it sealing. One connection whose peer stops reading until it holds just under the
cap, catches up, and does so again ten seconds later, under a clock the probe moves; the two buffers' sizes, in bytes:

| | Plain, before | Plain, after | Sealed, before | Sealed, after |
|---|---:|---:|---:|---:|
| A new connection | 1,024 | 1,024 | 1,024 | 1,024 |
| First stall, the peer not reading | 1,049,088 | 1,049,088 | 2,097,664 | 2,097,664 |
| Caught up, and ten seconds of play later | 1,049,088 | 1,049,088 | 2,097,664 | 2,097,664 |
| Second stall, the peer not reading | 2,097,152 | 2,097,152 | 4,194,304 | 4,194,304 |
| 32 s after the first stall | 2,097,152 | 1,049,088 | 4,194,304 | 2,097,664 |
| 32 s after that, and for the rest of its life | 2,097,152 | 1,024 | 4,194,304 | 1,024 |

- **The worst case was 2 MiB a connection plain and 4 MiB sealed**, for the connection's life, in arrays on the large
  object heap: a stall grows the buffer framed ahead into, and a later stall the other. After a network blip that
  stalls a thousand connections for a few seconds, short of the kicks, that is up to 2 to 4 GiB kept. With
  `MaxPendingBytes` raised it grows with it (the setting goes to 64 MiB).
- **A buffer above 64 KiB is now given back once the connection has outgrown it for 30 s**
  (`ConnectionSender.BurstTrimThreshold`, `BurstTrimDelay`). A watch begins at the first visit that finds a buffer
  that large with nothing framed ahead and no write in flight; the first such visit 30 s or more later gives back
  each large buffer that no burst written during the watch filled more than a quarter of, and begins another watch if
  a large buffer is left. In the table the buffer the first stall grew goes at the end of the first watch, and the
  one the second stall grew, which was filled inside that watch, at the end of the next. During a stall a connection
  still holds what it frames ahead: the cap is what bounds that.
- **Why a delay, and why 30 s.** A peer decides when its connection stalls and when it catches up. A first version of
  this change gave a buffer back at the first visit that found it idle, and its review measured what a peer could
  make of that: every stall rented the whole growth again, about 2 MiB plain, and past what the array pool keeps of a
  size (below) each cycle allocated it anew, 141 to 268 MiB a cycle for 200 connections cycling together, with
  gen2 collections, where the code before #883 allocated nothing after the first stall. With the delay a connection
  gives back at most its two buffers once in 30 s, whatever its peer does, and one that stalls again inside the
  delay finds its buffers there: each of the two is grown once, and stalls that close together rent nothing after
  that. 30 s is
  three times `Network:MaxWriteStall`'s default, the longest a stalled write is let live, and three ping periods, so
  a connection that is sent nothing else is still visited inside it.
- **What a buffer was used for is the largest burst written from it during the watch**, counted where each write
  starts, not what it holds when it is looked at. The first version read the writer's length, which is zero whenever
  a write went pending and ended with nothing queued, so a connection whose writes pend (which its peer decides) lost
  a buffer its bursts filled: 60 packets of 4 KiB a burst, each write pending until it is read, one burst a second
  for 70 s, rented 6 arrays and 516,096 B on every burst, and 5 arrays and 253,952 B with bursts of 18 packets. Now
  both rent nothing from the third burst on, across both delays, and hold two buffers of 256 KiB and of 128 KiB.
- **It costs three compares per burst**, on the send thread, for a connection whose buffers are small: two where the
  buffers are swapped and one of the burst's length as its write starts. Nothing per packet, nothing on the tick,
  and no clock: the clock is read only while a buffer is above the threshold. The rows below did not move.
- **Given back means to the array pool, and the process does not shrink at once.** `ArrayPool<byte>.Shared` keeps
  arrays of 1 and 2 MiB rather than dropping them, more of a size the more processors there are (the review measured
  65 at 2 processors and 129 at 4), and lets go of them only on gen2 collections, and not all at once. The review's
  figures: 1,000 arrays of 1 MiB returned at 24 processors were all still pooled after 60 s of forced gen2
  collections, and 684 were left at 132 s; at 4 processors, of 1,000 arrays of 2 MiB, 135 were pooled and 865 freed
  at the first gen2. So the change bounds what one connection pins, and a pooled array
  serves the next connection that stalls; it does not promise that the process's size falls when a blip ends.
- **A buffer goes back to the pool as it is**, here and when a writer grows or is disposed: it may hold what a
  connection was sent, framed, and in plain text when the world does not seal. Clearing it would be a write of up to
  2 MiB on a send thread at a moment a peer can choose, so the rule is the renter's and is written where the writer
  returns its array (`PooledArrayBufferWriter.Trim`): whatever rents from the shared pool writes before it reads or
  sends.
- **Not reached, as before #883**: a peer that reads just slowly enough that its connection always has a burst
  framed ahead keeps both buffers, since they are never both idle.
- `ConnectionSenderShould` holds it in one case: two buffers grown by a stall, the second while the first is still
  being written; a second stall inside the delay with nothing given back in between; both kept at the delay's end
  because they were filled within it, though a small burst came last; a second delay of small bursts that ends
  during a pending write, with nothing given back on the visit that frames ahead of it nor on the one that starts
  the next write; both given back at the first visit that finds them idle. Every packet carries its number and all
  arrive once and in order, read as each write ends. Each of these was checked by breaking it: the case fails when
  a buffer is given back while a write still reads it, when the watch is looked at on a visit with a write in
  flight, with no delay, with the last burst or the writer's length in place of the largest burst, and with no
  give-back.

**Serializers at startup (row 24, the send half).** protobuf-net builds a type's serializer on its first use and the
runtime compiles it as it first runs, so the first packet of each type a server sent paid for it, on a world's tick.
`PacketEncoder.PrepareServerPackets` now writes every server packet once at startup, as a message with something in
every member ([packet handlers](packet-handlers.md)). The first send of each of the 67 server packets, with every
member set, after the server's own startup calls, five runs before and six after:

| | Before | After |
|---|---:|---:|
| The first sends of all 67 types, together | 156 to 242 ms | 0.47 to 0.62 ms |
| The slowest of them | 14 to 32 ms (the quest log) | 79 to 113 µs |
| A type's first send, median | 1.5 to 2.8 ms | 4 to 5 µs |
| The second sends of all 67, together | 0.23 to 0.39 ms | 0.12 to 0.42 ms |

| Startup, a new process, 16 runs before and 11 after | Before | After |
|---|---:|---:|
| `PrepareServerPackets`, 67 types | 21 to 26 ms | 209 to 290 ms, and 389 ms once |
| The world's reader, 35 types (unchanged) | 108 to 199 ms | 113 to 152 ms |
| The auth server's reader, 10 types (unchanged) | 69 to 96 ms | 74 to 117 ms |

- **About 0.19 to 0.27 s more startup on each server**, once, off the tick and before the port opens, against some
  0.2 s of the tick's time spread over the first packet of each type, 14 to 32 ms of it in one tick when the first
  quest log went out.
- **Why a message with something in every member.** A member left at its default is skipped, and what would write it
  is built by the first packet that sets it. Three ways of preparing were measured before one was built, by what they
  left to the first sends: writing a new message of each packet left 49 ms of 153 (the nested messages); writing a
  new message of every nested type as well left 7 to 10 ms (the lists, and what each element needs); writing each
  packet once with every member set left 0.34 to 0.38 ms, which is what was built.
- **Both servers prepare all 67**, as both already compiled all 67 resets: nothing in a contract says which server
  sends it. The auth server sends about ten of them and pays the same.
- **The depth limit.** The reader sets protobuf-net's depth limit (`PacketReader.MaxDecodeDepth`) and each server
  builds it first. The order does not decide anything: a type built and used at the default depth, 512, refuses a
  message 40 deep, written or read, once the limit is set to 32, and takes one 20 deep.
- **What a thread still makes for its first packet of a type** is its own scratch message, one object.
- `PacketEncoderPrepareShould`: a prepared type's first packet allocates 0 B, against 274,248 to 371,896 B when
  nothing was built for it and 74,952 to 130,160 B when only a new message was written. It measures the first
  packets of three types of one shape and takes the fewest bytes: a first send happens once, and one window alone
  read 24,648 B in about one run in thirty of its project, something the runtime allocates once in a process on
  whichever thread first needs it (not identified; the shared array pool's per-processor stacks for a size, made by
  the first thread to return a second array of that size, are one such thing, 7,728 B at 24 processors, and not this
  one). The test has passed in every one of some 340 runs of its project since, 60 of them on runtime 10.0.13. In
  the 101 of those runs that recorded them (61 on 10.0.13, 40 on 10.0.12) all three windows read 0 B every time, so
  the one-off no longer falls in a window at all, on either runtime, and the fewest of three is there for the day it
  does.
- **What stops a start** is any packet the preparation cannot write in full, named: a contract protobuf-net cannot
  build, a member of a shape it has no value for, a message it cannot create, a setter that refuses the value, and a
  message that declares subtypes (`[ProtoInclude]`), which would be written as the base alone and have each subtype
  built by a first send (0.75 ms and 232 B in the review's probe). No shipped contract has any of them.

**What did not move.** The suites that time these paths, `--job short`, three runs before and three after, taking
turns, as the range of the runs' means. The machine was loaded and more so during the runs of the unchanged side:
read the rows as showing that nothing rose, not that anything fell. The allocations are exact.

| Suite, method | Before | After | Allocated, both |
|---|---:|---:|---:|
| `ReceiveQueueBenchmarks`, 1 packet a burst | 190 to 301 ns | 154 to 159 ns | 0 B |
| `ReceiveQueueBenchmarks`, 32 | 3,963 to 6,495 ns | 3,536 to 3,582 ns | 0 B |
| `ReceiveQueueBenchmarks`, 150 | 22,606 to 29,287 ns | 19,707 to 20,771 ns | 0 B |
| `PacketEncoderBenchmarks`, encode, 1 entity | 220 to 404 ns | 213 to 437 ns | 0 B |
| `PacketEncoderBenchmarks`, encode, seal and frame, 1 entity | 488 to 772 ns | 469 to 794 ns | 0 B |
| `PacketEncoderBenchmarks`, encode and frame plain, 1 entity | 372 to 439 ns | 233 to 324 ns | 0 B |
| `PacketEncoderBenchmarks`, encode, 30 entities | 4,214 to 6,392 ns | 3,603 to 7,320 ns | 0 B |
| `PacketEncoderBenchmarks`, encode, seal and frame, 30 entities | 5,586 to 6,696 ns | 4,034 to 8,144 ns | 0 B |
| `PacketEncoderBenchmarks`, encode and frame plain, 30 entities | 4,206 to 6,252 ns | 3,591 to 6,784 ns | 0 B |
| `PacketReaderDecryptGcBenchmarks`, `Legacy_DecryptAndRead` | 229 to 281 ns | 154 to 161 ns | 136 B |
| `PacketReaderDecryptGcBenchmarks`, `Fixed_DecryptAndRead` | 263 to 292 ns | 146 to 171 ns | 80 B |
| `PacketReaderDecryptGcBenchmarks`, `Session_MethodGroupPerPacket` | 496 to 582 ns | 381 to 476 ns | 144 B |
| `PacketReaderDecryptGcBenchmarks`, `Session_CachedDelegate` | 478 to 632 ns | 376 to 529 ns | 80 B |
| `TimeSyncBenchmarks`, `PingWritten` | 285 to 396 ns | 258 to 350 ns | 0 B |
| `TimeSyncBenchmarks`, `PingWrittenAndPongHandled` | 522 to 563 ns | 413 to 542 ns | 0 B |
| `TimeSyncBenchmarks`, `SurplusPong` | 20.9 to 23.7 ns | 17.6 to 22.3 ns | 0 B |

The `outbox-flush` harness, 1,000 connections × 4 movement acknowledgements a tick, 900 ticks, 8 send threads, over
real send threads, the same runs taking turns. Before is `808eec90` and after the first version of the change, which
gave a buffer back at once:

| | Before | After |
|---|---:|---:|
| `memory`, seal off: enqueue mean, µs a tick (3 runs) | 1,060 to 1,246 | 880 to 1,273 |
| `memory`, seal off: send threads, µs a connection | 0.74 to 1.03 | 0.54 to 0.89 |
| `memory`, seal on: enqueue mean, µs a tick (3 runs) | 915 to 1,324 | 828 to 1,363 |
| `memory`, seal on: send threads, µs a connection | 2.11 to 3.19 | 2.01 to 2.89 |
| `slow-tls`, seal off: enqueue mean, µs a tick (7 runs) | 1,281 to 1,502 | 1,152 to 1,377 |
| `slow-tls`, seal off: send threads, µs a connection (7 runs) | 27.5 to 29.3 | 25.9 to 29.6 |
| `slow-tls`: runs in which the tick allocated nothing | 2 of 7 | 5 of 7 |

The same harness on a quiet machine, the first version against the delay, three runs a side taking turns:
`memory`, seal off, enqueue 751 to 783 µs a tick before and 745 to 748 after, send threads 0.42 to 0.44 µs a
connection before and 0.40 to 0.41 after; seal on, enqueue 726 to 771 and 754 to 805, send threads 1.74 to 1.76 and
1.74. The tick allocated 0 B in all twelve runs.

In `slow-tls` one connection in twenty stops reading for three seconds, so fifty connections grow a buffer, catch up
and, in the first version of the change, give it back at once (with the delay they keep it for 30 s more). The tick allocated in some runs of both sides, `memory` ones among them, in a
few ticks of a run, and always by payload segments rented new, up to a whole tick's worth (544,000 B is 4,000 segments
of 136 B). That is what a send pass that comes late on a loaded machine looks like: the tick rents segments the pool
does not have yet, the growth at a new peak recorded under
[the send threads](#results--the-send-threads-875-2026-10-10). It is not the buffers: the tick neither frames nor
rents a burst buffer, and the unchanged side read it more often. On a quiet machine these runs read 0 B ([a client that stops reading](#a-client-that-stops-reading-875-2026-10-10)).

**The tests behind rows 11 and 12.** The frame-ahead tests asserted counts. Weakening the sender showed two things
nothing held: two packets framed ahead of a pending write in the wrong order, and a connection closed twice when a
seal fails and the pending write then fails too. `ConnectionSenderShould` now reads the wire in order across the burst
in flight, the one framed ahead and the one after, with each write's bytes copied when the write ends, and holds one
close. A burst framed over the one in flight, a fault that keeps the next burst and an unsealed packet left counted
were caught already. The send scheduler's comment on its stall clock said "once per tick"; what guarantees a pass is
the thread's timed wake, every 100 ms.

**The gate.** `ScenarioAllocationsShould` reads the committed figures in Debug and in Release, and the baseline did
not move: no scenario sends through `Connection.Send`, drops a stale packet or stalls a connection.

### Before: the outbox flushed on the tick

Until #875 the world's `TickDrivenOutbox` (capacity 100) flushed every connection's packets in the tick's outbox stage,
one connection after the other, on the tick thread. The harness then timed that flush; the results below are that
baseline. The `tls-pool` and `tls-parallel` rows were the prototypes of taking the write off the tick; they allocated
(a `Task.Run` per write, a `Parallel.For` per tick).

#### Results — 200 connections, one packet each per tick (2026-10-09)

i9-12900K. Linux: a container limited to 4 CPUs (Docker on WSL2, Ubuntu 24.04, .NET 10.0.5, workstation GC). Windows
11, .NET 10.0.12, workstation GC. 1800 measured ticks; flush time per tick in ms, and per connection in µs.

| Mode | Linux mean | p50 | p99 | µs/conn | Windows mean | p50 | p99 | µs/conn |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `memory` | 0.067 | 0.063 | 0.159 | 0.33 | 0.063 | 0.053 | 0.186 | 0.31 |
| `tcp` | 0.827 | 0.846 | 1.178 | 4.14 | 2.666 | 2.715 | 3.846 | 13.33 |
| `tls` | 1.899 | 1.919 | 2.538 | 9.50 | 2.782 | 2.812 | 4.244 | 13.91 |
| `tls-pool` | 0.189 | 0.184 | 0.289 | 0.94 | 0.129 | 0.119 | 0.472 | 0.65 |
| `tls-parallel` | 0.716 | 0.710 | 1.130 | 3.58 | 1.068 | 1.012 | 1.928 | 5.34 |

The TLS flush on Linux by connection count (µs per connection): 50, 0.533 ms (10.7); 100, 1.026 ms (10.3); 200,
1.899 ms (9.5); 400, 3.748 ms (9.4). `tls-pool` at 400: 0.361 ms.

- **The write is the cost, not the outbox.** Framing 200 connections' packets into their burst buffers takes 0.07 ms
  a tick; writing them out takes 25 to 40 times that. On Linux about 4 µs per connection is the send call and about
  5 µs the TLS record layer, linear in connections. On the homelab the world talks to its edge proxy over the pod
  network rather than loopback, and the ramp's 4.2 ms after the world update at 200 players is about 20 µs per
  connection: this cost, with pod networking added.
- **The flushers are not.** With nothing changed, the inventory, sheet and ability-amount flushers took 0.8, 8.8 and
  2.6 µs per tick for 200 connections together (a one-off in-process measurement), and the quest flusher's steady
  state is a handful of comparisons per connection.
- **Off the tick, the tick's share falls by a factor of 10.** `tls-pool` leaves the tick 0.9 µs per connection
  (framing and queueing), `tls-parallel` 3.6 µs (the same work on four cores, waited for). Whether the write should
  leave the tick is an open decision (#875): it changes when a packet leaves relative to the tick and puts the TLS
  and socket work on other threads.

#### Results — the seal inside the flush (#875, 2026-10-10)

Since the outbound packets (#875) each outbox seals its packets as it frames them, so this harness's timed flush now
includes one AES-GCM seal per connection per tick, which the 2026-10-09 run above did before queueing, outside the
timed region; in the server that cost moved out of `Create`, in the world update, into the outbox stage
([instrumentation](instrumentation.md#tick-and-instance-time)). Windows 11, .NET 10.0.12, workstation GC, 200
connections, one packet each, 1800 measured ticks; `862042c5` (sealed before queueing) against `724618bb` (sealed in
the flush), two runs each, alternating:

| Mode | Build | mean ms | p50 | p99 | µs/conn | B/tick |
|---|---|---:|---:|---:|---:|---:|
| `memory` | `862042c5` | 0.057 / 0.057 | 0.049 / 0.046 | 0.183 / 0.219 | 0.29 / 0.28 | 0 |
| `memory` | `724618bb` | 0.146 / 0.154 | 0.136 / 0.134 | 0.296 / 0.461 | 0.73 / 0.77 | 0 |
| `tls` | `862042c5` | 3.066 / 3.095 | 3.015 / 3.049 | 4.215 / 4.194 | 15.33 / 15.48 | 0 |
| `tls` | `724618bb` | 3.183 / 3.432 | 3.126 / 3.151 | 4.480 / 7.043 | 15.92 / 17.16 | 0 |

- **The flush now carries the seal**: about 0.46 µs more per connection in `memory`, one seal of a 70 B frame each
  (`SealInto` alone measures about 0.22 µs at 64 B; here 200 sessions' keys take turns in the cache). It is the cost
  `Create` no longer pays, not a new one: the scenario tick, which runs both, got faster (above).
- **Over TLS it is lost in the write**: 15.3-15.5 against 15.9-17.2 µs per connection, the second `724618bb` run
  with a 48 ms outlier tick that also lifts its mean and p99.

### Results — the read loop, per frame (#875, 2026-10-09)

`receive`, 200 connections each sending one frame per tick, 1800 ticks: 360,000 frames read over TLS.
`PacketStream.EnumerateRawFramesAsync` refilled its buffer through `RefillAsync`, which took a delegate to set the
new end of the data (one per read: the lambda captured the iterator's state) and was an `async ValueTask<bool>` (a
state-machine box per read that waited for data, which is nearly every read of a client sending 60 small packets a
second). It now returns the new end, or -1 once the stream ended, and its state machine is pooled
(`PoolingAsyncValueTaskMethodBuilder`).

| | Linux | Windows |
|---|---:|---:|
| Before, B allocated per frame read | 208.1 | 208.2 |
| After | 64.1 | 64.2 |
| After the header struct (#875, 2026-10-10) | 0.0 to 0.1 | 0.0 to 0.1 |

On 2026-10-10 (#875) `NetworkPacketHeader` became a struct, and `InboundPacketFrame.ParseFrame` reads its four
fields by hand rather than through protobuf-net (which allocates for a struct it deserializes too): before, 64.1 B per
frame read on Linux and 64.3 to 64.6 B on Windows; after, 0.0 to 0.1 B on both (two runs each, Linux in a 4-CPU
`dotnet/sdk:10.0` container, .NET 10.0.12). The read loop itself no longer allocates per frame; what is left is the
`Packet` the decrypt and deserialize that follow create (80 B, see
[GC-008](#packet-reader-decrypt-gc-008--benchmark-results)). This is off the tick, on the connection's read loop: at
the ramp's 12,000 packets a second in, the two changes together leave about 2.5 MB/s less garbage.

---

## Scenario baseline

The BenchmarkDotNet suites above measure one call site at a time. The scenario baseline measures whole ticks: a world
server built in process over the real `InstanceRegistry`, instances, input handler, replication and send path, with
scenario players connected, ticked the way the server ticks. It answers two questions a micro-benchmark cannot: how
much the tick thread allocates per tick under a given load, and how long the tick takes. The allocation figure is
gated in CI; the timings and GC figures are reported and never gated.

The scenarios live in `tests/Avalon.World.Testing/Scenarios`, the runner in `tools/Avalon.Scenarios`
([tooling](tooling.md#the-scenario-runner)), and the gate in
`tests/Avalon.Server.World.UnitTests/Performance/ScenarioAllocationsShould.cs`.

### What each scenario models

| Scenario | Instances | Players | What runs every tick |
|---|---:|---:|---|
| `town-idle` | 1 town | 30 | Players standing on a grid of points checked against the town's navmesh. Nothing moves, so this is the floor: what a tick costs when there is nothing to do. |
| `town-walk` | 1 town | 30 | Players walking an 8 m circle centred at (45, 10), the south-east chunk's open square. Each player's input goes through the real `PlayerInputHandler` (one input packet per player, reused every tick), and each player is sent the real `SPlayerStateAckPacket` through the real cipher every tick. Ten engaged "Bench Wolf" creatures (seed 425) share the instance. |
| `town-social` | 1 town | 30 | A town at its cap (`MaxPlayers`, 30) walking `town-walk`'s circle with no creatures, in ten parties of three that keep changing, and chatting, through the real party and chat handlers: in every second a party's leader leaves, is invited back and accepts, one player says a line to the town, one whispers, one speaks to its party and one runs `/ignorelist`. See [The town-social scenario](#the-town-social-scenario) (#972). |
| `many-instances` | 250 normal maps | 500 | Two walking players in each of 250 normal-map instances, every instance created through the real `InstanceRegistry.GetOrCreateNormalInstanceAsync`. It measures many small instances rather than one crowd. |
| `forest-combat` | 4 forests | 12 | A fight. Four forest instances on the fixed seeds 1 to 4, each built as its party's forest is in production, over the World server's reference data (generator, placement, spawn table, depth-band levels and real AI: 200, 269, 173 and 200 creatures, each run on the ticks its script is active or due, as in production since #979), with a party of three level 1 characters in each, joined at the entry; the classes cycle over all four across the roster, so three parties in four carry a healer. Every player is a `LoopFighter`: it walks to the nearest live creature through the real `PlayerInputHandler`, turning away from walls, and in reach casts its class's basic ability through the real `CastAbilityHandler` each time it is ready; a healer heals the most hurt member under 70% first. Kills roll real loot and experience, shared by the party through the real `PartyService`, and credit a kill quest each fighter holds (abandoned and accepted again on completion); fighters pick their drops up through the real `LootPickupHandler` and destroy an item through the real `ItemDestroyHandler` when the bag is near full (#972). See [The forest-combat scenario](#the-forest-combat-scenario). |

No scenario was reduced in size: `many-instances` takes 5 to 7 s for its warm-up and windows, `forest-combat` about
10 s for its rehearsal and run in the gate (since #972; 8 s before).

The walking circle is not centred on the entry spawn (15, 15), where the crowd-budget tests stand their players: an
8 m circle there crosses a building and a wall, and a walker pressed into one stops for good. A half-metre scan of the
town found two adjacent centres whose circle stays a metre clear of every wall, (45, 10) and (45.5, 10).

### The send path is real

Every scenario player sends through a real `WorldConnection` (#972): its `Connection.Send` (the packet's counted size,
the two interlocked counters) and the world connection's `OnQueued` (the `network.out.bytes` and `network.out.packets`
counters), onto its real `ConnectionSender` (#875) and its own real `AvalonCryptoSession` (the platform `AesGcm` since
#850), with no socket: its outbox writes to a stream that only counts bytes. Until #972 a scenario connection called
the sender's `Enqueue` directly; the send call added 0 B to every scenario. The scenario tick runs the send passes on
its own thread after the wake-up, so they count as tick work here. So the per-packet encryption cost, and its
allocations, are in the numbers; the [session cipher results](#session-cipher--benchmark-results) above show what it
costs per call. Three things differ from production, all on the cost side only:

- The counting stream stands in for production's `SslStream`, so the TLS record layer and the socket send are **not**
  in the numbers: [the send path](#the-send-path-875) measures them, 6.4 µs of send-thread time per connection per
  tick over TLS on Linux loopback ([results](#results--the-send-threads-875-2026-10-10)), against 0.3 µs here. The counting stream completes every write at once, as a
  socket with room in its send buffer does; the sender then reads the write's outcome inline (#875), so a pass over
  either allocates nothing on the tick thread.
- Scenario connections share the client's key; each makes its own server key pair, as a connection of a world that
  seals does. The point is the cost of sealing, not the secrecy of the result.
- The `DiagnosticsConfig` counters (bytes and packets sent) are called, but no listener is attached, so a measurement's
  cost is **excluded** from the numbers: with none a counter's `Add` is a check. Production attaches an OpenTelemetry
  listener (`ConnectionSendBenchmarks` measures the least a subscribed counter costs).

Not modelled at all: the session pass (the first of the tick's two passes), the readiness barrier, presence, pings,
continuations, persistence and saves. Of the flushers `WorldServer` runs after the world update, the scenario tick runs
the quest flusher (in a world with reference data, which has the quest service) and the three that need no service, in
its order and before the outbox flush (#875): quests, inventory, character sheet and ability amounts. Nothing a town
scenario does changes an inventory, the stats or the abilities, so there they send nothing; they must allocate nothing
either, and with a closure the inventory flusher allocated on every call (32 B per player per tick, 57,600 B per window
in `town-idle`), which the gate would fail. In `forest-combat` a fight changes them, and they send what they would in
production, the quest flusher's log, updates, lines, markers and Bag recount among them (#972). Combat, quests and the
character economy are modelled by `forest-combat` alone; parties (their tick, and the members' status after the
flushers) by it and by `town-social`, which holds the World server's reference data too, and chat, the command
dispatcher and the chat rate limit by `town-social` alone. Of `World.Update` itself, the scenario tick runs the registry's
publication of finished builds (`InstanceRegistry.PublishFinished`), the parties tick (in `forest-combat` and `town-social`) and
the instance pass, and leaves out:

- `Time.Update`, and the content and script hot reloads.
- `InstanceRegistry.ProcessExpiredInstances`, the last step. Its walk of the registry allocates a 72 B enumerator per
  tick in an unoptimized (Debug) build and, measured, nothing in Release, so it would add nothing to the committed
  figure and would fail `town-idle`'s gate (4,320 B per window over a 0 B baseline) in every local Debug run.
- The map pass's queue: `WorldConnection.ProcessQueue`. `LoopWalker` calls `PlayerInputHandler` directly, and
  `SocialSchedule` the party and chat handlers, so the queue and the per-packet session filter are not in the numbers
  (the input's dispatch is, since #875; see The allocation gate).

### How a scenario is measured

- **Window.** 60 ticks, one second of game time at 60 Hz. Allocations are read with
  `GC.GetAllocatedBytesForCurrentThread` on the tick thread, so work on other threads does not count.
- **Warm-up.** The scenario first ticks for a wall-clock warm-up (5 s in the gate; `--warmup-seconds`, default 10, in
  the runner), so the JIT's tiering, first-use caches and pools have settled.
- **Gated figure.** `bytesPerWindow` is the **minimum** of five consecutive windows after the warm-up. The minimum
  ignores a window that a one-off allocation (a pool growing, a late tier-up) happened to land in.
- **Checked.** After the windows, outside the measured region, each scenario checks that it still did its work
  (`IScenario.Verify`): every player still in its instance; in the walking scenarios every player sent packets and
  its input advanced by one per tick (300) with a change of position; in `town-walk` every wolf still in combat within
  4 m of the player it fights; in `town-social` every event of its schedule done and checked once in every window at
  least. A scenario that stopped walking or fighting would allocate less and pass the gate as an
  improvement, so a failed check throws instead, failing the gate and stopping the runner.
- **Two levels.** `town-walk` and `many-instances` alternated between two window levels from one window to the next;
  the minimum landed on the lower one, and that lower level was the committed figure. Each instance broadcast every
  seventh tick then, so a window held eight or nine broadcasts; since [the broadcast schedule](#results--the-broadcast-schedule-975-2026-10-14)
  it broadcasts every sixth, ten in every window, and both figures are 0 anyway.
- **Timing.** After the windows the runner times `--measure-ticks` ticks (default 3600, one minute at 60 Hz) and
  reports the tick-time distribution, the share of ticks over the 16.7 ms budget, and the collections and GC pause
  time of that phase. The gate skips this phase.
- **A fight has a fixed length.** The rules above are for a steady state, where after the warm-up one window is like
  the next. A fight is not: creatures die, players die and are revived, loot drops, and a wall-clock warm-up would end
  at a different point of the fight on every machine. So a scenario can name a fixed length (`IScenario.Length`;
  `forest-combat`: 600 warm-up ticks, then 3,600 measured ticks) and is then measured another way. First a
  **rehearsal**: the whole run on a world of its own, again on a new world each time, until the wall-clock warm-up has
  passed and two rehearsals in a row allocated the same over their measured ticks, with a 250 ms pause after each for
  the JIT's background promotions to finish, so its tiering, first-use caches and pools settle on the code the fight
  takes; see [Dynamic PGO](#dynamic-pgo). Rehearsals that have not settled 12 runs past the warm-up end the run with
  an error naming the last two figures: an unsettled run is never measured. Then a **fresh world**: a full collection
  first (before its warm-up ticks, so the shared pools are refilled by the warm-up rather than by the measured ticks),
  its warm-up ticks, then its measured ticks, every one counted: the gated `bytesPerWindow` is their **mean** per
  60-tick window, so every kill, death and drop of the minute is in it (the least window would keep only the quietest
  second). The whole measurement runs on a thread of its own, so what a thread keeps for itself (a pool's per-thread
  buffers, the packet encoder's per-thread scratch) starts empty on every run, whatever tests ran before on the
  caller's thread. Its timing is that of the same measured ticks (none with `--measure-ticks 0`). Its check
  (`Verify`) requires every player present and sent to, every fighter casting and
  every forest losing creatures during the measured ticks.

#### Dynamic PGO

The world server runs with dynamic PGO on (the runtime's default), and so do the gate and the runner. The optimised JIT
tier devirtualises an interface call from the profile it gathered at run time and can then keep on the stack the boxed
enumerator, closure, delegate or box the call would have allocated. How much it keeps depends on the profile and on
when a method reached that tier, so an allocation of that kind is a figure that moves: between runs, between machines,
and between Debug (never optimised) and Release. `town-idle` and `town-walk` have none on their path, and `town-social` none since #973 (104 B per window from #972, inside the floor: [a social town](#results--a-social-town-972-2026-10-14), [a social town, quieter](#results--a-social-town-quieter-973-2026-10-15)). `forest-combat` had many
when it was added, all in combat, AI and kill code, and the fight's rarer paths (a kill's drop, an aura's tick) reach
the optimised tier late, so its first figure was not reproducible:

| `forest-combat`, bytes per window, before the fix (`ddfa6db7`) | |
|---|---:|
| Release, PGO on, the runner (Server GC), two runs | 351,215 / 351,215 |
| Release, PGO on, the test process, two runs in one process | 322,415 / 322,412; with an earlier driver, pairs up to 0.3% apart |
| Release, PGO off (`DOTNET_TieredPGO=0`) | 362,459 / 362,459 |
| Debug | 363,733 / 363,733 |

#880 removed the allocations rather than the profile (owner, 2026-10-10: PGO stays on everywhere). Each was found by
diffing the per-tick allocations of two runs, then narrowing with allocation counters around the steps of the tick:

- `CharacterAbilityContainer`: `Update` (every character, every tick), the ability lookup (`Spells[id]`) and
  `IsCasting` walked an `IReadOnlyCollection<IAbility>` through its interface, with a lambda for the last two. They walk
  the loaded collection by index when it is a list (always, in production); this was #880's `Enumerator[GameAbility]`.
- `CreatureRangeDetectorScript`: each idle creature's search (once a second) walked `Context.Characters.Values` as an
  `IEnumerable<ICharacter>`. It walks the instance's own `Dictionary` with its struct enumerator.
- `UnitHitQuery` (every cone swung and every step of every projectile): a new result list, an overlap lambda and a sort
  lambda, both over closures, the boxed enumerators of both dictionaries, and the `Comparison` delegate `List.Sort`
  makes of an `IComparer`. It now walks the concrete dictionaries, tests the shape with a struct, sorts with a
  `Comparison` made once, and fills a list it keeps: every list handed out stays valid until the instance's next tick
  (`Recycle`, at the start of `MapInstance.Update`), so a query asked while another's result is still being walked
  never overwrites it. The order (nearest first, ties by raw guid) is unchanged. Its callers (`AbilityEffect.ApplyToAll`,
  the projectile's step) read the result by index.
- `InstanceAbilityCastSystem.GetAbility` (the state broadcast, for each projectile each viewer is sent): `Find` over a
  lambda on the guid; a plain loop.
- The kill: `LootRoller` walked its tables' lists through their interfaces and found each dropped item with a lambda;
  the drop then walked the placed drops and the connections through interfaces and built the packet's list with
  `Select`. All are loops now. The experience award's level-row and map-template lookups used lambdas; loops.
- A periodic tick whose caster is gone allocated a new empty `ObjectGuid` (a class) per tick; one shared, read-only
  instance. `CastAbilityHandler` tested its two flags with `Enum.HasFlag`, which boxes in unoptimised code; bit tests.

After the fix, bytes per window (developer machine, i9-12900K, Windows 11, .NET 10.0.12, and the Linux container used
for CI's runner size), before the outbound packets of #888 took the send path off the tick thread's bill (see
[The forest-combat scenario](#the-forest-combat-scenario) for the figures since):

| Run | `forest-combat` |
|---|---:|
| Release, PGO on, the runner (Server GC) | 201,643 |
| Release, PGO on, the test process, two runs in one process, five times | 201,643 / 201,643, every time |
| Release, PGO on, the same under load (the World suite running beside it), five times | 201,643 / 201,643, every time |
| Release, PGO off | 201,916 / 201,916 |
| Debug | 202,116 / 202,116 |
| Linux container (4 CPUs, 16 GB), Release, PGO on: the gate and the two-run check, twice | 201,643, and 201,643 / 201,643 |

Like for like, the figure falls by 44% with PGO off (362,459 to 201,916) and by 37% or 43% with PGO on (322,415 in the
test process, 351,215 in the runner, to 201,643): about 120 to 150 KB per window less of the tick thread's garbage.
It is an allocation reduction; no change in tick time was measured. PGO on and off then read within 0.14%, and Debug
0.23% above Release, inside the 1% band.
What PGO still keeps on the stack is spread thin, on paths the rehearsal settles. Before the pause after each
rehearsal, a busy machine could still finish a promotion during the first measured run of a process (201,681 against
201,643 once in three runs under load); with it, none did in five. Should a later change bring back an allocation of
this kind, the two-run check (`ForestCombatScenarioShould`), or the Release gate (CI), shows it: find it by diffing
the per-tick bytes of two runs, as above. One kind neither shows: an allocation the optimised tier keeps off the heap
in code that is compiled once for the process, from the profile its first callers in the process gave it. Every later
caller runs that code, so the figure reads the same twice in one process, and differently in the gate alone and in a
whole suite run, where other tests call first. In a Debug build only library code is optimised, so there it takes code
the whole process shares (a LINQ query's generic body, #890). In Release every World method is compiled that way too,
and the same holds for any of them wherever an interface call has more than one concrete type behind it: four of the
five rows of [what Release keeps off the heap](#results--the-melee-slots-release-890-2026-10-12) rested on the
profile, 273 B per window between them, more than the 256 B floor had all of them turned. All five are gone, taken
out at the source ([the kill path](#results--the-kill-path-890-883-2026-10-13)): Release reads the same figure with
dynamic PGO on and with `DOTNET_TieredPGO=0`, and a Debug build reads it too. To find a new one, diff the per-tick
bytes of the gate alone and of the suite run, and read the Release gate with `DOTNET_TieredPGO=0`: a figure above
the committed one there is an allocation that only the profile keeps off the heap.

### Running it and reading the table

```bash
# Every scenario, keeping the run as this machine's baseline (perf/local/ is ignored by git)
dotnet run -c Release --project tools/Avalon.Scenarios -- --scenario all --write-baseline perf/local/$(hostname).json

# A later run against it: current, baseline and change per figure; never fails
dotnet run -c Release --project tools/Avalon.Scenarios -- --baseline perf/local/$(hostname).json

# One scenario, a shorter warm-up
dotnet run -c Release --project tools/Avalon.Scenarios -- --scenario town-walk --warmup-seconds 5
```

Options: `--scenario <name>|all` (default `all`), `--warmup-seconds` (default 10), `--measure-ticks` (default 3600;
0 skips timing), `--json <file>`, `--baseline <file>`, `--write-baseline <file>`, `--write-allocations <file>`,
`--broadcast-ticks <n>` (default 0; see below). The
runner uses Server GC with concurrent collection, the world server's own settings, and warns when built in Debug.

The table has one row per scenario: `bytes/window` (the gated figure), `B/player/tick` (that figure over 60 ticks and
the player count), tick ms mean, p50, p95, p99 and max, `% > 16.7 ms`, gen0, gen1 and gen2 collections, and GC pause in ms
and as a share of the timed phase. Only `bytes/window` is gated. Everything else depends on the machine and its load,
and compares only against a baseline taken on the same machine.

#### Comparing broadcast schedules (#976)

`TickPhaseBenchmarks` reports means, which spreading the state broadcasts over the ticks does not move; the tail
does. `many-instances` is the scenario for it: 250 instances built together before the first tick, every one ticked
with the same fixed step, no random roll anywhere (its world's seed is the default 0 and nothing in it rolls), so two
builds of it run the same work tick for tick. Compare two variants by the timed run's p50, p95 and p99, each against
the other's file, on one machine:

```bash
# variant A (say main), then variant B (the change) on the same machine, the machine otherwise idle
dotnet run -c Release --project tools/Avalon.Scenarios -- --scenario many-instances --write-baseline perf/local/broadcast-a.json --broadcast-ticks 420
dotnet run -c Release --project tools/Avalon.Scenarios -- --scenario many-instances --baseline perf/local/broadcast-a.json --broadcast-ticks 420
```

`--broadcast-ticks <n>` adds, after the timed runs, a census per scenario: a fresh world ticked `n` times with the
tick trace (`Avalon-World-Tick`, [tick phase traces](tick-profiling.md)) attached in process, counting on each tick
the instances whose record says it broadcast. It is never timed (the trace costs the tick its timestamps, and the
in-process listener boxes every payload). Lockstep reads as every instance on the same ticks; at `1e7d9618` every
scenario reads that way, `many-instances` as 60 ticks of 420 with all 250 instances broadcasting and none in between
(the scenario's step reaches the 0.1 s interval every seventh tick, the 9.1 Hz cadence of
[#974](https://github.com/WoozChucky/Avalon.Server/issues/974)). A broadcast spread over the ticks reads as most
ticks with some, and a per-tick maximum near the instance count over the ticks per interval. With
[the broadcast schedule](#results--the-broadcast-schedule-975-2026-10-14), `many-instances` reads 41 or 42 instances
on 419 ticks of 420 (the first tick has none) and every instance on none.

What the census shows is the phase each instance starts with under a fixed step, every instance built on the same
tick. It does not show the two ways instances fall into step in production: a long tick re-synchronising them, and
slots bunching as instances come and go. Those are the ramp's to judge.

The timed run's tail is what to compare, and it is noisy on a shared machine: three runs of `many-instances` at
`5b3d3adf` on the developer machine, with other builds and tests running beside it, read p50 2.75 to 3.30 ms, p95
4.07 to 5.61 ms and p99 4.74 to 22.38 ms. Take each variant's figure from several runs on an idle machine. With
lockstep, one tick in seven carries every instance's broadcast, so p95 and p99 fall on broadcast ticks and p50 on the
others.

### The allocation gate

`perf/scenario-allocations.json` is the committed baseline: per scenario its `bytesPerWindow` (and, for reading,
`bytesPerPlayerPerTick`), with the commit and date it was generated at. `perf/local/` holds per-machine runner
baselines and is never committed. `ScenarioAllocationsShould` runs every scenario and compares its `bytesPerWindow`
with the committed figure, in every build (Debug and Release, see below) and for every scenario:

- **Fail** when the current figure is more than 1% **and** more than 256 B over the committed one. The 256 B floor
  keeps a scenario that allocates almost nothing (`town-idle`) from failing on one stray object.
- **Improvement notice** (test output, not a failure) when it is more than 1% and more than 256 B under.
- **Detection floor.** `town-idle`, `town-walk` and `many-instances` read 0 since `75a16697` (#875: none of them
  allocates on the tick thread since server packets are built in per-thread messages; the file was regenerated at
  `2ca42aff` for `town-social`'s first entry, #972, and at `490f1785` for #973's chat and party fixes, and
  `forest-combat`'s figure lowered by #980 and #973 between), so the 256 B floor decides in every scenario:
  the gate fails on a rise of more than **256 B per window**, about 4 B per tick for the whole scenario. Any one object
  allocated every tick, anywhere on the measured path, fails every steady scenario (the smallest, 24 B, is 1,440 B
  per window).
  `town-social` reads 728 B per window since #973 (3,904 B from #972), all of it its events' own, one of each kind a
  window ([a social town, quieter](#results--a-social-town-quieter-973-2026-10-15)): the floor decides for it too (1% is
  7 B), so it fails above 984 B, and one more 40 B enumerator on each of its seven events (280 B) fails it. Its Debug
  run reads the same 728 B (104 B above Release from #972 to #973, see [a social town](#results--a-social-town-972-2026-10-14)).
  Before #875 the floors were 0.5 B per player per tick in the moving scenarios, and 40 and 19 B under the 5% band
  before #852.
  `forest-combat` is the largest figure (14,138 B per window since #973 built each party roster once, which its one
  level-up of the minute sends, [a social town, quieter](#results--a-social-town-quieter-973-2026-10-15); 14,176 B from
  #980, which shared the aura updates and reused their entries,
  [shared state and aura encoding](#results--shared-state-and-aura-encoding-980-2026-10-14); 14,292 B from #972,
  which gave the fight a healer, a kill quest and loot, [a fuller fight](#results--a-fuller-fight-972-2026-10-14);
  10,357 B before), and at that size the 256 B floor decides for it too (1% is 141 B): it fails above 14,394 B, a little
  over 4 B per tick for the whole fight, so one 24 B object every five ticks fails it. Its Debug run reads the committed
  figure again since #981 (#973): from #972 it read 68 B above Release, because each item destroy in the measured
  minute paid 48 B in unoptimised code for the two boxes `Enum.HasFlag` made in `InventoryMove.DecideDestroy`.
  Release paid the same while that code was not yet optimised, so a change that moved when it was (#981 did) made
  Release read up to 14,244 B in some runs and `ForestCombatScenarioShould`'s two runs disagree. The item, quest, vendor
  and maintenance rules test their flags with bit tests now, and every build reads the same figure. From #890 to #972 Debug read the committed
  figure exactly (since [the kill path](#results--the-kill-path-890-883-2026-10-13)). Before that a Debug build read 237 B per
  window above the committed 11,543 B (objects on the kill path and in the creature scripts), 19 B less than the
  floor allows, and 305 B above, 49 B more than it allows, until #883 took the closure out of `CancelScriptsOf`.
  For that time the escape hatch below applied to it alone (owner, #890): `forest-combat` was decided in Release (CI)
  and reported, not gated, in Debug. It is gated in every build again (owner, 2026-10-13, #890), like every other
  scenario.
- **Why 1%.** Every run measured after #854 reads within ±0.05% of the committed figures (the table below), and
  CI's runner, before #854, read within about 0.1% of the developer machine once the per-send delegate it alone
  paid was taken out (+1,384 B in `town-walk`, +4,000 B in `many-instances`, see #851's results). 1% is ten times the
  widest of these.
- **Ratchet.** The committed file goes down only by an explicit commit. When a change makes a scenario cheaper,
  regenerate and commit the lower figure, so the gain cannot be lost again unnoticed. When an increase is intended,
  regenerating is also the fix, and the JSON diff shows the reviewer what it costs.

Since #875 each walker's input is dispatched through `PacketDispatchTelemetry`, as `WorldConnection` dispatches a
received packet, so the gate covers the dispatch too; for a high-rate type such as `CMSG_PLAYER_INPUT` it allocates
nothing (it was 248 B per packet with a scope-reading log provider: the dispatch object, the log scope's state and
scope, and the `ExecutionContext` and `OneElementAsyncLocalValueMap` the scope's `AsyncLocal` write creates).

Regenerate (every scenario, in Release; the runner refuses `--write-allocations` for a subset):

```bash
dotnet run -c Release --project tools/Avalon.Scenarios -- --scenario all --write-allocations perf/scenario-allocations.json
```

**A red gate with no code change: check the runtime, then regenerate.** CI installs the latest .NET 10 SDK and runtime
(`dotnet-version: 10.0.x`), and `global.json` lets the SDK roll forward (`rollForward: latestMajor`), so a new runtime
patch can move the figures with no change in this repository. The CI run's summary table names the runtime and OS that measured it; when they differ from the run that
committed the baseline and the code did not change, regenerate the baseline on the new runtime and commit it.

**The gate decides in every build, and every machine reads the same figures.** CI builds Release on a GitHub-hosted
runner; a plain local `dotnet test` builds Debug. Since #854 the committed figures are a plain Release run on the
developer machine, and Debug, Release, Windows and Linux read them within run-to-run noise. Measured at `2fe836e8`
(#852, #859), bytes per window, committed `town-walk` 1,440,240 and `many-instances` 11,589,728:

| Run | `town-walk` | `many-instances` |
|---|---|---|
| Windows, Release, the gate alone (×3) | 1,440,000 / 1,439,760 / 1,440,208 | 11,589,728 / 11,587,728 / 11,593,728 |
| Windows, Debug, the gate alone (×3) | 1,440,208 / 1,440,688 / 1,440,432 | 11,589,728 / 11,589,728 / 11,593,728 |
| Linux container (4 CPUs, 16 GB, CI's runner size), Release, the gate alone (×3) | 1,440,240 / 1,440,000 / 1,440,960 | 11,593,728 / 11,589,728 / 11,589,728 |
| Linux container, Release, the whole solution's `dotnet test` as CI runs it (×2) | 1,440,664 / 1,439,944 | 11,593,728 / 11,589,728 |
| Windows, Release runner, `DOTNET_JitObjectStackAllocation=0` (#854) | 1,439,520 | 11,589,728 |

The CI runs on `main` after #854 passed the gate, but CI's test step does not print a passing test's output, so their
figures are not in the logs, only the verdict. Since #852 the gate writes them to the run's summary page: when
`GITHUB_STEP_SUMMARY` is set (on GitHub Actions), every scenario appends a row, pass or fail, to a "Scenario allocation
gate" table (scenario, committed, measured, change in percent, tolerance, verdict) on the workflow run's Summary tab. A
summary file that cannot be written is skipped with a line in the test output and never fails the gate. Read CI's
figures there.

A Debug build never optimises, so it never keeps an object on the stack; a Release build keeps some there once the
optimised JIT tier applies. That Debug reads the same as Release shows that nothing on the measured path allocates
differently depending on how far the JIT has got, which is what made the CI runner read higher before #854 (below).
**Escape hatch:** should a change make Debug and Release diverge again (an allocation the optimised JIT keeps on the
stack and unoptimised code does not), or should another machine or architecture read differently in Debug (arm64,
for one, has not been measured), the gate goes back to deciding in Release only, as #856 had it: put the
`Regressed` branch of `ScenarioAllocationsShould` under `#if !DEBUG` and write the comparison in Debug instead. A
scenario that stopped doing its work still fails its `Verify` check in every build. The hatch was used once, for
`forest-combat` alone and for a day (#890, 2026-10-12 to 2026-10-13): a Debug build read up to 305 B per window above
Release there, across the 256 B floor. It was closed by removing those allocations at their source rather than by
keeping a second rule ([the kill path](#results--the-kill-path-890-883-2026-10-13)), which is the first thing to try
should it be needed again: an allocation that only optimised code keeps off the heap is also one the JIT's profile
can bring back in Release.

Until #854 the figures depended on the machine, through one object: the delegate each send created by passing
`CryptoSession.Encrypt` as a method group, about 64 B per walking player per tick. At most send sites the optimised JIT
tier kept it on the stack (not everywhere: the Serialization benchmark shows it on the heap, fully optimised, where
`Create` is not inlined); until that tier applied, it was on the heap. The CI runner (a small, shared VM, with the other
test assemblies running in parallel) ended the gate's 5 s warm-up before the optimised tier reached the send path, and a
Debug build never optimises, so both read about 7% higher in `town-walk` and 14% higher in `many-instances` than a
developer machine or the homelab node in Release; the committed figures were CI's, and a faster machine's run was not to
be committed. #854 passes the session's `Encryptor`, a delegate created once with the session, so how far the JIT has
got no longer changes what a send allocates. The gate runs in a non-parallel xUnit collection, so no other test in the
World assembly competes for the CPU during the wall-clock warm-up; other test assemblies still run in parallel processes
under a solution-wide `dotnet test`, and the minimum-of-windows rule absorbs that; a fixed-length scenario
(`forest-combat`) has no minimum to lean on, and relies instead on its settled rehearsal, which waits out a busy machine
before its one measured run. The gate adds about 30 to 40 s to the World
suite (`forest-combat` 7 to 11 s of it since #972, `town-social` about 6 s), and `ForestCombatScenarioShould`, the forest's two-run check, 12 to 17 s more.

### Allocation tests and background collections

Beside the scenario gate, single paths are held at zero bytes by tests that read `GC.GetAllocatedBytesForCurrentThread`
around a loop of calls (`ConnectionTickAllocationShould`, `ReceiveQueueAllocationShould`, `CombatTickAllocationShould`
and others). That counter is not exact across a background collection (#953: `ConnectionTickAllocationShould` read
7,816 B in CI over 1,000 calls that allocate nothing, and `ReceiveQueueAllocationShould` 7,920 B in a local
whole-solution run).

A thread allocates out of a buffer the collector hands it, about 8 KB at a time, and the counter is what the thread was
handed minus what is left of its buffer. A background collection, in the pause that ends its marking, takes every
thread's buffer away without subtracting what was left of it. A thread that allocated nothing then reads up to about
8 KB more, once, and no collection is counted in between: the collection started earlier. A blocking collection takes
the buffers away too, and does subtract.

Measured on runtime 10.0.12: a thread that spins for 5 ms and allocates nothing, 2,000 times, beside a thread that
starts background collections over a 410 MB heap. The figures are the windows that did not read 0 B:

| | Windows 11, 24 processors | Linux container, 4 CPUs |
|---|---|---|
| Background collections (the default) | 60, 72 and 67, of 120 to 8,144 B | 50, of 152 to 8,024 B |
| `DOTNET_gcConcurrent=0` | 0 | 0 |
| Blocking collections only | 0 | 0 |
| Background collections, a blocking generation 0 collection before each window | 0 and 0 | 0 (69 without it, in the same build) |

In the World suite itself (Release, the whole project, eleven runs, 2,000 copies of the lease check of
`ConnectionTickAllocationShould` in each): 15 of 22,000 windows read above 0 B, one 16 B and fourteen 7,800 to
8,088 B, each in one step at one call. None compiled a method on its thread, fourteen counted no collection, and in
all six whose collection events were recorded a background collection was running and its final pause fell inside
the window. With the blocking collection first: 0 of 22,000.

So a test of this kind reads the counter one of two ways:

- **A window that must read zero** starts with `ThreadAllocations.BeforeQuietWindow()`
  (`tests/Avalon.World.Testing`): a blocking generation 0 collection, then the counter. The thread then holds no
  buffer, and gets none while it allocates nothing, so a background collection finds nothing of its to count. The
  window stays exact: one object in 1,000 calls still fails it.
- **A window that allocates a known amount** takes a buffer with its first object, so the test takes the fewest bytes
  of three windows (`WaypointRepathAllocationShould`). An allocation per call is in every window; a background
  collection is in one.

The scenario gate needs neither. The steady scenarios keep the least of five windows. `forest-combat`, one run of 60
windows with no least to lean on, runs alone (its xUnit collection is not parallel) and after two blocking collections;
one background collection in its run would add at most about 136 B per window, under the 256 B floor.

### The town-social scenario

`town-social` (#972) puts a full town through what its players do besides walking: their parties change and they
chat. One town at its cap, 30 players (`MaxPlayers` of the town, and the default where a template sets none), in a
world with the World server's reference data, which it needs for the party service and for level 1 characters of a
class (the classes cycle over all four). They walk `town-walk`'s 8 m circle through the real `PlayerInputHandler`, as
there but with no creatures, since walking players are what a town holds; they are named `Socialaa` to `Socialbd`,
because an invite and a whisper find a player by a name of letters only (`CharacterName.IsValid`), and every name has
the same length, so every line naming one is the same size. At the start they form ten parties of three (players
3k, 3k+1 and 3k+2) through the real `PartyService`.

Every player then runs `SocialSchedule` (`tests/Avalon.World.Testing/Scenarios/SocialSchedule.cs`) from its map update,
as `LoopWalker` does: client packets handed to the real handlers, inside the instance pass and the input's dispatch,
as the forest's casts are, one prepared packet of each kind, so the driver allocates nothing. The chat goes through
the real `ChatMessageHandler`, `CommandDispatcher`, `ChatRateLimiter` and commands, the limit at production's
`Game:ChatMessagesPerMinute` of 10 (the C# default, 0, turns it off), and every listener's ignore check runs
(`Ignoring.Hides`; every ignore list is empty). The schedule repeats every second, one window, and every second holds
one event of each kind, at a fixed tick, period w (second w since the build) choosing who acts:

| Tick of the second | Event | Who | Through |
|---:|---|---|---|
| 0 | The leader leaves; the leadership goes to the longest-standing member, and the party keeps two | Party w mod 10's leader | `PartyLeaveHandler` |
| 10 | A line said to the town, heard by all 30 | Player w mod 30 | `ChatMessageHandler` |
| 20 | The new leader invites the one who left, by name | That party's new leader | `PartyInviteHandler` |
| 30 | A whisper (`/w`) to a player of another party, and its echo | Player (w + 10) mod 30 | `ChatMessageHandler`, `CommandDispatcher`, `WhisperCommand` |
| 40 | The one who left accepts, and joins last | The leader who left | `PartyInviteResponseHandler` |
| 45 | `/ignorelist`, the cheap command: no database and no state, one constant line back while the list is empty | Player (w + 5) mod 30 | `ChatMessageHandler`, `CommandDispatcher`, `IgnoreListCommand` |
| 50 | A party line (`/p`), heard by the three members | Player (w + 20) mod 30 | `ChatMessageHandler`, `CommandDispatcher`, `PartyChatCommand` |

So each player leaves its party, is invited back and accepts once every 30 s (the one who joined last leads last and
leaves last, so the three take turns), chats every 10 s (said, party and whisper in turn, six lines a minute, inside
the limit, so none is refused), and runs the command every 30 s, which the limit does not count.

**Why the parties never break up.** The gated figure is the least of five windows, and the windows start wherever the
wall-clock warm-up ends, so every 60 ticks in a row must hold the same events, made by players in the same state, or
the least window would keep only the cheapest second. Ten parties of three, each player changing party every 30 s,
make one leave, one invite and one accept a second; a cycle that formed a party and disbanded it (two invites, two
accepts, two leaves) would make six different events every three seconds, which no window of one second can hold
alike. A party that hands its leadership round, by contrast, does the same three events every second. So no party is
formed or disbanded during the windows: those two paths (a new `Party` and its log lines, every member's empty roster
and line) are not in the gate.

**Checks.** Each event is checked by the driver as it is handed in, by what it changed, without allocating: the party
left with its new leader, the invite sent to the one invited, the party whole again, a said line sent to every player,
a whisper to its target and its echo to the sender, a party line to each member, and the command run (counted around
`IgnoreListCommand`, since "Unknown command." is a line too) and answered. `Verify` requires every player present, sent
to and walking, no failed check, and each of the seven events at least as many times as there are measured windows
(the schedule puts each event once in every window). Weakened in a scratch copy, it fails: a whisper sent to the sender's own name ("player 10 (Socialak) whispered, and the line did not
reach its target with its echo back, at tick 30"), and a schedule with no party line ("0 party lines delivered during
the 5 measured windows").

### Results — a social town (#972, 2026-10-14)

`town-social` is committed at **3,904 B per window** (2.17 B per player per tick), the runner's Release run at
`2ca42aff` that wrote it (developer machine, i9-12900K, Windows 11, .NET 10.0.13, other agents building and testing
beside it). Every other figure stayed where it was: `forest-combat` 14,292 B, the others 0. The figure is the seven
events of one second and nothing else: over 30 consecutive windows after a 5 s warm-up (a probe on the scenario's
world, the first window starting 22 ticks into the schedule's second), every window held one of each event and read
3,904 B, and 1,590 of the 1,800 ticks read 0 B, the other 210 being exactly the seven event ticks of each window. So
no tick allocates without an event, and the least window, the gated one, holds them all.

Bytes per event, read on its tick (the same in every window; Release with dynamic PGO on, and Debug, which reads the
same as Release with `DOTNET_TieredPGO=0` or `DOTNET_JitObjectStackAllocation=0`):

| Event | Release | Debug | What allocates (Debug) |
|---|---:|---:|---|
| A leader leaves | 1,184 | 1,248 | Two rosters to the two left, after the handover and after the leave (424 B each: per recipient a new `List<PartyMemberDto>` and a DTO per member, and an enumerator); the lines "X left the party." (72 B) and "X is now the party leader." (88 B); `Party.Remove`'s closure and predicate (88 B); the leaver's empty roster's new empty list (32 B); three boxed enumerators over `Party.Members` (120 B) |
| An invite | 104 | 104 | The `PartyInvite` (64 B) and the target's lookup key (40 B, `CharacterName.LookupKey`) |
| The accept | 960 | 1,000 | The roster to three (760 B: three lists and nine DTOs, and enumerators); the `PartyMember` and its `CharacterId` (80 B); "X joined the party." (80 B); two boxed enumerators over `Party.Members` (80 B), one of which Release keeps on the stack |
| A line said to the town | 40 | 40 | The boxed enumerator over `MapInstance.Connections` |
| A whisper (`/w`, 7 words) | 872 | 872 | The dispatcher's arguments (312 B: an array and a string per word); the text after the command word made twice (192 B: a slice, then `Trim`'s copy) and split again (192 B: the name, then the rest made twice); three lookup keys (120 B: `CharacterName.Same` makes two, `OnlineCharacters.ByName` one); the `CommandContext` (56 B) |
| A party line (`/p`, 9 words) | 688 | 688 | The dispatcher's arguments (384 B); the text after the command word made twice (208 B); the `CommandContext` (56 B); the boxed enumerator over `Party.Members` (40 B) |
| `/ignorelist` | 56 | 56 | The `CommandContext` |
| **A window** | **3,904** | **4,008** | |

A whisper and a party line are the dearest chat by far, and both grow with the message: the dispatcher splits every
command's text into an array of new strings before it runs the command, and neither `/p` nor `/w` reads them (each
reads its text from the packet, `CommandText`), so a long `/p` of short words pays a string per word for nothing. The
party events cost what their rosters cost: each roster is built anew for each recipient.

**Debug and Release.** Debug reads 104 B per window above Release: 64 B on a leave and 40 B on the accept, which
optimised code keeps on the stack with the dynamic profile (Release with `DOTNET_TieredPGO=0` reads Debug's 4,008 B).
The accept's is a boxed enumerator over `Party.Members`, kept by the runtime's conditional escape analysis
(`DOTNET_JitObjectStackAllocationConditionalEscape=0` puts it back alone); the leave's was not traced to its object. An
allocation that only the profile keeps off the heap can read differently in the gate alone and in a whole suite run
([Dynamic PGO](#dynamic-pgo)); here both readings are inside the 256 B floor.

**Stability.** The runner, five runs in Release: 3,904 B every time. The gate alone, five runs in each build: 3,904 B
in Release and 4,008 B in Debug every time (+2.66%, +104 B, within), and 4,008 B in Release with dynamic PGO off. A
spread of 0 B within each build.

**Findings, per event, none on a per-tick path** (production code is unchanged here; each looks avoidable, and #973
took them all out: [a social town, quieter](#results--a-social-town-quieter-973-2026-10-15)):

- `CommandDispatcher.Dispatch` builds the arguments (`Arguments`: an array and a string per word) for every command it
  runs, and `/p` and `/w` never read them: 384 B per 9-word party line and 312 B per 7-word whisper here, more for a
  longer message.
- `CommandText.AfterCommandWord` slices the text and then trims the slice into a second copy, and `FirstWord` does the
  same for the rest after the name: a party line's text is made twice (104 B here), a whisper's three times (96 + 96 B,
  then 80 + 72 B for the rest).
- `CharacterName.LookupKey` makes a new upper-case string per lookup: in `OnlineCharacters.TryIdByName` (an invite, a
  whisper: 40 B each) and twice in `CharacterName.Same` (the whisper's self check: 80 B). A comparison over spans,
  or an alternate lookup with an ignore-case comparer, would make none.
- `PartyService.SendRoster` builds a new `List<PartyMemberDto>` and a new DTO per member for every recipient (n lists
  and n² DTOs a roster: 760 B for three, 424 B for two), and a leader's leave sends the roster twice (after the
  handover, then after the leave). `SPartyRosterPacket.Empty` makes a new empty list for every empty roster (32 B).
- `Party.Remove` uses `RemoveAll` over a lambda capturing the id: a closure and a delegate, 88 B per leave or kick.
- Boxed enumerators, 40 B each: `ChatMessageHandler` walks `MapInstance.Connections` (an `IEnumerable` over a
  dictionary's values) per said line; `PartyChatCommand` and `PartyService` (`SendLine` to a party, `SendRoster` once
  and once per recipient, `ResetStatuses`, `HandOverLeadership`) walk `Party.Members` as an `IReadOnlyList`.

Not counted as findings: the state an event creates (the `PartyInvite`, the `PartyMember` and its `CharacterId`), the
`CommandContext` a command runs with (#883), and the system lines, which name a member and so are a string each.

### Results — a social town, quieter (#973, 2026-10-15)

#973 rows 22 to 28 took out every finding above, and with them the same costs in their neighbours. What each event
still allocates is its own state and its own words:

- **Command text** (rows 22, 23). A command may declare `ICommand.ReadsRawText`: it reads its text from the message as
  typed (`CommandText`) and never its arguments, and the dispatcher hands it an empty array rather than a string per
  word. `/p` and `/w` declare it; every other command gets its arguments as before. `CommandText` returns slices of the
  message, trimmed in place, so `/p` and `/w` make one string, the text they send on, when they send it.
- **Names** (row 24). `CharacterName.Same` compares two names in place, as their keys compare (ASCII letters in any
  case, every other character as it is), and `OnlineCharacters` holds the names under `CharacterName.KeyComparer` and
  looks a typed name up by a trimmed slice (the dictionary's alternate lookup): no key is made. The key the database
  stores is unchanged, and so is the name rule (#944).
- **Rosters** (rows 25, 26). `PartyService.SendRoster` fills one list of reused entries once per roster and sets only
  `SameInstance` for each recipient; `SPartyRosterPacket.Create` encodes before it returns, and the encoder's scratch
  lets go of the list, so it is free for the next recipient. A handover's roster is the only one a leader's leave or
  going offline sends (one packet fewer to each member; the last roster a client holds is the same). An empty roster
  passes no list: the scratch message's null members encode as the empty list did, nothing (both payloads compared
  byte for byte).
- **Closures and boxes** (rows 27, 28). `Party.Remove`, and in the same name lookups `IgnoreList.FindByName` and
  `IgnoreList.Remove`, walk by index (88 B a call each before); `Party.Members` is walked by index in `PartyService` and
  `/p`, and `Disband` without its copy; `MapInstance.Connections` returns the dictionary's read-only `ValueCollection`,
  so a said line's walk takes the struct enumerator.

Bytes per event, read on its tick as in [a social town](#results--a-social-town-972-2026-10-14) (a probe on the
scenario's world: 30 windows after a 5 s warm-up, every window the same, Release and Debug alike):

| Event | Before, Release | Before, Debug | After | What is left |
|---|---:|---:|---:|---|
| A leader leaves | 1,184 | 1,248 | 160 | "X left the party." (72 B) and "X is now the party leader." (88 B) |
| An invite | 104 | 104 | 64 | The `PartyInvite` |
| The accept | 960 | 1,000 | 160 | The `PartyMember` and its `CharacterId` (80 B), "X joined the party." (80 B) |
| A line said to the town | 40 | 40 | 0 | |
| A whisper (`/w`, 7 words) | 872 | 872 | 128 | The `CommandContext` (56 B) and the text sent on (72 B) |
| A party line (`/p`, 9 words) | 688 | 688 | 160 | The `CommandContext` (56 B) and the text sent on (104 B) |
| `/ignorelist` | 56 | 56 | 56 | The `CommandContext` |
| **A window** | **3,904** | **4,008** | **728** | |

1,620 of the 1,800 probed ticks read 0 B, the other 180 the six event ticks of each window that still allocate. Debug
now reads Release's figure: the leave's delegate and the accept's enumerator, which only the optimised code kept off
the heap, are gone. Whispers and party lines no longer grow with their number of words, only with the text itself.

**The gate.** `town-social` is committed at **728 B per window** (0.40 B per player per tick), from 3,904 B, and
`forest-combat` at **14,138 B**, from 14,176 B (Debug 14,206 B, from 14,244 B): the forest's one level-up in the
measured minute, a whole party at one tick (tick 3,422 of 3,600), sends the roster of three once per member, and each
no longer builds three lists and nine entries (3 × 760 B = 2,280 B over 60 windows, 38 B a window). `town-idle`,
`town-walk` and `many-instances` stay at 0. The runner's Release run at `490f1785` wrote the file (developer machine,
i9-12900K, Windows 11, .NET 10.0.13, other agents building and testing beside it); the Release runner at `main`
(`c83cbd59`) read `forest-combat` 14,176 B on the same machine, the committed figure, so the 38 B are this change's.
**Stability.** The runner, two runs in Release: 728 B and 14,138 B both times. The gate alone, three runs in Release: 728 B and 14,138 B every time; three in Debug: 728 B and 14,206 B every time (+0.48%, +68 B, within). A spread of 0 B within each build.

**Timing**, `Benchmarks/SocialEventBenchmarks.cs` (#973): one of each event as the scenario makes it, through the real
handlers, with the send pass after each (framing and sealing every packet), on a world of 30 in ten parties of three,
the chat rate limit off so every line is delivered. Three runs each at `c83cbd59`'s production code and after, on the
developer machine with other agents building and testing beside it, so the times are ranges; the bytes are exact.

| Benchmark | Before | After | Allocated before | Allocated after |
|---|---|---|---:|---:|
| `SaidLine` (30 hear it) | 12.0–12.2 µs | 11.8–12.0 µs | 40 B | 0 B |
| `Whisper` | 1.49–1.50 µs | 1.29–1.34 µs | 872 B | 128 B |
| `PartyLine` | 1.67–1.70 µs | 1.49–1.55 µs | 688 B | 160 B |
| `LeaveInviteAccept` (three events) | 11.2–11.7 µs | 9.7–10.2 µs | 2,248 B | 384 B |

A fourth run after the change, taken while other builds ran, read all four higher (15.1, 2.32, 1.77 and 10.1 µs); it
is left out of the ranges, with the same bytes. A said line is the sends' cost, 30 packets sealed and framed, and does
not move; a whisper and a party line are 0.15 to 0.2 µs (10 to 13%) faster, and a party turn about 1.5 µs (13%), for
the strings, lists and entries they no longer make.

### The forest-combat scenario

`forest-combat` (B2 of the load-test combat work) puts the tick through a fight: four forest instances on the fixed
seeds 1 to 4, built through the World server's own `ChunkLayoutInstanceFactory` over its reference data
(`ScenarioReferenceData`: the `HasData` seed in an in-memory SQLite `WorldDbContext`, read by the production
repositories; the script manager loaded, so creatures fight with their real AI and casts run their real ability
scripts), each as its party's forest (`GetOrCreatePartyInstanceAsync`). In each, a party of three level 1 characters
built as character creation and select build them (the class's level 1 stats and starting abilities), formed through
the real `PartyService` and joined at the forest's entry (15, 1, 15). The harness cannot transfer a player between
instances, so players start in the forest. Since #972 the classes cycle over all four across the roster rather than per
party slot (warrior, wizard, hunter; healer, warrior, wizard; hunter, healer, warrior; wizard, hunter, healer), as the
load test's parties of three do, so three parties in four carry a healer. Each character starts with 28 of its 30 bag
slots holding a piece of forest armour (item 12, which does not stack), added through its inventory service: a level 1
bag fills far slower than a minute's fight, and this stands for a character that has played a while, so the item
destroy path runs in every minute.

Each player is a `LoopFighter` (`tests/Avalon.World.Testing/Scenarios/LoopFighter.cs`), the load-test fighter's
decisions over the instance's own state rather than the world-state packets: every tick it finds the nearest live
creature among the forest's creatures, steers at it with one `CPlayerInputPacket` through `PlayerInputHandler`
(bump and turn: a step that asked to move and moved under 0.1 m/s met a wall, and the fighter turns 90 to 270 degrees
away, from a seeded random, for a second), and within reach stands, faces it and casts its class's basic ability
through `CastAbilityHandler` when the ability and the global cooldown are ready (Cleave 200 along the facing; Arcane
Bolt 210, Quick Shot 220 and Smite 230 at the creature's position). It stops `min(1 m, 0.2 × reach)` inside the
ability's reach, the load-test fighter's rule: a warrior stops at 2 m, outside the 1.5 m a creature keeps from what it
fights. Since #972 it also does, every step, in this order:

- **Quest.** It holds the scenarios' kill quest (9001: one stage, kill two Thornback Boars and a Grey Fen Wolf, the
  forests' commonest kills; written by `ScenarioReferenceData` to its in-memory database only, never to the World
  seed). It accepts it at its first step, and once the quest is ready to hand in it abandons it through the real
  `QuestAbandonHandler` and accepts it again (no turn-in). The accept goes through `QuestService.StartFromItem`,
  answered by `QuestReplies` as the accept handler answers: `QuestAcceptHandler`'s `QuestService.Accept` requires an
  open conversation with the quest's giver within the dialogue leash, and no forest holds a quest giver.
  `StartFromItem` runs the same start (availability, stage 0, the line, the hooks, the settle) without the
  conversation check and without re-sending the giver's dialogue. The kill credit is the instance's own
  (`QuestService.CreatureKilled` on every kill the party shares); the scenario tick runs `QuestFlusher` as
  `WorldServer` does.
- **Bag.** With two free bag slots or fewer, it destroys the lowest occupied slot through the real
  `ItemDestroyHandler`, so a pickup is never refused for a full bag.
- **Loot.** It walks to the nearest drop it may take now (reserved for it, or free for all by now) within 26 m, a
  little past the longest basic reach, and within 3.5 m stands and asks for it through the real `LootPickupHandler`.
  A drop it walks at for 10 s, or one refused, is given up (none is, in the committed run).
- **Heal.** A healer casts Mending Circle (232: an ally circle at the aim point, 15 m reach, 8 s cooldown, 25 mana,
  about 65 health at level 1) at the feet of the most hurt living member under 70% of its health within reach, when
  the heal and the global cooldown are ready and it has the mana, walking or standing, through the same
  `CastAbilityHandler`; otherwise it fights. A member under 70% has lost at least 30%, and the heal restores a
  quarter of a warrior's health to a little over half a wizard's, so most of it lands. The heal goes through the
  combat service as a hit does, with heal threat on every creature fighting the healed (#961).

**Death.** Level 1 characters die to packs. A dead fighter sends nothing (the input handler drops a dead character's
input); a client would respawn in town and walk back in, which the harness cannot do. After 10 s (600 ticks) it is
revived at the forest's entry through the instance's own `ICombatService.RevivePlayer` (the dead flag cleared, a
quarter of its health, the revive broadcast). So each forest keeps three fighters for the whole run.

**What one run does.** Every run, on every machine, fights the same fight (the layouts, placement, combat and loot
rolls and the fighters' turns are seeded, item instance ids are handed out in order, and every timer reads the
world's clock). Over the 4,200 ticks since #972: 105 creatures killed (93 in the measured minute), 2 deaths, 18 heals
cast and landed (16 in the minute), 36 quest accepts, 93 kill credits (78) and 24 completions (all 24 in the minute),
198 drops picked up (180) and 106 bag slots destroyed (85, the first 21 in the first steps, from the filled bags), and
no drop given up. Before #972 the run killed 71 (60 in the minute) with 9 deaths: the healers keep their parties alive,
and fighters walking to their drops meet more creatures. Experience is shared; one party's three members reach level 2
together near the end of the measured minute (tick 3,422 of 3,600, counted at #973), and no other character does. The
scenario's check (`Verify`) requires, over the measured ticks, besides every fighter casting and every forest losing
creatures, heals landed, kill credit, a completion, pickups and items destroyed. `ForestCombatScenarioShould` runs the
scenario twice in one process and requires the same outcome (every count above) and the same bytes per window, and
kills and deaths in it; the second run's creatures carry higher ids (the process-wide object id counter), which
changes neither.

**Gate.** Committed at **14,138 B per window** (19.6 B per player per tick, 236 B per tick) since #973, 38 B under the
14,176 B committed since #980: the fight's one level-up, of a whole party at one tick, sends its roster three times
(one per member levelling), and each roster no longer builds three lists and nine entries, 760 B, 2,280 B over the
run's 60 windows ([a social town, quieter](#results--a-social-town-quieter-973-2026-10-15)). The 14,176 B were 116 B
under the 14,292 B committed since #972 ([shared state and aura encoding](#results--shared-state-and-aura-encoding-980-2026-10-14)).
The 14,292 B were the sum of
10,361 B (the fight before #972: the file held 10,357 B, and the 4 B are #898's roster lists growing to their size
once during the run, see [aura scans](#aura-scans--benchmarksaurascanbenchmarkscs-898)) and the increments of [a fuller fight](#results--a-fuller-fight-972-2026-10-14): the real send
call 0 B, the healer +1,118 B, the kill quest +1,457 B and loot +1,356 B. A hard gate like the town scenarios: see
[The allocation gate](#the-allocation-gate) for the floor. Before #972 it was committed at **10,357 B per window**
(14.4 B per player per tick, 173 B per tick), recorded on the developer machine, Release,
dynamic PGO on, after #880's allocations were taken out of the combat and AI paths ([Dynamic PGO](#dynamic-pgo)), #888
took the send path off the tick thread's bill, #875 encoded each broadcast once
([shared broadcasts](#results--shared-broadcasts-875-2026-10-10)), #890 took the query out of the melee slots'
release ([the melee slots' release](#results--the-melee-slots-release-890-2026-10-12)), #925 and #883 took most
of a cast's script build, and its log lines, off the heap
([the cast path](#results--the-cast-path-925-883-2026-10-13)), #890 and #883 took a kill's lists, closures and
delegates with them ([the kill path](#results--the-kill-path-890-883-2026-10-13)), and #883 took the timer object
out of each corpse's wait
([the corpse remover](#gameplay-requests-quests-aura-hooks-and-corpses--the-allocation-tests-benchmarksencounterlookupbenchmarkscs-883)).
The same figure every run: at
16,252, five two-run checks in Release on the developer machine, and the gate and the check in a Linux container
(4 CPUs, 16 GB); at 16,244, three runner runs, the run that wrote it and the Release gate on the developer machine; at
15,635, two runner runs, the second the one that wrote it, three runs of the Release gate alone and three of the whole
World suite in Release, the two-run check in each of those, all on the developer machine; at 11,543, two runner
runs, the second the one that wrote it, three runs of the Release gate alone and one of the whole World suite in
Release, on the developer machine; at 10,385, two runner runs, the second the one that wrote it, three runs of the
Release gate alone, three of the Debug gate alone, two of the Release gate with dynamic PGO off, and one of the
whole World suite in each of Release and Debug, on the developer machine; at 10,357, the runner run that wrote it,
three runs of the Release gate alone, two of the Debug gate alone, one of the Release gate with dynamic PGO off, and
the whole World suite in Debug, on the developer machine. No Linux run was made at 15,635, at 11,543, at 10,385 or
at 10,357: CI decides on Linux, and its figure is to be read from the first CI run's "Scenario allocation gate"
summary.

| `forest-combat`, bytes per window | |
|---|---:|
| First committed (PGO off, before #880's fix) | 362,459 |
| After #880's fix (PGO on) | 201,643 |
| After #888's outbound packets (PGO on) | 16,252 |
| After shared broadcasts (#875, PGO on) | 16,244 |
| After the melee slots' release (#890, PGO on) | 15,635 |
| After the cast path (#925, #883, PGO on) | 11,543 |
| After the kill path (#890, #883, PGO on) | 10,385 |
| After the corpse remover (#883, PGO on) | 10,357 |
| Release with PGO off, then | 10,357 |
| Debug, then | 10,357 |
| A fuller fight: healer, kill quest, loot (#972, PGO on) | 14,292 |
| Debug, then | 14,360 |
| Aura updates shared, their entries reused (#980, PGO on) | 14,176 |
| Debug, then | 14,244 |
| Each party roster built once: the level-up's roster (#973, PGO on), now | 14,138 |
| Debug, now | 14,206 |

No tick-time change is claimed for the first two steps: tick time was not measured like for like. For the third, it
was, and none was measurable ([shared broadcasts](#results--shared-broadcasts-875-2026-10-10)).

Four forests of about 840 creatures and twelve fighters cost about as much per tick as `town-walk`'s 30 walkers: most
creatures are far from any player, and an idle creature's AI looks for players once a second rather than every tick.
The minute allocated 0.62 MB on the tick thread before #972, and 0.86 MB since.

**What dominates its allocations.** The send path costs the tick thread nothing since #888, once its payload pool
holds its free segments in a stack under a lock per size class. It held them in a `ConcurrentQueue` per size: after a
burst, it kept allocating new, larger internal segments with no new peak until its free segments sat in one; those
allocations landed in later ticks, so the fight's figure depended on what ran in the process before. A stack allocates
only at a new peak (`PayloadSegmentPoolShould`: 20,000 segments left free, then 64,000 taken and given back, cost
524,544 B on the queue, one 32,768-slot internal segment, a one-off, and 0 B on the stack).

What the lock costs (the world-performance review's measurements, and `PacketEncoderBenchmarks` before and after on the
developer machine, i9-12900K, Windows 11, .NET 10.0.12, Release, BenchmarkDotNet defaults):

- **Rent and return, uncontended:** 33.5 ns with the lock against 17.8 ns with the `ConcurrentQueue`, about 16 ns more
  per packet.
- **`OutboxFlush` harness, 500 connections × 4 packets:** 0.794 / 0.803 / 0.817 ms per flush with the lock against
  0.808 / 0.790 ms on main, within noise, and 0 B per tick on both.
- **`PacketEncoderBenchmarks`, mean, lock against queue:** encode a state update 209.4 against 197.4 ns (1 entity) and
  3,298 against 3,275 ns (30); encode, seal and frame 495.6 against 459.2 ns and 3,642 against 3,710 ns; encode and
  frame plain 224.4 against 212.5 ns and 3,296 against 3,449 ns. About 12 to 36 ns more for one packet, within noise
  for 30, and 0 B allocated in every case.

Rent runs on the tick and, since #875, the send threads give segments back from their own threads; a send pass returns
them with one lock per size class ([the payload pool under the send threads](#the-payload-pool-under-the-send-threads-875-2026-10-10)). The lock-free alternatives allocate
(`ConcurrentStack` creates a node per push), which is what the pool exists to avoid.

Of the 938 KB a measured minute allocated at 15,635 B per window, before #925, measured with allocation counters
around the steps of the tick (Release):

| Share | Where |
|---:|---|
| 54% | The players' casts (`CastAbilityHandler`). Most of it is building each cast's ability script: `InstanceAbilityCastSystem.Build` calls `ActivatorUtilities.CreateInstance` per cast, which costs its argument array, the boxed aim, the ability's clone and the script itself (about 700 B a cast). Each cast packet's id also becomes a new `AbilityId` (24 B). |
| 30% | The creatures' AI scripts, their own casts' scripts (built the same way) among it |
| 16% | The cast system's tick: projectiles in flight, their hit queries and effects |
| <1% | Corpse removal (about 60 B a corpse) |
| <1% | Broadcasts and auras |

The per-cast script construction was the next thing to take out, and #925 took out nearly half of it (3,498 B per
window of 7,322), and the cast's log lines with it: 4,092 B per window in all, 26% of the minute
([the cast path](#results--the-cast-path-925-883-2026-10-13)). The "about 700 B a cast" above was the whole build,
488 B for a circle or a cone and 584 B for a projectile, and the lines logged around it; a build is 224 B and 320 B
now, 229,440 B of the 692,580 B the minute allocated then, 33%, and of the 623,100 B it allocates since
[the kill path](#results--the-kill-path-890-883-2026-10-13), 37%.

Before #880's fix, a sampled allocation profile of the
measured ticks (the runtime's `GCAllocationTick` events, about 800 samples over four runs, PGO off; read by type, so a
share is approximate) read:

| Share | Type | Where |
|---:|---|---|
| 30% | `byte[]` | sealed packet payloads, and serialization buffers |
| 11% | `NetworkPacket` | one per packet sent |
| 11% | `Dictionary<ObjectGuid, ICharacter>` enumerator, boxed | `CreatureRangeDetectorScript.Update`: each idle creature walks `Context.Characters.Values` as an `IEnumerable<ICharacter>` at every search (once a second); and `UnitHitQuery.Collect` (below) |
| 8% | `SPlayerStateAckPacket` | one per input, as in the walking scenarios |
| 7% | `List<IAbility>` enumerator, boxed | `CharacterAbilityContainer.Update`: `foreach` over an `IReadOnlyCollection<IAbility>`, every character, every tick |
| 4% each | `Func<IUnit, bool>`, `Comparison<IUnit>` | `UnitHitQuery`: each hit query (a cone swung, a projectile's step) builds its overlap test and its distance sort as delegates over closures |
| 3.5% | `Dictionary<ObjectGuid, ICreature>` enumerator, boxed | `UnitHitQuery.Collect`: it walks the instance's characters and creatures through `IReadOnlyDictionary`, per query |
| 7% | closures (`<>c__DisplayClass*`) | those of the hit queries and of `GetAbility` (next row) among them; the profile names a closure class without its declaring type |
| 3% | `Predicate<ActiveScript>` | `InstanceAbilityCastSystem.GetAbility`: `Find(a => a.Script.Guid == guid)`, from the state broadcast, for each projectile each viewer is sent |
| 2% | `List<IUnit>` | `UnitHitQuery`: a new list of hits per query |

Of #880's suspects (its section 2), this resolves the boxed ability enumerator (#880 traced it as
`Enumerator[GameAbility]`: production loads a `List<GameAbility>` into the same `IReadOnlyCollection<IAbility>`, the
scenario a `List<IAbility>`) and the closures on the combat path this scenario reaches (#880's `<>c__DisplayClass3_1`
and `147_0` cannot be matched by name from a profile; any left on paths the forest does not take stay open). It does
not touch `MapId`, which no forest tick allocates, nor the network-layer items. The driver itself allocates nothing: it
holds its packets and its ability, and reads the ability's cooldown itself rather than asking the character.

### Results — visibility on the broadcast cadence (#981, 2026-10-15)

Every tick, every character rescanned every creature, character and projectile of its instance for visibility and ran
three tracking passes, while state goes out on the instance's broadcast tick alone, one tick in six. The 1,300-player
profile read 5.25 ms of a traced tick for it, the town part growing with players per town squared (#1001, sections 4
and 9 D1). Since #981 (the owner's decision; see [world simulation](world-simulation.md)) an instance decides its
clients' views on its broadcast tick, right before the broadcast, and holds them on the five ticks between: an object
is listed or removed up to one period (100 ms) later. An arriving client still scans on its first tick, and a
character that left is still removed on the next tick; both are rare next to the ticks they save.

What one scan costs is unchanged: **`InterestVisibilityBenchmarks`** (one `CharacterCharacterGameState.Update` over 200
creatures; `--inProcess`, short job, ten warmups, eight iterations; two runs each, before on the commit that adds `forest-3` to
`TickPhaseBenchmarks`, after on the cadence change):

| Layout | Before | After | Allocated |
|---|---:|---:|---:|
| dense | 2.48–2.79 µs | 2.62–2.67 µs | 0 B |
| margin | 2.47–2.68 µs | 2.56–3.41 µs | 0 B |
| sparse | 0.93–1.02 µs | 0.92–0.99 µs | 0 B |

The 3.41 µs is one noisy run (its error was ±0.99 µs). What changes is how many scans a tick runs: a sixth.

**`TickPhaseBenchmarks`** (the tick, 42 ticks per operation; `--inProcess`, short job, ten warmups, eight iterations;
three runs each, interleaved after, before, after, before, after, the before build run from a copy of its binaries;
`forest-3` is new with this change, forests of three players standing at the entry with seed 1's creatures and AI; the
machine otherwise idle, but the second round's errors were 30% to 50% of its means):

| Players | Layout | Before | After | Allocated before | Allocated after |
|---:|---|---:|---:|---:|---:|
| 300 | town-1 | 2,099–4,750 µs | 724–1,190 µs | 120,710–120,724 B | 120,692–120,698 B |
| 300 | town-20 | 204–274 µs | 108–287 µs | 3–4 B | 2–4 B |
| 300 | forest-3 | 1,283–1,454 µs | 495–998 µs | 0–33 B | 0–15 B |
| 600 | town-1 | 8,491–9,831 µs | 3,052–4,677 µs | 2,583,612–2,583,644 B | 2,583,471–2,583,480 B |
| 600 | town-20 | 671–1,589 µs | 341–649 µs | 6–33 B | 4–14 B |
| 600 | forest-3 | 2,964–4,705 µs | 984–2,297 µs | 50–58 B | 19–38 B |

The crowded town (one instance, every player seeing every other, all moving) loses half to two thirds of its tick, as
the scan and the tracking were most of the ticks between broadcasts. The forests lose a third to two thirds: each of the
three players checked about 200 creatures every tick. Twenty towns of 15 or 30 read the same or up to half less; their
ticks are short and the runs disagree. Allocation per operation is the same: what the crowded towns allocate is the
payload segments of their broadcasts, which are unchanged.

**The allocation gate does not move**: `forest-combat` 14,176 B in Release, `town-social` 3,904 B (4,008 B in Debug),
the others 0 B, as before. The change did move when the item rules' `Enum.HasFlag` was optimised, so Release read up to
14,244 B in some runs, the 68 B those boxes cost while unoptimised; with bit tests in their place (#973), six Release
runs and two Debug runs of the gate and `ForestCombatScenarioShould` all read 14,176 B, Debug included, where it read
14,244 B before. `ForestCombatScenarioShould`'s fight is the same kill for kill before and
after (105 kills, 2 deaths, 18 heals, 93 quest credits, 198 pickups, 106 destroys): nothing in gameplay reads the views,
and the fighters find their creatures in the instance, not through what their clients were told. Windows figures
(i9-12900K, Windows 11, .NET 10.0.13, BenchmarkDotNet 0.15.8).

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter '*TickPhaseBenchmarks*' '*InterestVisibilityBenchmarks*' --inProcess --job short --warmupCount 10 --iterationCount 8
```

### Results — shared state and aura encoding (#980, 2026-10-14)

On a broadcast every recipient got its own state update, and every entity it sees was encoded into it again: in a
town, where every player sees every other, each moving player was encoded once per recipient. At 950 players on world 4
that was 2.33 ms of a broadcast tick, 1.90 ms of it town: 7,879 entries in 677 packets, 397 KB (#974, sections 4 and
10 C6). Aura updates were the same shape: one unit's `SMSG_AURA_UPDATE` built for each watcher, though its bytes depend
only on the unit. Since #980 (see [world simulation](world-simulation.md) and [auras](auras.md)):

- each changed unit's aura update is encoded once per broadcast pass and the same packet goes to every watcher
  (`SharedAuraUpdates`, `OutboundPacket.Share`), and its entries, and the aura lists', are reused from one packet to
  the next (`AuraWire`);
- each recipient's state add and update is copied together from entries encoded once per broadcast
  (`StatePacketAssembler`, `SInstanceStateAddPacket.WriteEntry`, `SInstanceStateUpdatePacket.WriteEntry`,
  `PacketEncoder.FromPayload`); a character's add or update to its own client, whose motion is left out, is encoded
  for that client alone.

The bytes on the wire are unchanged: `StateUpdateEncodingShould` compares every recipient's add and update of real
broadcasts (characters, creatures, and projectiles cast through the real cast system; a solo player, encoded whole, and
two to twelve recipients, from shared entries) with the per-recipient encoding, and one entity asked for with two field
sets in one broadcast gets an entry for each; `WireSchemaShould` and the corpus are untouched. `SharedAuraUpdatesShould` checks, through a real broadcast and a
counting pool, that every shared aura payload stays out while a recipient holds it and goes back once they all have,
also when the broadcast throws partway.

**Encodes and bytes per broadcast** (`TickPhaseBenchmarks`' town: everyone moving, so every update is motion alone and
no recipient has an entry of its own; counted on one instance's real broadcast, and identical on every broadcast after
the first):

| Players in the town | Packets | Entries in them | Encoded before: entries, bytes | Encoded after: entries, bytes | Copied after |
|---:|---:|---:|---|---|---:|
| 15 | 15 | 210 | 210, 8,554 B | 15, 611 B | 8,554 B |
| 30 | 30 | 870 | 870, 35,699 B | 30, 1,231 B | 35,699 B |
| 300 | 300 | 89,700 | 89,700, 3,765,008 B | 300, 12,592 B | 3,765,008 B |

The protobuf encoding falls from one per entry sent to one per entity that changed. What each packet still costs is
copying its entries into the packet being put together, then that into its pooled segment (the second copy, from the
encoder's stream, was there before), and the segment, as before.

**Adds**, counted the same way on a town's first tick, where everyone is listed to everyone and each player its own
character without its motion (a full add per entity):

| Players in the town | Packets | Entries in them | Encoded before: entries, bytes | Encoded after: entries, bytes | Copied after |
|---:|---:|---:|---|---|---:|
| 15 | 15 | 225 | 225, 15,150 B | 30, 1,812 B | 15,150 B |
| 30 | 30 | 900 | 900, 61,350 B | 60, 3,642 B | 61,350 B |
| 300 | 300 | 90,000 | 90,000, 6,270,275 B | 600, 37,009 B | 6,270,275 B |

After, an entity listed this tick is encoded once for everyone who is listed it, plus each recipient's own character
once. A single arrival in a town of 30 is one encode of the newcomer for the 29 others (29 before); what the newcomer is
listed is encoded once each, as before, unless someone else is listed the same entities that tick. Adds come on
arrivals and on coming into view, not on every broadcast, so they move neither `TickPhaseBenchmarks` (no arrival in its
measured ticks) nor the allocation gate.

**`StateBroadcastEncodingBenchmarks`** (one broadcast, encoding only, players seeing one another and fifteen creatures;
two runs, `--inProcess`, default job; nothing allocated in any row, 0 to 3 B):

| Players | Updates before | shared | after | Adds before | shared | after |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 2.31–2.38 µs | 3.79–3.81 µs | 2.32–2.46 µs | 3.19–3.21 µs | 4.82–4.95 µs | 3.15–3.16 µs |
| 2 | 5.07–5.13 µs | 4.62–4.63 µs | 4.67–4.78 µs | 6.89–6.90 µs | 5.85–5.91 µs | 5.96–5.97 µs |
| 3 | 8.18–8.26 µs | 5.12–7.66 µs | 5.11–9.30 µs | 10.8–20.3 µs | 6.71–6.84 µs | 6.75–6.80 µs |
| 10 | 39.0–39.2 µs | 8.76–9.06 µs | 8.75–8.93 µs | 50.2–50.4 µs | 12.3–12.6 µs | 12.4–13.0 µs |
| 25 | 157–160 µs | 19.6–19.8 µs | 19.4–21.2 µs | 213–221 µs | 27.9–29.7 µs | 27.8–36.6 µs |
| 30 | 211–216 µs | 23.9–24.0 µs | 23.9–26.9 µs | 268–282 µs | 33.6–33.7 µs | 33.2–33.8 µs |

The 9.30 µs, 20.3 µs and 36.6 µs cells are one noisy run each (the other read 5.11, 10.8 and 27.8 µs, and the
machine was shared with other agents' builds and tests); every other pair agrees within a few per cent.

**Why two recipients.** An entry encoded on its own is a serializer call of its own, then copied into the packet and
the packet into its segment: about 1.6 times its share of a whole encode. It pays off only when another recipient
copies it instead of encoding it again. With one recipient nothing is copied, and the shared entries were 59% to 65%
slower for the updates and 50% to 55% for the adds than a whole encode; a solo player's instance, most of the forest
instances at 950 players, would have paid that on every broadcast. So a broadcast with fewer characters than
`StatePacketAssembler.MinRecipientsToShare` encodes each list whole, as before #980, and the row "after" matches
"before" at one player. From two the shared entries won: 9% to 10% for the updates and 14% for the adds at two, 37% to
38% at three, and up to 89% at thirty. A probe of the break-even alone (one to three recipients seeing the same 1, 5,
15 or 40 creatures, updates only, two runs at two recipients) read the same: one recipient 20% to 71% slower shared,
two 15% to 39% faster (a run at 2 × 5 creatures read slower, the rerun faster), three 39% to 69% faster. A party
whose members see different creatures shares less than this model, so two is the lowest count worth sharing at, not
a count at which sharing always wins; nothing between "before" and "after" regresses at any count measured.

**`TickPhaseBenchmarks`** (the tick, per tick, 42 ticks per operation, every player moving; `--inProcess`, short job,
ten warmups and eight iterations; before is `ba0d327b` built from a copy of the tree, after is `85f98ed9`; two rounds,
each after then before, on a machine shared with other agents' builds and tests):

| Players | Instances | Before | After | Allocated before | Allocated after |
|---:|---:|---:|---:|---:|---:|
| 300 | 1 | 3,580–3,683 µs | 1,857–1,928 µs | 120,677 B | 120,677 B |
| 300 | 20 | 268–286 µs | 191–285 µs | 2 B | 1–2 B |
| 600 | 1 | 14,345–14,572 µs | 7,683–9,842 µs | 2,583,524 B | 2,583,413–2,583,517 B |
| 600 | 20 | 945–1,031 µs | 580–1,104 µs | 9 B | 3–12 B |

The crowded town halves its tick at 300 players and loses a third to a half at 600: there the broadcast's encoding was
most of the tick. Spread over 20 towns (15 and 30 players each) the encoding was a small share of a tick that
visibility and tracking dominate, and the two rounds disagree (the second shows -29% and -39%, the first nothing):
within this machine's noise. Allocation per operation is the same before and after: what these cases allocate is the
payload segments the pool cannot keep at that many packets of that size (300 and 600 recipients of 12 KB and 25 KB
updates), and the update sizes are unchanged.

**`AuraScanBenchmarks.Tick`** (whole world ticks, a tenth of the holders refreshed every second, each refresh an aura
update to every character; two runs each, `--inProcess`, five warmups and eight iterations):

| Units | Holding | Before | After | Allocated before | Allocated after |
|---:|---:|---:|---:|---:|---:|
| 50 | 10% | 39.6–44.4 µs | 38.4–40.3 µs | 93 B | 0 B |
| 50 | 100% | 49.4–65.8 µs | 41.8–44.0 µs | 467–468 B | 1 B |
| 100 | 10% | 165.8–184.5 µs | 158.3–174.1 µs | 189 B | 2–4 B |
| 100 | 100% | 205.7–214.6 µs | 173.6–177.8 µs | 1,869 B | 2 B |
| 200 | 10% | 698.7–832.5 µs | 688.9–730.5 µs | 757–759 B | 10–11 B |
| 200 | 100% | 762.6–887.2 µs | 677.3–679.3 µs | 7,479 B | 10 B |

An aura update is now one encode and one list of entries per changed unit rather than per watcher, and the entries are
reused: what was left allocated per tick is the run's few bytes of noise.

**The allocation gate**: `forest-combat` reads 14,216 B per window in Release with the aura updates shared (14,292 B
before) and 14,176 B with their entries reused too, the new committed figure; Debug reads 14,284 B and 14,244 B
(14,360 B before). The fight has three watchers per update at most, so the saving is small. `town-idle`, `town-walk`
and `many-instances` read 0 B before and after, and `town-social` its committed 3,904 B (4,008 B in Debug), read after
rebasing onto it: the shared encoding allocates nothing per tick or per packet beyond the pooled payload segment each
packet had before. `TickPhaseBenchmarks`, `StateBroadcastEncodingBenchmarks` and the gate
are Windows figures (i9-12900K, Windows 11, .NET 10.0.13, BenchmarkDotNet 0.15.8).

### Results — a fuller fight (#972, 2026-10-14)

The gate held only what the scenarios ran, and several paths that allocate per packet or per event were outside every
one of them (#972). This widens what it sees, in four steps, each measured on its own before the next: the real send
call in every scenario, then a healer, a kill quest and loot in `forest-combat` ([what the fighters
do](#the-forest-combat-scenario)). Bytes per window of `forest-combat`, the runner (`--scenario forest-combat
--measure-ticks 0`, 5 s rehearsal), developer machine (i9-12900K, Windows 11, .NET 10.0.12), with other builds and
tests running beside it; every run of a step read the same figure, to the byte:

| Step | Commit | Release | Debug | Increment (Release) | Runs |
|---|---|---:|---:|---:|---|
| Before (main `2d948654`; the file held 10,357, 4 B under #898's roster lists) | | 10,361 | 10,361 | | 3 + 3 |
| The real send path: `Connection.Send` and `OnQueued` | `6291eaa2` | 10,361 | 10,361 | 0 | 3 + 3 |
| A healer in three parties in four | `45704b77` | 11,479 | 11,479 | +1,118 | 3 + 3 |
| The kill quest | `aaca7ed8` | 12,936 | 12,936 | +1,457 | 3 + 3 |
| Loot and item destroys | `75aeb59e` | 14,292 | 14,360 | +1,356 | 3 + 3, then 5 + 5 |

**The committed figure, 14,292 B, is the sum**: 10,361 + 0 + 1,118 + 1,457 + 1,356. Release with dynamic PGO off
(`DOTNET_TieredPGO=0`) reads 14,292 too. The gate reads 14,292 in Release and 14,360 in Debug (+0.48%, inside the
256 B floor), and the town scenarios and `many-instances` read 0 B in both: the send call allocates nothing on any
scenario's path. Stability: five more runs in each build read 14,292 (Release) and 14,360 (Debug) every time, a
spread of 0 B, against the 256 B the gate tolerates.

What each step costs, and why:

- **The send path, 0 B.** Each scenario connection now sends through a real `WorldConnection` with no socket (its
  outbox on the counting stream): `Connection.Send` reads the packet's size, adds to two counters with `Interlocked`
  and calls `OnQueued`, whose two `Counter<long>.Add` calls, with no listener, are a check each. None of it
  allocates, as `ConnectionSendBenchmarks` showed (#970).
- **The healer, +1,118 B.** Changing the roster alone (the healer casting Smite, no heals) is +435 B; the heals take it
  to +1,118 B. A heal cast itself allocates about 460 B (its circle script and the heal's hit and threat), about what
  an attack cast does, and there are 8 in the measured minute; most of the rest is the fight the heals change (the
  party lives, casts and kills more: 83 kills over the run against 71). Nothing is allocated per tick.
- **The kill quest, +1,457 B.** About 690 B per quest flush that has something to send (an update, a line, the markers
  recomputed: 83 in the minute), about 960 B per abandon and accept (21 in the minute), and the kill path's quest walk.
  The fight is unchanged (the same 83 kills). Nothing is allocated per tick: a flush with nothing owed returns at its
  first check (#977).
- **Loot, +1,356 B.** 180 pickups and 85 destroys in the minute, each through its handler with its reply, the
  despawn broadcast for a pickup, the inventory flush of the changed slots and the quest flush's Bag recount, and a
  bigger fight (93 kills in the minute against 71 before): fighters walking to their drops meet more creatures.

**Findings, none on a per-tick path** (production code is unchanged here):

- `InventoryMove.DecideDestroy` tests the template's flags with `Enum.HasFlag(ItemTemplateFlags.NoDestroy)`, which
  boxes the receiver and the argument in unoptimised code: 48 B per item destroy in Debug, 0 in Release. It is why
  Debug reads 68 B per window above Release (85 destroys × 48 B = 4,080 B per minute), found by diffing the per-tick
  bytes of a Release and a Debug run: 84 ticks differ, each by 48 B (one by 96). A bit test, as `CastAbilityHandler`
  uses since #880, would make Debug read the committed figure again. Done since #981 (#973), with the other item, quest,
  vendor and maintenance flag tests: Debug reads 14,176 B.
- `QuestService.FlushMarkers` walks every creature of the character's instance through
  `IReadOnlyDictionary.Values` (a boxed enumerator, 40 B) and builds a new list (32 B) each time the character's
  quest log, level or instance changed: 72 B and a walk of 200 creatures per kill credit per party member in a
  forest, which holds no quest NPC, so the list it builds is always empty.

### Results — the broadcast schedule (#975, 2026-10-14)

Every instance used to broadcast its state on the same tick, and each instance's timer reset to 0 on a broadcast,
dropping its overshoot. The 950-player profile (#974, section 5) found all 121 instances broadcasting together on 273
of 1,800 ticks and on no other, those ticks averaging 15.78 ms against 11.60 ms, and the interval six or seven ticks
apart, 9.1 broadcasts a second. Each instance now has a slot on the world's broadcast schedule, six slots a tick apart
given to each instance as it is published (the slot with the fewest live instances), and its clock keeps the remainder (see
[world simulation](world-simulation.md)). The figures below were taken with slots handed out round-robin; with every
instance published together, as here, the fewest-live rule hands out the same slots. Measured on `many-instances` (250 instances published together, two walkers
in each), before at `1e7d9618` and after at `ab7913c7`, Release, developer machine (i9-12900K, Windows 11, .NET
10.0.12), with other builds and tests running on it, so wall-clock figures are given as ranges over runs.

**Where the broadcasts fall** (3,600 ticks after a 600-tick warm-up, read from the `Avalon-World-Tick` provider by an
in-process listener; the same every run):

| | Before | After |
|---|---|---|
| Instances broadcasting on a tick | all 250 on 514-515 ticks, none on the other 3,085-3,086 | 41 on 1,200 ticks, 42 on 2,400; never none |
| Interval between one instance's broadcasts | 7 ticks (116.7 ms), every time | 6 ticks (100.0 ms), every time |
| Broadcasts per instance per second of game time | 8.57 | 10.00 |
| State packets per tick | 500 on a broadcast tick, 0 otherwise | 82 or 84 |

The scenario's tick is 4 TimeSpan ticks under 1/60 s (`ScenarioWorld.Dt`, 166,666), so the old timer needed a seventh
tick every time (production's measured deltas made it six or seven: 9.1 a second); with the remainder carried a
seventh is needed once in about 40,000 intervals.

**Tick time** (3,600 ticks timed without the listener, each tick's state packets counted; three runs of each,
interleaved):

| | Before | After |
|---|---|---|
| Ticks carrying the broadcasts, mean | 2.59 / 3.62 / 4.00 ms (514-515 ticks) | 1.72 / 3.73 / 2.73 ms (every tick) |
| The other ticks, mean | 1.90 / 2.83 / 3.09 ms | the same: 1.73 / 3.76 / 2.72 ms |
| A broadcast tick against the others, same run | +0.69 to +0.91 ms (+27% to +36%) | none |

The runs' absolute figures move with the machine's load (the after runs' means range from 1.7 to 3.7 ms); the
comparison inside each run does not: before, a seventh of the ticks paid 0.7 to 0.9 ms more for all 250 broadcasts;
after, every tick pays a sixth of them and no tick stands out. The runner's own timed phase
(`--scenario many-instances`, six runs before and three after) read p95 4.2 to 15.5 ms before and 3.0 to 7.0 ms after,
p99 4.9 to 68 ms and 3.5 to 16 ms, on a machine whose load swung the means between 1.7 and 6.2 ms: consistent with the
spread, and too noisy to quote as its size. The mean is the same work, plus a sixth more broadcasts (ten a second
against 8.57 here).

**`TickPhaseBenchmarks`** (the tick, crowded or spread over towns, 42 ticks per operation; `--inProcess`, one benchmark binary run
once with the before build's `Avalon.World` and `Avalon.World.Testing` and once with the after build's; StdDev 10% to
25% on this machine):

| Players | Instances | Before | After | Allocated before | Allocated after |
|---:|---:|---:|---:|---:|---:|
| 300 | 1 | 5,285 µs | 5,120 µs | 103,438 B | 120,677 B |
| 300 | 20 | 521 µs | 530 µs | 3 B | 2 B |
| 600 | 1 | 24,919 µs | 25,321 µs | 2,214,458 B | 2,583,517 B |
| 600 | 20 | 2,131 µs | 2,470 µs | 16 B | 21 B |

An operation of 42 ticks held six broadcasts per instance before (every seventh tick) and seven after (every sixth),
so it does a sixth more broadcast work; the allocation of the crowded single town grows by exactly that (103,438 B ×
7/6 = 120,678 B; 2,214,458 B × 7/6 = 2,583,534 B): what one broadcast to 300 or 600 players in one instance allocates is
unchanged. The times are within the noise: the four cases moved by -3% to +16% at the same 7/6 ratio of broadcasts,
with a standard deviation of 10% to 25%, so they show no cost per broadcast either way.

From #972 on, `TickPhaseBenchmarks` (and `CharacterDeltaBenchmarks`, built on the same harness) send every packet
through the real `Connection.Send` and the world connection's `OnQueued` rather than the sender's `Enqueue` alone:
rows from #972 on include the send call, so their timings and those above are not compared like for like.

**The allocation gate does not move**: `town-idle`, `town-walk` and `many-instances` 0 B, `forest-combat` 10,357 B, in
Debug and Release, and `ForestCombatScenarioShould`'s two runs agree. The fight's four forests, published together,
now broadcast in four slots, and ten times a second rather than 8.57: what a broadcast and a fight allocate is the
same.

### Results — the kill path (#890, #883, 2026-10-13)

What a kill, a creature's attack, a heal's threat and an aura's removal allocated beside what they keep or send, taken
out site by site: the four sites left of [what Release keeps off the heap, and Debug does
not](#results--the-melee-slots-release-890-2026-10-12) (#890), and rows 6a, 6c and 7 of #883. `forest-combat`, bytes
per window, the gate alone, i9-12900K, Windows 11 (10.0.26200), .NET 10.0.12; before is `507b37d8`, which read its
committed 11,543 B. The measured minute kills 60 creatures (parties of three) and runs 174 creature attacks.

| Change | Release | Debug | Release, PGO off | What went |
|---|---:|---:|---:|---|
| Before | 11,543 | 11,780 | 11,748 | |
| `CreatureAbilities.Holds` walks its array (#890) | 11,543 | 11,687 | 11,655 | The boxed enumerator of the creature's `IAbility[]`, walked through `IReadOnlyList<IAbility>`: 32 B an attack. |
| The eligible members in a list the instance keeps (#890) | 11,351 | 11,431 | 11,399 | A delegate made from `PartyService.InCountdown` at each kill (64 B, the part the profile decided), the list and its array, a new `ObjectGuid` for each party member, the boxed enumerator of the members: 256 B a kill. |
| The experience shares in a list the instance keeps (#890) | 11,063 | 11,095 | 11,063 | Two lists and their arrays, a delegate over the reference data, the boxed enumerator of the eligible, and the boxed enumerator of the result (48 B, the part the profile decided: the result was an empty array, a one-element list or a `List` behind one interface): 336 B a kill. |
| A kill's drops placed without a closure, its lists or its messages (#890) | 10,407 | 10,407 | 10,407 | The lambda's closure (56 B; 88 B in two objects in Debug, below) and its delegate, the merged list of rolled drops, the list of placed drops, the list of loot messages and each drop's message with two `Vector3Dto`: 480 B a kill of one drop, and 192 B for each drop more. |
| Item templates, level rows and the map template found by key (#883, row 7) | 10,407 | 10,407 | 10,407 | Nothing allocated: three walks per kill ([below](#reference-row-lookup--benchmarksreferencerowlookupbenchmarkscs-883)). |
| A heal's threat shared out in place (#883, row 6a) | 10,407 | 10,407 | 10,407 | No healer fights in the forest; measured by its own test, below. Read together with the row above. |
| Auras ended without a copy, a closure or a delegate (#883, row 6c) | 10,396 | 10,396 | 10,396 | The forest's few poisons: a closure for every apply, tick and removal of an aura, and a closure, a delegate and an array for each removal. |
| An aura's fit checked without a closure (#883) | 10,385 | 10,385 | 10,385 | `AuraSystem.Apply` handed the aura's row to the link check through a lambda: a closure for every application (24 B, made on entry, so before any refusal) and a delegate with it for an ability's aura (64 B). Read together with the lookups of one row by key through `StaticData`, none of which is on the fight's path. |

1,158 B per window less in Release, 10.0%, and 1,395 B less in Debug. **The three builds read the same figure**, for
the first time: nothing on the fight's path is kept off the heap by optimised code alone, and nothing by the
profile. With `DOTNET_TieredPGO=0` Release read 205 B above the committed figure before (the 273 B recorded under
[the melee slots' release](#results--the-melee-slots-release-890-2026-10-12), less the 68 B of `CancelScriptsOf`'s
delegate, gone since) and reads the committed figure now. Every cell is one run of the gate; the last row was read
again at the end, as below. `ForestCombatScenarioShould` reads 71 kills on both of its runs, as before.

| `forest-combat`, bytes per window, after | The gate alone | The whole World suite |
|---|---:|---:|
| Release, committed | 10,385 (three runs) | 10,385 |
| Debug | 10,385 (three runs) | 10,385 |
| Release, `DOTNET_TieredPGO=0` | 10,385 (two runs) | not run |

The runner read 10,385 twice, the second run the one that wrote the file at `8f21757d`, and `town-idle`, `town-walk`
and `many-instances` read 0 in every run, in both builds. In each suite run the two-run check read 71 kills and
10,385 B twice. (At 10,396, before the last row, the same runs read the same: four of the gate alone in each build,
three with PGO off, one suite run in each build and two of the runner.) These are Windows figures; CI decides on Linux, where no run was made for this change: read its
figure from the first CI run's "Scenario allocation gate" summary. With the three builds reading the same, alone and
in the suite, the owner had `forest-combat` gated in every build again (2026-10-13, #890): the branch of
`ScenarioAllocationsShould` that reported its regression in Debug instead of failing is gone
([The allocation gate](#the-allocation-gate)), and with it the last open part of #890.

**The object nobody had identified in `DropLoot`** (32 B a kill in Debug, 0 in Release, and the one row of five that
did not move with the profile) was a second closure. The lambda that answered each drop's allocation captured locals
of two scopes: the method's (the quest drops, the eligible members, `credited`, `this`) and the `try` block's (the
allocator and the table's count). The C# compiler makes one closure object per scope when it does not optimise (48 B
and 40 B) and merges them into one when it does (56 B). The difference was the compiler's, not the JIT's: the two
builds' assemblies hold two and one closure classes for the method. It was found by counting allocations around the
steps of `DropLoot`, every one of which read the same in both builds, which left the method's own prologue.

**What made each one safe.**

- **The kill's lists** (`KillBuffers`: the eligible members, the experience shares, the rolled and placed drops, the
  loot messages) belong to the `MapInstance`, never to a static: an instance's kills run one at a time on its tick,
  and instances may come to tick on separate threads (#639). They are emptied in a `finally` when the kill returns, so
  they keep no character or drop alive, and nothing a kill hands them to keeps them (the quest service and the loot
  allocator read the eligible members during the call). A kill reached from inside another's handling, which nothing
  does today, takes lists of its own. Each fill asserts (`Debug.Assert`) that the list it is handed is empty, and two
  tests kill twice in one instance (`PartyKillShould.Share_each_kill_among_its_own_members_only`,
  `MapInstanceLootShould.Tell_the_instance_of_each_kills_own_drops_only`): with any one of the five lists left
  uncleared, at least one test of the kill fails on its outcome. `PartyEligibility.For`, `PartyExperience.Split` and
  the overloads of `LootPlacement.Place` that answered in a list of their own are gone; their rule tests go through
  the fills.
- **Who is eligible, each share and each drop are unchanged.** The members are walked in join order, the shares
  computed with the same decimal arithmetic in the same order, and a drop's position, its allocation (one draw of
  the combat random per table drop in a party instance) and its id are taken in the order they were. The drops are
  still placed whole before any is stored, so a placement that throws leaves nothing on the ground.
- **The party member's lookup key** is one `ObjectGuid` the instance keeps and sets to each member in turn; it is
  only ever a lookup key, never stored.
- **The loot messages** are refilled, every member of them, for each kill, and the packet is encoded before the kill
  returns; a message holds numbers only. The snapshot a character entering an instance is sent still builds its own.
- **The lookups by key** (`KeyedRows`) are the rows themselves: the Items and Progression patches publish their rows
  and the rows' index as one object, so `/reload items` and `/reload progression` replace both together and no
  lookup can outlive its rows. The first row of a key answers, as the walk did, and a collection handed to the loot
  roller that carries no lookup (a test's list) is walked as before. `StaticData.FindItemTemplate` and `FindLevel`
  are the way in for any reader that wants one row: character select's level row and six lookups of one item
  template (character creation, the vendor trade, item use, quest rewards and pickups, the vendor list) use them,
  where each walked the rows through LINQ with a closure. Map templates are never reloaded; an instance keeps its own template
  with the world's list it found it in, and looks again only if the world holds another list.
- **Heal threat** is counted and shared out in two walks of the encounter's hostiles in place. Joining the encounter
  and adding threat, which happen between the two, write the threat lists only, so both walks see the same hostiles.
- **Aura removals** copy the auras they end into lists the aura system keeps, one per depth: ending an aura runs its
  script's `OnRemove`, which may kill the unit and so end the rest from inside the walk. The auras end in the order
  held, and one already gone by its turn is passed over, as before.
- **An aura's fit** is asked of `AuraRules.FitProblem`, the half of `LinkProblem` that takes the row itself;
  `LinkProblem` ends in it, so the rule and its wording are one.

**Rows 6a and 6c, which the forest does not reach** (it has no healer, and few auras), each have an allocation
assertion, read before and after on the same test:

| Test | Before | After |
|---|---:|---:|
| `CombatServiceHealShould.Share_out_heal_threat_without_allocating`: 100 heals on a character two creatures fight | 16,800 B | 0 B |
| `AuraApplyShould.Refuse_and_end_auras_without_allocating`: two cancels, two removals on death and two on a reset, ten auras in all | 1,904 B | 0 B |
| The same test: an ability's aura refused on a corpse | 88 B | 0 B |

A heal cost 168 B (the query's iterator, the boxed enumerator of the set, the list and its array). An aura's removal
cost 88 B for the closure and delegate of its `OnRemove` hook, and 32 B more for the hook's own closure, whether the
aura had a script or not; the same 32 B was paid by every apply and every tick of every aura.

**What a kill still allocates**, the same in every build:

| Per kill | Bytes | Why it is left |
|---|---:|---|
| The corpse's removal timer | 32 B | Kept until the corpse is removed. Gone since: the remover keeps a count for each corpse, not an object ([the corpse remover](#gameplay-requests-quests-aura-hooks-and-corpses--the-allocation-tests-benchmarksencounterlookupbenchmarkscs-883)). |
| The list `ILootRoller.Roll` hands back | 152 B for up to four drops | The roller is one object for every instance, so it cannot keep the list, and the tests of the kill substitute `Roll` and answer with a list: taking it out means a signature that fills the caller's list. |
| Each drop left on the ground | 96 B | Kept: the `GroundLoot` and its guid, until it is picked up. |

Not measured by the scenario, which builds no quest service: the kill's quest credit and quest drops
(`QuestService.CreatureKilled`, `RollQuestDrops`) copied each eligible member's quests and walked the eligible
through their interface, and an aura with a script made its context and a closure for each hook. Both are gone
since, measured by their own tests
([quests and aura hooks](#gameplay-requests-quests-aura-hooks-and-corpses--the-allocation-tests-benchmarksencounterlookupbenchmarkscs-883)):
a kill that moves no quest allocates nothing for its quests, and a hook only its context. An aura with stat modifiers
still refreshes its unit's stats when it ends.

### Results — the cast path (#925, #883, 2026-10-13)

What a cast allocates on its way to a script, taken out one part at a time. `forest-combat`, bytes per window, the
Release gate alone, i9-12900K, Windows 11 (10.0.26200), .NET 10.0.12, dynamic PGO on; before is `c6d52b01`, which
read its committed 15,635 B (Debug 15,940). The measured minute builds 795 scripts: 621 for the players' casts (112
cones and 509 projectiles) and 174 for the creatures' (26 circles, 122 cones and 26 projectiles); it removes 64
creatures.

| Change | Bytes per window | Step | What went |
|---|---:|---:|---|
| Before | 15,635 | | |
| Ability scripts built through a factory kept per script type (#925) | 14,893 | -742 | The `params` array of `ActivatorUtilities.CreateInstance`, 56 B a cast. On this runtime that was all the activation allocated: it keeps its constructor search in a cache and its argument buffers on the stack. |
| `CancelScriptsOf` without a closure (#883) | 14,867 | -26 | The closure of its `RemoveAll` predicate, 24 B for each creature removed (Debug also made the delegate, 64 B). |
| Active scripts found by guid above eight (#883) | 14,873 | +6 | One forest's index, 360 B, the one time its active scripts pass eight ([active script lookup](#active-script-lookup--benchmarksactivescriptlookupbenchmarkscs-883)). |
| The cast system's Debug lines through `CombatLog`, one list for `CancelCasts` (#925, #883) | 14,299 | -574 | The "fired" line's argument array, 40 B a cast (530 B of the step); the "queued" line's array and its boxed cast time and queue size, the two dropped-cast lines' arrays, and the list `CancelCasts` made for every creature and character that left. |
| One aim box per cast system (#925) | 13,451 | -848 | The aim boxed for the factory, 64 B a cast. |
| The ability's metadata shared with the cast's copy (#925) | 11,967 | -1,484 | The `AbilityMetadata` copied with the ability, 112 B a cast. |
| The chain list made on first use (#925) | 11,543 | -424 | The empty `ChainedScripts` list, 32 B a cast. |

4,092 B per window less, 26%. Every row from the log lines down was read twice, the same; the first three once on
this base and three times, with the same steps, on the base before it. Two commits in between change no figure and
have no row: the factories made at `ScriptManager.Load` (not read on its own), and the index emptied when a removal
sweep throws (14,873, read once).

| `forest-combat`, bytes per window | The gate alone | The whole World suite |
|---|---:|---:|
| Release, committed | 11,543 | 11,543 |
| Debug | 11,780 | 11,780 |

The runner read 11,543 twice, the second run the one that wrote the file, and `town-idle`, `town-walk` and
`many-instances` read 0 in every run. Debug's excess over the committed figure is 237 B per window (14,208 B a
minute): the table under [the melee slots' release](#results--the-melee-slots-release-890-2026-10-12) less its first
row, the `RemoveAll` delegate of `CancelScriptsOf` (4,096 B a minute), which is gone. That is 19 B under the 256 B
floor, so a Debug run then read Within. `forest-combat` was still decided in Release and reported in Debug at that
point: the rule was the owner's (#890) and was not changed with the cast path. The rest of that excess went with
[the kill path](#results--the-kill-path-890-883-2026-10-13), after which Debug and Release read the same and the
gate decides in Debug again.

**What made each one safe.**

- **The factory.** It binds by the declared types `IAbility`, `IUnit`, `AbilityAim` and `IAbilityArena`, which every
  shipped and seeded script declares. A script has exactly one public constructor accepting the four, or marks one
  `[ActivatorUtilitiesConstructor]`. Factories are held weakly by script type, and the script manager is asked for
  the type on every cast ([packet handlers](packet-handlers.md)).
- **The aim box.** The factory unboxes the aim into the constructor's `AbilityAim` parameter, a copy, so a script
  keeps the aim it was built with whatever is cast next (`Fire_each_script_with_the_aim_of_its_own_cast`: a cast still
  winding up when the next one's aim goes into the box). A constructor that could take the aim as a reference
  (`object`, `ValueType`, an interface the aim implements) would be handed the box itself; a script type with such a
  parameter is refused when its factory is made.
- **The shared metadata.** A script is still handed a copy of the ability, so nothing it writes reaches its caster's
  cooldown, cast timer or casting flag (`Keep_the_casters_cooldown_and_cast_state_out_of_its_scripts_reach`). Only the
  metadata is shared, and it cannot change once built: every member is an init-only number, flag, enum or string
  (`AbilityMetadataShould`), nothing writes one after `AbilityMetadataMapper` built it, and a reload or a template
  edit makes new metadata for the abilities loaded afterwards, never rewriting one in use, so a cast in flight keeps
  the row it started with. `IAbility.Clone` is unchanged and still copies it.
- **The chain list.** `ChainedScripts` keeps its type, its accessors and what a script sees: one that adds to it or
  walks it finds a list. The base class's own `Update` and `Clone` read the field, so neither makes one.

**What a cast still allocates.** A build is 224 B for a circle or a cone and 320 B for a projectile
([ability script construction](#ability-script-construction--benchmarksabilityscriptbuildbenchmarkscs-925)): 229,440 B
of the 692,580 B the minute allocates, 33%, from 439,320 B of 938,100 B, 47%. By part, with what would take it out:

| Part of a build | Per cast | Per window | Share | Why it is left |
|---|---:|---:|---:|---|
| The script object | 152 B, a projectile 184 B | 2,299 B | 20% | A script is a new object for every cast. Reusing one needs a way to reset it, on the scripts' public base class, and a script that forgot a field would carry it into the next cast. |
| The ability's copy | 48 B | 636 B | 6% | It is what keeps a script from its caster's cooldown, cast timer and casting flag. |
| The projectile's set of units hit | 64 B | 571 B | 5% | Made with the projectile; one that hits nothing never uses it. Not looked at in this change. |
| The script's `State`, boxed | 24 B | 318 B | 3% | The property is an `object` on the scripts' public base class. |

The first, second and fourth are decisions on the script contract and stay open on #925; none is a fix to make
unasked.

### Results — the melee slots' release (#890, 2026-10-12)

`forest-combat` read 80 B per window more, or less, in a whole World suite run than in the gate alone: the same two
figures on every run, with the sign depending on the build. i9-12900K, Windows 11 (10.0.26200), .NET 10.0.12, dynamic
PGO on; before is `46b38375`, after is this change on it.

| `forest-combat`, bytes per window | The gate alone | The whole World suite |
|---|---:|---:|
| Debug, before | 16,469 | 16,549 |
| Release, before | 16,244 | 16,164 |
| Debug, after | 15,940 | 15,940 |
| Release, after | 15,635 | 15,635 |

Each "after" cell is three runs, every one the same; a suite run also reads the two-run check's two figures, the same
again. The "before" Debug cells were read on the unchanged tree (the suite twice), the Release pair once each with
the allocation counters in the tree, which add nothing to the figure (the Debug cells read the same with them). These
are Windows figures. CI decides on Linux, where
no run was made for this change: read its figure from the first CI run's "Scenario allocation gate" summary.

**The allocation.** `MeleeSlots.ReleaseClaimant`, which every kill and every corpse removal calls (124 times in the
measured minute), found the claimant's targets with `Where`, `Select` and `ToList` over its dictionary of claims. That
cost 256 B a call (the closure, the delegate, two iterators and the list) and, on the 86 calls that found the
dictionary not empty, the dictionary's enumerator: boxed (56 B) in one run, kept on the stack in the other. 86 × 56 B
is 4,816 B a minute, 80 B per window. An empty dictionary hands out one shared empty enumerator and allocates none.

**What switched it.** The query ends in `Enumerable.IEnumerableWhereSelectIterator<TSource, TResult>.ToList()`, and
`ObjectGuid` is a class, so the code that ran is the one body the runtime shares between every such query in the
process over a dictionary of two reference types that selects a reference type. It lives in System.Linq, which is
optimised, with dynamic PGO, under a Debug build too. It walks its source as an `IEnumerable<T>`, which boxes the
dictionary's enumerator; the optimised tier keeps that box on the stack only when the profile the body gathered over its
first calls says the enumerator it will be handed is that dictionary's own, rather than the shared empty one or another
dictionary's. So the first callers in the process decide for all later ones:

- **The gate alone:** the first calls are the first rehearsal's kills, most of them with claims held.
- **The whole suite:** 69 calls from other tests come first, 67 of them with no claims at all.
- **Either way** the profile is read when the JIT's background promotion runs, so the outcome also depends on timing:
  writing the JIT's method listing to a file was enough to turn either Debug run over.

A Release build came out the other way round in both runs (boxed alone, on the stack in the suite), at the same call
and for the same 4,816 B. Why was not narrowed further for Release: every experiment below is a Debug one.

The evidence: the per-tick bytes of the two runs differ by 56 B in 82 ticks and 112 B in 2; allocation counters around
the steps of the tick put all of it in `ReleaseClaimant`; with `DOTNET_TieredPGO=0` the gate alone reads the suite's
16,549 B (Debug); and calling the same query 1,200 times before the scenario settles the figure at will: over an empty
dictionary, or a filled `Dictionary<string, string>`, 16,549 B, and over a filled dictionary of the claims' own type,
16,469 B. The optimised listing of the shared `ToList` (`DOTNET_JitDisasm`) has the enumerator's allocation in one case
and not in the other.

**The fix** removes the allocation rather than the profile, as #880 did ([Dynamic PGO](#dynamic-pgo)):
`ReleaseClaimant` walks the dictionary with its own struct enumerator into a list it keeps, then releases, and
allocates nothing. The fight allocates 529 B per window less than the run that kept the enumerator on the stack and
609 B less than the one that boxed it, the Release figure committed until then among them (16,244 to 15,635 B, 3.7%);
the committed figure was lowered with it. Tick time
was not measured: the change is to what a kill and a corpse removal allocate, about 2 calls a second in the fight.

**What Release kept off the heap, and Debug did not** (history: none of it is left, see the end of this section).
305 B per window (18,304 B a minute), the same in every run, by the same counters:

| Bytes a minute | Where | Debug | Release |
|---:|---|---:|---:|
| 4,096 | `InstanceAbilityCastSystem.CancelScriptsOf`, per corpse removed (64): the `RemoveAll` predicate's delegate; its closure is allocated in both | 88 B | 24 B |
| 3,840 | `MapInstance.EligibleFor`, per kill (60) | 256 B | 192 B |
| 1,920 | `MapInstance.DropLoot`, per kill | 32 B more | |
| 2,880 | `MapInstance.AwardExperience`, per kill | 336 B | 288 B |
| 5,568 | `CreatureCombatScript.Holds`, per call (174): it walks the creature's `IAbility[]` through `IReadOnlyList<IAbility>`, which boxes the array's enumerator | 32 B | 0 B |

The locomotion step allocates nothing in either build.

This is Debug's excess over the committed figure, and it is also what Release stands to lose. With
`DOTNET_TieredPGO=0` the Release gate alone reads 15,908 B (one run), 273 B per window above the committed figure and
over the floor. That is the table less `DropLoot`'s row, to the byte (16,384 B a minute), so the other four are taken
to rest on the profile (by that sum; they were not counted one by one with the profile off): each is in a World method
compiled once per process from what its first callers showed it. None of them turned in the runs above, alone or in
the suite; were all four to turn, the Release gate would fail with no change in the code.

**Since then** every row of this table is gone: `CancelScriptsOf`'s delegate with
[the cast path](#results--the-cast-path-925-883-2026-10-13), the other four with
[the kill path](#results--the-kill-path-890-883-2026-10-13), which also says what each was. One correction to the
table: `DropLoot`'s 32 B was not kept off the heap by the JIT. It was a second closure object that the C# compiler
makes when it does not optimise and merges into the first when it does, which is why it alone did not depend on the
profile. A Debug build, a Release build and a Release build with `DOTNET_TieredPGO=0` now read the same figure, and
`forest-combat` is gated in every build again (owner, 2026-10-13, #890).

### Results — shared broadcasts (#875, 2026-10-10)

A broadcast whose bytes are the same for every recipient (a hit, a cast's start, finish or interruption, an ability
fired, a death, a revive, an attack animation, a heal, an aura's periodic tick, the three item-cast broadcasts, loot
spawned and despawned, the owed loot snapshots, the party health-scale notice, instance and party chat, a party
member's status) is now encoded once, for the first recipient that hears it, and each recipient takes a reference
(`OutboundPacket.Share`); see [Sending a packet](networking-packet-protocol.md#sending-a-packet). World-state packets
and aura updates stay per recipient.

i9-12900K (24 logical processors), Windows 11 (10.0.26200), .NET 10.0.12, Release, dynamic PGO on (the default). Before
is `8ca86346`, after is this change on it; three runs per side, interleaved (before, after, before, after, before,
after), each a `forest-combat` runner run (`--scenario forest-combat`, its defaults) and a `crowd-budget` run (its
defaults: 4 players, 900 warm-up and 3,600 measured ticks, N = 10, 25, 50, 100). Medians of the three.

| `forest-combat` | Before | After |
|---|---:|---:|
| Bytes per window (every run) | 16,252 | 16,244 |
| Tick ms, mean | 0.125 | 0.127 |
| p95 | 0.236 | 0.264 |
| p99 | 0.318 | 0.329 |

| `crowd-budget`, update ms | N | Before mean | After mean | Before p95 | After p95 |
|---|---:|---:|---:|---:|---:|
| Waypoint | 10 | 0.099 | 0.077 | 0.276 | 0.221 |
| Crowd | 10 | 0.286 | 0.314 | 0.415 | 0.392 |
| CrowdPlayers | 10 | 0.130 | 0.124 | 0.217 | 0.218 |
| Waypoint | 25 | 0.062 | 0.060 | 0.123 | 0.144 |
| Crowd | 25 | 0.222 | 0.228 | 0.355 | 0.351 |
| CrowdPlayers | 25 | 0.251 | 0.242 | 0.394 | 0.386 |
| Waypoint | 50 | 0.060 | 0.064 | 0.145 | 0.148 |
| Crowd | 50 | 0.553 | 0.561 | 0.736 | 0.758 |
| CrowdPlayers | 50 | 0.580 | 0.580 | 0.778 | 0.788 |
| Waypoint | 100 | 0.091 | 0.090 | 0.223 | 0.223 |
| Crowd | 100 | 1.130 | 1.122 | 1.404 | 1.366 |
| CrowdPlayers | 100 | 1.142 | 1.179 | 1.410 | 1.475 |

- **Allocation: 8 B less per window** in `forest-combat` (16,252 to 16,244, every run on both sides), the figure
  committed with it; the town scenarios and `many-instances` stay at 0. Sharing allocates nothing (`Share` is one interlocked
  increment). The fall is small because a forest has three listeners, so a broadcast's factory ran at most three times
  before, and the factories that still allocate per call (revive, ability fired, loot despawned, the owed loot
  snapshot's list) are rare there.
- **Tick time: no measurable change.** In `forest-combat` and in most `crowd-budget` rows the interleaved pairs
  disagree; the rows where all three pairs agree go both ways (after faster: Waypoint 10, CrowdPlayers 25; after
  slower: Crowd 10, CrowdPlayers 100), within the runs' spread. Four listeners encode at most four packets per broadcast
  either way; the saving grows with the number of listeners, which neither harness models.

### Results — server packets built in per-thread messages (#875, 2026-10-10)

The last allocation on the send path was the message object each server packet's `Create` built: 24 B for a state
update, 48 B for a movement acknowledgement. `Create` now fills the calling thread's instance of its message type
(`PacketEncoder.Scratch<T>()`, reset to a new message's values on every take) and encodes it before it returns, and
`SInstanceStateRemovePacket` keeps its id list per thread too. With the payload already a pooled segment and the seal
written in place at the drain, a packet sent costs the tick thread nothing, and no scenario allocates on it at all.

Allocations, from `perf/scenario-allocations.json` regenerated at `75a16697` (the developer machine's Release run,
i9-12900K, Windows 11, .NET 10.0.12), against the committed figures before this change (`6eafe191`) and before the
payloads went into pooled segments (`44d0db3a`, the header struct's results):

| Scenario | Players | bytes/window | B/player/tick | Before | B/player/tick before | B/player/tick at `44d0db3a` |
|---|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 | 0 | 0.00 | 0 | 0.00 | 0.00 |
| `town-walk` | 30 | 0 | 0.00 | 92,160 | 51.20 | 624.67 |
| `many-instances` | 500 | 0 | 0.00 | 1,536,000 | 51.20 | 210.99 |

The gate passes on the new figures in Debug and in Release.

Tick time like for like: `862042c5` (main before the outbound packets: payloads sealed into new arrays in `Create`)
against `724618bb` (this work), the runner in Release, 5 s warm-up, 3600 timed ticks, three alternating pairs on the
developer machine (i9-12900K, Windows 11, .NET 10.0.12, Server GC). Tick ms per run, then the mean of the three:

| Scenario | Build | mean (3 runs) | mean | p99 (3 runs) | p99 | gen0 per minute | GC pause ms |
|---|---|---|---:|---|---:|---|---|
| `town-walk` | `862042c5` | 0.143 / 0.143 / 0.144 | 0.143 | 0.582 / 0.575 / 0.598 | 0.585 | 26 / 26 / 25 | 4.5 / 4.2 / 4.2 |
| `town-walk` | `724618bb` | 0.131 / 0.132 / 0.130 | 0.131 | 0.431 / 0.443 / 0.431 | 0.435 | 0 / 0 / 0 | 0 |
| `many-instances` | `862042c5` | 1.265 / 1.498 / 1.360 | 1.374 | 2.316 / 3.011 / 2.909 | 2.745 | 131 / 133 / 103 | 26.2 / 27.6 / 22.7 |
| `many-instances` | `724618bb` | 1.167 / 1.277 / 1.171 | 1.205 | 2.154 / 2.438 / 2.142 | 2.245 | 0 / 0 / 0 | 0 |

The mean tick falls by 8% in `town-walk` and 12% in `many-instances`, and p99 by 26% and 18%: the scenario tick both
builds and flushes the packets, so it shows the net of the seal leaving `Create` and entering the outbox flush (see
[outbox flush](#the-send-path-875) for the flush alone), and no collection runs at all.

### Results — server packets encoded into pooled segments, sealed at the drain (#875, 2026-10-10)

A server packet's `Create` used to serialize its message, seal it with the session's `Encryptor` into a new array (the
payload plus the nonce and tag), and wrap that in a `NetworkPacket`; the outbox then serialized the `NetworkPacket`
again through protobuf-net to frame it (`OutboxSerializer`). Now `Create` encodes the message, plain, into a pooled
`PayloadSegment` (`PacketEncoder`) and returns an `OutboundPacket` struct; the outbox seals the payload straight into
its burst buffer as it frames it (`PacketEnvelope`, `SealInto`) and returns the segment to its pool. No array, no
`NetworkPacket` and no framing serializer per packet sent. What the moving scenarios still allocate, 51.2 B per player
per tick in both, includes the message object each packet is built from (48 B for a state ack).

Allocations, from `perf/scenario-allocations.json` regenerated at `6eafe191` (the developer machine's Release run,
i9-12900K, Windows 11, .NET 10.0.12):

| Scenario | Players | bytes/window | B/tick | B/player/tick | Before | Change |
|---|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 | 0 | 0 | 0.00 | 0 | 0 |
| `town-walk` | 30 | 92,160 | 1,536 | 51.20 | 1,124,400 | −91.8% |
| `many-instances` | 500 | 1,536,000 | 25,600 | 51.20 | 6,329,728 | −75.7% |

### Results — the header struct (#875, 2026-10-10)

`NetworkPacketHeader` was a class, and every sent packet allocated two of them (32 B each): one from `NetworkPacket`'s
`Header { get; set; } = new()` initializer and one from `PacketSerializationHelper.Serialize`'s
`Header = new NetworkPacketHeader { … }`, which replaced it. As a struct it allocates nothing, on the send path as on the
receive path (`InboundPacketFrame.ParseFrame` reads it by hand). Both moving scenarios fall by about 63.5 B per player per
tick, the two header objects of the one state ack each walking player is sent per tick (63.7 in `town-walk`, 63.5 in
`many-instances`). The scenarios now also dispatch each walker's input through `PacketDispatchTelemetry`, as
`WorldConnection.ProcessQueue` does; for `CMSG_PLAYER_INPUT` that dispatch is measured at 0 B (it was 248 B per packet
with a scope-reading log provider, see [instrumentation](instrumentation.md#packet-handlers)).

Allocations, from `perf/scenario-allocations.json` regenerated at `44d0db3a` (the developer machine's Release run,
i9-12900K, Windows 11, .NET 10.0.12):

| Scenario | Players | bytes/window | B/tick | B/player/tick | Before | Change |
|---|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 | 0 | 0 | 0.00 | 0 | 0 |
| `town-walk` | 30 | 1,124,400 | 18,740 | 624.67 | 1,239,120 | −9.3% |
| `many-instances` | 500 | 6,329,728 | 105,495 | 210.99 | 8,233,728 | −23.1% |

### Results — no closure per flusher call (#875, 2026-10-09)

Two methods the post-update flushers call for every connection on every tick declared a lambda over a local whose
scope was the whole method, so the compiler allocated the lambda's closure on entry, before the early return that
almost every call takes: `InventoryUpdateFlusher.Flush` (the slot query captures the character, 32 B) and
`QuestService.EnteredInstanceIfChanged` (the hook captures the instance, 40 B). Both moved the lambda into a method
called only when there is work. Measured in process with 200 connections and nothing changed: the inventory flush
went from 6,400 B and 1.6 µs per tick to 0 B and 0.8 µs, and `EnteredInstanceIfChanged` from 40 B per call to 0. At
200 players that is 72 B per player per tick, about 0.86 MB/s less garbage from the tick thread. The committed
figures do not move: the scenario tick did not run the flushers before, and now runs the inventory, sheet and
ability-amount flushers, guarded at 0 by `town-idle`.

### Results — no task per outbox flush (#875, 2026-10-09)

`TickDrivenOutbox.Flush` observed every write through `WriteAsync(...).AsTask().ContinueWith(...)`. A write that is
over before `WriteAsync` returns, which is every write to a socket with room in its send buffer, `SslStream` included,
and every write to the scenarios' counting stream, still cost a continuation task: 112 B per connection that had
anything to send, every tick. The flush now reads a finished write's outcome inline and keeps the continuation for a
write still in flight or already failed. The existing `TickDrivenOutbox` tests cover the three ways a write ends.

Allocations, from `perf/scenario-allocations.json` (the developer machine's Release run, i9-12900K, Windows 11,
.NET 10.0.12): each scenario falls by about 112 B per player per tick, the continuation (111.7 in `town-walk`, 111.9
in `many-instances`).

| Scenario | Players | bytes/window | B/tick | B/player/tick | Before #875 | Change |
|---|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 | 0 | 0 | 0.00 | 0 | 0 |
| `town-walk` | 30 | 1,239,120 | 20,652 | 688.40 | 1,440,240 | −14.0% |
| `many-instances` | 500 | 8,233,728 | 137,229 | 274.46 | 11,589,728 | −29.0% |

Like for like on the same machine (Release, the runner, 5 s warm-up, 3600 timed ticks), `77000278` and with #875:

| Scenario | bytes/window before | after | Tick ms mean before | after | p99 before | after | gen0 before | after |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `town-walk` | 1,440,960 | 1,238,640 | 0.142 | 0.143 | 0.573 | 0.469 | 33 | 27 |
| `many-instances` | 11,589,728 | 8,233,728 | 1.278 | 1.209 | 2.292 | 2.239 | 239 | 42 |

Over real sockets ([outbox flush](#the-send-path-875), 200 connections), the tick
thread allocated 22,400 B per tick before, in every mode, TLS included, and 0 B after, on Linux and Windows. The flush
time does not move by more than run-to-run noise: the task was cheap to run, only not to collect. At 200 players with
something to send each tick, that is about 1.3 MB/s less garbage from the tick thread.

### Results — the cached cipher delegates (#854, 2026-10-09)

Every send passed `CryptoSession.Encrypt` to its packet's `Create` as a method group, a new delegate per packet; the
read loop did the same with `CryptoSession.Decrypt`. The session now exposes `Encryptor`, created once with it, and
`Connection` creates its decrypt delegate once with its session. `SessionDelegatesShould` fails if a method group
comes back anywhere in `src/`.

Allocations, from `perf/scenario-allocations.json`: the developer machine's Release run with #854 (i9-12900K, Windows
11, .NET 10.0.12). "Before #854" is the committed figure before it (#851's, which carried the CI runner's `Encrypt`
delegate). "Fails from" and "Notice at" are at the 5% band of the time; #852 narrowed it to 1% (see
[The allocation gate](#the-allocation-gate)).

| Scenario | Players | bytes/window | B/tick | B/player/tick | Fails from | Notice at | Before #854 | Change |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 | 0 | 0 | 0.00 | 257 | — | 0 | 0 |
| `town-walk` | 30 | 1,440,240 | 24,004 | 800.13 | 1,512,253 | 1,368,227 | 1,570,560 | −8.3% |
| `many-instances` | 500 | 11,589,728 | 193,162 | 386.32 | 12,169,215 | 11,010,241 | 13,769,728 | −15.8% |

Like for like on the developer machine (Release, the runner, 10 s warm-up, 3600 timed ticks), `main` at `a892abe7`
and with #854:

| Scenario | bytes/window before | after | Change | Tick ms mean before | after | p95 before | after |
|---|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 0 | 0 | 0 | 0.037 | 0.031 | 0.065 | 0.054 |
| `town-walk` | 1,455,840 | 1,440,240 | −15,600 | 0.193 | 0.191 | 0.550 | 0.490 |
| `many-instances` | 11,849,728 | 11,589,728 | −260,000 | 2.021 | 1.954 | 3.501 | 3.500 |

- **The committed figures fall by the delegate CI paid**: 130,320 B per window in `town-walk` and 2,180,000 B in
  `many-instances`, about 72 B per walking player per tick.
- **The developer machine gains too**, about 8.7 B per walking player per tick (15,600 and 260,000 B per window), in
  code the optimised tier had already reached: most likely not every send site's delegate was kept on the stack. Both
  scenarios fall by the same amount per player per tick, as a cost of the per-player send path would.
- **No dependency on the JIT is left on this path.** The same Release runner with stack allocation turned off
  (`DOTNET_JitObjectStackAllocation=0`) reads 1,439,520 and 11,589,728 B, and the Debug gate 1,440,688 and
  11,587,728 B, within run-to-run noise of the committed figures. Before #854 the same switch added 64 B per walking
  player per tick.
- **The timings are not read as a change.** They move between runs by more than a delegate could cost.

### Results — the tick's instance snapshot (#851, 2026-10-09)

`World.Update` used to tick a fresh copy of the registry's instance list (`InstanceRegistry.ActiveInstances`, a
`ToList` of a `ConcurrentDictionary`'s values) every tick and walk it through a boxed enumerator. It now ticks
`InstanceRegistry.TickInstances`: an array the registry rebuilds only after it publishes or removes an instance, in
publication order, walked as a span. `ActiveInstances` keeps its fresh copy for the readers off the tick (the gauges,
presence, the hot reload walk).

Allocations, from `perf/scenario-allocations.json` at the time: the developer machine's Release run at `7a40ed08`
plus the `Encrypt` delegate CI still allocated then (see #854 above). They were derived, not measured on CI: the
Release run plus 64 B × 60 ticks × the walking players (115,200 B per window in `town-walk`, 1,920,000 B in
`many-instances`); the offset #850 measured between CI and the developer machine was slightly larger (+116,584 B and
+1,924,000 B). "Before #851" is the committed figure before it, CI's run
of #850.

| Scenario | Players | bytes/window | B/tick | B/player/tick | Fails from | Notice at | Before #851 | Change |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 | 0 | 0 | 0.00 | 257 | — | 9,600 | −100% |
| `town-walk` | 30 | 1,570,560 | 26,176 | 872.53 | 1,649,089 | 1,492,031 | 1,581,064 | −0.7% |
| `many-instances` | 500 | 13,769,728 | 229,495 | 458.99 | 14,458,215 | 13,081,241 | 14,018,368 | −1.8% |

Like for like on the developer machine (Release, the same i9-12900K), before and after:

| Scenario | bytes/window before | after | Change | Tick ms mean before | after | p95 before | after |
|---|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 9,600 | 0 | −9,600 | 0.034 | 0.030 | 0.057 | 0.053 |
| `town-walk` | 1,464,480 | 1,455,360 | −9,120 | 0.211 | 0.171 | 0.739 | 0.444 |
| `many-instances` | 12,094,368 | 11,849,728 | −244,640 | 2.235 | 2.027 | 3.799 | 3.621 |

- **An idle tick allocates nothing.** `town-idle` falls from 160 B per tick to 0: the copy was all it allocated.
- **The saving grows with the instances.** About 160 B per tick with one instance and about 4 KB per tick
  (244,640 B per window) with 250, the size of the copy; with no instance published or removed, the walk allocates
  nothing at any count. A tick that publishes or removes one allocates one array of the live instances.
- **The timings are not read as a change.** The tick time moves between runs by more than the copy could cost.

### Results — after the platform cipher (#850, 2026-10-09)

Allocations, from `perf/scenario-allocations.json` at the time: CI's Release run of #850 on its GitHub-hosted runner
(2026-10-09). "Fails from" is the first figure the gate fails on; "Notice at" the highest figure that prints the
improvement notice. "Before #850" is the committed figure before it (the first baseline below, a Release run on the
developer machine).

| Scenario | Players | bytes/window | B/tick | B/player/tick | Fails from | Notice at | Before #850 | Change |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 | 9,600 | 160 | 5.33 | 10,081 | 9,119 | 9,600 | 0 |
| `town-walk` | 30 | 1,581,064 | 26,351 | 878.37 | 1,660,118 | 1,502,010 | 5,964,720 | −73.5% |
| `many-instances` | 500 | 14,018,368 | 233,639 | 467.28 | 14,719,287 | 13,317,449 | 62,226,368 | −77.5% |

The committed change mixes machines. Like for like, on the developer machine below, the walking scenarios fell to
1,464,952 B (`town-walk`, −75.4%) and 12,098,368 B (`many-instances`, −80.6%); the difference was the `Encrypt`
delegate, still on the heap when CI's warm-up ended (see #854 above).

Timing and GC, from the developer machine's Release run (2026-10-09; the same machine as the first baseline:
i9-12900K, Windows 11 Pro, .NET 10.0.12, Server GC; 10 s warm-up, 3600 timed ticks):

| Scenario | Tick ms mean | p95 | p99 | max | % > 16.7 ms | gen0 | gen1 | gen2 | GC pause ms | GC pause % |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 0.033 | 0.059 | 0.076 | 0.175 | 0.00 | 0 | 0 | 0 | 0.00 | 0.00 |
| `town-walk` | 0.182 | 0.479 | 0.858 | 2.900 | 0.00 | 7 | 0 | 0 | 2.52 | 0.38 |
| `many-instances` | 2.099 | 3.533 | 4.456 | 11.257 | 0.00 | 234 | 0 | 0 | 58.12 | 0.77 |

The homelab k3s node, the machine the capacity runs will measure (2026-10-09; Linux, AMD Ryzen 9 5900X, 12 cores / 24
threads, .NET 10.0.12, Server GC, Release, in the `dotnet/sdk:10.0` container, 0 players online on the node's worlds
at the time). Its allocations match the developer machine's, not CI's:

| Scenario | Players / instances | bytes/window | B/player/tick | Tick ms mean | p95 | p99 | max | % > 16.7 ms | gen0 | gen1 | gen2 | GC pause ms | GC pause % |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 / 1 | 9,600 | 5.33 | 0.041 | 0.042 | 0.045 | 0.054 | 0.00 | 0 | 0 | 0 | 0.00 | 0.00 |
| `town-walk` | 30 / 1 | 1,466,160 | 814.53 | 0.212 | 0.594 | 0.654 | 1.909 | 0.00 | 7 | 0 | 0 | 4.04 | 0.53 |
| `many-instances` | 500 / 250 | 12,094,368 | 403.15 | 1.626 | 2.562 | 2.789 | 5.312 | 0.00 | 235 | 0 | 0 | 54.15 | 0.92 |

- **The cipher was three quarters of what a walking player allocated.** `town-walk` falls from 3.3 KB to 814 B per
  player per tick and `many-instances` from 2.1 KB to 403 B on the developer machine and the homelab node (878 B and
  467 B in CI's figures);
  `town-idle`, which sends nothing, is unchanged. What is
  left per walker is the state acknowledgement itself (its serialization and the sealed `byte[]`) and the input path.
- **GC follows.** `town-walk` ran 7 gen0 collections in its timed minute against 116, and spent 0.38% of the time in
  GC pauses against 3.09%; `many-instances` 0.77% against 1.63%, with no gen1 or gen2 collection. Its gen0 count is
  about the same (234 against 225) at a fifth of the allocation, which was not investigated. The mean and p95
  tick fell (`town-walk` 0.220 → 0.182 ms mean, `many-instances` 2.564 → 2.099 ms); the max is one tick per run and
  moves between runs (`many-instances` 11.3 ms here against 7.7 ms), so it is not read as a change.

### Results — first baseline (2026-10-08)

Allocations, from `perf/scenario-allocations.json` at the time (commit `9dec695b`, Release; regenerated when the
scenario tick gained the registry's `PublishFinished`, which moved no figure beyond run-to-run noise), before #850.

| Scenario | Players | bytes/window | B/tick | B/player/tick | Fails from | Notice at |
|---|---:|---:|---:|---:|---:|---:|
| `town-idle` | 30 | 9,600 | 160 | 5.33 | 10,081 | 9,119 |
| `town-walk` | 30 | 5,964,720 | 99,412 | 3,313.73 | 6,262,957 | 5,666,483 |
| `many-instances` | 500 | 62,226,368 | 1,037,106 | 2,074.21 | 65,337,687 | 59,115,049 |

Timing and GC, from the runner's first Release run (2026-10-08, commit `799fc830`; 12th Gen Intel Core i9-12900K,
16 cores / 24 logical processors, 64 GB, Windows 11 Pro, .NET 10.0.12, Server GC, latency mode Interactive; 10 s
warm-up, 3600 timed ticks). Its allocation figures were within 0.01% of the committed ones.

| Scenario | Tick ms mean | p95 | p99 | max | % > 16.7 ms | gen0 | gen1 | gen2 | GC pause ms | GC pause % |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `town-idle` | 0.037 | 0.065 | 0.082 | 0.273 | 0.00 | 0 | 0 | 0 | 0.00 | 0.00 |
| `town-walk` | 0.220 | 0.702 | 1.052 | 4.385 | 0.00 | 116 | 0 | 0 | 24.57 | 3.09 |
| `many-instances` | 2.564 | 4.399 | 5.766 | 7.713 | 0.00 | 225 | 2 | 2 | 150.57 | 1.63 |

### Key observations

- **An idle tick still allocates 160 B, every tick.** `town-idle` has nothing to do, yet each tick copies
  `InstanceRegistry.ActiveInstances` and `InstanceTicker` walks the copy through a boxed enumerator (issue #851).
  It is small, but it is a fixed cost of every tick, and the floor the other scenarios stand on. #851 removed it
  (results above).
- **Walking costs kilobytes per player per tick, and most of it is encryption.** About 3.3 KB per player per tick in
  `town-walk` and 2.1 KB in `many-instances`, dominated by the per-packet BouncyCastle encrypt (1.7–4.6 KB per call in
  the session cipher results above) of the state acknowledgement each walker is sent every tick. Issue #850 was the
  fix, measured in the results above it.
- **The allocation rate shows up as GC.** `town-walk` allocates about 6 MB per 60-tick window on the tick thread and
  ran 116 gen0 collections in its timed minute, 3.1% of the time in GC pauses; `many-instances` allocates about 62 MB
  per window, with 225 gen0, 2 gen1 and 2 gen2 collections. No tick went over the 16.7 ms budget, but the worst ticks
  (4.4 ms and 7.7 ms) sit well above the p99, as a few ticks absorbing a GC pause would.
- **Tick time follows the walking players, not the instance count.** `many-instances` holds about 17 times the
  players of `town-walk` in 250 times the instances, and its mean tick is about 12 times longer: a little less per
  player (`town-walk` also runs ten creatures), so at this scale the instance count adds no cost that stands out. Its p95 of 4.4 ms is about a quarter of the tick
  budget for 500 walking players, before saves, combat or the session pass.
