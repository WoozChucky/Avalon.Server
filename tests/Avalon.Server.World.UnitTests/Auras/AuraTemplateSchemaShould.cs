using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World.Public.Abilities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>The aura tables over SQLite: the rows round-trip whole, and the database refuses what the world would.</summary>
public class AuraTemplateSchemaShould
{
    private static AuraTemplate Fortified(uint id = 900) => new()
    {
        Id = new AuraId(id), Name = "Fortified", Icon = "fortified", Kind = AuraKind.Helpful, DurationMs = 30000,
        PeriodicKind = AuraPeriodicKind.None, ScalingStat = ScalingStat.Attack, Stacking = AuraStacking.Refresh, MaxStacks = 1,
        Modifiers = [new AuraStatModifier { AuraId = new AuraId(id), Stat = AuraStat.Armor, Kind = AuraModifierKind.Percent, Value = 20f }],
    };

    [Fact]
    public async Task Read_an_aura_back_whole_with_its_modifiers()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using (WorldDbContext context = database.CreateDbContext())
        {
            context.AuraTemplates.Add(Fortified());
            await context.SaveChangesAsync();
        }

        var repository = new AuraTemplateRepository(database);
        AuraTemplate read = (await repository.FindByIdAsync(new AuraId(900)))!;

        Assert.Equal(("Fortified", AuraKind.Helpful, 30000u), (read.Name, read.Kind, read.DurationMs));
        AuraStatModifier armour = Assert.Single(read.Modifiers);
        Assert.Equal((AuraStat.Armor, AuraModifierKind.Percent, 20f), (armour.Stat, armour.Kind, armour.Value));
        Assert.Contains(await repository.GetAllAsync(), a => a.Id.Value == 900 && a.Modifiers.Count == 1);
        Assert.Contains((await repository.PaginateAsync(1, 50)).Items, a => a.Id.Value == 900);
    }

    public static TheoryData<string, string, Action<AuraTemplate>> Refused() => new()
    {
        { "no duration", "CK_AuraTemplates_DurationMs", a => a.DurationMs = 0 },
        { "a tick longer than the duration", "CK_AuraTemplates_TickIntervalMs", a => a.TickIntervalMs = 40000 },
        { "a periodic aura with no tick", "CK_AuraTemplates_TickIntervalMs", a => { a.Kind = AuraKind.Helpful; a.PeriodicKind = AuraPeriodicKind.Heal; } },
        { "a harmful heal", "CK_AuraTemplates_PeriodicFitsKind", a => { a.Kind = AuraKind.Harmful; a.PeriodicKind = AuraPeriodicKind.Heal; a.TickIntervalMs = 3000; } },
        { "no kind", "CK_AuraTemplates_Kind", a => a.Kind = 0 },
        { "an unknown scaling stat", "CK_AuraTemplates_ScalingStat", a => a.ScalingStat = (ScalingStat)2 },
        { "no stacks", "CK_AuraTemplates_MaxStacks", a => a.MaxStacks = 0 },
        { "a negative base", "CK_AuraTemplates_PeriodicBase", a => a.PeriodicBase = -1f },
        { "a negative base damage coefficient", "CK_AuraTemplates_BaseDamageCoefficient", a => a.BaseDamageCoefficient = -0.5f },
        { "a percent that takes everything", "CK_AuraStatModifiers_Value", a => a.Modifiers[0].Value = -100f },
        { "no stat", "CK_AuraStatModifiers_Stat", a => a.Modifiers[0].Stat = 0 },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Refuse_a_row_the_world_could_not_run(string _, string constraint, Action<AuraTemplate> breakIt)
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using WorldDbContext context = database.CreateDbContext();
        AuraTemplate aura = Fortified();
        breakIt(aura);
        context.AuraTemplates.Add(aura);

        DbUpdateException refused = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains($"CHECK constraint failed: {constraint}", refused.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuse_an_ability_naming_an_aura_that_does_not_exist()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using WorldDbContext context = database.CreateDbContext();
        AbilityTemplate cleave = context.AbilityTemplates.AsEnumerable().Single(a => a.Id.Value == 200);
        cleave.AuraId = new AuraId(999);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Delete_an_auras_modifiers_with_it()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using (WorldDbContext context = database.CreateDbContext())
        {
            context.AuraTemplates.Add(Fortified());
            await context.SaveChangesAsync();
        }

        await using (WorldDbContext context = database.CreateDbContext())
        {
            await context.AuraTemplates.Where(a => a.Id == new AuraId(900)).ExecuteDeleteAsync();
        }

        await using WorldDbContext after = database.CreateDbContext();
        Assert.DoesNotContain(after.AuraStatModifiers.AsEnumerable(), m => m.AuraId.Value == 900);
    }
}
