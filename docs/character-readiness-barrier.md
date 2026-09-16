# Character Readiness Barrier

A selected character does not enter the world until its client says it is ready.

## Why select was the wrong place to spawn

`CharacterSelectHandler` used to finish by assigning `connection.Character` and calling
`World.SpawnInInstance`. That is the line after which `MapInstance.Update` can see the entity, so a
player became visible to everyone else in the instance the instant they clicked a character — before
their client had received `SChunkLayoutPacket`, composed the chunks, or baked its navmesh. Other
players saw a character standing in a town whose owner had no map yet, and any broadcast aimed at
that character went to a client with nowhere to put it.

## Three states, not two

Select is a chain of database round trips handed back through the connection's continuation queue.
`connection.Character` is null for all of it and always was; the barrier adds a second span where a
built character is waiting on its client. Both spans have to be recognisable, so a connection says
which of three states it is in:

| | meaning |
|---|---|
| `SelectInProgress` | a select was accepted and is still loading. `Character` and `PendingSpawn` are both null. |
| `PendingSpawn` | the character is built and waiting for its client. `Character` is still null. |
| `Character` | spawned, in an instance, visible to the tick. |

The list, create, delete and select handlers refuse on **any** of the three. Before this they checked
only `Character`, so a `CMSG_CHARACTER_DELETE` pipelined behind a `CMSG_CHARACTER_SELECTED` for the
same account passed its ownership check, found no state check, and deleted the character that was
being spawned. A second `CMSG_CHARACTER_SELECTED` orphaned the entity the first one was building.

## What the barrier is

Select still does all of its work: it loads the row, builds the `CharacterEntity`, resolves the
instance, and sends `SCharacterSelectedPacket`, `SChunkLayoutPacket` and `SCharacterAbilitiesPacket`.
It then hands the entity to the connection as a **pending spawn** (`IWorldConnection.SetPendingSpawn`)
instead of spawning it. Nothing on the tick can see the character while one is pending.

Two things release it, both through `CharacterReadinessBarrier.Release`:

- **`CMSG_CHARACTER_LOADED` (0x2014)** from the client — `CharacterLoadedHandler`.
- **The world tick**, once the wait expires — `CharacterReadinessBarrier.ReleaseExpired`, called
  from `WorldServer.Update` after the session pass and before the world update.

Release assigns `connection.Character` and calls `World.SpawnInInstance`. If the spawn throws it
puts `Character` back to null, hands the pending spawn back, and closes the connection: a character
assigned but not in an instance would be visible to every reader of `Character` while belonging to
no map, and the despawn can only write the row back through a pending spawn.

## Online is a property of being in the world

`World.SpawnInInstance` marks the row online, mirroring `DeSpawnPlayerAsync` marking it offline.
Select persists the row — it may have corrected the map — but persists it **offline**, because
between select and release the character is built and not in the world. A row written online at
select is a claim nothing can retract: a disconnect in that window has no instance membership for
the despawn to write back from.

## When it expires

`Game:CharacterLoadTimeoutSeconds` (default **15**). A client that never reports in must not strand a
connected player outside the world, so the tick spawns them anyway and logs a warning naming the
character, the account and how long it waited. A client that does report in waits none of it.

## What a client must send

After receiving `SCharacterSelectedPacket` and — when one arrives — `SChunkLayoutPacket`, and after
finishing whatever loading those imply, send `CMSG_CHARACTER_LOADED` with an empty body. It is
`NetworkPacketFlags.Encrypted` like every other post-handshake packet, and the message
(`CCharacterLoadedPacket`) has no fields, so the sealed payload is zero bytes.

The server answers nothing. The character entering the world is observable as the entity broadcasts
that follow. Sending it twice, or after the barrier already expired, or without having selected a
character, is a no-op logged at debug; it does not close the connection.

Until a client sends it, every login takes the timeout path and waits the full
`CharacterLoadTimeoutSeconds` before entering the world.

## Notes for anyone changing this

- **`CMSG_CHARACTER_LOADED` is in the session filter unconditionally** — above the
  `Character != null` early return, not below it. `OnReceive` queues a packet a filter accepted **at
  arrival**, and `ProcessQueue` **peeks** at dispatch, so a packet whose acceptance changed in
  between is not dropped: it sits at the head of that connection's queue and nothing behind it is
  ever dispatched. The barrier can expire between the report being queued and the pass that
  dispatches it, which is exactly when a `Character != null` guard on that line would wedge the
  connection. `ProcessQueueWedgeShould` holds that premise.
- **A test for anything in this area must start the chain, not start after it.** A fixture handed a
  ready-made `PendingSpawn` begins after the interesting window has closed and will agree with any
  guard. `CharacterSelectChainShould` drives a real `WorldConnection` from the select packet and
  steps its continuation queue one flush at a time.
- The select chain clears `SelectInProgress` on every path that gives up. A continuation that faults
  is dropped by `ProcessContinuations` without reaching either, so the flag stays set and the next
  select-phase packet closes the connection. That is the safe direction, but it is a disconnect
  rather than a recovery.
- Handlers are constructed once from the **root** service provider while repositories and
  `DbContext`s are registered `AddScoped`, so every handler shares one `CharacterDbContext` for the
  life of the process. Unrelated to the barrier, and not changed here.
