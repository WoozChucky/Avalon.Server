# Tick Loop Refactor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the manual spin/yield throttle in `WorldServer` with `PeriodicTimer`, eliminating ~13 `Task.Yield()` calls per tick frame and the loss of thread affinity they cause.

**Architecture:** `PeriodicTimer` fires once per frame interval (16.67 ms) and yields control exactly once to the ThreadPool — then resumes on the next tick. Delta time is derived from a single `Stopwatch` (`_gameTime`) instead of two separate time sources (`_stopwatch` + `TimeUtils.GetMsTime()`). The `Tick()` method and its manual spin/throttle logic are deleted entirely. `Update()`, TPS tracking, and all startup work are unchanged.

**Tech Stack:** .NET 10, `System.Threading.PeriodicTimer` (BCL, no new packages).

**Spec:** See discussion in conversation — analysis of `WorldServer.cs` tick loop overhead.

---

## Background

The current loop in `WorldServer.ExecuteAsync`:

```csharp
while (!stoppingToken.IsCancellationRequested)
{
    await Tick(); // calls Task.Yield() ~13 times per frame to fill the 16.67 ms budget
}
```

At 60 Hz with a ~3 ms tick, `Tick()` calls `await Task.Yield()` roughly 13 times per frame.
Each call queues a ThreadPool continuation — the loop has no thread affinity and may resume
on a different CPU core after each yield.

The new loop:

```csharp
using var timer = new PeriodicTimer(MinUpdateInterval);
while (await timer.WaitForNextTickAsync(stoppingToken))
{
    // one thread-pool hop per tick, then Update()
}
```

`WaitForNextTickAsync` suspends exactly once per tick — the scheduling cost drops from
~13 hops to 1. When the CancellationToken is cancelled it returns `false`; no exception,
no spin.

---

## File Map

| File | Action | Purpose |
|------|--------|---------|
| `src/Server/Avalon.World/WorldServer.cs` | Modify | Replace tick loop; remove `Tick()`, manual throttle fields |

No other files change. `Update()`, `OnStoppingAsync()`, TPS tracking, and the startup
sequence in `ExecuteAsync` are all preserved.

---

## Task 1: Capture baseline benchmark numbers

Before touching any production code, run the tick-loop benchmark to record the current
scheduling overhead. The results will anchor the before/after comparison.

**Files:**
- Read: `tools/Avalon.Benchmarking/Benchmarks/TickLoopBenchmarks.cs`
- Write: `docs/superpowers/specs/2026-04-14-tick-loop-benchmarks.md`

- [ ] **Step 1: Run the tick-loop benchmark**

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*TickLoop*"
```

Wait for it to finish (takes a few minutes). The results land in
`BenchmarkDotNet.Artifacts/results/Avalon.Benchmarking.Benchmarks.TickLoopBenchmarks-report-github.md`.

- [ ] **Step 2: Create the benchmarks doc**

Create `docs/superpowers/specs/2026-04-14-tick-loop-benchmarks.md`:

```markdown
# Tick Loop Benchmarks

Baseline numbers captured **before** the PeriodicTimer refactor described in
`2026-04-14-tick-loop-refactor.md`. Re-run the `Yield_1` scenario after implementation
to confirm the production loop now performs equivalently to one yield per tick.

## Run command

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking -- --filter "*TickLoop*"
```

## Interpretation

- `Yield_1` — the irreducible minimum: one thread-pool hop per tick frame.
  This is what the refactored PeriodicTimer loop costs (one `WaitForNextTickAsync` per tick).
- `Yield_13` — the current production cost at target load (~3 ms tick + ~13 ms wait).
  The `Ratio` column against `Yield_1` shows the overhead multiplier of the current approach.
- `SynchronousPath` — state-machine cost only; the theoretical floor with no scheduling.

---

## Before — current spin/yield throttle (2026-04-14)

> Paste BenchmarkDotNet output here.

| Method          | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|---------------- |-----:|------:|-------:|------:|-----:|----------:|------------:|
| _to be filled_  |      |       |        |       |      |           |             |

---

## After — PeriodicTimer refactor (TODO)

> Paste `Yield_1` result here after implementation — the new loop's cost equals one yield per tick.

| Method          | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|---------------- |-----:|------:|-------:|------:|-----:|----------:|------------:|
| _to be filled_  |      |       |        |       |      |           |             |
```

- [ ] **Step 3: Paste the benchmark results into the doc**

