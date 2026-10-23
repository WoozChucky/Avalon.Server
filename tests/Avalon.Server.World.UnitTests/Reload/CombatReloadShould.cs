using Avalon.Common;
using Avalon.Database.World.Repositories;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Reload;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Reload;

/// <summary>
/// The combat area (#506): the formula row and the class stat factors, reloaded forward-only, and a bad
/// row refused with the previous generation kept.
/// </summary>
public class CombatReloadShould
{
    public sealed class Rows
    {
        public List<CombatFormula> Formulas = [CombatSeed.Formula()];
        public List<ClassStatFactors> Factors = [.. CombatSeed.ClassFactors()];

        public ICombatDataRepository Repository()
        {
            ICombatDataRepository repository = Substitute.For<ICombatDataRepository>();
            repository.GetFormulasAsync(Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IReadOnlyCollection<CombatFormula>>(Formulas.ToList()));
            repository.GetClassStatFactorsAsync(Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IReadOnlyCollection<ClassStatFactors>>(Factors.ToList()));
            return repository;
        }
    }

    private static readonly ClassLevelStat s_warriorLevel1 = new()
    {
        Class = CharacterClass.Warrior,
        Level = 1,
        BaseHp = 130,
        BaseMana = 0,
        Stamina = 22,
        Strength = 23,
        Agility = 20,
        Intellect = 20,
    };

    private static Task<StaticData> Load(Rows rows) =>
        TestStaticData.LoadAsync(TestStaticData.Repositories(classStats: () => [s_warriorLevel1], combat: rows.Repository()));

    [Fact]
    public async Task Load_the_seeded_formula_and_every_class()
    {
        StaticData data = await Load(new Rows());

        Assert.Equal(50f, data.Combat.Formula.ArmorBase);
        Assert.Equal(CharacterClasses.Playable.Order(), data.Combat.Factors.Keys.Order());
    }

    [Fact]
    public async Task Change_a_characters_stats_at_its_next_refresh_and_not_before()
    {
        var rows = new Rows();
        StaticData data = await Load(rows);
        CharacterEntity warrior = New();
        Assert.True(CharacterStatsRefresh.Apply(warrior, data, CurrentValues.Refill));
        Assert.Equal(130u + 22u * 5u, warrior.Health);

        rows.Formulas[0].LifePerStamina = 10;
        Task applied = data.ApplyOnNextTickAsync(await data.PrepareAsync(ReloadArea.Combat));

        Assert.Equal(240u, warrior.Health);
        data.ApplyPending();
        await applied;
        Assert.Equal(240u, warrior.Health);

        Assert.True(CharacterStatsRefresh.Apply(warrior, data, CurrentValues.Refill));
        Assert.Equal(130u + 22u * 10u, warrior.Health);
    }

    [Fact]
    public async Task Show_a_changed_formula_only_after_the_next_tick_applies_it()
    {
        var rows = new Rows();
        StaticData data = await Load(rows);
        CombatPatch before = data.Combat;

        rows.Formulas[0].CritMultiplier = 2f;
        Task applied = data.ApplyOnNextTickAsync(await data.PrepareAsync(ReloadArea.Combat));

        Assert.Same(before, data.Combat);
        data.ApplyPending();
        await applied;
        Assert.Equal(2f, data.Combat.Formula.CritMultiplier);
    }

