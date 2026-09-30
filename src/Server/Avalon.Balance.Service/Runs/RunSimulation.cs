using Avalon.Balance.Core;

namespace Avalon.Balance.Service.Runs;

/// <summary>The simulation call: <see cref="Simulation.Run" /> in the service, a fake in tests.</summary>
public delegate RunResult RunSimulation(SeedTables seed, BalanceConfig defaults, RunRequest request,
    IProgress<RunProgress>? progress, CancellationToken ct);
