# Load testing

`tools/Avalon.LoadTest` answers one question: how many players one world server holds, and what gives out first.
It runs headless bots from one PC. Each bot signs in over the REST API as the launcher and the game client do,
enters a world over TLS as the game client does, and then stands, walks, changes character or fights its way through
a forest of its own while a ramp adds bots in steps. Each step is judged against a set of limits: the server's
numbers come from Prometheus, the bots' from the tool, and the bot PC's own load decides whether those numbers can be
trusted. The ramp ends at the first limit breached twice in a row, and the tool writes a report with the capacity and
what failed first.

It runs against the homelab's load-test world, world 4. Its accounts are real accounts on the live API, created for a
run and deleted after it, and the run shares the node with the live world.

## Contents

- [Before a run](#before-a-run)
- [Commands](#commands)
- [Run files](#run-files)
- [What the bots do](#what-the-bots-do)
- [The ramp](#the-ramp)
- [The limits](#the-limits)
- [The decision rule](#the-decision-rule)
- [Stopping](#stopping)
- [The report](#the-report)
- [Ctrl+C](#ctrlc)
- [Cautions](#cautions)

## Before a run

Everything here has to be in place, or the run fails early or measures the wrong thing.

1. **Identity serves the load-test accounts.** `Application:LoadTest:Enabled` is true on the identity service (Helm
   `loadTest.enabled` in the homelab's `api.yml`). Without it `provision` and `cleanup` get 404. At most
   `Application:LoadTest:MaxAccounts` (default 5,000) load-test accounts can exist at once, across every run
   ([API services](api-services.md#load-test-accounts),
   [configuration](configuration-reference.md#rest-api-load-test-accounts)).
2. **The bot PC's address is exempt from the per-source limits.** Hundreds of bots behind one address would
   otherwise be refused as one caller: `Application:RateLimiting:ExemptSources` (Helm `rateLimiting.exemptSources`)
   has to list the address the API sees for the bot PC
   ([exempt sources](configuration-reference.md#exempt-sources)). On the homelab, LAN machines reach the API by
   public DNS through the router, so every one of them arrives as the homelab's WAN address. That address is the
   entry in the homelab's `api-shared-values.yml`, and it has to be updated if the ISP changes it. Per-account and
   per-username limits still apply to every bot.
3. **The bot PC resolves the load-test world to Caddy.** World 4 (`loadtest.avalon.nunolevezinho.xyz`, port 21004) is
   LAN-only. The public wildcard resolves the name to the WAN address, where 21004 is not forwarded. Either add the
   hosts line

   ```text
   10.10.1.17 loadtest.avalon.nunolevezinho.xyz
   ```

   (`C:\Windows\System32\drivers\etc\hosts`, or `/etc/hosts`), or pass `--dial 10.10.1.17` to `check` and `ramp`.
   With `--dial` the bots connect to that address, but TLS still sends the join reply's server name and checks the
   certificate against the pin from the join reply, so nothing is trusted that would not be trusted otherwise.
4. **World 4 exports metrics every 10 seconds.** The world chart's `otel.metricExportIntervalMs` is `10000` on the
   load-test release ([World metrics export](configuration-reference.md#world-metrics-export)). The SDK's default is
   60 seconds, which gives a 60-second judged window one sample of each series. A rate needs two, so tick time, GC
   and the other rates come back empty, every step is unknown, and the ramp stops as unknown after its second step.
   To check the interval, ask Prometheus how many samples a minute holds:
   `count_over_time(world_tick_rate_tps{avalon_world_id="4"}[60s])` should read about 6, not 1.
5. **World 4 frees an emptied forest at once.** For [fighters](#fighters), the load-test world runs
   `Game:AbandonedInstanceLifetimeMinutes = 0` (the world chart's `server.game.abandonedInstanceLifetimeMinutes`, set
   on the load-test release only: server PR #877, homelab PR #173). An emptied dungeon instance is then freed on the
   tick after its last player leaves, and every forest entry builds a fresh instance with its creatures
   ([instanced maps](instanced-maps.md)). At the default of 15 minutes a fighter that leaves and enters again within
   15 minutes gets its own forest back, already cleared (creatures never respawn), and the ramp measures an empty map.
6. **Prometheus is reachable from the bot PC.** The default is `http://10.10.1.15:30090/` (`--prometheus`). The ramp
   reads the world's players online before it signs in a single bot and refuses to start when Prometheus does not
   have that value. It first measures the bot PC's clock against Prometheus's (`time()`) and corrects every query's
   time by the difference, which the report's header gives; when the two clocks are more than 60 seconds apart it
   refuses to start and says so: synchronise the bot PC's clock (`w32tm /resync` on Windows) and run again. The
   memory limit comes from kube-state-metrics, by pod (`--pod`, default `avalon-world-loadtest-0`, in namespace
   `avalon`, container `avalon-world`).
7. **An admin account without MFA.** `provision` and `cleanup` ask for an admin's username and password. The account
   needs the Admin (or Console) role, and MFA has to be off on it: the tool does not answer an MFA challenge. The API
   checks the password again on every create and delete, so a wrong one counts as a failed login.
8. **A Release build.** The bot PC's CPU is one of the limits, and a Debug build spends more of it per bot. Run the
   tool with `-c Release`, as every example below does. The input driver sleeps towards each 60 Hz step on a
   high-resolution waitable timer on Windows 10 1803 and later, and with `Thread.Sleep` where that timer is missing
   (older Windows, Linux, macOS); on every system it yields the thread through the last millisecond of each step.
9. **Asthoria is quiet.** See [Cautions](#cautions): check its player count first.

## Commands

```bash
dotnet run -c Release --project tools/Avalon.LoadTest -- <command> [options]
```

Every option takes a value (`--run ABC`, not `--run=ABC`). A command line the tool cannot run prints the usage and
exits 2; so does a run file that cannot be read, and, for `check` and `ramp`, a run that lists no bots (a provision
that saved none: clean it up and provision again), has no bot password or names world 0 (`cleanup` deletes such a
run all the same). A REST or Prometheus failure that ends a command prints its reason and exits 1.

| Command | Exit 0 | Exit 1 |
|---|---|---|
| `provision` | every account created and kept in the run file | an API refusal, a cancel or a run-file write failure; what exists is printed with how to delete it |
| `check` | every step passed, the leave and the sign-out included | the failing step and its reason |
| `ramp` | a verdict: a capacity, no limit reached, or the bot PC saturated | stopped short of one: unknown steps, Ctrl+C, an error, bots that could not sign in, clocks more than 60 s apart; or the run does not stand: the world server restarted and that the old process ended after the last judged window is not proven (the end time unknown included), or no restart was seen but the [restart check](#a-world-restart) was partial or unknown |
| `cleanup` | every run deleted and the run file gone | any run not deleted; the run file is kept |

### provision

```bash
dotnet run -c Release --project tools/Avalon.LoadTest -- provision --count 1000
```

This creates `--count` bot accounts as an admin and keeps them in a new [run file](#run-files). It asks for the
admin's username on standard input and the password without echo; piped input is read as it comes. The password is
never stored.

| Option | Default | Meaning |
|---|---|---|
| `--count N` | required | Accounts to create, 1 to 5,000 |
| `--world W` | `4` | The only world the run's bots enter, 1 to 65535 |
| `--api URL` | `https://avalon.nunolevezinho.xyz/api/` | The API origin. Only https is accepted; a missing trailing slash is added, so a path prefix like `/api` is kept |
| `--run ABC` | the tool picks one | The first run's id, three letters, upper-cased. A run file of that name must not exist already |

The API makes at most 1,000 accounts per call and uses a run id only once, so a count above 1,000 becomes several
runs in one run file, each with its own id. The tool picks each id itself (three random letters that no kept run
file lists). When the API answers that an id it picked is already used, the tool forgets that id and picks another,
up to five times. An id named with `--run` is never swapped: if the API reports it used, `provision` fails.

All of a provision's accounts share one random 24-character password, which the run file keeps. The API names the
accounts `LT` + run id + seven letters (`LTABCAAAAAAA`, ...). Each holds `Player | PTR` and one base-game license
marked as a load-test one. They have no characters until a bot first enters a world.

### check

```bash
dotnet run -c Release --project tools/Avalon.LoadTest -- check
dotnet run -c Release --project tools/Avalon.LoadTest -- check --dial 10.10.1.17 --bot 7 --behaviour walker
dotnet run -c Release --project tools/Avalon.LoadTest -- check --behaviour fighter --forest-time 90s
dotnet run -c Release --project tools/Avalon.LoadTest -- check --behaviour fighter --party-size 3 --bot 4
```

`check` takes one bot through everything a ramp does to every bot. It signs in, enters the run's world (join
ticket, TLS, admission, handshake, character list, create on the first entry, select, load report, spawn, first
answered input), sends input at 60 Hz for 10 seconds through the same input driver the ramp uses, then leaves and
signs out. It prints each step's duration, the input-to-ack latency (p50, p95 and p99), the driver's lateness p95,
and for a walker how far it walked from the spawn point. Run it before the first ramp of a run, and after anything
changed on the server or the network: it is the quickest way to see that the whole chain works.

| Option | Default | Meaning |
|---|---|---|
| `--run ABC` | the only run kept | The run whose account is used |
| `--dial HOST` | the join reply's host | A host name or address to connect to instead; TLS still names and pins the reply's server |
| `--bot N` | `0` | The account's index in the run |
| `--behaviour idle\|walker\|fighter` | `idle` | What the bot does: idle or walk for 10 seconds, or make one forest trip |
| `--forest-time T` | `5m` | A fighter's time in the forest: `90s`, `5m` or plain seconds, above 0, at most 1 h |
| `--party-size N` | `1` | Fighters only, 1 to 6: N bots from `--bot` on check together as one party |

**A fighter's check** (`--behaviour fighter`) drives one [forest trip](#fighters) instead of the 10 seconds, at once
(no first-trip wait): to the town's portal, into the forest, fighting for `--forest-time` (or until no live creature
is in sight for a minute), back out along its trail, and into town. It prints each stage and how long it took
(`to-portal`, `enter-map` from the portal request to the transition, `in-forest`, `to-exit`, `exit-map`, or
`respawn` after a death), then the forest entries and their p50, the casts sent and refused by reason, the kills seen
and its own deaths, the trips completed, and each failed step by kind. It passes only when the trip completed: the
fighter walked out into town. A death fails it (`forest:died`: it respawned in town before it walked out), and so
does a failed step (`forest:failed`, with the [failure kinds](#failure-kinds)); a trip that has not ended within
`--forest-time`, plus the most its exit budget can be (60 seconds or twice `--forest-time` plus 5 seconds (the 10 m
from the entry spawn to the back portal), whichever is longer), plus 3 minutes, fails as `forest:timeout`; a
connection the world closes during it as `forest:closed`.

With `--party-size N` above 1 (fighters only; any other behaviour refuses it), the N bots from `--bot` on enter
together, form one party with the party packets once all are in (it prints how long that took; a party that did not
form after its second attempt fails the check as `party:failed`, with the reasons), set out at once, share one forest
and each make one trip; the check passes when the party formed and every member's trip completed. A member back first
stands in town until the others are. At 1 the fighter sends no party packet, except one leave when its character is
still in a party of an earlier run ([parties](#parties)).

A failure prints `Check failed at <step>: <reason>`, and the bot still leaves and signs out (with 30 seconds of its
own). A leave or a sign-out that fails after the bot was driven fails the check too: `Check failed at leave:` with
the leave's failure kinds (`leave:timeout`, ...), or `Check failed at logout:`. The steps and their timeouts are in
[entry](#entry-and-retries).

### ramp

```bash
dotnet run -c Release --project tools/Avalon.LoadTest -- ramp
dotnet run -c Release --project tools/Avalon.LoadTest -- ramp --start 100 --step 100 --max 1000 --limit tick-p99=20 --dial 10.10.1.17
```

This is the capacity run, described under [The ramp](#the-ramp).

| Option | Default | Meaning |
|---|---|---|
| `--run ABC` | the only run kept | The run whose accounts are used |
| `--mix SHARES` | `idle=60,walker=30,churner=10` | Behaviour weights: `name=weight` pairs of `idle`, `walker`, `churner`, `fighter`, each at most once, weights 0 to 1000, at least one above 0; a behaviour left out has weight 0 (for example `idle=40,walker=20,churner=10,fighter=30`) |
| `--start N` | `50` | The first step's bots, 1 to 5,000 (cut to `--max`) |
| `--step N` | `50` | Bots added each step, 1 to 5,000 |
| `--hold T` | `90s` | How long each step holds: `90s`, `2m` or plain seconds, 50 s to 1 h. The judged window is its last 60 s (the whole hold less 30 s when shorter than 90 s); everything before it settles: 30 s, longer when the hold exceeds 90 s |
| `--max N` | the run's size | The most bots, 1 to the run's size |
| `--limit name=value` | the [defaults](#the-limits) | Overrides one limit, in its unit; repeat for more |
| `--dial HOST` | the join reply's host | As for `check` |
| `--prometheus URL` | `http://10.10.1.15:30090/` | Prometheus's http(s) origin |
| `--pod NAME` | `avalon-world-loadtest-0` | The world server's pod, for its memory limit |
| `--sign-in-concurrency N` | `8` | Sign-ins at once, 1 to 64: the ramp's and the context refresher's together |
| `--forest-time T` | `5m` | How long a [fighter](#fighters)'s trip stays in the forest: `90s`, `5m` or plain seconds, above 0, at most 1 h |
| `--party-size N` | `1` | The fighters' [party](#parties) size, 1 (solo) to 6 |

The console prints one line per step, then the result and the report's path. A step with fighters adds their count,
the forest instances the world ticked per tick and the trips completed:

```text
step 3  bots 150  tick p99 4.2 ms  ack p95 31 ms  ws 22%  → pass
step 4  bots 200  tick p99 9.8 ms  ack p95 35 ms  ws 31%  fighters 60  forest 41.2  trips 18  → pass
```

### cleanup

```bash
dotnet run -c Release --project tools/Avalon.LoadTest -- cleanup
dotnet run -c Release --project tools/Avalon.LoadTest -- cleanup --run ABC
```

This deletes the run's accounts as an admin (who is asked for a username and password, as in `provision`), with
their characters in every world, then deletes the run file. `--run` names the run; it is needed only when several
are kept. How each run id is handled, and what a 409 means, is under [Run files](#run-files).

## Run files

A run file is kept per provision in `%LOCALAPPDATA%\Avalon.LoadTest\runs\<RunId>.json` (on Linux
`~/.local/share/Avalon.LoadTest/runs`, on macOS `~/Library/Application Support/Avalon.LoadTest/runs`; readable by its
owner only there). `<RunId>` is the first run's id, and it is the
handle `--run` takes. Without `--run`, a command uses the only file there. With none it says to provision first, and
with several it lists them.

It holds:

| Field | What |
|---|---|
| `runId` | The first run's id |
| `runs` | Every run id this provision asked the API for, each `Pending` or `Created` |
| `api` | The API origin the accounts were made on. Every command of the run uses it, and it must be https |
| `worldId` | The only world the bots enter |
| `botPassword` | The password every account of the run signs in with. This is the only copy: the file is a secret |
| `bots` | The accounts' usernames, in index order (`--bot N` and the ramp count from 0) |
| `createdAt` | When the provision started |

**Pending and Created.** Each run id is written to the file as `Pending`, together with the bot password, before
its create call goes out, and becomes `Created` once the API has answered. A create whose answer is lost to a
timeout or a cancel, after the API may already have committed it, therefore still leaves its id in the file. A
`Pending` id may hold no accounts at all. Its create may never have landed, and then another provision may have taken
the same id since.

**Cleanup**, run by run in the file:

- A `Created` run is deleted.
- A `Pending` run is deleted only when no other kept run file lists the same id. If another file lists it, it is
  skipped (that file's cleanup deletes it). If another kept file cannot be read, it is skipped and this file is kept:
  repair or remove that file and run cleanup again. Deleting an id that holds no accounts answers 0 and is harmless.
- Each deleted run prints its count. An account that gained something a run never gives (another license, a
  purchase, an email, a role beyond `Player | PTR`, ...) is kept and named.
- **409**: the API refused the delete, and the tool prints its reason. Nothing was deleted when a bot of the run
  still plays (a live game session, or a live gameplay fence in a world, whose lease has not run out) or a world's
  characters database is unavailable. A 409 also comes when a bot entered a game while the delete ran: the run's
  characters are already gone, its accounts not, and running cleanup again once the bots are stopped finishes it.
  Stop the ramp, wait about a minute (a closed session's lease runs out), and run cleanup again. A 409 right after a
  ramp usually means its stop was cut short (a second Ctrl+C) or the world had not drained: see
  [Stopping](#stopping).
- **500**: a delete that deadlocked with a bot still entering. Nothing was half-done; stop the bots and repeat.
- The run file is deleted only when every run was deleted. Otherwise it is kept and cleanup can be run again; it is
  safe to repeat.

When `provision` itself fails part way, it prints which runs were created and which are pending, and
`cleanup --run <RunId>` deletes them. If the run file could not even be written, it prints the ids and the
`DELETE` request that removes them by hand.

## What the bots do

### Accounts and characters

Bot `N` is the run's `N`th account. Its character is named after the account, with class `N % 4 + 1` and gender
`N % 2`. The character is created on the bot's first entry into the world and selected on every later one, so the
characters persist from one ramp of a run to the next, until cleanup.

A bot signs in once per ramp, through the same REST chain as the launcher and the game client (authenticate, a PKCE
launcher code, the launcher token, a game ticket, a provider attempt, the handoff redemption). That costs identity
two BCrypt checks. After that the game context is refreshed rather than signed in again. The context refresher looks
at every bot every 5 seconds and refreshes the ones due: a context is due a minute before the earlier of its
credential's expiry and its authorization deadline, less a jitter of 0 to 20 seconds per bot. A refresh that fails is
retried on the next pass, under the same idempotency key, and counted as a sign-in failure (`refresh:no-reply`, or
`refresh:<status>`). A context that can no longer be refreshed (revoked, or its refresh token spent) is counted as a
sign-in failure (`refresh:<code>`) and replaced by a fresh sign-in. A bot whose fresh sign-in also fails
(`sign-in-again:<call>`) gives up for good. It no longer counts as live, and the next fill signs in another account
in its place. A first sign-in that fails counts as `sign-in:<call>`. Sign-in failures are identity's side and never
part of admission. A churner's reconnect keeps its context, so the refresher goes on refreshing (and if need be
replacing) it; only the bot's final leave, which signs the context out, ends that.

### Entry and retries

Entering is the client's sequence. Each step has its own timeout:

| Step | Timeout | Notes |
|---|---|---|
| `join` | 60 s | The join ticket. A timeout, a 5xx or `CONTEXT_CHANGED` is retried twice under the same idempotency key. A reply for any world but the run's is refused (`join:wrong-world`) and never dialled |
| `connect` | 10 s | TCP and TLS, pinned to the SHA-256 of the certificate the join reply names |
| `admission` | 20 s | The join ticket and the client's session key |
| `handshake` | 10 s | The protocol version, `0.2.0` |
| `list`, `create`, `select` | 30 s each | `create` only when the list lacks the character |
| `spawn` | 35 s from the select reply | Inputs start only after the first world-state frame, the sign the character has spawned |
| `first-ack` | 10 s | An idle input repeated every 50 ms until one is answered: the bot is in the world. These probes are not ack-latency samples |
| `leave` | 20 s | Waits for the world's logout save |

A failed entry is counted by kind (`<step>:<code>`, for example `join:ACTIVE_GAME_SESSION`, `admission:<result>`,
`spawn:timeout`, `connect:tls`) and retried up to three times, after 1, 3 and 9 seconds, each retry taking over any
session the failed attempt left. In a ramp, a bot whose last retry failed waits 30 seconds and starts again, and in
each step it tries without getting in it counts against admission once, so a bot never drops out quietly; `check`
stops at the last failure.

### Behaviours

One input driver sends every in-world bot's input at 60 Hz, as the game client's fixed step does. Each bot's input
numbers start at 1 on every connection. If the driver falls more than three steps behind, it drops the missed steps
rather than sending them in a burst, and its lateness shows it.

| Behaviour | Does |
|---|---|
| `idle` | A zero-direction input every step, as the real client sends while standing |
| `walker` | Walks a random heading. When an ack reports less than 0.1 m/s while it asked to move (a wall), it turns 90 to 270 degrees and goes on. There is no navmesh in the tool, so walkers gather along walls more than players do |
| `churner` | Walks like a walker. After a random 2 to 5 minutes in the world it changes character on the same connection (leave, the logout save, list, select, load, first ack). Every fourth churn it reconnects instead: it closes the connection, keeps its game context, and enters again with a new join ticket and a takeover |
| `fighter` | Walks from town into a forest of its own, fights its creatures with its class's basic ability, walks back out and goes again, alone or in a party: see [Fighters](#fighters) |

A connection the world closes while a bot is in the world is counted as a disconnect, and the bot enters again with
a takeover. A churn that fails is counted, and the bot closes its connection and enters afresh.

**The mix.** Behaviours go to bots by smooth weighted round-robin over the bot index, so every count of bots holds the
`--mix` shares as closely as whole bots allow, interleaved. With the default mix, every block of ten bot indexes from
0 (0-9, 10-19, ...) holds 6 idle bots, 3 walkers and 1 churner. Parties never change a bot's behaviour: see
[parties](#parties).

### Fighters

A fighter makes round trips from town (map 1) through a forest of its own (map 2) and back for as long as it is in
the world. Its first trip waits a random 0 to 30 seconds after it entered (a party's members wait one such draw
together, once their party has formed or gone solo), so that the forests' builds, each a navmesh bake of a quarter to
half a second on the thread pool, do not all land at once. A trip:

1. **To the portal.** From the town's spawn (15, 15) it walks straight north to the forest portal (15, 45), until its
   acked position is within 2.5 m of it (the world takes the portal within 3 m).
2. **In.** It sends `CMSG_ENTER_MAP` for map 2 and stands until `SMSG_MAP_TRANSITION` answers. The **forest entry
   time** is the portal request to that transition; an entry builds the instance, off the tick, so it includes the
   bake.
3. **Fighting.** Ten times a second (the rate the world sends state) it picks the nearest live creature within 60 m
   from its [world-state table](#the-world-state-table) and walks at it. Within its basic ability's reach, less a
   margin (1 m, or a fifth of a shorter reach: a creature holds its station 1.5 m from what it fights, so a warrior
   stops at 2 m), it stands facing the creature and casts every 0.85 s (the basic abilities' 0.8 s cooldown and a
   margin), aimed at the creature's position: warrior Cleave (200, 2.5 m), wizard Arcane Bolt (210, 20 m), hunter
   Quick Shot (220, 25 m), healer Smite (230, 18 m); the class is `index % 4 + 1`. The abilities cost nothing. With no
   creature in sight it walks north, away from the entry. Up to eight creatures it cast at are watched, and each is
   counted as a **kill** when the table shows it dead (a creature several fighters cast at counts for each of them);
   one that leaves the view is no longer watched.
4. **Out.** After `--forest-time`, or after a minute with no live creature within 60 m (the forest around it is
   cleared), it walks back out along its trail (below), then by the entry spawn (15, 15) to the back portal (15, 5),
   and sends `CMSG_ENTER_MAP` for map 1. The transition into town completes the trip, and the next one starts at once.

**Moving.** A fighter steers straight at its goal (the portal, a creature, a crumb). When an ack shows its last heading
stopped against a wall, it turns 90 to 270 degrees away, as a walker does, and holds that heading for a second before
steering at the goal again. The tool has no navmesh.

**Breadcrumbs.** The forest is built of 30 m chunks with branches, so from deep inside there is no straight way back.
In the forest a fighter drops a crumb each time it is 4 m from the last one; walking back within 4 m of an older crumb
cuts the loop since out of the trail. The trail holds 256 crumbs: full, every other one is dropped (the oldest and the
newest kept), so a longer walk keeps a coarser trail rather than none. On the way out it steers at the crumbs newest
first, passing each within 1.5 m, then at the entry spawn and the back portal. The way out has an **exit budget**, set
when the fighter turns for the exit: the longer of 60 seconds and twice the walk back along its trail, then by the
entry spawn to the back portal, at the base walk speed of 4 m/s. The trail is never longer than the walk in, so the
budget is at most 60 seconds or twice `--forest-time` plus 5 seconds (the 10 m from the entry spawn to the back
portal), whichever is longer. A fighter not back in town within it reconnects (`forest:exit-timeout`).

**Death.** The fighter's table holds its own character too, by the guid of the character it selected. When the table
shows that character dead, the fighter counts an **own death**, drops whatever it was doing and sends
`CMSG_RESPAWN_AT_TOWN`, repeated every 5 seconds until the world moves it (the world drops the ask while a move is
under way). The transition into town ends the trip as a death, and the next trip starts. Still dead 30 seconds after
dying, it reconnects (`forest:respawn-timeout`): a fresh login lands in town.

**After a transition.** The acks of inputs sent before a map transition may still describe the old map. Until an ack
answers an input sent since the transition, the fighter stands and reads neither its position nor a wall from them; a
dead fighter is never held. Held 10 seconds with no such ack, it reconnects (`forest:stale-acks`).

**Reconnecting.** A fighter that asks to reconnect stands still while its bot closes the connection, keeps its game
context, and enters again with a new join ticket and a takeover, as a churner's reconnect does. That entry is an entry
like any other, counted in admission. A character that logs out in the forest is saved to its town, so the fighter
starts again from town.

**Admission.** A forest entry is a map transition inside the world, not an entry into it, and is never part of
admission. Fighters' entries into the world, and their reconnects, are, and the admission limit applies to them as to
every bot.

#### The world-state table

Only fighters decode the world-state packets (`SMSG_WORLD_STATE_ADD`, `_UPDATE`, `_REMOVE`), into a table per
connection of the objects in the character's view by guid: the type (`guid >> 56`: 1 character, 2 creature, ...), the
position, whether it is dead and its current health. An add replaces what the table held for its guid, an update
changes only the members it carries, a remove drops the guid. The table is cleared when a character spawns and on
every map transition. Every other behaviour leaves those packets unread, so the bot PC's cost of them grows only with
fighters. `WorldStateDecodeShould` checks the decoder against packets built by the server's own code.

#### Timeouts

| What | Time | Then |
|---|---|---|
| The walk to the town's portal | 20 s | `forest:portal-timeout`: back to town, and the next trip after 30 s |
| An entry, portal request to transition | 30 s | `forest:enter:timeout`: back to town, and the next trip after 30 s |
| An entry refused | at once | `forest:enter:<result>`: back to town, and the next trip after 30 s |
| No live creature within 60 m | 60 s | The fighter leaves the forest; not a failure |
| The way out | the exit budget | `forest:exit-timeout`: reconnect |
| The back portal refused | at once | `forest:leave:<result>`: back toward the portal, asked again no sooner than 1 s later, while the exit budget lasts |
| Dead | respawn asked every 5 s | Still dead at 30 s: `forest:respawn-timeout`, reconnect |
| A fresh ack after a transition | 10 s | `forest:stale-acks`: reconnect |
| A party's formation attempt | 20 s | Tried once more from scratch; then counted, and its members fight solo |
| A formed party's roster on a member's connection | 20 s without the whole party | `party:fell-apart`: its members fight solo |

#### Failure kinds

A fighter's failed step is counted by kind in the step's trip failures; a party that failed to form, or fell apart,
once by reason in the step's party failures. `<result>` is the world's `MapTransitionResult` (`NotNearPortal`,
`GenerationFailed`, `InstanceFull`, `MoveInProgress`, ...), and a party's `<result>` its `PartyResult`.

| Kind | What |
|---|---|
| `forest:portal-timeout` | The fighter was not within the town portal's radius 20 s after it set out for it |
| `forest:enter:<result>` | The world refused the forest entry |
| `forest:enter:timeout` | The world did not answer the forest entry within 30 s |
| `forest:leave:<result>` | The world refused the back portal; the fighter asks again (one count per refusal) |
| `forest:exit-timeout` | Not back in town within the exit budget; the fighter reconnects |
| `forest:respawn-timeout` | Still dead 30 s after dying; the fighter reconnects |
| `forest:stale-acks` | No ack from the new map 10 s after a transition; the fighter reconnects |
| `party:timeout` | An attempt ran out its 20 s with every member in the world |
| `party:not-in-world` | An attempt ran out with a member not in the world (its entry failed and is retried), or a member left it as a request went out |
| `party:decline:<result>`, `party:leave:<result>` | Starting from scratch, a member's decline of an invite it held, or its leave of a party it was in, was refused |
| `party:invite:<result>` | The world refused the leader's invite |
| `party:accept:<result>` | The world refused a member's accept |
| `party:InviteExpired`, `party:InviteDeclined` | An invite of the attempt ended unanswered |
| `party:io` | A connection failed while a request was sent |
| `party:unexpected` | Anything else went wrong in the attempt |
| `party:fell-apart` | A formed party's roster did not list the whole party for 20 s on a member's connection |

A party failure is counted once per party, with the reason of its second failed attempt.

#### Parties

`--party-size N` (1 to 6, the world's `Game:MaxPartySize` and the forest's seats; 1, the default, is solo) groups the
fighters into parties. Each time the ramp enters bots, the fighters among them are grouped in bot-index order into
whole parties of N; the fighters left over, fewer than N, fight solo. The mix decides every bot's behaviour as it does
without parties, so the step's total and the mix's shares stay exact: a step that adds 30 fighters with
`--party-size 4` makes 7 parties and 2 solo fighters. A party never spans two steps.

**Forming.** A party's members stand in town while it forms. An attempt waits until every member is in the world,
then starts from scratch: every member, the leader last, declines any invite it holds and leaves any party it is in.
Then the leader (its first member) invites each member by character name (`CMSG_PARTY_INVITE`), and each member answers
its `SMSG_PARTY_INVITE` with an accept (`CMSG_PARTY_INVITE_RESPONSE`). The party is formed once the leader's
`SMSG_PARTY_ROSTER` lists every member. Each attempt has 20 seconds, the wait for the members included; a failed
attempt is tried once more, and a second failure is counted by reason, every member leaves what was formed, and the
members fight solo.

**Together.** Once formed (or gone solo), the members set out after one random 0 to 30 second wait, together; a
member of a formed party sets out once the roster on its current connection lists the whole party. Each enters the
portal on its own, and the world puts them all in the party's one forest. Each member fights, dies and leaves on its
own, and goes again on its own.

**Falling apart.** The world keeps a party in memory while it runs, a member who logged out included (shown offline),
so a member that reconnects is still in it and gets its roster as its character spawns. A member that gave up for
good stays on the roster, offline, and holds nobody back. A member whose roster on its current connection has not
listed the whole party for 20 seconds (the world restarted and forgot its parties, or a member was removed) makes the
party fall apart: counted once (`party:fell-apart`), every member leaves what is left of it, and they fight solo from
then on.

**Stale parties.** Until it restarts, the world keeps a party for a character that left it only by logging out, and a
character in a party is taken by the portal to its party's forest. So a fighter that fights solo (`--party-size 1`, a
fighter left over from whole parties) leaves a party the world still holds for it: on the first roster with members
that a connection of it reads (the world sends one as a party member's character spawns), it sends one
`CMSG_PARTY_LEAVE`. A member of a party gone solo that was out of the world when the others left does the same once
back. A character in no party gets no roster, and nothing is sent.

## The ramp

1. The ramp reads world `W`'s version, pod uid and players online from Prometheus, with its container's restart count
   and last start time (kube-state-metrics), before any sign-in. It reads them again when the ramp ends, and once
   more after the bots have left, to tell whether the world restarted and whether the old process ended after the
   last judged window (see [a world restart](#a-world-restart)).
2. It signs in `--start` bots (at most `--sign-in-concurrency` at once). If a sign-in fails, that account is passed
   over for the next one.
3. It enters them, 32 at a time, each with its behaviour from the mix. With `--party-size` above 1, the fighters among
   them are grouped into [parties](#parties), which form in the background once their members are in.
4. It holds the step for `--hold`. The judged window is the hold's last 60 seconds (the hold less 30 seconds, for a
   hold under 90 seconds); everything before it settles and is not judged: 30 seconds, longer when the hold exceeds
   90 seconds. While the step holds, the next step's bots sign in.
5. 15 seconds after the hold ends, so the world's last 10-second export has landed, it reads Prometheus for the
   window that ended with the hold. Then it judges the step: the [limits](#the-limits) and the
   [decision rule](#the-decision-rule) say whether it moves to the next step, holds the same count again, or stops.
6. The next step tops the live bots up to the new count (`--step` more, up to `--max`), and the ramp goes on.

Each step therefore takes the hold plus about 15 seconds. With the defaults, a ramp to 1,000 bots is 20 steps of
about 105 seconds, roughly 35 minutes, plus any re-holds.

**Live bots** are those sent into the world that have not given up. A step's bot count, the decision and the capacity
count live bots. A live bot may be between a churn and its next entry. The report adds two cross-checks: the bots in
the world at the hold's end, and the world's own players online less the count before the ramp.

**Running out.** The ramp stops only when it cannot add a single bot to a step (the run's accounts ran out, or no
further bot could sign in): it stops as `stopped`, with the reason and the last passing count. A step it can fill only
in part is held, and judged, at the live count it reached.

## The limits

A step breaches a limit when its value is on the tripping side of the threshold. Override a limit with
`--limit name=value` in the unit below. A fraction is 0 to 1, and no threshold may be negative. Examples:
`--limit tick-p99=20` (20 ms), `--limit memory=0.9` (90 %).

| Name | Default | Unit | Trips when | Source |
|---|---|---|---|---|
| `tick-p99` | 16.7 | ms | above | Prometheus: `histogram_quantile(0.99, sum by (le)(rate(world_tick_duration_microseconds_bucket[w]))) / 1000` |
| `tps` | 58 | ticks/s | below | Prometheus: `avg_over_time(world_tick_rate_tps[w])` |
| `ack-p95` | 150 | ms | above | Bots: the 95th percentile of input-to-ack latency over the judged window, over the input driver's inputs (an entry's `first-ack` probes are not samples). An input still unanswered when its slot is reused, about a second later, counts at its age then |
| `drops` | 0 | count | above | Prometheus: `(sum(increase(network_out_dropped_total[w]) and network_out_dropped_total offset w) or vector(0)) + (sum(network_out_dropped_total unless network_out_dropped_total offset w) or vector(0))`, the packets a full outbox evicted. A packet type's series exists only from its first drop, and `increase` alone misses a new series' first sample, so a series already there at the window's start counts its increase and one first seen within the window counts its whole value |
| `admission` | 0.01 | fraction | above | Bots: the bots that could not get into the world during the step ÷ the bots that tried, over the whole step, settle included. Each bot counts once: it tried when any entry attempt of its ended in the step, and got in when any of those succeeded, so a failure followed by a success in the step is a bot that got in, and a bot that only failed counts once however many retries it made. An attempt counts in the step its outcome lands in. Entries are first entries, re-entries, reconnects and character changes; sign-in and leave failures are not part of it. The step row also gives the attempts, the failed ones and the failures by kind |
| `memory` | 0.85 | fraction | above | Prometheus: `max(dotnet_process_memory_working_set_bytes)` ÷ `kube_pod_container_resource_limits{namespace="avalon",pod=<--pod>,container="avalon-world",resource="memory"}`, at the hold's end |
| `gen2` | 1 | per minute | above | Prometheus: `sum(increase(dotnet_gc_collections_total{gc_heap_generation="gen2"}[w])) * 60 / w` |
| `gc-pause` | 0.05 | fraction | above | Prometheus: `sum(rate(dotnet_gc_pause_time_seconds_total[w]))`, the share of the window the GC paused the process |
| `save-p95` | 1000 | ms | above | Prometheus: `histogram_quantile(0.95, sum by (le)(rate(world_character_save_duration_milliseconds_bucket[w])))`; 0 when the window is known to have had no save |
| `gen-cpu` | 0.80 | fraction | above | The tool: its process CPU time over the judged window ÷ (the window × the bot PC's logical cores) |
| `gen-lag` | 5 | ms | above | The tool: the input driver's lateness p95 over the judged window (how late each 60 Hz step started) |

Every Prometheus query is filtered to the run's world (`avalon_world_id="W"`). `w` is the judged window in whole
seconds, and the query is evaluated at the hold's end. When a query returns several series, the worst one counts
(the highest, or the lowest for `tps`). The metrics themselves are described in
[instrumentation](instrumentation.md#world-metrics-for-load-testing).

`gen-cpu` and `gen-lag` measure the bot PC, not the server. When the bot PC is saturated, it sends late and reads
late, so the server's numbers from that step are not trusted.

## The decision rule

Each step gets one verdict:

- **breach**: at least one limit tripped. A breach wins over a missing value in the same step.
- **unknown**: nothing tripped, but at least one value is missing or not a finite number: an empty series, a failed
  query, Prometheus unreachable. A missing `drops` value is not unknown, since its query already reads no series as
  0. A failed `drops` query is unknown.
- **pass**: every limit was judged and none tripped.

Then:

| Verdict | What the ramp does |
|---|---|
| pass | The next step. If this was the re-hold of a breached step, the step is recorded as a **blip** and the ramp continues. If the step held `--max` bots, the ramp stops: **no limit reached** up to that count |
| breach, with no re-hold pending | **Re-hold**: the same count is held and judged again |
| unknown, with no re-hold pending | **Re-hold**, as for a breach: an unknown step stops the ramp only when the next one is unknown too |
| breach, right after a breach | **Stop**. The capacity is the live bots of the last passing step (0 if none passed). "Failed first" lists the breaches of this confirming step. If any of them is `gen-cpu` or `gen-lag`, the result is instead **bot PC saturated: capacity ≥ N** |
| unknown, right after an unknown | **Stop, unknown**, with the last passing count (if any) as a lower bound |
| breach after an unknown, or unknown after a breach | Re-hold again. The earlier verdict is neither confirmed nor cleared, and the newer one is now the one pending |
| after three re-holds in a row at one count, a verdict that does not confirm the pending one | **Stop, unknown** (inconclusive). At most three re-holds are made at one count, so a series that keeps alternating between breach and unknown ends here (breach, unknown, breach, unknown), with the last passing count as a lower bound. A fourth verdict that does confirm the pending one stops as the rows above say |

**The drops flag.** A `drops` breach in a step where the bot PC was above 60 % CPU is flagged in the report's notes: a
busy bot PC reads its sockets slowly, so the drops may be the bots' doing rather than the server's. The flag does not
change the decision.

## Stopping

Whatever ends the ramp (a stop, `--max`, Ctrl+C, an error, or bots that could not be filled), it then runs the same
stop sequence. Ctrl+C does not cut it short; each part has a timeout of its own.

1. The report is saved as soon as the outcome is known, before any bot leaves. The world-drained note reads
   "pending".
2. The two breakers are armed (see below). Sign-ins still running for the next step are cancelled, and each bot's
   life loop (churns and re-entries) stops. Fresh sign-ins of bots whose context could not be refreshed stop too, but
   context refreshes go on, so a bot waiting for its turn to leave keeps its context.
3. Every signed-in bot leaves the world and signs out, 32 at a time. A leave waits up to 20 seconds for the world's
   logout save, and a sign-out has 10 seconds of its own; the ramp gives each bot at most 60 seconds in all. The
   sign-out runs however the leave ended. Within its 10 seconds a sign-out with no reply, a 5xx or a 409
   `IN_PROGRESS` (the context kept changing under it, a refresh most likely, and is still live) is sent again, each
   time with the newest credential the bot holds: the one a 409 answered no longer ends the context.
4. The context refresher finishes its current pass. Any refresh that never got an answer is sent again, all within
   one 60-second deadline, so that no context the server rotated is left alive behind a sign-out that missed it.
5. The ramp polls the world's players online every 5 seconds, for up to 90 seconds, until it is back to the count
   before the ramp.
6. The report is written again over the first one, now complete.

**How long it takes.**

- **Normal**: one leave (the world's logout save) and one sign-out per bot, in waves of 32, then the drain, which
  lasts until the closed sessions leave the players-online gauge and the next 10-second export has landed.
- **API down**: sign-outs get no reply, a 5xx, or time out after 10 seconds. After 32 such failures in a row the
  sign-out breaker trips. From then on sign-outs are skipped at once and counted, except one probe every 5 seconds;
  any answered sign-out closes the breaker again. A dead API therefore costs about one 10-second window rather than
  10 seconds per wave of bots, plus at most the refresher's 60-second settle and one last sign-out. Skipped contexts
  expire within 5 minutes. The console and the report say
  `API down during the stop: N sign-outs skipped; those contexts expire within 5 minutes.`
- **World hung**: leaves time out after 20 seconds. After 32 timeouts in a row the leave breaker trips. From then on
  leaves are skipped and each socket is closed at once, except one probe every 5 seconds; any answer from the world
  closes the breaker. A closed connection or a TLS or I/O failure says nothing about a hung world and does not count
  toward the trip. The world ends a session when its socket closes. The console and the report say
  `World unresponsive during the stop: N leaves skipped; sockets closed.`

The breakers are armed only for the stop sequence. During the ramp every leave and sign-out is made, and its failures
are counted in the step (leave failures by kind, sign-out failures), so a breaker can never change the load a step is
judged on.

If the world has not drained after 90 seconds, the console says how many players it still had, and the report says
to wait before cleanup. A cleanup then may get a 409 until the leases run out, about a minute.

## The report

Each ramp writes two files with the same data to `%LOCALAPPDATA%\Avalon.LoadTest\reports\` (on Linux
`~/.local/share/Avalon.LoadTest/reports`, on macOS `~/Library/Application Support/Avalon.LoadTest/reports`): `<yyyyMMdd-HHmmss>-<RunId>.md` to read and `.json` with the same data
for tools, named from the ramp's start time in local time. A name that already exists gets `-2`, `-3`, and so on;
no report is ever overwritten, except the ramp's own early save. Reports are never written to the repository and hold
no secret (no password, ticket or credential). If the report cannot be completed after the stop (a full disk, a file
held open), the console says so and names the early report, which remains as it was saved (world drained: pending);
the exit code still follows the verdict, and is 1 whenever the run does not stand (see
[a world restart](#a-world-restart)).

- **Header**: the date and time, the world's server version (Prometheus `target_info`'s `service_version`, from the
  series with the newest sample: a world restarted within the last 5 minutes still has its old process's series in
  Prometheus's lookback; versions sharing the newest sample are all named), read with its pod uid (`k8s_pod_uid`)
  before the ramp and again at its end, with the container's restarts (kube-state-metrics'
  `kube_pod_container_status_restarts_total`, scoped to the pod uid read before the ramp, or to `--pod` when there was
  none) and its last start (`kube_pod_container_state_started`, by that uid and by `--pod`): `world restarted during
  the ramp (A → B; pod x → y; container restarted N times, last started 18:34:40 UTC)`, naming what showed it, when
  the pod differs, the restarts rose or the container started again, whatever the version, or `changed during the
  ramp: A → B` when only the versions differ. `during the ramp` is said only when the restart is proven to have come
  before the last judged window ended: the new process started by then, or the container in the pod read before the
  ramp last ended within the ramp by then. A new pod's start bounds the old one's end because the world runs as a
  StatefulSet, whose pod is replaced under the same name only once the old pod is gone. `world restarted after the
  ramp's last judged step (...)`, with `, while the bots left` when the old process ended after the stop began, when
  that is proven; otherwise `world restarted; not proven after the last judged step (<the missing fact>): ...` (see [a
  world restart](#a-world-restart)). A version read only at the end is marked `(read at the end)`, and a check that
  could not be made whole says so: `(restart check: partial, ...)` or `(restart check: unknown, Prometheus gave
  nothing at the end)`. The tool warns when `target_info`'s pod name is not `--pod`. Then the run and its size, the
  API, the mix, the fighters' settings (`--forest-time` and `--party-size`, with the 0 to 30 second first-trip wait, or
  `none in the mix` when the mix gives fighters no weight), the ramp settings (start, step, hold and judged window,
  max, sign-in concurrency), Prometheus, the pod, `--dial`, the bot PC's CPU model and logical cores, its clock's
  offset from Prometheus's at the start (by which every query's time was corrected), and the limits, each marked when
  overridden.
- **Result**: one of `capacity N bots`, `no limit reached up to N bots`, `bot PC saturated: capacity ≥ N bots`,
  `stopped: steps that could not be judged`, or `stopped (<reason>)`, followed by `; does not stand: <reason>` when
  the run does not stand (`the world server restarted during the ramp`, `the world server restarted; not proven after
  the last judged step (<the missing fact>)`, or `the restart check was partial (...)`). When the finished report's
  reads change the line, the console prints it again. A stop reason is one of: Ctrl+C, an error, the run's accounts
  running out, or no further bot able to sign in. Each `stopped` result adds the last passing step's count, or says
  that no step passed. Then "failed first": each confirmed breach with its value and threshold (`tick-p99 18.2 ms >
  16.7 ms`).
- **Steps**, one row per held step (a re-hold is a row of its own):
  - live bots, by behaviour (idle / walker / churner / fighter); bots in the world at the hold's end; players online
    less the count before the ramp;
  - map instances at the hold's end, every map together (`avalon_world_instances_active` carries no map type; the
    [fighters' section](#the-fighters-section) splits them); tick p99; average TPS; ack p50, p95 and p99; drops; the
    deepest receive backlog of any connection; working set (MB and % of the limit); gen2 per minute; GC pause; save
    p95;
  - admission as bots that never got in ÷ bots that tried; entry attempts and failed attempts, and failures by kind;
    leave failures by kind and sign-in failures by kind (both apart from admission); disconnects;
  - the bot PC's CPU and the driver's lateness p95;
  - the verdict: `pass`, `pass (blip)`, `re-hold (<breaches, or unknown: names>)`, `stop (...)`,
    `stop, unknown (...)`, or `pass, the last step`. A value Prometheus did not give reads `n/a`.
- **Fighters**, for reading only (see [the fighters' section](#the-fighters-section)); `None: no step held a fighter.`
  when none did.
- **Post-update stages**, for reading only (no limit is judged on them): one row per step, one column per stage of
  `world.post_update.duration` (`quests`, `inventory`, `sheet`, `ability_amounts`, `party_status`, `presence`,
  `pings`, `outbox`, `continuations`, in the order the tick runs them; [instrumentation](instrumentation.md#tick-and-instance-time)),
  each `mean / p99` per tick in µs over the judged window. The mean is `sum by (stage)` of
  `rate(world_post_update_duration_microseconds_sum[w])` over the same of `_count`; the p99 is `histogram_quantile(0.99,
  sum by (stage, le)(rate(world_post_update_duration_microseconds_bucket[w])))`, interpolated within the stage buckets.
  Whether the world exports them is asked against the tick histogram every build has:
  `count(world_post_update_duration_microseconds_count) or (0 * count(world_tick_duration_microseconds_count))`. A
  world that reports its ticks and no stage (0: a build from before #875) reads `not exported by this world build` on
  its rows, and when no step was reported and at least one came from such a world, the section says so in one line
  instead of a table. A world Prometheus has nothing from at all (an empty answer: a stalled export, a scrape gap, a
  wrong world id), failed queries, or a window too short for a rate read `n/a`; when only one of the two stage queries
  failed, only its half of each cell reads `n/a`. The JSON has them under each step's
  `server.postUpdate`: `readout` (`Reported`, `NotExported` or `Unknown`) and `stages` (`stage`, `meanUs`, `p99Us`).
- **Notes**: a world that restarted during the ramp, or after its last judged step without proof (the run does not
  stand: run again); one proven to have restarted after the last judged step (in bold; the verdict stands); or a
  restart check that was partial or unknown (the run does not stand); the blips; the steps whose drops may be the bot
  PC's; sign-ins, with their throughput (one every X s with N at once, and per minute; identity's side, apart from the
  world) and the sign-in and refresh failures, by kind; whether the world drained after the stop; failed sign-outs;
  the stop's leave failures by kind; and the leaves and sign-outs the breakers skipped.

**Reading it.** The capacity is the last step that passed. "Failed first" says which limit gave out, and the steps
before it show how that value climbed. A `bot PC saturated` result is a lower bound: run again with a lighter mix
(fewer walkers and churners) or a stronger bot PC. A ramp that stopped unknown usually means a missing metric: check
[the export interval](#before-a-run) and Prometheus. If the bot count and the two cross-checks disagree, bots were
not where the tool thought they were. Look at the failure kinds and the disconnects.

### The fighters' section

One row per step, for reading only: no fighter number is a limit, and none changes a verdict.
Fighters load the world (each forest is about 200 creatures whose AI ticks every tick, and its bake), and the limits
judge that load as they judge any other; the admission limit still applies to fighters' entries into the world. The
counts cover the whole step, settle included. The entry percentiles of the settle and of the judged window do not
merge, so each is the slower of the two, an upper bound.

| Column | What |
|---|---|
| Fighters | The step's live fighters |
| Instances per tick: town / forest | The town and normal-map (forest) instances the world ticked, on average per tick over the judged window (below) |
| Forest entries | Forest entries answered with the forest |
| Entry p50 / p95 | Forest entry time, portal request to transition, in ms |
| Trips completed | Trips that walked out of the forest into town |
| Casts sent | Casts of the basic abilities |
| Casts refused (by reason) | Casts the world refused (`SMSG_ABILITY_NOT_READY`), in all and by `CastRejectReason` (`Gcd`, `Cooldown`, `OutOfRange`, `TargetNotFound`, ...) |
| Kills seen | Creatures a fighter cast at seen dead afterwards, once per fighter that cast at it |
| Own deaths | Deaths of the fighters' characters |
| Trip failures by kind | Failed steps of the trips, by [kind](#failure-kinds) |
| Parties formed | Parties whose leader's roster listed every member |
| Party failures by reason | Parties that failed to form twice, or fell apart, by [reason](#failure-kinds) |

**Instances by map type.** `avalon_world_instances_active` carries no map type, so the steps table gives the total
only. Every live instance's update is recorded once per tick in `world.instance.update.duration`, tagged `map.type`
(`map_type` in Prometheus: `Town` or `Normal`; [instrumentation](instrumentation.md#tick-and-instance-time)), so a map
type's updates per tick are its mean count of live instances over the window:
`sum by (map_type)(rate(world_instance_update_duration_microseconds_count[w])) /
scalar(sum(rate(world_tick_duration_microseconds_count[w])))`. On world 4 the only normal map the portals lead to is the
forest, and with the [lifetime at 0](#before-a-run) an emptied forest is freed on the next tick, so the forest figure is
the forests in use. Whether the world exports the updates is asked as for the post-update stages:
`count(world_instance_update_duration_microseconds_count) or (0 * count(world_tick_duration_microseconds_count))`; 0
(a world build without it) reads `not exported by this world build`. Nothing from the world at all, a failed query, an
empty rate (a window too short for one) or no tick to divide by reads `n/a`. With the town's rate there, a map type
the answer lacks reads 0: no instance of it has ticked since the world started, or its first did so only in the
window's last sample.

**JSON.** The ramp settings carry `ramp.forestTimeSeconds` and `ramp.partySize`, and each step:

- `byBehaviour.Fighter`, the live fighters;
- `client.forestEntries`, `client.forestEntryP50` and `client.forestEntryP95` (ms; `NaN` with no entry),
  `client.forestTrips`, `client.castsSent`, `client.castsRefused` (counts by reason), `client.kills`,
  `client.ownDeaths`, `client.fighterFailures` (counts by kind), `client.partiesFormed` and
  `client.partyFormFailures` (counts by reason);
- `server.instancesByMap`: `readout` (`Reported`, `NotExported` or `Unknown`), `town` and `forest` (instances per
  tick; null unless reported).

### A world restart

A run stands only when the world server that ran its judged steps is known not to have restarted before the last
judged window ended. A restart shows as a new `target_info` version or pod uid, a new newest pod named by `--pod`
(`kube_pod_start_time`), a rise in the container's restart count, or a later container start. It counts as after the
last judged step only when that is proven, by when the old process ended rather than when the new one started:

- a container restart inside the pod (a restart count up by exactly 1): its last termination,
  `kube_pod_container_status_last_terminated_timestamp`, is after the window's end;
- a replaced pod (its container's restart count unchanged): the old pod was still up after the window's end (its
  `kube_pod_deletion_timestamp`, which kube-state-metrics shows only while a pod terminates and so often never for a
  quick one, or its process's last `target_info` export, over the ramp: exported every 60 seconds, that evidence is
  up to 60 seconds older than the process's end), and the new pod started after the window's end and has not
  restarted.

Anything else, an unknown read included, does not stand and exits 1; so does a ramp whose restart check was partial or
unknown even though no restart was seen. The last read can come early: a replaced pod starts at 0 players online, so
the drain ends at once, and `target_info` (exported every 60 seconds) may still name the old process. The
kube-state-metrics reads, by the pod's uid and by `--pod`, show the new one; before them the runner waits, at most
45 seconds, for a kube-state-metrics sample taken after the drain ended, and when none comes the check is partial
(`kube-state-metrics not scraped since the drain ended`). That read, given 10 seconds, takes none of the earlier
read's values: what it cannot read stays unknown. Every time is corrected by the clock offset measured at the start.

Without kube-state-metrics in Prometheus the restart check can never be complete, so every ramp fails: it exits 1
with `does not stand: the restart check was partial (...)`.

## Ctrl+C

- **ramp**: the first Ctrl+C ends the current step and the ramp, which stops as `stopped (Ctrl+C)`, with the last
  passing count. The report is saved at once, and the whole [stop sequence](#stopping) runs, on its own timeouts.
  A second Ctrl+C ends the process at once. The report saved before the stop survives (world drained: pending).
  The bots' sockets close with the process, and the world ends their sessions. Their game contexts are not signed
  out and expire within 5 minutes. A cleanup may get a 409 until the session leases run out, about a minute.
- **check**: the bot leaves and signs out (30 seconds at most), and the command exits 1.
- **provision**: the runs already asked for stay in the run file, pending or created. The command prints them and
  exits 1, and `cleanup --run <RunId>` deletes them.
- **cleanup**: the runs not yet deleted stay in the run file; run cleanup again.

## Cautions

- **World 4 shares a node with Asthoria.** The load-test world runs on the same k3s node as the live world, and uses
  the same Postgres, Redis and identity. Check Asthoria's player count in Grafana before a run, and do not ramp
  while players are on. A ramp's saves, sign-ins and CPU are theirs to share.
- **Identity's sign-in rate.** Every bot sign-in costs identity two BCrypt checks, on the same identity that serves
  live sign-ins (CPU limit 2 on the homelab). `--sign-in-concurrency` (default 8) bounds how many run at once, the
  refresher's fresh sign-ins included. The report gives the throughput identity managed. Lower the concurrency if
  live sign-ins matter more than the ramp's pace.
- **The exempt address is a hole in the per-source limits.** While it is listed, anyone at that address can guess
  passwords without the per-source budget. Remove it from `exemptSources` when load testing stops.
- **Bots can enter any PTR world.** They hold `Player | PTR`, so the API would admit them to PTR as well. The tool
  enters only the run's world, and refuses a join reply for any other. Provision a run with `--world` set to the
  load-test world only.
- **The run file is a secret.** It holds the only copy of the bots' password. Keep it out of the repository, and do
  not paste it anywhere.
- **Cleanup after the drain.** Delete a run only once its ramp has finished stopping. A 409 means a bot still holds
  a live session: wait about a minute and retry.
- **One bot PC.** The bot PC's own CPU and timer accuracy cap what the tool can measure, and the report says when
  they did (`gen-cpu`, `gen-lag`). Close heavy programs before a ramp, and always run the Release build.
