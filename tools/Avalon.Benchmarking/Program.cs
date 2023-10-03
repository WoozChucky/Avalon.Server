using Avalon.Benchmarking.Benchmarks;
using BenchmarkDotNet.Running;

namespace Avalon.Benchmarking;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<SerializationBenchmarks>();
    }
}
