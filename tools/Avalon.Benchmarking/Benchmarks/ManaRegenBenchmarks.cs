using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Seeding;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.State;
using Avalon.World.Characters;
using Avalon.World.Entities;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// What mana regeneration (#1181) costs the tick: one <see cref="CharacterEntity.Update" /> of 1/60 s on each of 1,000
/// level 1 Wizards with an empty pool of 400, in combat and out of it. Each operation empties every pool first, so every
/// character takes the regeneration branch on every tick. The clock never moves: a character marked in combat stays in
/// combat, and one never marked stays out of it. 0 B expected on both rows.
/// </summary>
/// <remarks>
/// Uses only members that exist before and after #1181 part A, so the same file measures both. Setup fails the run
/// unless one <see cref="CharacterEntity.Update" /> of 1 s from an empty pool gives the first character some mana back.
/// </remarks>
[MemoryDiagnoser]
public class ManaRegenBenchmarks
{
    private const int Characters = 1000;
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    private readonly TimeProvider _clock = new FrozenClock(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
    private CharacterEntity[] _characters = null!;

    [Params(true, false)]
    public bool InCombat { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _characters = new CharacterEntity[Characters];
        for (int i = 0; i < Characters; i++)
        {
            var character = new CharacterEntity(NullLoggerFactory.Instance, new Character
            {
                Id = new CharacterId((uint)i + 1),
                Class = CharacterClass.Wizard,
                Level = 1,
                Health = 100,
                Power1 = 400,
            }, new RegenConfiguration(), _clock);
            character.PowerType = PowerType.Mana;
            character.Spells.Load([]);
            character.ApplyStats(
                new DerivedCharacterStats(MaxHealth: 100, MaxPower: 400, Stamina: 0, Strength: 0, Agility: 0,
                    Intellect: 50, Armor: 0, BlockPct: 0f, Evasion: 0, CritPct: 0f),
                CurrentValues.EnterWorld, CombatSeed.Formula());
            if (InCombat)
                character.MarkCombat();
            _characters[i] = character;
        }

        CharacterEntity first = _characters[0];
        first.CurrentPower = 0;
        first.Update(TimeSpan.FromSeconds(1));
        if ((first.CurrentPower ?? 0) == 0)
            throw new InvalidOperationException("One second of regeneration from an empty pool gave no mana back.");
    }

    [Benchmark(OperationsPerInvoke = Characters)]
    public void Regenerate()
    {
        foreach (CharacterEntity character in _characters)
        {
            character.CurrentPower = 0;
            character.Update(s_tick);
        }
    }

    /// <summary>A clock that never moves.</summary>
    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
