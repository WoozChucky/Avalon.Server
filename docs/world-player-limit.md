# World player limit

Each world may be given a limit on the accounts it admits (#967). The limit is enforced where join tickets are issued,
by the identity service, which keeps per world a ledger of the places it has given out and, for a full world, a line
of the accounts waiting for one. The world keeps a backstop of its own. A world with no limit behaves as before.

The feature ships in five parts. This page describes what is in place now and marks the rest:

| Part | What it adds | State |
|---|---|---|
| 1 | The `PlayerLimit` column and its endpoint; the ledger, kept with every session; true counts in the world lists | in place |
| 2 | The reason a world gives for a session's end; the held place after a drop | from PR 2 |
| 3 | Enforcement at the join (`WORLD_FULL`), the `Full` status, the world's backstop | from PR 3 |
| 4 | The queue: the "queued" answer, the status and leave calls, their rate limit, the metrics | from PR 4 |
| 5 | The goodbye opcode (`CMSG_LEAVE_WORLD`), so a quit frees its place at once | from PR 5 |

Until part 3 nothing is enforced: every join is admitted as before, and the ledger only counts.

## What the limit is

`Worlds.PlayerLimit` (auth database) is a nullable integer from 1 to 100,000 (`World.MaxPlayerLimit`, and the check
constraint `CK_Worlds_PlayerLimit`); null means no limit. Identity reads it from the row on each join request and
passes it to the ledger's script; it is never copied into Redis.

What the limit counts is **places taken**: every account holding a place in the world, whether it is in the world, at
character select, holding a join ticket it has not redeemed yet, or (from part 2) holding its place after a dropped
connection. This is not the gauge `avalon.world.players.online`, which counts only the characters in the world: an
account at character select holds a place but no character.

Staff (accounts with the Admin flag, the flag that passes maintenance) are always given a place, so places taken can
exceed the limit by the staff admitted (from part 3).

## Places

The ledger is kept whether or not a limit is set, so the world lists show a true count and a limit set later starts
from it. Identity is its only writer.

Each place records who holds it, so that no late or stale call removes or shortens a place another holder still has.
A stale call can at most keep a place counted a little longer (see the last point below), never count it out:

- **The ticket's hold.** A join or reconnect ticket is known to the ledger by the id of the session it will become (its
  grant's `GameSessionId`, which the redemption and the activation carry). Its hold on the place runs until 15 s after
  the ticket's expiry (`GameAuthPolicy.TicketPlaceMargin`). The hold names the last ticket to enter, with the later of
  its end and the hold already there. The hold goes with the ticket's withdrawal, its own session's activation, or the
  place itself: the session's end when no hold runs any more, the sweep of an expired place, the activation of the
  account's session in another world once the hold is over.
- **The owning session.** The session that owns the place's lease, by its fencing token (`GameSessions.FencingToken`,
  which only grows for an account: a takeover's session has a larger one than the session it replaces). Redemption,
  activation and heartbeats make their session the owner; a session older than the owner renews and ends nothing. A
  session's end that keeps the place (held after a drop, or kept by a ticket's hold) records the session as ended
  there, so that session's own late heartbeats, and older ones', change nothing.

| Moment | The place |
|---|---|
| A join or reconnect ticket is issued | taken, kept or returned (a held place, a turn); never shortened (a held place keeps its grace); the ticket holds it until 15 s after its expiry |
| The ticket's issue is known to have failed (its write was refused) | the ticket's hold is given back, best effort, and the place with it unless a live session owns it; nothing when another ticket entered since. An issue that threw (a cancellation, a timeout) gives nothing back: its write may have landed |
| The same request is replayed (its `Idempotency-Key`) | while the ticket is unredeemed, its place, if still there, is extended (never shortened) and held by that ticket again. A replay never creates a place, so a used ticket's replay cannot take back a place given to another |
| The world redeems the ticket | the pending session owns the place, to its lease |
| The session is activated | renewed to the lease; it spends its own ticket's hold (never another ticket's); the account's place in every other world is removed, since a session is one per account, but for a ticket's hold still running there |
| Each heartbeat (every 15 s) | renewed to the lease's new end, or a ticket's later hold |
| The session ends (identity answers `ended`) | removed (from part 2, held for the grace after a drop), unless a ticket's hold still runs: then it stays until that hold ends. Never shortened, and never re-created when it is already gone |
| The end or heartbeat of a session older than the owner, or of one that ended there (arriving late) | unchanged |
| The session's end is refused as `SESSION_REPLACED` or `BARRIER_PENDING` | unchanged |
| The world cannot report (a crash) | expires with the lease, within 45 s |

