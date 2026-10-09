# Load testing

`tools/Avalon.LoadTest` answers one question: how many players one world server holds, and what gives out first.
It runs headless bots from one PC. Each bot signs in over the REST API as the launcher and the game client do,
enters a world over TLS as the game client does, and then stands, walks or changes character while a ramp adds
bots in steps. Each step is judged against a set of limits: the server's numbers come from Prometheus, the bots' from
the tool, and the bot PC's own load decides whether those numbers can be trusted. The ramp ends at the first limit
breached twice in a row, and the tool writes a report with the capacity and what failed first.

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
5. **Prometheus is reachable from the bot PC.** The default is `http://10.10.1.15:30090/` (`--prometheus`). The ramp
   reads the world's players online before it signs in a single bot and refuses to start when Prometheus does not
   have that value. The memory limit comes from kube-state-metrics, by pod (`--pod`, default
   `avalon-world-loadtest-0`, in namespace `avalon`, container `avalon-world`).
6. **An admin account without MFA.** `provision` and `cleanup` ask for an admin's username and password. The account
   needs the Admin (or Console) role, and MFA has to be off on it: the tool does not answer an MFA challenge. The API
   checks the password again on every create and delete, so a wrong one counts as a failed login.
7. **A Release build.** The bot PC's CPU is one of the limits, and a Debug build spends more of it per bot. Run the
   tool with `-c Release`, as every example below does. The input driver sleeps on a high-resolution waitable timer
   on Windows 10 1803 and later; elsewhere it uses `Thread.Sleep` and spins through the last millisecond of each step.