    public static TheoryData<string, Action<Rows>> BadRows => new()
    {
        { "ClassStatFactors Warrior", r => r.Factors.Single(f => f.Class == CharacterClass.Warrior).PowerPerIntellect = -1 },
        { "ClassStatFactors Hunter", r => r.Factors.Single(f => f.Class == CharacterClass.Hunter).PowerPerAgility = float.NaN },
        { "ClassStatFactors Cleric", r => r.Factors.RemoveAll(f => f.Class == CharacterClass.Cleric) },
        { "ClassStatFactors Warrior", r => r.Factors.Single(f => f.Class == CharacterClass.Warrior).FixedPower = -5 },
        { "ClassStatFactors Hunter", r => r.Factors.Single(f => f.Class == CharacterClass.Hunter).FixedPower = 5_000_000_000 },
        { "CombatFormula 1", r => r.Formulas[0].ArmorCap = 2f },
        { "CombatFormula 1", r => r.Formulas[0].EvasionCap = 101f },
        { "CombatFormula 1", r => r.Formulas[0].CritMultiplier = float.PositiveInfinity },
        { "CombatFormula 1", r => { r.Formulas[0].ArmorBase = 0f; r.Formulas[0].ArmorPerLevel = 0f; } },
        { "CombatFormula 1", r => r.Formulas[0].ArmorPerLevel = -10f },
        { "CombatFormula", r => r.Formulas.Clear() },
        // #627: a floor at -100 % would stop a character dead, and one above the cap has no range.
        { "CombatFormula 1", r => r.Formulas[0].MoveSpeedFloor = -100f },
        { "CombatFormula 1", r => r.Formulas[0].MoveSpeedFloor = float.NegativeInfinity },
        { "CombatFormula 1", r => { r.Formulas[0].MoveSpeedFloor = 20f; r.Formulas[0].MoveSpeedCap = 10f; } },
        { "CombatFormula 1", r => r.Formulas[0].MoveSpeedCap = float.NaN },
        { "CombatFormula 1", r => r.Formulas[0].MoveSpeedCap = float.PositiveInfinity },
        // #1178: a speed cap is finite and 0 or more.
        { "CombatFormula 1", r => r.Formulas[0].AttackSpeedCap = -1f },
        { "CombatFormula 1", r => r.Formulas[0].CooldownRecoveryCap = float.NaN },
    };

    [Theory]
    [MemberData(nameof(BadRows))]
    public async Task Refuse_a_bad_row_by_name_and_keep_the_previous_values(string named, Action<Rows> spoil)
    {
        var rows = new Rows();
        StaticData data = await Load(rows);
        CombatPatch before = data.Combat;

        spoil(rows);

        InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => data.PrepareAsync(ReloadArea.Combat));
        Assert.StartsWith(named, refused.Message, StringComparison.Ordinal);
        Assert.Same(before, data.Combat);
    }

    /// <summary>
    /// #506 review: the database refuses a negative or oversized FixedPower itself, so the
    /// load's refusal is a second line, never EF's cast.
    /// </summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(4_294_967_296L)]
    public void Be_refused_by_the_database_out_of_the_uint_range(long fixedPower)
    {
        using var database = Handlers.SqliteDatabase.World();
        using Avalon.Database.World.WorldDbContext context = database.CreateDbContext();
        ClassStatFactors warrior = context.ClassStatFactors.Single(f => f.Class == CharacterClass.Warrior);
        warrior.FixedPower = fixedPower;

        DbUpdateException refused = Assert.Throws<Microsoft.EntityFrameworkCore.DbUpdateException>(() => context.SaveChanges());
        Assert.Contains("CHECK constraint failed", refused.InnerException!.Message, StringComparison.Ordinal);
    }

    /// <summary>#627, #1178: the database refuses a negative attack speed cap, a floor at -100 % or below, and a floor above the cap.</summary>
    [Theory]
    [InlineData(-1f, 50f, -50f)]
    [InlineData(50f, 50f, -100f)]
    [InlineData(50f, 10f, 20f)]
    public void Be_refused_by_the_database_outside_the_speed_bounds(float attackSpeedCap, float moveCap, float moveFloor)
    {
        using var database = Handlers.SqliteDatabase.World();
        using Avalon.Database.World.WorldDbContext context = database.CreateDbContext();
        CombatFormula formula = context.CombatFormulas.Single();
        formula.AttackSpeedCap = attackSpeedCap;
        formula.MoveSpeedCap = moveCap;
        formula.MoveSpeedFloor = moveFloor;

        DbUpdateException refused = Assert.Throws<Microsoft.EntityFrameworkCore.DbUpdateException>(() => context.SaveChanges());
        Assert.Contains("CHECK constraint failed", refused.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fail_startup_on_a_bad_row()
    {
        var rows = new Rows();
        rows.Formulas[0].ArmorCap = 2f;

        await Assert.ThrowsAsync<InvalidDataException>(() => Load(rows));
    }
}
