using BenchmarkDotNet.Running;

namespace Avalon.Benchmarking;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "crowd-budget")
        {
            CrowdBudget.CrowdBudgetHarness.Run(args[1..]);
            return;
        }

        if (args.Length > 0 && args[0] == "outbox-flush")
        {
            OutboxFlush.OutboxFlushHarness.Run(args[1..]);
            return;
        }

        if (args.Length > 0 && args[0] == "navmesh-bake")
        {
            NavmeshBake.NavmeshBakeHarness.Run(args[1..]);
            return;
        }

        if (args.Length > 0 && args[0] == "candidate-ratio")
        {
            CandidateRatio.CandidateRatioHarness.Run(args[1..]);
            return;
        }

        if (args.Length > 0 && args[0] == "hit-candidates")
        {
            HitCandidates.HitCandidatesHarness.Run(args[1..]);
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
