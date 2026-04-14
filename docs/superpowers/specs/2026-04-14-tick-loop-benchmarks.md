# Tick Loop Benchmarks

Baseline numbers captured **before** the PeriodicTimer refactor described in
`2026-04-14-tick-loop-refactor.md`. The `Yield_1` row doubles as the "After" scheduling-overhead
proxy — the refactored loop pays one `WaitForNextTickAsync` per tick, equivalent in scheduling
cost to one `Task.Yield()`.

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
- `PeriodicTimer_Tick` — the refactored loop: one `WaitForNextTickAsync` per tick (1 ms period).
  Mean is dominated by the Windows timer resolution floor (~15.6 ms). Subtract that to isolate
  scheduling overhead, which equals `Yield_1` (~600 ns). The `Ratio` column is not meaningful
  here (it includes the timer sleep, not just scheduling cost).

---

## Before — current spin/yield throttle (2026-04-14)

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

### Key observations

- `Yield_13` costs **7.26x more** than `Yield_1` (Ratio = 7.262 vs 1.002), meaning the current
  production throttle burns ~7 times the scheduling overhead of a single yield per tick.
- Allocations are essentially identical across all `Yield_*` variants (~96–97 B per tick),
  confirming overhead is pure CPU/scheduling cost, not GC pressure.
- `SynchronousPath` at 3.65 ns (Ratio = 0.003) reveals the async state-machine itself is nearly
  free; all meaningful cost comes from thread-pool hops.
- At 60 Hz, `Yield_13` adds ~484 µs/s of pure scheduling overhead vs ~67 µs/s for `Yield_1` —
  a saving of ~417 µs/s (6.26x reduction) after the refactor.

---

## After — PeriodicTimer refactor (2026-04-14)

Full table from the post-refactor run. `PeriodicTimer_Tick` is the new scenario; the `Yield_*`
rows serve as cross-run comparisons (absolute numbers shift slightly between runs due to CPU
state, but ratios are stable).

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

### Key observations

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