- An account holds at most one place per world, so a reconnect or a takeover of one's own session never needs a
  second place. The ticket of a takeover or a reconnect keeps the place through the end of the session it replaces,
  should that end arrive before the ticket is redeemed: the ledger never counts the account out in between.
- The capacity step is the join's last: every check before it (the request, the credential, the account's standing,
  the application, the allocator's non-disclosing refusals, the session rules) answers first and takes no place, so
  capacity never speaks for a world the context may not enter.
- A ticket that is never redeemed holds its place until 15 s after its expiry, then the place lapses.
- A session's end that removes the place leaves no record. A heartbeat of that session crossing its end in flight (the
  end first) may put the place back, owned by that session, until the later of the lease it carried and a ticket's
  hold still running: at most 45 s after that heartbeat or that ticket's issue. Likewise a replay that extends a
  place just before its ticket is redeemed and its session ends keeps that place counted until the replayed hold ends,
  at most 45 s after the replay. Both only ever count the account in for longer, never out.

## Setting a limit

`PUT /world/{id}/player-limit` (worlds service, Admin):

```json
{ "playerLimit": 300 }
```

```json
{ "playerLimit": null }
```

- `playerLimit` is required, null included: an empty body is refused (400) rather than read as "no limit". A value
  outside 1 to 100,000 is 400; an unknown world 404. The reply is the world's `WorldDto`, read from the row as written.
- The limit is written by column (`PlayerLimit` and `UpdatedAt` alone, `IWorldRepository.SetPlayerLimitAsync`), so a
  maintenance transition or a metadata edit written at the same time survives, and a metadata edit (`PUT /world/{id}`)
  never writes the limit.
- Each change is logged at Information with who made it and the limit before and after.
- It takes effect on identity's next join request (enforced from part 3). Lowering it below places taken removes
  nobody: nobody new enters until places taken falls below it.
- The world's backstop (from part 3) has its own limit, the world's configuration `Game:PlayerLimit` (Helm
  `server.game.playerLimit`), which the operator sets to the same number.

The world lists show the counts: `GET /world` and `GET /world/{id}` fill `WorldDto.OnlineCount` with places taken and
carry `playerLimit` and `queueLength`; each world of identity's `POST /game/worlds` carries `status` (`Online` or
`Maintenance`), `playerCount`, `playerLimit` and `queueLength`, and a join reply's `destination` carries `status` and
`playerLimit` too. The counts read only entries still current (a place whose expiry, or a waiter whose check-in
deadline, is still ahead), so what no script has swept yet is not shown. When Redis cannot be read the counts are 0;
an error Redis answers (a missing permission, `NOPERM`) is also logged at Warning, at most every 10 s
(`RedisWorldOccupancy`), so a Redis user without the rules below does not show every world empty in silence.

## Redis