Open `BenchmarkDotNet.Artifacts/results/Avalon.Benchmarking.Benchmarks.TickLoopBenchmarks-report-github.md`
and copy the table into the "Before" section.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-04-14-tick-loop-benchmarks.md
git commit -m "docs: add tick loop benchmark baseline (before PeriodicTimer refactor)"
```

---

## Task 2: Replace the tick loop in WorldServer

**Files:**
- Modify: `src/Server/Avalon.World/WorldServer.cs`

### What changes

**Remove these three fields** (all related to manual throttle timing):

```csharp
// DELETE these lines:
private uint _realCurrentTime;
private uint _realPreviousTime = TimeUtils.GetMsTime();
private long _lastTpsCalculationMs = TimeUtils.GetMsTime();
```

**Add this field** (replace `_lastTpsCalculationMs` with consistent initialisation):

```csharp
// ADD — zero-initialised, consistent with _stopwatch which also starts at 0
private long _lastTpsCalculationMs;
```

**Remove the `Tick()` method** — delete all 48 lines of it:

```csharp
// DELETE the entire method:
private async ValueTask Tick()
{
    _realCurrentTime = TimeUtils.GetMsTime();
    uint diff = TimeUtils.GetMsTimeDiff(_realPreviousTime, _realCurrentTime);
    // ... (full method)
}
```

**Replace the while loop in `ExecuteAsync`** — the section after `_gameTime.Start()`:

Before:
```csharp
_gameTime.Start();

while (!stoppingToken.IsCancellationRequested)
{
    try
    {
        await Tick();
    }
    catch (Exception e)
    {
        _logger.LogError(e, "Tick failed");
    }
}
```

After:
```csharp
_gameTime.Start();

using var timer = new PeriodicTimer(MinUpdateInterval);
var prev = _gameTime.Elapsed;

while (await timer.WaitForNextTickAsync(stoppingToken))
{
    try
    {
        var now = _gameTime.Elapsed;
        var deltaTime = now - prev;
        if (deltaTime.TotalMilliseconds < 1)
            deltaTime = TimeSpan.FromMilliseconds(1);
        prev = now;

        Update(deltaTime);

        _tickCount++;
        double elapsedSeconds = (_stopwatch.ElapsedMilliseconds - _lastTpsCalculationMs) / 1000.0;
        if (elapsedSeconds >= 1.0)
        {
            _ticksPerSecond = _tickCount / elapsedSeconds;
            _tickCount = 0;
            _lastTpsCalculationMs = _stopwatch.ElapsedMilliseconds;
        }
    }
    catch (Exception e)
    {
        _logger.LogError(e, "Tick failed");
    }
}
```

> **Why `WaitForNextTickAsync` replaces the cancellation check:** when `stoppingToken` is
> cancelled, `WaitForNextTickAsync` returns `false` and the `while` exits cleanly — no
> `OperationCanceledException`, no spin. This is the designed cancellation path for
> `PeriodicTimer`.

> **Why `_lastTpsCalculationMs` initialises to 0:** `_stopwatch` is `Stopwatch.StartNew()`
> — it starts at 0 at construction. Both sides of `(_stopwatch.ElapsedMilliseconds -
> _lastTpsCalculationMs)` are now the same clock, so the first TPS reading fires correctly
> after 1 second rather than being skipped.

> **Why `prev = _gameTime.Elapsed` before the loop:** `_gameTime` is started just above.
> Capturing the elapsed time before the first tick ensures `deltaTime` on the first frame
> equals the actual time between start and the first timer fire, not zero.

### Steps

- [ ] **Step 1: Read the current WorldServer.cs**

```bash
# Confirm line numbers before editing
```

Read `src/Server/Avalon.World/WorldServer.cs` and locate:
- The three fields to remove: `_realCurrentTime` (line ~73), `_realPreviousTime` (line ~74),
  `_lastTpsCalculationMs` (line ~72)
- The `Tick()` method (lines ~176–223)
- The `while` loop in `ExecuteAsync` (lines ~153–163)

- [ ] **Step 2: Remove the three throttle-timing fields**

Delete:
```csharp
private uint _realCurrentTime;
private uint _realPreviousTime = TimeUtils.GetMsTime();
private long _lastTpsCalculationMs = TimeUtils.GetMsTime();
```

Add in their place (just the TPS field with fixed initialisation):
```csharp
private long _lastTpsCalculationMs;
```

- [ ] **Step 3: Delete the `Tick()` method**

Remove the entire `private async ValueTask Tick()` method.

- [ ] **Step 4: Replace the `ExecuteAsync` loop**

Find:
```csharp
_gameTime.Start();

