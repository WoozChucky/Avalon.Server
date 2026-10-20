using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>The speed read by player input and the character sheet, once per character.</summary>
[MemoryDiagnoser]
public class CharacterMovementSpeedBenchmarks
{
    private const int Characters = 1024;
    private ICharacter[] _characters = null!;

    [GlobalSetup]
    public void Setup()
    {
        _characters = new ICharacter[Characters];
        for (int i = 0; i < Characters; i++)
            _characters[i] = new CharacterEntity();
    }

    [Benchmark(OperationsPerInvoke = Characters)]
    public float ReadSpeed()
    {
        float total = 0;
        foreach (ICharacter character in _characters)
            total += character.GetMovementSpeed();
        return total;
    }
}
