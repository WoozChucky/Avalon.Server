# World player limit

Each world may be given a limit on the accounts it admits (#967). The limit is enforced where join tickets are issued,
by the identity service, which keeps per world a ledger of the places it has given out and, for a full world, a line
of the accounts waiting for one. The world keeps a backstop of its own. A world with no limit behaves as before.

The feature shipped in five parts, all in place:

| Part | What it adds | State |
|---|---|---|
| 1 | The `PlayerLimit` column and its endpoint; the ledger, kept with every session; true counts in the world lists | in place |
| 2 | The reason a world gives for a session's end; the held place after a drop | in place |
| 3 | Enforcement at the join (`WORLD_FULL`), the `Full` status, the world's backstop | in place |
| 4 | The queue: the "queued" answer, the status and leave calls, their rate limit, the metrics | in place |
| 5 | The goodbye opcode (`CMSG_LEAVE_WORLD`), so a quit frees its place at once; the client floor 0.4.0 | in place |

A world with a limit queues a newcomer once it is full ([enforcement](#enforcement), [the queue](#the-queue)), and
answers `WORLD_FULL` only once its line is at its cap.

## What the limit is

`Worlds.PlayerLimit` (auth database) is a nullable integer from 1 to 100,000 (`World.MaxPlayerLimit`, and the check
constraint `CK_Worlds_PlayerLimit`); null means no limit. Identity reads it from the row on each join request and
passes it to the ledger's script; it is never copied into Redis.

What the limit counts is **places taken**: every account holding a place in the world, whether it is in the world, at
character select, holding a join ticket it has not redeemed yet, or holding its place after a dropped connection
([the held place](#the-held-place)). This is not the gauge `avalon.world.players.online`, which counts only the characters in the world: an
account at character select holds a place but no character.

Staff (accounts with the Admin flag, the flag that passes maintenance) are always given a place, so places taken can
exceed the limit by the staff admitted.

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
  account's session in another world once the hold is over. A place released by the account's ticket for another world
  (`r`) goes at once at that activation, its hold or not.
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
| The session is activated | renewed to the lease; it spends its own ticket's hold (never another ticket's); the account's place in every other world is removed, since a session is one per account, but for a ticket's hold still running there (a place released by a ticket for another world, `r`, goes at once) |
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
- **Tickets alone keep an unclaimed place for 15 minutes at most** (`Application:WorldPlaces:UnclaimedPlaceMinutes`).
  A place is unclaimed from the moment a ticket takes it, or a session's end leaves it (a held place after a drop
  included), until a session claims it: its activation, or a heartbeat of the session that owns it (a redemption alone
  does not). Asking a new join ticket every half minute would otherwise keep a place on a full world indefinitely
  without ever playing. Past the horizon a further ticket is not kept on the place: it goes through the capacity step
  as a newcomer's, its own place still counted. With room (a free place beyond its own, and nobody in the line), or
  for staff, it is admitted and the place is its own again with a new horizon; otherwise the answer is a newcomer's
  (the line, or `WORLD_FULL` once the line is at its cap) and the place is released, but not while its last ticket's hold still runs:
  that ticket may still be redeemed, and its session then finds the place it was counted for (the redemption renews it
  as any other, never over the limit, and the unclaimed time stays until an activation). The place goes at its own
  end: that hold's, or later when a session renewed it meanwhile. A live session's renewals are untouched: its
  heartbeats claim the place every 15 s, so a takeover or a reconnect is never refused for it. A session redeemed and
  not yet activated is marked pending on its place (`{accountId}:p`, its fencing token) for one lease at most, and its
  place does not lapse while the mark lives and no lockout does; the activation, the owner's heartbeat, the session's
  end or the place's removal removes it. A live owner field alone does not stop a lapse: a session replaced in
  another world, whose end identity answers `SESSION_REPLACED` and never records here, would otherwise leave a fence
  nobody renews and a place that never lapses.
- **A lapsed place refused is not taken back at once.** An account whose unclaimed place lapsed and that is refused
  `WORLD_FULL` (the world full and its line at its cap) is locked out of that world for 45 s
  (`GameAuthPolicy.WorldFullRetryAfterMin`, the shortest `Retry-After` the others are told), counted from when its
  place is free: the place's own end, when it is kept for its last ticket's hold. Until then each of its joins is
  `WORLD_FULL` again,
  before any capacity check, so it cannot ask again a moment later and take back the place it was released from ahead
  of the players told to come back. Anyone else can take that place meanwhile. The lockout is the marks hash's field
  `{accountId}:l`, which Redis expires itself (`HPEXPIRE`); a place's removal leaves it to expire. Letting the place
  run out instead and asking again at its end changes nothing: the sweep that removes a place unclaimed past the
  horizon writes the same lockout, 45 s from then, so the account meets it as a newcomer. Only a place tickets alone
  kept to the end can be swept unclaimed past the horizon: a live session's heartbeats claim the place, and a drop's
  held place lasts the held grace, and startup requires the horizon to run at least a minute past it
  (`UnclaimedPlaceMinutes` × 60 ≥ `HeldPlaceSeconds` + 60). A session's
  activation removes it (the account is playing), and a session end while it lives gives the place no held grace (the
  grace would outlast the lockout): the place stays only while a ticket's hold or the session's lease still runs. Should Redis refuse
  the expiry, the field is not kept (see the Warning below), and the account can retake the place as before. A lapsed
  account queued instead (the world's line has room) gets no lockout: it waits behind whoever is already in the line,
  and the place it still has counted (kept for its last ticket's hold) is no turn ([the queue](#the-queue)). A
  lockout the sweep writes while the account waits in the line changes nothing for it there: it is checked only for an
  account outside the line, so an account that waits its turn is never locked out of it, and one that leaves the line
  and asks again within the 45 s is refused as above.
- **One reservation per account, across worlds.** A join ticket admitted in one world, or a turn the line gives in one
  world, releases what the account holds in every other configured world with no live session owning it: a place only
  a ticket holds (a ticket's mark), and an unused turn, which is given back as a leave gives it back
  ([the queue](#the-queue)). A place held after a drop (`h`), one a ticket returned to from a drop (`j:...:h`) and one a
  live session owns all stay. So one account cannot squat one world while it asks tickets, or waits turns, for another;
  the release is in the same script as the admission or the turn, over the other worlds' keys as an activation's is. A
  released place whose ticket's hold still runs is not removed at once, since that ticket may still be redeemed: it
  stays counted to the hold's end, marked released (`r`), so no further ticket keeps it (the account asks as a
  newcomer) and no replay extends it, and a redemption of that ticket renews a place that never left the count, with
  its unclaimed time, rather than putting one back over the limit.
- A heartbeat of a session crossing its end in flight (the end first) changes nothing: the end's tombstone refuses
  it, whether the end removed the place or kept it, so a place given to another meanwhile is never taken back. The
  tombstone lasts one lease from the end, and a renewal still in flight at the end was sent within the session's lease,
  so it lands well before; the lease is counted by Redis from the end it records (a relative expiry), so no skew
  between identity's clock and Redis's shortens it. Should Redis refuse the expiry (no `hpexpire` permission, or a
  Redis before 7.4), the script deletes the `{accountId}:s` field it just wrote, whatever that field held before (a live
  owner's fence, or an earlier tombstone the call was re-arming), so the account is left with no owner or tombstone
  there, never with a field that lasts forever. The end and the activation still do the rest, and only this protection
  is lost (a late heartbeat can then bring a removed place back for at most one lease, counting the account in, never
  out). Identity logs the refusal at Warning, at most every 10 s: "Redis refused the expiry of a place ledger field"
  (the same line for a lockout or a pending marker, which are then not kept). What remains: a replay that
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
| The client said goodbye (`CMSG_LEAVE_WORLD`, [the goodbye](#the-goodbye)) | `logout` | removed |
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
| The world's backstop refused the admission ([the backstop](#the-backstop)) | `backstop` | removed |

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
  rather than removing it. A drop while a ticket's hold still runs on the place (a takeover's ticket, say) keeps that
  ticket's mark for the session it becomes, marked `:h` the same way: the place is held after a drop all the same, so
  no ticket or turn for another world releases it or cuts its grace short.
- **After a stop or a maintenance cutoff nobody holds a place** (decision 10): everyone is equal and joins the line with
  everybody else. The world's ends tell identity when: the first `shutdown` or `maintenance` end of the world releases
  every place it holds after a drop (`IWorldPlaceStore.ReleaseHeldAsync`; their tombstones stay). A stop's ends come in
  a burst, so the release runs at most once every 10 s for a world (`IWorldPlaceStore.HeldReleaseWindow`: each identity
  replica asks Redis at most once in it, and the marks hash's field `released` holds it across replicas). The release
  scans the world's places, at most once in the window: tracking the held ones apart would need a key the identity
  Redis user's patterns do not grant. A drop whose end lands within those 10 s keeps its place until the next `shutdown` or
  `maintenance` end after them, or its grace. A place held after a drop that a ticket's hold runs on (a return within the
  grace, or a drop while a takeover's ticket was out, `j:...:h`) loses its grace too but stays that ticket's to its
  hold's end, since the ticket is already issued and may still be redeemed; a hold already over goes with the place. A stop or a cutoff that ends no session (no player left, or Admins
  only) sends no such end, and the places held there run out with their grace, within 90 s.
- **An end that is not sent holds nothing.** An end skipped in the `SessionEnds` lane (the stop's deadline passed, or
  the lease ran out more than 5 s ago with no renewal outstanding, #1051, counted on `world.session_ends.skipped`)
  never reaches identity, and the place runs out with the lease identity renewed last, within 45 s, never held for
  the grace.
- **Change Character keeps the connection and the session**, so the place is untouched.
- A held place exists only for an account that was admitted, for the grace, and gives nothing a player could not have
  by staying connected. It counts against the limit, and a return within the grace is admitted however full the
  world is.

## The goodbye

A client that quits on purpose, to the desktop or back to the launcher, sends `CMSG_LEAVE_WORLD` (0x201F, no payload,
sealed as any gameplay packet when the world seals) and then closes the connection
([packet protocol](networking-packet-protocol.md#the-goodbye-cmsg_leave_world)). The world ends the session as a
`logout`, and identity releases the place at once. Every other close without a world reason is a drop, which holds the
place for the grace: so a crash, a lost connection or a killed client keeps the place, and a quit frees it.

- **Noted as it arrives.** The read loop notes the end's reason the moment the goodbye arrives
  (`WorldConnection.OnReceive`, `NoteGoodbye`), because the client closes right after it and the close's despawn runs
  on the tick before any session packet would be dispatched. The handler (`LeaveWorldHandler`) closes a connection the
  client left open. The first reason noted wins (`SessionEndReasons.Note`), so no close after the goodbye, the
  client's or the world's (a slow connection, a failed save), turns it into a drop; a reason the world noted before
  it, as it was already closing the connection, stands (a kick, a cutoff, a stop all release the place too; a
  world-side drop noted in the same instant holds it for the grace).
- **A goodbye that wins the race with a stop or a cutoff** (it arrives before the world noted `shutdown` or
  `maintenance` for the connection) ends its own session as `logout`: that end releases only its own place, not the
  places the world holds after a drop. The other sessions' `shutdown` or `maintenance` ends in the same stop or cutoff
  still release those ([the held place](#the-held-place)); a stop or cutoff whose every session said goodbye first
  releases none, and those places run out with their grace, as after a stop that ends no session.
- **Before admission, or blocked by maintenance,** it is dropped as every gameplay packet is: a connection maintenance
  blocked is closed with its own reason, which releases the place anyway.
- **Change Character** keeps the connection and the session, and sends no goodbye: the place is untouched.
- **Only its own session.** The goodbye ends the session of the connection it arrives on and nothing else: a client
  can release only its own place.
- **An identity from before part 2** knows no reason: it removes the place at every end, so a goodbye changes nothing
  there (identity is deployed before the worlds). A world that met such an identity sends its ends without the reason
  until identity takes it again, offered at most every 5 minutes ([the held place](#the-held-place)): in that window a
  newer identity reads a goodbye's end as a drop and holds the place for the grace.

### The client's side

The game client (Avalon.Client) implements, from 0.4.0:

- its version 0.4.0, in the provider attempt and in the world handshake;
- the goodbye: `CMSG_LEAVE_WORLD` before closing on a deliberate quit or a return to the launcher, never on Change
  Character;
- the join's new answers: **202** `{ position, checkInAfterSeconds }` with no ticket (queued), and **503** `WORLD_FULL`
  with `Retry-After`;
- a queue screen showing the position, with a cancel; `POST /game/queue/status` (the game context credential and the
  world id) at the interval each reply gives, `state` one of `queued`, `paused`, `your_turn`, `not_queued`
  (`estimatedWaitSeconds` always null for now); on `your_turn` the ordinary join-ticket call within 45 s; on
  `not_queued` a new join; `POST /game/queue/leave` on cancel (safe to repeat); both calls limited to 30 a minute per
  account; the game context refreshed while it waits;
- a message for `GameAdmissionResult.WorldFull` (5) at the world's door ("back off a minute or more");
- in the world picker, `status` (`Online`, `Maintenance`, `Full`), `playerCount`, `playerLimit` and `queueLength` from
  `POST /game/worlds`;
- the wire schema re-vendored.

## Enforcement

The capacity step is the join's last (`JoinTicketStore.IssueAsync`, `POST /game/join-tickets` and
`/game/reconnect-tickets`): every refusal before it answers first and takes no place, so a full world is told only to a
context that may enter it. The step asks the ledger for a place with the world's stored limit and the account's Admin
flag:

| The account | On a full world (places taken at or over the limit) |
|---|---|
| Holds a place in the world: its own session (a reconnect, a takeover), a ticket's hold, a place held after a drop, or a turn from the line | kept; ticket as before |
| Staff (the Admin flag, tested with `Allows`) | a place, over the limit if need be; ticket as before |
| In the line, its rank within the places free | its turn: a place and a ticket, as at a check-in's turn ([the queue](#the-queue)) |
| Anyone else, while the line is under its cap (`Application:WorldPlaces:QueueCapacity`, 5,000) | **202** with `{ position, checkInAfterSeconds }` and no ticket: queued at the back of the line, or kept at its position when already in it |
| Anyone else, the line at its cap | **503 `WORLD_FULL`** with `Retry-After` a whole number of seconds from 45 to 75, drawn at random so the players refused together do not all ask again at once (`GameAuthPolicy.WorldFullRetryAfterMin`, `Max`); no ticket, no place |

A newcomer never jumps the line: while anyone waits, it queues too, even when a place is free at that instant.

- `WORLD_FULL` is one of the retryable 503s, beside `WORLD_UNAVAILABLE` and `SERVICE_UNAVAILABLE`
  ([world refusals](api-authentication.md#world-refusals)). It, and the 202 of a queued join, are answered before the
  ticket and its `join-issue` receipt are written, so the same `Idempotency-Key` asked again is decided again rather
  than replayed: the key a client queued with gets its ticket at its turn.
- No limit set: no refusal and no line, as before.
- **`Full` in the lists.** `WorldStatus` gains `Full` (appended, in the REST contract's enum and the domain's): a world
  `Online` with a limit that places taken have reached. The order is `Maintenance`, then `Offline`, then `Full`, then
  `Online` (`WorldReadiness.WithCapacity`, on top of `Resolve`, which keeps its three values). `GET /world`,
  `GET /world/{id}` and identity's `POST /game/worlds` derive it from the ledger's count; `GET /world?sortBy=status`
  orders `Full` after `Maintenance`, by the enum's number. A world listed only with `requiresUpdate` gets its status
  the same way. The TCP auth server's world list is unchanged.

## The queue

A full world (a limit set, and places taken at or over it) queues the accounts that ask to enter, in arrival order,
and lets them in as places free up. The line is identity's, in Redis, changed only by the ledger's scripts; the world
knows nothing of it.

- **Arrival order, no jumping.** A join that is queued is placed at the back by a per-world arrival counter (the
  score's high part is a priority band, reserved and always 0). While anyone waits, every newcomer queues too, even
  when a place is free at that instant. A join from an account already in the line keeps its position.
- **The join's answer.** `POST /game/join-tickets` and `/game/reconnect-tickets` answer **202** with
  `{ position, checkInAfterSeconds }` and no ticket. Nothing is stored for the request's `Idempotency-Key`, so the
  same key, or a new one, asks afresh at the turn.
- **Checking in.** The client then calls `POST /game/queue/status` with its game context credential and the world id,
  at the interval each reply gives. The reply is `{ state, position, checkInAfterSeconds, estimatedWaitSeconds }`;
  `estimatedWaitSeconds` is reserved and always null. A waiter that stops checking in is removed:

  | Position | Check in every | Removed after |
  |---|---|---|
  | 1 to 10 (`WorldPlacesConfiguration.FrontPositions`) | `FrontCheckInSeconds` (5 s) | `FrontTimeoutSeconds` (15 s) without a check-in |
  | 11 and further back | `BackCheckInSeconds` (15 s) | `BackTimeoutSeconds` (45 s) without a check-in |

  Each check-in stores the moment the waiter is removed unless it checks in again, so a waiter told "every 15 s" keeps
  its 45 s when the line moves it into the first ten. Silent waiters are removed by whichever script runs next for the
  world (a join, a check-in, or identity's sampler every 15 s), before any position is computed, so a silent waiter at
  the head never stalls the line.
- **The states.** `queued` with the position; `paused` with the position while the world is not available (no row, no
  ready heartbeat, or a maintenance cutoff in force: positions kept as long as the waiters check in, nobody is let in;
  staff enter as ever); `your_turn`; `not_queued` (the account is not in this world's line: ask to join).
- **The turn.** At a waiter's own check-in, with the world available and its rank within the places free (the limit
  less places taken, the rank counted from 0), the script moves it out of the line and gives it a place for the turn
  window (`TurnWindowSeconds`, 45 s), and the state is `your_turn`. The client then makes the ordinary join-ticket call,
  which finds the place, runs every check a join runs (standing, license, application, the world, the session rules)
  and issues the ticket; the queue never issues one itself. A waiter takes a place only at its own check-in, so nobody
  is let in while away. A turn not used within the window expires (`turn_expired`) and the next in line takes it at its
  check-in. A turn given releases what the account holds in other worlds with no session: a ticket-only place, and an
  unused turn, given back ([one reservation per account](#places)).
- **A turn while the world is not available.** A turn given just before the world goes away (it stopped, or a
  maintenance cutoff came) is not lost: while the world is not available, a check-in by an account holding an unused
  turn keeps the turn, its window starting again, and answers `paused` at position 1. The client goes on checking in
  (a join meanwhile is refused 503 `WORLD_UNAVAILABLE`, as for anyone), and once the world is back the check-in answers
  `your_turn` again. A client that stops checking in loses the turn when its window runs out. The turn is kept so at
  most `Application:WorldPlaces:PausedTurnMinutes` (30) from when it was given, which a stop or a maintenance window of
  that length fits, so a world whose readiness comes and goes cannot keep a turn for ever; past it the turn expires
  (`turn_expired`) and the account asks to join again, at the back of the line.
- **A place of its own is its turn at once.** An account that holds a place in the world its join would keep (a turn
  already taken, a place held after a drop, its session's, a ticket's hold not lapsed) is told `your_turn` at once,
  and leaves the line if it is in it; so does its join. A place that is not its own is counted but is no turn: one
  released by its ticket for another world (`r`), or one that lapsed under tickets alone and is kept only for its last
  ticket's hold. Such an account waits its rank like anyone, its own place counted among the places taken, and its
  turn, when it comes, is taken over that place: never shortened (that ticket may still be redeemed), and its own, no
  longer lapsed, so its join takes it. The turn's mark records when the account joined the line and when the turn was
  given (`t:{enqueuedAtMs}:{givenAtMs}`), and, over such a place, that place's end and unclaimed time
  (`t:{enqueuedAtMs}:{givenAtMs}:{endMs}:{unclaimedSinceMs}`), so a turn given back returns it to what it was.
- **Leaving.** `POST /game/queue/leave` (same body) leaves the line and gives back a turn not yet used (`cancelled`);
  it is safe to repeat and answers `not_queued`. A turn given over a place still counted for an earlier ticket is given
  back to that place: counted again to its own end, released (`r`, kept by no further ticket), so that ticket's
  redemption renews a place that never left the count rather than putting one back over the limit; otherwise the place
  goes. Silence leaves the line too, within its timeout.
- **One line per account.** Joining a second world's line leaves the first.
- **The cap.** `QueueCapacity` (5,000) per world. A join that would queue past it is answered 503 `WORLD_FULL` with
  `Retry-After` (45 to 75 s), and counted `refused_at_cap`. A `WORLD_FULL` for a lockout (a lapsed place refused a
  moment ago) is counted `refused_locked_out` instead.
- **The limit changed.** Raised or removed: the waiters within the new room enter at their next check-in. Lowered:
  nobody leaves the world or the line. A check-in reads the limit from identity's cached view, so for up to 5 s after a
  change, turns are still given at the previous limit (the join reads the row afresh, so tickets follow the new one at
  once).
- **What the calls trust.** The credential is checked on Redis alone (`GameAuthorizationService.GetCurrentContextAsync`:
  current, not spent, not expired, the context not revoked by a logout, a refresh reuse or a replacing sign-in), with
  no account standing or license: the calls grant nothing, and the join that follows a turn checks both. A banned
  waiter can still check in until its credential runs out (at most 5 minutes: its refresh is refused, as the ban is
  checked there), then is removed from the line within its timeout; any join meanwhile is refused. The world's
  availability and limit are identity's cached view (`GameWorldAvailability`), read from the row and the ready
  heartbeat at most every 5 s per world, so a check-in reads Redis alone.
- **The credential lives 5 minutes.** A wait longer than that needs the game context refreshed, as at any other time
  (`client/auth/game-context/refresh`); a check-in with a credential that ran out is 401. The client refreshes while it
  waits; LocalDev's `check` and the load-test bots do.
- **Their limits.** The two calls are out of the shared request limiter (`[DisableRateLimiting]`): its anonymous
  partition, 60 a minute per source, would throttle the waiters of a household behind one address. `QueueRateLimiter`
  limits them instead, in memory as the shared limiter is, in a one-minute window of thirty segments:
  - **A credential identity knows** (it issued it, at a sign-in, a link or a refresh, or read it current at a check-in,
    until it expires) has `Application:RateLimiting:QueuePermitsPerMinute` (30) calls a minute of its own, counted
    before it is read, and goes through no gate of its source: nothing another caller behind its address does slows or
    refuses it. It is still read on every call, so a revocation answers at once. The known credentials are a bounded
    cache (100,000, by the first 16 bytes of each one's SHA-256); one not remembered for the bound is a stranger's. A credential
    read current is kept before one only issued, and the credential a refresh or a sign-in replaces is forgotten at once.
  - **Any other credential is a stranger's.** Its reads from one source go two at a time, the others waiting their turn
    (refused once 64 wait), and once the source has presented 30 credentials in a minute that were not current, at most
    `Application:RateLimiting:QueueUnknownReadsPerMinute` (60) a minute. The whole process reads at most
    `Application:RateLimiting:QueueStrangerReadsInFlight` (256) strangers' credentials at once, whatever their sources;
    past it a stranger's call is refused (429). Nothing is kept per stranger's credential, so a
    flood of junk costs only its source's limiter state. Such a source can delay or refuse a waiter identity does not
    know yet behind the same address (after an identity restart emptied the cache, say), never one it knows.
  - **Each account** has `QueuePermitsPerMinute` (30) calls a minute after the read, its fair share across the
    credentials it rotates through.
  - A credential not of a credential's shape is refused (401) before any of this, at no cost.

  An exempt source is not gated. Each limiter partition is dropped once it has been idle a window, as the shared
  limiter's are. A refusal is 429 with `{ "error": "LOCKED" }` and a
  `Retry-After` of at most 2 s (one segment), well inside the time a waiter has between its check-in interval and its
  removal (10 s by default), so a waiter refused once and asking again when told keeps its position.
- **No `Idempotency-Key`**: both calls are idempotent by nature and bind none. A successful check-in's request line is
  logged at Debug, as a lease heartbeat's is: a full world's waiters check in every 5 or 15 s each.
- **Errors.** 400 for plain HTTP or a malformed body, 401 `AUTHORIZATION_REQUIRED` for a credential that is not
  current (refresh the context and ask again), 429 as above, 503 `SERVICE_UNAVAILABLE` when Redis or the auth database
  cannot be read.

## The backstop

The world keeps a ceiling of its own on admitted accounts (`WorldBackstop`), which guards the pod if identity's
ledger is wrong or was lost (a Redis flush, and the window before heartbeats put the places back). It should never fire.

- **Its limit is the world's configuration**, `Game:PlayerLimit` (Helm `server.game.playerLimit`, rendered only when
  set), 1 to 100,000; unset, no backstop. It reads nothing from the auth database. The ceiling is the limit plus a
  margin: `Game:PlayerLimitMarginPercent` of the limit (5 by default, 0 to 100), at least
  `Game:PlayerLimitMarginMinimum` accounts (32 by default, the admission lane's concurrency); at 1,000 that is 1,050.
  Read once at startup, and logged
  at Information at every start: `Player limit {Limit}, backstop ceiling {Ceiling}`, or `No player limit configured:
  no backstop`.
- **Where it runs.** In the admission's own work on the `Admission` lane, off the tick, after identity redeemed the
  ticket and activated the session (so the account's access level is known), and only for an admission the per-source
  budget let start. It counts accounts, not connections, over the server's connection snapshot: each account with a
  lease not revoked, once, and this one's. A takeover's old connection keeps its lease until its next heartbeat is
  refused (within 15 s), so counting connections would count that account twice and could refuse the takeover, and
  cycling takeovers could inflate the count; counting accounts cannot. Staff (the Admin flag) are neither counted nor
  refused. A lease is read without its lock or the clock (`IsRevoked`): the tick marks one that ran out within a tick,
  which can only count an account a little longer. The accounts are gathered in a set the backstop reuses under a lock,
  so a check allocates nothing once the set has grown.
- **How far over it can go.** Two windows let admissions past the ceiling: admissions on the lane at once (at most 32,
  `WorldDatabaseWork.AdmissionConcurrency`) do not see each other, and an admission the backstop passed counts for the
  next only once the tick publishes it (the next tick, about 16 ms). The margin's minimum of 32 covers the first. The
  second adds about the admissions that finish within one tick: with identity answering in a few milliseconds, the lane
  can finish around a hundred a tick during a reconnect storm, all past the backstop before any is published. So in the
  worst case a world can be over its ceiling by a little over a hundred accounts; at a limit of about 640 or more the
  5% margin covers that, and below it the overshoot can pass the margin. The backstop bounds the damage of a ledger
  fault roughly, not exactly; identity's own limit is the exact one.
- **One account, several connections, counts once.** An account whose takeover connections overlap (the old one keeps
  its lease until its next heartbeat is refused) counts as one account. That is the trade for never refusing a
  takeover; how many connections one account can hold at once is bounded by the join ticket's rate limit and the
  world's per-source admission budget, not by the backstop.
- **A refusal.** Over the ceiling and not staff: the lease is revoked, the session is ended with reason `backstop`,
  which removes the place (never the reasonless end of a failed admission, which would hold it), and the client is
  answered `GameAdmissionResult.WorldFull` (5) and closed. The admission's result then carries no lease, so a close's
  drain ends nothing a second time. The end holds the admission lane and the source's slot, so it is bounded to 2 s
  (`WorldBackstop.EndTimeout`), as a failed admission's abandon is; an end that fails or times out leaves the session
  to run out with its lease (within 45 s), and the place with it. Staff always pass.
- **When it fires** it counts `world.admission.backstop_refusals` and logs at Error, at most once every 10 s with the
  count left out. Any value above zero means identity's ledger was wrong or lost, or identity's stored limit is above
  the world's `Game:PlayerLimit`: the homelab alerts on it.

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
- It takes effect on identity's next join request. Lowering it below places taken removes nobody: nobody new enters
  until places taken falls below it, and nobody leaves the line for it. Raising or removing it lets the waiters
  within the new room in, each at its next check-in.
- The world's backstop has its own limit, the world's configuration `Game:PlayerLimit` (Helm
  `server.game.playerLimit`), which the operator sets to the same number. **A limit is always set in two steps, together:**
  1. `PUT /world/{id}/player-limit` with the number: identity's limit, which queues newcomers and refuses them
     (`WORLD_FULL`) once the line is at its cap. Effective on the next join.
  2. The same number as the world's chart value `server.game.playerLimit` (removed, or empty, to lift it): the
     backstop's limit. Effective when the pod rolls, which the change does: a restart, after which nobody holds a place
     (decision 10). So change it in a maintenance window or before the world's players arrive.

  The two can disagree, and that is accepted: a world value below identity's makes the backstop refuse admissions
  identity granted, which shows as `world.admission.backstop_refusals` and its alert; a world value above identity's,
  or none, leaves the pod a looser guard, or none, while identity still enforces its own. Check after a change that the
  world logged `Player limit {Limit}, backstop ceiling {Ceiling}` at its start.

  **A world value below identity's is a loop, not just a count.** Identity keeps issuing tickets up to its own limit;
  the world refuses each admission past its ceiling with `WorldFull` and ends the session, which frees the place, so
  identity issues the next ticket to someone else (or to the same client trying again), and the world refuses that too.
  Players between the two limits are turned away at the world's door over and over, each costing a redemption and a
  session end. The backstop alert (`world.admission.backstop_refusals` above zero) is the signal: on it, compare the two
  limits first, and bring identity's down to the world's (`PUT /world/{id}/player-limit`, at once) before anything
  else. A client should treat `WorldFull` as "back off a minute or more".

The world lists show the counts: `GET /world` and `GET /world/{id}` fill `WorldDto.OnlineCount` with places taken and
carry `playerLimit` and `queueLength`; each world of identity's `POST /game/worlds` carries `status` (`Online`, `Maintenance`
or `Full`), `playerCount`, `playerLimit` and `queueLength`, and a join reply's `destination` carries `status` and
`playerLimit` too. The counts read only entries still current (a place whose expiry, or a waiter whose check-in
deadline, is still ahead), so what no script has swept yet is not shown. When Redis cannot be read the counts are 0;
an error Redis answers (a missing permission, `NOPERM`) is also logged at Warning, at most every 10 s
(`RedisWorldOccupancy`), so a Redis user without the rules below does not show every world empty in silence.

## Metrics

Identity's, on the API's meter (`avalon-api`), each tagged `world`:

| Metric | Kind | Meaning |
|---|---|---|
| `avalon.world.places.taken` | gauge | Places taken: every admitted account, in the world, at character select, holding an unredeemed ticket or a turn, or held after a drop. Not `avalon.world.players.online` (the world's gauge), which counts only characters in the world |
| `avalon.world.places.held` | gauge | Places held after a dropped connection, a ticket's hold on one (a return within the grace) included |
| `avalon.world.queue.length` | gauge | Accounts waiting in the line |
| `avalon.world.places.limit` | gauge | The stored player limit, only for a world that has one |
| `avalon.world.queue.outcomes` | counter, tag `reason` | How waits ended: `entered` (the join after a turn issued its ticket), `cancelled` (a leave that removed something), `timed_out` (a waiter removed for not checking in), `turn_expired` (a turn not used within its window), `refused_at_cap` (a join answered `WORLD_FULL`, the line at its cap), `refused_locked_out` (a join answered `WORLD_FULL` for the account's lockout: its unclaimed place lapsed and was refused a moment ago) |
| `avalon.world.queue.wait` | histogram, seconds | The time in the line of those who entered, from joining the line to the ticket |

The gauges are sampled every 15 s for each configured world (`WorldPlaceGauges`, a background service in identity),
which also sweeps what expired where nobody joins or checks in, counting what it removed. A world whose sample fails (Redis,
or its row) is logged at Debug and left out of the gauges until a sample succeeds. The world's own `world.admission.backstop_refusals` is
described under [the backstop](#the-backstop).

## Redis

Six keys, all named in `CacheKeys` and described in [Redis cache keys](redis-cache-keys.md#world-places-and-queue-967):

| Key | Type | Content |
|---|---|---|
| `world:{worldId}:places` | sorted set | the ledger: member the account id, score the moment the place expires (Unix ms) |
| `world:{worldId}:places:marks` | hash | per account five fields at most: `{accountId}`, the kind of place, `h` (held after a drop), `t:{enqueuedAtMs}:{givenAtMs}` (a turn taken, not yet used; `t:{enqueuedAtMs}:{givenAtMs}:{endMs}:{unclaimedSinceMs}` when given over a place still counted), `j:{untilMs}:{ticket}` (a join ticket's hold, the last ticket to enter, by its session id; `j:{untilMs}:{ticket}:h` when it took a held place) or `r` (released by the account's ticket for another world while its own ticket's hold ran: counted until that hold ends, kept by no ticket), and none for a place a session alone holds; `{accountId}:s`, the fencing token of the session that owns the place (`{fence}`), or a tombstone of the one that ended there (`e:{fence}`, expiring one lease after it was written, kept when the place goes); `{accountId}:f`, since when (Unix ms) no session has claimed the place, absent once one does, gone with the place; `{accountId}:l` (`1`), the lockout of an account refused when its unclaimed place lapsed, which Redis expires 45 s after its place is free, removed by an activation; `{accountId}:p`, the fencing token of a session redeemed and not yet activated, which Redis expires one lease after the redemption, removed by the activation, the owner's heartbeat, the session's end or the place's removal. One field more, `released`: when the world's held places were last released (a stop or a cutoff) |
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
  places taken and can be admitted over the limit (the world's backstop bounds it).
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
   Deployed without homelab#199, every join answers 503. Without `hpexpire` (or on an older Redis) joins, ends and
   renewals still work, but every field that must expire on its own is dropped instead (the scripts catch the refused
   expiry and delete the field): the tombstones, so an ended session's late heartbeat can bring a removed place back for
   at most one lease; the lockout of a lapsed squatter, which can then take its place back at once; the pending marker
   of a redeemed session, whose place can then lapse before its activation. Each refusal is the same Warning, "Redis
   refused the expiry of a place ledger field".
2. Identity, then the worlds service and the worlds, as in every release (identity before the worlds).
   **Turns and an identity from before part 4.** Part 4 writes turns (marks `t:{enqueuedAtMs}:{givenAtMs}` and
   `t:{enqueuedAtMs}:{givenAtMs}:{endMs}:{unclaimedSinceMs}`), which an identity from before part 4 cannot read: its join of an account holding a turn fails with 503
   `SERVICE_UNAVAILABLE`. So do not run identity pods from before part 4 beside part 4's: roll identity over without
   overlap (stop the old pod, then start the new), or accept that 503 for the few seconds both run. Turns exist only on
   a world with a limit, and no live world has one before part 5, so on today's worlds the overlap meets no turn.
3. Check: `/game/worlds` and `GET /world` show places taken for a world with players, and identity's and the worlds
   service's logs carry no "could not read world ... places" Warning, and identity's carry no "Redis refused the expiry of
   a place ledger field" (the identity Redis user lacks `hpexpire`, or Redis is older than 7.4: check step 1).

**Part 5, the client first.** Part 5 raises the API's default client floor (`Application:GameWorkloads:MinClientVersion`)
to 0.4.0, so from its deploy identity refuses a 0.3.0 client at sign-in (400 `UNSUPPORTED_PROTOCOL`). In this order:

1. Publish the 0.4.0 client against the services as they are: their floor is "at or above" (#949), so they take it; its
   goodbye is an opcode a world before part 5 drops, with one Warning per connection (#897).
2. Deploy part 5, identity first, then the worlds. Merge it only when this step is next: any deploy after the merge
   raises the floor.
3. Raise each world's `MinVersion` to 0.4.0 (`PUT /world/{id}`): a game context signed in before step 2 is told
   `CLIENT_OUTDATED` (426, `requiredVersion` 0.4.0) at its next join, and the world's handshake refuses a 0.3.0 client
   with `ClientVersionTooOld`.
4. Only then set a live limit ([setting a limit](#setting-a-limit)), after a load test with churning bots, which say
   goodbye when they leave.

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
after. Expected: `Passed!  - Failed: 0, Passed: 55, Skipped: 0` (the 54 scenarios and the world list's count). A
scenario that passes in memory and fails against Redis is a script bug: the memory store is the reference. Every row
uses the same world and account ids (worlds 40001 and 40002) and deletes their keys before it starts, so it needs no
empty Redis and a reused container is fine; what a run leaves behind is only those ids' keys. The PR carries the summary
line and the `redis-server --version` line.