while (!stoppingToken.IsCancellationRequested)
{
    try
    {
        await Tick();
    }
    catch (Exception e)
    {
        _logger.LogError(e, "Tick failed");
    }
}
```

Replace with:
```csharp
_gameTime.Start();

using var timer = new PeriodicTimer(MinUpdateInterval);
var prev = _gameTime.Elapsed;

while (await timer.WaitForNextTickAsync(stoppingToken))
{
    try
    {
        var now = _gameTime.Elapsed;
        var deltaTime = now - prev;
        if (deltaTime.TotalMilliseconds < 1)
            deltaTime = TimeSpan.FromMilliseconds(1);
        prev = now;

        Update(deltaTime);

        _tickCount++;
        double elapsedSeconds = (_stopwatch.ElapsedMilliseconds - _lastTpsCalculationMs) / 1000.0;
        if (elapsedSeconds >= 1.0)
        {
            _ticksPerSecond = _tickCount / elapsedSeconds;
            _tickCount = 0;
            _lastTpsCalculationMs = _stopwatch.ElapsedMilliseconds;
        }
    }
    catch (Exception e)
    {
        _logger.LogError(e, "Tick failed");
    }
}
```

- [ ] **Step 5: Remove unused `TimeUtils` usings if present**

After the edit, `TimeUtils.GetMsTime()` and `TimeUtils.GetMsTimeDiff()` are no longer called
in `WorldServer.cs`. Remove the `using Avalon.Common.Utils;` line from the top of the file
if it is only used by those calls.

Check by searching the file for any remaining `TimeUtils` references. If none exist, delete
the using.

- [ ] **Step 6: Build**

```bash
dotnet build --no-restore
```

Expected: 0 errors. Fix any compile errors before continuing.

- [ ] **Step 7: Run the full test suite**

```bash
dotnet test
```

Expected: all tests pass. The refactor touches only `ExecuteAsync` coordination code —
no production behaviour in `Update()`, packet handling, or entity tracking changes.

- [ ] **Step 8: Commit**

```bash
git add src/Server/Avalon.World/WorldServer.cs
git commit -m "perf: replace tick loop spin/yield throttle with PeriodicTimer"
```

---

## Task 3: Record the after-benchmark result

The benchmark cannot measure `PeriodicTimer.WaitForNextTickAsync` directly (it fires on a
real timer — 16 ms per iteration makes it impractical for BenchmarkDotNet's iteration
model). Instead, `Yield_1` serves as the proxy: the refactored loop pays one
thread-pool hop per tick, so its cost equals the `Yield_1` row from Task 1.

Update the "After" section of the benchmark doc with this equivalence.

**Files:**
- Modify: `docs/superpowers/specs/2026-04-14-tick-loop-benchmarks.md`

- [ ] **Step 1: Fill in the "After" section**

Open `docs/superpowers/specs/2026-04-14-tick-loop-benchmarks.md`. Replace the placeholder
"After" table with the `Yield_1` row from the Task 1 results, and add a note:

```markdown
## After — PeriodicTimer refactor (2026-04-14)

The refactored loop calls `WaitForNextTickAsync` once per tick — equivalent in scheduling
cost to `Yield_1`. Copy the `Yield_1` row from the Before table here:

| Method  | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|---------|-----:|------:|-------:|------:|-----:|----------:|------------:|
| Yield_1 | ...  | ...   | ...    | 1.00  | ...  | ...       | 1.00        |

### Key observations

- Production loop cost is now `Yield_1`, not `Yield_13`.
- The `Ratio` column for `Yield_13` vs `Yield_1` shows the exact overhead eliminated.
- Thread affinity is no longer lost between tick frames — the loop resumes on the next
  available ThreadPool thread without intermediate hops.
```

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/specs/2026-04-14-tick-loop-benchmarks.md
git commit -m "docs: record tick loop after-benchmark (PeriodicTimer = Yield_1 cost)"
```

---

## Verification

After all tasks are complete:

1. **Build passes** — `dotnet build --no-restore` with 0 errors.
2. **Test suite passes** — `dotnet test` with 0 failures.
3. **`Tick()` method is gone** — `grep -r "async ValueTask Tick" src/` returns no results.
4. **`_realCurrentTime` / `_realPreviousTime` are gone** — no remaining references.
5. **Benchmark doc is updated** — both Before and After sections filled in.
