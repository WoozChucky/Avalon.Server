# World player limit

Each world may be given a limit on the accounts it admits (#967). The limit is enforced where join tickets are issued,
by the identity service, which keeps per world a ledger of the places it has given out and, for a full world, a line
of the accounts waiting for one. The world keeps a backstop of its own. A world with no limit behaves as before.

The feature ships in five parts. This page describes what is in place now and marks the rest:

| Part | What it adds | State |
|---|---|---|
| 1 | The `PlayerLimit` column and its endpoint; the ledger, kept with every session; true counts in the world lists | in place |
| 2 | The reason a world gives for a session's end; the held place after a drop | in place |
| 3 | Enforcement at the join (`WORLD_FULL`), the `Full` status, the world's backstop | from PR 3 |
| 4 | The queue: the "queued" answer, the status and leave calls, their rate limit, the metrics | from PR 4 |
| 5 | The goodbye opcode (`CMSG_LEAVE_WORLD`), so a quit frees its place at once | from PR 5 |

Until part 3 nothing is enforced: every join is admitted as before, and the ledger only counts.

## What the limit is

`Worlds.PlayerLimit` (auth database) is a nullable integer from 1 to 100,000 (`World.MaxPlayerLimit`, and the check
constraint `CK_Worlds_PlayerLimit`); null means no limit. Identity reads it from the row on each join request and
passes it to the ledger's script; it is never copied into Redis.

What the limit counts is **places taken**: every account holding a place in the world, whether it is in the world, at
character select, holding a join ticket it has not redeemed yet, or holding its place after a dropped connection
([the held place](#the-held-place)). This is not the gauge `avalon.world.players.online`, which counts only the characters in the world: an
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
  activation and heartbeats make their session the owner; a session older than the owner renews and ends nothing.
  Every session's end, whatever it does to the place (removed, held after a drop, kept by a ticket's hold, or already
  gone), and every activation, in each other world of the account (whether it still has a place there or not), leaves
  a tombstone: the field records the session as ended there for one lease (45 s, counted by Redis from the moment it
  is written, `HPEXPIRE`), so that session's own late heartbeats, and older ones', change nothing, even once the place
  is gone. A newer session's fence still wins.

| Moment | The place |
|---|---|
| A join or reconnect ticket is issued | taken, kept or returned (a held place, a turn); never shortened (a held place keeps its grace); the ticket holds it until 15 s after its expiry |
| The ticket's issue is known to have failed (its write was refused) | the ticket's hold is given back, best effort, and the place with it unless a live session owns it; nothing when another ticket entered since. An issue that threw (a cancellation, a timeout) gives nothing back: its write may have landed |
| The same request is replayed (its `Idempotency-Key`) | while the ticket is unredeemed, its place, if still there, is extended (never shortened) and held by that ticket again. A replay never creates a place, so a used ticket's replay cannot take back a place given to another |
| The world redeems the ticket | the pending session owns the place, to its lease |
| The session is activated | renewed to the lease; it spends its own ticket's hold (never another ticket's); the account's place in every other world is removed, since a session is one per account, but for a ticket's hold still running there |
| Each heartbeat (every 15 s) | renewed to the lease's new end, or a ticket's later hold |
| The session ends (identity answers `ended`) | removed, or held for the grace after a drop ([the held place](#the-held-place)), unless a ticket's hold still runs: then it stays until that hold ends. Never shortened, and never re-created when it is already gone |
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
- A heartbeat of a session crossing its end in flight (the end first) changes nothing: the end's tombstone refuses
  it, whether the end removed the place or kept it, so a place given to another meanwhile is never taken back. The
  tombstone lasts one lease from the end, and a renewal still in flight at the end was sent within the session's lease,
  so it lands well before; the lease is counted by Redis from the end it records (a relative expiry), so no skew
  between identity's clock and Redis's shortens it. Should Redis refuse the expiry (no `hpexpire` permission, or a
  Redis before 7.4), the script deletes the `{accountId}:s` field it just wrote, whatever that field held before (a live
  owner's fence, or an earlier tombstone the call was re-arming), so the account is left with no owner or tombstone
  there, never with a field that lasts forever. The end and the activation still do the rest, and only this protection
  is lost (a late heartbeat can then bring a removed place back for at most one lease, counting the account in, never
  out). Identity logs the refusal at Warning, at most every 10 s: "Redis refused the expiry of an ended session's
  tombstone". What remains: a replay that
  extends a place just before its ticket is redeemed and its session ends keeps that place counted until the
  replayed hold ends, at most 45 s after the replay. That only ever counts the account in for longer, never out.

## The held place

A player whose connection drops keeps their place for a grace, `Application:WorldPlaces:HeldPlaceSeconds` (identity,
90 s by default): a join within it finds the place and goes straight in; after it the place expires and counts no
more. A deliberate quit, a kick, a ban, a maintenance cutoff or a restart releases the place at once.

The world says why each session ended: `sessions/end` carries an optional `reason`
([game server admission](steam-authentication-workloads.md#session-activation-and-durable-save-authority)), and
identity holds the place or removes it by it (`GameSessionEndReasons.HoldsPlace`). The world knows why a connection
closed only when it closed it itself, which it always does through `GracefulShutdownHelper` with a `DisconnectReason`;
the connection notes the first reason (`IDisconnectReasonNote`, mapped by `SessionEndReasons.For`), and the drain
reads it once, as it builds the session's end (`WorldConnection.SessionEndReason`), before the end waits in the
`SessionEnds` lane. Where the client is told one thing and identity another, the world notes the end's reason itself
before the close (`SessionEndReasons.Note`); the first noted wins. Nothing on the wire changed: the client is told the
same `DisconnectReason` as before.

| The world closed the connection, or saw it close, because | `reason` | The place |
|---|---|---|
| The client said goodbye (`CMSG_LEAVE_WORLD`, from part 5) | `logout` | removed |
| The peer closed it, or it was lost, with no goodbye | `drop` | held |
| The liveness rule closed it: no pong for 60 s (#1061) | `drop` | held |
| A slow connection (`SlowConnection`), the receive queue's cap, a failed character save (`CharacterSaveFailed`), a lease that ran out or was revoked | `drop` | held |
| A select the world could not finish (`SelectTimeout`: the stalled-select sweep, or a full work queue, #1050): the world failed the player, who is told to log in again | `drop` | held |
| The world entry check at character select or at the spawn refused the entry for anything but a cutoff or the account: it threw or took more than 5 s, the world has no maintenance row, or an allowed decision expired before the tick applied it. The client is told `Maintenance`, as before (`EntryRefusal`) | `drop` | held |
| An account-wide session end for the account's credentials or security (`world:accounts:disconnect`: a password, email or role change, an MFA reset or removal, a refresh-token reuse); the client is told `Kicked`, as before | `drop` | held |
| Another login took the session over (`DuplicateLogin`) | `drop` | unchanged: the old session's end is answered `SESSION_REPLACED` and changes nothing |
| Banned or deactivated (`world:accounts:status`: `Banned`, `Deactivated`), any other `Kicked` close (none in the world today), or the entry check refused the account itself (no longer active, `WorldEntryDecision.AccountRefused`) | `kicked` | removed |
| A maintenance cutoff in force (`Maintenance`: the cutoff's close; or an entry refused for the cutoff, `WorldEntryDecision.Cutoff`, or the cutoff or restart drain the world applies when it turns a non-Admin away at character select or the spawn, `WorldMaintenanceCoordinator.CutsOff`, whatever the gate's check said; only where the world applies no maintenance, an allowed decision that ended at the cutoff's deadline, `EndsAtCutoff`) | `maintenance` | removed, with every place the world holds after a drop |
| The world stopped or restarted (`ServerShutdown`), the restart drain included | `shutdown` | removed, with every place the world holds after a drop |
| The world's backstop refused the admission (from part 3) | `backstop` | removed |

- **Identity decides with the account's standing.** A reason that would hold the place holds it only for an account
  that is still active (not banned, deactivated or being consolidated, and not gone): identity reads the account at
  the end (`GameSessionFenceService.MayHoldAPlaceAsync`). So a ban whose notice the world missed (the world then sees
  its heartbeat refused and closes as a drop), or a ban between the ticket and the activation (the failed admission's
  end has no reason), still removes the place. A moved session epoch alone does not remove it: a credential change
  moves it too, and those hold. When the account cannot be read, the reason decides.
- **No reason is a drop.** A world from before part 2 sends none, and a failed admission's pending session, which the
  world abandons (#1013), is ended without one: identity reads both as a drop and holds the place (for an active
  account), so the client's next join finds it. A reason identity does not know (a newer world's) is a drop too.
  `select_timeout` is in the vocabulary and removes the place, but no world sends it: a `SelectTimeout` close is a drop.
- **Identity is deployed first.** An identity from before part 2 knows no held place: it removes the place at every
  end, and it refuses the unknown member (400, the body rejects unknown members). The world then sends the end again
  without the reason, so the session ends anyway. When that end succeeds, the world remembers it: until identity takes
  the reason, ends go without it, one request each, offering it again at most every 5 minutes (one Warning when it
  starts). An end refused for anything else, with or without the reason, remembers nothing.
- **A repeated end changes nothing.** The world may send the same end again (a retry, its first reply lost): identity
  answers `ended` again but touches no place (`GameSessions` tells the end that did it from a repeat, and the end's
  tombstone refuses one within its lease), so a repeat neither extends a held place nor brings one back. An end never
  brings back a place whose time has passed, swept or not.
- **A return that fails holds the place again.** A join within the grace takes the held place as its ticket's
  (`j:{untilMs}:{ticket}:h`); should that ticket's issue fail, its withdrawal holds the place again, to the end it has,
  rather than removing it.
- **After a stop or a maintenance cutoff nobody holds a place** (decision 10): everyone is equal and joins the line with
  everybody else. The world's ends tell identity when: the first `shutdown` or `maintenance` end of the world releases
  every place it holds after a drop (`IWorldPlaceStore.ReleaseHeldAsync`; their tombstones stay). A stop's ends come in
  a burst, so the release runs at most once every 10 s for a world (`IWorldPlaceStore.HeldReleaseWindow`: each identity
  replica asks Redis at most once in it, and the marks hash's field `released` holds it across replicas). The release
  scans the world's places, at most once in the window: tracking the held ones apart would need a key the identity
  Redis user's patterns do not grant. A drop whose end lands within those 10 s keeps its place until the next `shutdown` or
  `maintenance` end after them, or its grace. A place a ticket has taken since (a return within the grace) is that
  ticket's and stays: the ticket is already issued. A stop or a cutoff that ends no session (no player left, or Admins
  only) sends no such end, and the places held there run out with their grace, within 90 s.
- **An end that is not sent holds nothing.** An end skipped in the `SessionEnds` lane (the stop's deadline passed, or
  the lease ran out more than 5 s ago with no renewal outstanding, #1051, counted on `world.session_ends.skipped`)
  never reaches identity, and the place runs out with the lease identity renewed last, within 45 s, never held for
  the grace.
- **Change Character keeps the connection and the session**, so the place is untouched.
- A held place exists only for an account that was admitted, for the grace, and gives nothing a player could not have
  by staying connected. Until part 3 it only counts: it is enforced with the limit.

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
| `world:{worldId}:places:marks` | hash | per account two fields at most: `{accountId}`, the kind of place, `h` (held after a drop), `t:{enqueuedAtMs}` (a turn taken, not yet used) or `j:{untilMs}:{ticket}` (a join ticket's hold, the last ticket to enter, by its session id; `j:{untilMs}:{ticket}:h` when it took a held place), and none for a place a session alone holds; `{accountId}:s`, the fencing token of the session that owns the place (`{fence}`), or a tombstone of the one that ended there (`e:{fence}`, expiring one lease after it was written, kept when the place goes). One field more, `released`: when the world's held places were last released (a stop or a cutoff) |
| `world:{worldId}:queue` | sorted set | the line: member the account id, score its arrival number |
| `world:{worldId}:queue:checkins` | sorted set | member the account id, score the moment the waiter leaves the line unless it checks in again (Unix ms) |
| `world:{worldId}:queue:arrivals` | string | the arrival counter |
| `world-queue:account:{accountId}` | string | `{worldId}:{enqueuedAtMs}`: the world the account waits for; a day's expiry, as garbage collection only |

- Every change is one Lua script (`RedisWorldPlaceStore`), so "check the limit and take a place" and "leave the line
  and take a place" are single steps. "Now" is passed in by identity from its `TimeProvider`, and every place, lease,
  hold and check-in deadline is on that clock; the scripts never call Redis `TIME`. The one time on Redis's clock is a
  tombstone's 45 s, which Redis counts itself from when the tombstone is written (`HPEXPIRE`), so it needs no agreement
  between the two clocks.
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
  Until a live session's next heartbeat (at most 15 s), its place is missing: new joins in that window see too few
  places taken and can be admitted over the limit (from part 3, where the limit is enforced; the world's backstop
  bounds it).
- The identity service's Redis user needs the six patterns and the sorted-set commands its scripts call; the worlds
  service's user reads two of the keys (see [Redis users per API service](redis-cache-keys.md#redis-users-per-api-service)).
  Without them every join answers 503, the script failing with `NOPERM`.

## Releasing

Each step only after the one before is done:

1. **Redis 7.4 or later** (the field expiry `HPEXPIRE`; the homelab runs `redis:8`), and **homelab#199 and
   homelab#202 deployed**: the Redis users carry the rules of
   [Redis users per API service](redis-cache-keys.md#redis-users-per-api-service) (homelab#199: `api-identity` the six
   key patterns and the sorted-set and hash commands, `api-worlds` read on `world:*:places` and
   `world:*:queue:checkins`, and `zcount`; homelab#202: `api-identity` `hpexpire`, which an ended session's tombstone
   is written with, in place of the `hpexpireat` homelab#200 granted). Check on the cluster before the identity release
   rolls: `kubectl -n avalon exec avalon-redis-0 -- redis-cli -a "$PW" ACL GETUSER api-identity` (and `api-worlds`).
   Deployed without homelab#199, every join answers 503. Without `hpexpire` (or on an older Redis) everything works
   but the tombstones: the scripts catch the refused expiry and keep no tombstone, so an ended session's late heartbeat can
   bring a removed place back for at most one lease.
2. Identity, then the worlds service and the worlds, as in every release (identity before the worlds).
3. Check: `/game/worlds` and `GET /world` show places taken for a world with players, and identity's and the worlds
   service's logs carry no "could not read world ... places" Warning, and identity's carry no "Redis refused the expiry of
   an ended session's tombstone" (the identity Redis user lacks `hpexpire`, or Redis is older than 7.4: check step 1).

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
after. Expected: `Passed!  - Failed: 0, Passed: 38, Skipped: 0` (the 37 scenarios and the world list's count). A
scenario that passes in memory and fails against Redis is a script bug: the memory store is the reference. Every row
uses the same world and account ids (worlds 40001 and 40002) and deletes their keys before it starts, so it needs no
empty Redis and a reused container is fine; what a run leaves behind is only those ids' keys. The PR carries the summary
line and the `redis-server --version` line.