Six keys, all named in `CacheKeys` and described in [Redis cache keys](redis-cache-keys.md#world-places-and-queue-967):

| Key | Type | Content |
|---|---|---|
| `world:{worldId}:places` | sorted set | the ledger: member the account id, score the moment the place expires (Unix ms) |
| `world:{worldId}:places:marks` | hash | per account two fields at most: `{accountId}`, the kind of place, `h` (held after a drop), `t:{enqueuedAtMs}` (a turn taken, not yet used) or `j:{untilMs}:{ticket}` (a join ticket's hold, the last ticket to enter, by its session id), and none for a place a session alone holds; `{accountId}:s`, the fencing token of the session that owns the place (`{fence}`), or that ended with the place kept (`e:{fence}`) |
| `world:{worldId}:queue` | sorted set | the line: member the account id, score its arrival number |
| `world:{worldId}:queue:checkins` | sorted set | member the account id, score the moment the waiter leaves the line unless it checks in again (Unix ms) |
| `world:{worldId}:queue:arrivals` | string | the arrival counter |
| `world-queue:account:{accountId}` | string | `{worldId}:{enqueuedAtMs}`: the world the account waits for; a day's expiry, as garbage collection only |

- Every change is one Lua script (`RedisWorldPlaceStore`), so "check the limit and take a place" and "leave the line
  and take a place" are single steps. "Now" is passed in by identity from its `TimeProvider`; the scripts never call
  Redis `TIME`.
- An entry whose time has passed does not count and is removed by the next script that runs for its world.
- **Single-instance Redis only.** Joining a line leaves the account's line in another world, which the script names
  from the account's waiting key rather than from its declared keys, and an activation removes the account's place
  in every configured world in one call. Both hold on one Redis instance, which every environment runs; neither would
  on a Redis Cluster.
- **Redis unavailable at a join** fails it closed: the join answers 503 `SERVICE_UNAVAILABLE` and no ticket is issued.
- **Redis unavailable at a redemption, activation, heartbeat, end or withdrawal** changes nothing for the session: the failure is
  logged at Warning at most every 10 s, with the count left out (`PlaceLedgerOutage`), and the next renewal that
  reaches Redis puts the place back.
- **A lost ledger** (Redis emptied or replaced) is rebuilt by the heartbeats: every admitted account's next heartbeat
  puts its place back, within 15 s, over the limit if need be; an admitted player is never removed for capacity.
- The identity service's Redis user needs the six patterns and the sorted-set commands its scripts call; the worlds
  service's user reads two of the keys (see [Redis users per API service](redis-cache-keys.md#redis-users-per-api-service)).
  Without them every join answers 503, the script failing with `NOPERM`.

## Releasing

Each step only after the one before is done:

1. **homelab#199 deployed**: the Redis users carry the rules of
   [Redis users per API service](redis-cache-keys.md#redis-users-per-api-service) (`api-identity` the six key patterns
   and the sorted-set and hash commands; `api-worlds` read on `world:*:places` and `world:*:queue:checkins`, and
   `zcount`). Check on the cluster before the identity release rolls:
   `kubectl -n avalon exec avalon-redis-0 -- redis-cli -a "$PW" ACL GETUSER api-identity` (and `api-worlds`).
   Deployed without them, every join answers 503.
2. Identity, then the worlds service and the worlds, as in every release (identity before the worlds).
3. Check: `/game/worlds` and `GET /world` show places taken for a world with players, and identity's and the worlds
   service's logs carry no "could not read world ... places" Warning.

## Testing the scripts

The rules live in the Redis scripts, and the unit tests run no Redis. Every place and queue operation sits behind one
interface, `IWorldPlaceStore`, with two implementations: `RedisWorldPlaceStore` and `MemoryWorldPlaceStore` (in
`tests/Avalon.Api.Testing`, every operation under one lock, the reference the scripts are held to). One scenario suite,
`WorldPlaceScenarios`, runs against both: CI runs it against the memory store (`MemoryWorldPlaceStoreShould`), and the
Redis runner (`RedisWorldPlaceStoreShould`) is skipped unless `AVALON_TEST_REDIS` names a throwaway Redis (xUnit reports
its two theories as 2 skipped, without enumerating their rows). Before a PR that changes a script, run it there (Git
Bash; Docker must be running):

```bash
docker run -d --rm --name avalon-redis-967 -p 56379:6379 redis:8
AVALON_TEST_REDIS=127.0.0.1:56379 dotnet test tests/Avalon.Api.Identity.UnitTests --no-build \
  --filter "FullyQualifiedName~RedisWorldPlaceStoreShould" --logger "console;verbosity=normal"
docker exec avalon-redis-967 redis-server --version
docker stop avalon-redis-967
```

PowerShell: `$env:AVALON_TEST_REDIS = '127.0.0.1:56379'` before the `dotnet test` line, `Remove-Item Env:AVALON_TEST_REDIS`
after. Expected: `Passed!  - Failed: 0, Passed: 32, Skipped: 0` (the 31 scenarios and the world list's count). A
scenario that passes in memory and fails against Redis is a script bug: the memory store is the reference. Each run
takes fresh world and account ids, so it needs an empty Redis no more than it changes one. The PR carries the summary
line and the `redis-server --version` line.