8. **Asthoria is quiet.** See [Cautions](#cautions): check its player count first.

## Commands

```bash
dotnet run -c Release --project tools/Avalon.LoadTest -- <command> [options]
```

Every option takes a value (`--run ABC`, not `--run=ABC`). A command line the tool cannot run prints the usage and
exits 2. A REST or Prometheus failure that ends a command prints its reason and exits 1.

| Command | Exit 0 | Exit 1 |
|---|---|---|
| `provision` | every account created and kept in the run file | an API refusal, a cancel or a run-file write failure; what exists is printed with how to delete it |
| `check` | every step passed | the failing step and its reason |
| `ramp` | a verdict: a capacity, no limit reached, or the bot PC saturated | stopped short of one: unknown steps, Ctrl+C, an error, bots that could not sign in |
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
| `--behaviour idle\|walker` | `idle` | What the bot does for its 10 seconds |

A failure prints `Check failed at <step>: <reason>`, and the bot still leaves and signs out (with 30 seconds of its
own). The steps and their timeouts are in [entry](#entry-and-retries).

### ramp

```bash
dotnet run -c Release --project tools/Avalon.LoadTest -- ramp
dotnet run -c Release --project tools/Avalon.LoadTest -- ramp --start 100 --step 100 --max 1000 --limit tick-p99=20 --dial 10.10.1.17
```

This is the capacity run, described under [The ramp](#the-ramp).

| Option | Default | Meaning |
|---|---|---|
| `--run ABC` | the only run kept | The run whose accounts are used |
| `--mix SHARES` | `idle=60,walker=30,churner=10` | Behaviour weights: `name=weight` pairs of `idle`, `walker`, `churner`, each at most once, weights 0 to 1000, at least one above 0; a behaviour left out has weight 0 |
| `--start N` | `50` | The first step's bots, 1 to 5,000 (cut to `--max`) |
| `--step N` | `50` | Bots added each step, 1 to 5,000 |
| `--hold T` | `90s` | How long each step holds: `90s`, `2m` or plain seconds, 50 s to 1 h. The first 30 s settle; the rest, at most its last 60 s, is judged |
| `--max N` | the run's size | The most bots, 1 to the run's size |
| `--limit name=value` | the [defaults](#the-limits) | Overrides one limit, in its unit; repeat for more |
| `--dial HOST` | the join reply's host | As for `check` |
| `--prometheus URL` | `http://10.10.1.15:30090/` | Prometheus's http(s) origin |
| `--pod NAME` | `avalon-world-loadtest-0` | The world server's pod, for its memory limit |
| `--sign-in-concurrency N` | `8` | Sign-ins at once, 1 to 64: the ramp's and the context refresher's together |

The console prints one line per step, then the result and the report's path:

```text
step 3  bots 150  tick p99 4.2 ms  ack p95 31 ms  ws 22%  → pass
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

A run file is kept per provision in `%LOCALAPPDATA%\Avalon.LoadTest\runs\<RunId>.json` (on Linux and macOS,
`~/.local/share/Avalon.LoadTest/runs`, readable by its owner only). `<RunId>` is the first run's id, and it is the
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
- **409**: a bot of the run still holds a live game session, or a world's characters database is unavailable. Stop
  the ramp, wait about a minute (a closed session's lease runs out), and run cleanup again. A 409 right after a ramp
  usually means its stop was cut short (a second Ctrl+C) or the world had not drained: see [Stopping](#stopping).
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
credential's expiry and its authorization deadline, less a jitter of 0 to 20 seconds per bot. A refresh that fails
is retried on the next pass, under the same idempotency key. A context that can no longer be refreshed (revoked, or
its refresh token spent) is counted as a sign-in failure and replaced by a fresh sign-in. A bot whose fresh sign-in
also fails gives up for good. It no longer counts as live, and the next fill signs in another account in its place.

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
| `first-ack` | 10 s | An idle input repeated every 50 ms until one is answered: the bot is in the world |
| `leave` | 20 s | Waits for the world's logout save |

A failed entry is counted by kind (`<step>:<code>`, for example `join:ACTIVE_GAME_SESSION`, `admission:<result>`,
`spawn:timeout`, `connect:tls`) and retried up to three times, after 1, 3 and 9 seconds, each retry taking over any
session the failed attempt left. In a ramp, a bot whose last retry failed waits 30 seconds and starts again, its
failures still counting against admission, so a bot never drops out quietly; `check` stops at the last failure.

### Behaviours

One input driver sends every in-world bot's input at 60 Hz, as the game client's fixed step does. Each bot's input
numbers start at 1 on every connection. If the driver falls more than three steps behind, it drops the missed steps
rather than sending them in a burst, and its lateness shows it.

| Behaviour | Does |
|---|---|
| `idle` | A zero-direction input every step, as the real client sends while standing |
| `walker` | Walks a random heading. When an ack reports less than 0.1 m/s while it asked to move (a wall), it turns 90 to 270 degrees and goes on. There is no navmesh in the tool, so walkers gather along walls more than players do |
| `churner` | Walks like a walker. After a random 2 to 5 minutes in the world it changes character on the same connection (leave, the logout save, list, select, load, first ack). Every fourth churn it reconnects instead: it closes the connection, keeps its game context, and enters again with a new join ticket and a takeover |

A connection the world closes while a bot is in the world is counted as a disconnect, and the bot enters again with
a takeover. A churn that fails is counted, and the bot closes its connection and enters afresh.

**The mix.** Behaviours go to bots by smooth weighted round-robin over the bot index, so every count of bots holds the
`--mix` shares as closely as whole bots allow, interleaved. With the default mix, every block of ten bot indexes from
0 (0-9, 10-19, ...) holds 6 idle bots, 3 walkers and 1 churner.

## The ramp

1. The ramp reads world `W`'s version and its players online from Prometheus, before any sign-in.
2. It signs in `--start` bots (at most `--sign-in-concurrency` at once). If a sign-in fails, that account is passed
   over for the next one.
3. It enters them, 32 at a time, each with its behaviour from the mix.
4. It holds the step for `--hold`. The first 30 seconds settle and are not judged. The rest (its last 60 seconds at
   most) is the judged window. While the step holds, the next step's bots sign in.
5. 15 seconds after the hold ends, so the world's last 10-second export has landed, it reads Prometheus for the
   window that ended with the hold. Then it judges the step: the [limits](#the-limits) and the
   [decision rule](#the-decision-rule) say whether it moves to the next step, holds the same count again, or stops.
6. The next step tops the live bots up to the new count (`--step` more, up to `--max`), and the ramp goes on.

Each step therefore takes the hold plus about 15 seconds. With the defaults, a ramp to 1,000 bots is 20 steps of
about 105 seconds, roughly 35 minutes, plus any re-holds.

**Live bots** are those sent into the world that have not given up. A step's bot count, the decision and the capacity
count live bots. A live bot may be between a churn and its next entry. The report adds two cross-checks: the bots in
the world at the hold's end, and the world's own players online less the count before the ramp.

**Running out.** If a step cannot be filled (the run's accounts ran out, or no further bot could sign in), the ramp
stops as `stopped`, with the reason and the last passing count.

## The limits

A step breaches a limit when its value is on the tripping side of the threshold. Override a limit with
`--limit name=value` in the unit below. A fraction is 0 to 1, and no threshold may be negative. Examples:
`--limit tick-p99=20` (20 ms), `--limit memory=0.9` (90 %).

| Name | Default | Unit | Trips when | Source |
|---|---|---|---|---|
| `tick-p99` | 16.7 | ms | above | Prometheus: `histogram_quantile(0.99, sum by (le)(rate(world_tick_duration_microseconds_bucket[w]))) / 1000` |
| `tps` | 58 | ticks/s | below | Prometheus: `avg_over_time(world_tick_rate_tps[w])` |
| `ack-p95` | 150 | ms | above | Bots: the 95th percentile of input-to-ack latency over the judged window. An input still unanswered when its slot is reused, about a second later, counts at its age then |
| `drops` | 0 | count | above | Prometheus: `sum(increase(network_out_dropped_total[w])) or vector(0)`, the packets a full outbox evicted |
| `admission` | 0.01 | fraction | above | Bots: entry failures ÷ entry attempts over the whole step, settle included. Entries are first entries, re-entries, reconnects and character changes; sign-in and leave failures are not part of it |
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
   sign-out runs however the leave ended.
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

Each ramp writes two files with the same data to `%LOCALAPPDATA%\Avalon.LoadTest\reports\` (on Linux and macOS,
`~/.local/share/Avalon.LoadTest/reports`): `<yyyyMMdd-HHmmss>-<RunId>.md` to read and `.json` with the same data
for tools, named from the ramp's start time in local time. A name that already exists gets `-2`, `-3`, and so on;
no report is ever overwritten, except the ramp's own early save. Reports are never written to the repository and hold
no secret (no password, ticket or credential).

- **Header**: the date and time, the world's server version (Prometheus `target_info`'s `service_version`), the run
  and its size, the API, the mix, the ramp settings (start, step, hold and judged window, max, sign-in concurrency),
  Prometheus, the pod, `--dial`, the bot PC's CPU model and logical cores, and the limits, each marked when
  overridden.
- **Result**: one of `capacity N bots`, `no limit reached up to N bots`, `bot PC saturated: capacity ≥ N bots`,
  `stopped: steps that could not be judged`, or `stopped (<reason>)`. A stop reason is one of: Ctrl+C, an error, the
  run's accounts running out, or no further bot able to sign in. Each `stopped` result adds the last passing step's
  count, or says that no step passed. Then "failed first": each confirmed breach with its value and threshold
  (`tick-p99 18.2 ms > 16.7 ms`).
- **Steps**, one row per held step (a re-hold is a row of its own):
  - live bots, by behaviour; bots in the world at the hold's end; players online less the count before the ramp;
  - map instances; tick p99; average TPS; ack p50, p95 and p99; drops; the deepest receive backlog of any
    connection; working set (MB and % of the limit); gen2 per minute; GC pause; save p95;
  - entries and entry failures, and failures by kind; leave failures by kind (apart from admission); disconnects;
  - the bot PC's CPU and the driver's lateness p95;
  - the verdict: `pass`, `pass (blip)`, `re-hold (<breaches, or unknown: names>)`, `stop (...)`,
    `stop, unknown (...)`, or `pass, the last step`. A value Prometheus did not give reads `n/a`.
- **Notes**: the blips; the steps whose drops may be the bot PC's; sign-ins, with their throughput (one every X s
  with N at once, and per minute; identity's side, apart from the world) and the sign-in and refresh failures;
  whether the world drained after the stop; failed sign-outs; the stop's leave failures by kind; and the leaves and
  sign-outs the breakers skipped.

**Reading it.** The capacity is the last step that passed. "Failed first" says which limit gave out, and the steps
before it show how that value climbed. A `bot PC saturated` result is a lower bound: run again with a lighter mix
(fewer walkers and churners) or a stronger bot PC. A ramp that stopped unknown usually means a missing metric: check
[the export interval](#before-a-run) and Prometheus. If the bot count and the two cross-checks disagree, bots were
not where the tool thought they were. Look at the failure kinds and the disconnects.

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
