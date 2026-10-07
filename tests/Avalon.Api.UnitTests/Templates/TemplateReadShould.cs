using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Api.Templates;
using Avalon.Api.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Templates;

/// <summary>A template read says which version it is (body and ETag) and whether its world can be edited.</summary>
public class TemplateReadShould
{
    private static readonly TemplateEditingOptions Options = new()
    {
        EditableWorlds = [1, 2],
        ReloadTimeout = TimeSpan.FromSeconds(10),
    };

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public async Task Item_get_returns_version_etag_and_editable(ushort world, bool editable)
    {
        IItemTemplateRepository repository = Substitute.For<IItemTemplateRepository>();
        var row = new ItemTemplate { Id = new ItemTemplateId(1), Name = "Sword" };
        repository.FindByIdAsync(Arg.Any<ItemTemplateId>(), false, Arg.Any<CancellationToken>()).Returns(row);
        var sut = new ItemTemplateController(repository, World(world), Microsoft.Extensions.Options.Options.Create(Options))
        { ControllerContext = Context() };

        ItemTemplateDto dto = Dto<ItemTemplateDto>(await sut.Get(1, CancellationToken.None));

        Assert.Equal(TemplateVersion.Of(row), dto.Version);
        Assert.Equal($"\"{dto.Version}\"", sut.Response.Headers.ETag.ToString());
        Assert.Equal(editable, dto.Editable);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, false)]
    public async Task Ability_get_returns_version_etag_and_editable(ushort world, bool editable)
    {
        IAbilityTemplateRepository repository = Substitute.For<IAbilityTemplateRepository>();
        var row = new AbilityTemplate { Id = new AbilityId(1), Name = "Cleave", ScriptName = "s", AuraId = new AuraId(1) };
        repository.FindByIdAsync(Arg.Any<AbilityId>(), false, Arg.Any<CancellationToken>()).Returns(row);
        var sut = new AbilityTemplateController(repository, World(world), Microsoft.Extensions.Options.Options.Create(Options))
        { ControllerContext = Context() };

        AbilityTemplateDto dto = Dto<AbilityTemplateDto>(await sut.Get(1, CancellationToken.None));

        Assert.Equal(TemplateVersion.Of(row), dto.Version);
        Assert.Equal($"\"{dto.Version}\"", sut.Response.Headers.ETag.ToString());
        Assert.Equal(editable, dto.Editable);
        Assert.Equal(1u, dto.AuraId);
    }

    [Fact]
    public async Task Aura_get_returns_the_row_with_its_modifiers()
    {
        IAuraTemplateRepository repository = Substitute.For<IAuraTemplateRepository>();
        var row = new Avalon.Domain.World.AuraTemplate
        {
            Id = new AuraId(5),
            Name = "Fortified",
            Icon = "fortified",
            Kind = Avalon.Domain.World.AuraKind.Helpful,
            DurationMs = 30000,
            Stacking = Avalon.Domain.World.AuraStacking.Refresh,
            MaxStacks = 1,
            Modifiers = [new Avalon.Domain.World.AuraStatModifier { AuraId = new AuraId(5), Stat = Avalon.Domain.World.AuraStat.Armor, Kind = Avalon.Domain.World.AuraModifierKind.Percent, Value = 20f }],
        };
        repository.FindByIdAsync(Arg.Any<AuraId>(), Arg.Any<CancellationToken>()).Returns(row);
        var sut = new AuraTemplateController(repository, World(1), Microsoft.Extensions.Options.Options.Create(Options))
        { ControllerContext = Context() };

        AuraTemplateDto dto = Dto<AuraTemplateDto>(await sut.Get(5, CancellationToken.None));

        Assert.Equal(("Fortified", Avalon.Api.Contract.AuraKind.Helpful, 30000u), (dto.Name, dto.Kind, dto.DurationMs));
        AuraStatModifierDto modifier = Assert.Single(dto.Modifiers);
        Assert.Equal((Avalon.Api.Contract.AuraStat.Armor, Avalon.Api.Contract.AuraModifierKind.Percent, 20f),
            (modifier.Stat, modifier.Kind, modifier.Value));
        Assert.Equal(TemplateVersion.Of(row), dto.Version);
        Assert.Equal($"\"{dto.Version}\"", sut.Response.Headers.ETag.ToString());
        Assert.True(dto.Editable);
    }

    [Fact]
    public async Task Aura_get_returns_its_periodic_amounts_and_base_damage_coefficient()
    {
        IAuraTemplateRepository repository = Substitute.For<IAuraTemplateRepository>();
        var row = new Avalon.Domain.World.AuraTemplate
        {
            Id = new AuraId(6),
            Name = "Poison",
            Icon = "poison",
            Kind = Avalon.Domain.World.AuraKind.Harmful,
            DurationMs = 9000,
            TickIntervalMs = 3000,
            PeriodicKind = Avalon.Domain.World.AuraPeriodicKind.Damage,
            PeriodicBase = 3f,
            BaseDamageCoefficient = 1f,
            Stacking = Avalon.Domain.World.AuraStacking.Stack,
            MaxStacks = 3,
        };
        repository.FindByIdAsync(Arg.Any<AuraId>(), Arg.Any<CancellationToken>()).Returns(row);
        var sut = new AuraTemplateController(repository, World(1), Microsoft.Extensions.Options.Options.Create(Options))
        { ControllerContext = Context() };

        AuraTemplateDto dto = Dto<AuraTemplateDto>(await sut.Get(6, CancellationToken.None));

        Assert.Equal((3f, 0f, 1f), (dto.PeriodicBase, dto.ScalingCoefficient, dto.BaseDamageCoefficient));
        Assert.Equal((Avalon.Api.Contract.AuraPeriodicKind.Damage, 3000u, Avalon.Api.Contract.AuraStacking.Stack, 3u),
            (dto.PeriodicKind, dto.TickIntervalMs, dto.Stacking, dto.MaxStacks));
    }

    [Fact]
    public async Task Aura_get_answers_404_for_an_unknown_id()
    {
        IAuraTemplateRepository repository = Substitute.For<IAuraTemplateRepository>();
        var sut = new AuraTemplateController(repository, World(1), Microsoft.Extensions.Options.Options.Create(Options))
        { ControllerContext = Context() };

        Assert.IsType<NotFoundResult>(await sut.Get(7, CancellationToken.None));
    }

    /// <summary>The contract enums are cast from the stored ones, so the two must agree name for value.</summary>
    [Fact]
    public void Mirror_every_aura_enum_by_name_and_value()
    {
        static IEnumerable<(string, int)> Of<T>() where T : struct, Enum =>
            Enum.GetValues<T>().Select(v => (v.ToString(), Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(Of<Avalon.Domain.World.AuraKind>(), Of<Avalon.Api.Contract.AuraKind>());
        Assert.Equal(Of<Avalon.Domain.World.AuraPeriodicKind>(), Of<Avalon.Api.Contract.AuraPeriodicKind>());
        Assert.Equal(Of<Avalon.Domain.World.AuraStacking>(), Of<Avalon.Api.Contract.AuraStacking>());
        Assert.Equal(Of<Avalon.Domain.World.AuraStat>(), Of<Avalon.Api.Contract.AuraStat>());
        Assert.Equal(Of<Avalon.Domain.World.AuraModifierKind>(), Of<Avalon.Api.Contract.AuraModifierKind>());
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(9, false)]
    public async Task Creature_get_returns_version_etag_and_editable(ushort world, bool editable)
    {
        ICreatureTemplateRepository repository = Substitute.For<ICreatureTemplateRepository>();
        var row = new CreatureTemplate { Id = new CreatureTemplateId(1), Name = "Wolf" };
        repository.FindByIdAsync(Arg.Any<CreatureTemplateId>(), false, Arg.Any<CancellationToken>()).Returns(row);
        var sut = new CreatureTemplateController(repository, World(world), Microsoft.Extensions.Options.Options.Create(Options))
        { ControllerContext = Context() };

        CreatureTemplateDto dto = Dto<CreatureTemplateDto>(await sut.Get(1, CancellationToken.None));

        Assert.Equal(TemplateVersion.Of(row), dto.Version);
        Assert.Equal($"\"{dto.Version}\"", sut.Response.Headers.ETag.ToString());
        Assert.Equal(editable, dto.Editable);
    }

    [Fact]
    public async Task Item_list_carries_the_version_and_editable_on_each_row()
    {
        IItemTemplateRepository repository = Substitute.For<IItemTemplateRepository>();
        var row = new ItemTemplate { Id = new ItemTemplateId(1), Name = "Sword" };
        repository.PaginateAsync(Arg.Any<EntityPaginateFilter<ItemTemplate>>(), false, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ItemTemplate>(1, 50, 1, [row]));
        var sut = new ItemTemplateController(repository, World(1), Microsoft.Extensions.Options.Options.Create(Options))
        { ControllerContext = Context() };

        PagedResult<ItemTemplateDto> page = await sut.List(1, 50, CancellationToken.None);

        Assert.Equal(TemplateVersion.Of(row), page.Items.Single().Version);
        Assert.True(page.Items.Single().Editable);
    }

    [Fact]
    public async Task Item_get_returns_the_use_fields()
    {
        IItemTemplateRepository repository = Substitute.For<IItemTemplateRepository>();
        var row = new ItemTemplate
        {
            Id = new ItemTemplateId(3),
            Name = "Town Portal Scroll",
            UseScript = "TownPortalScroll",
            UseCastTimeMs = 3000,
            UseCooldownMs = 30000,
            UseCooldownGroup = "scroll",
            UseValue = 7,
        };
        repository.FindByIdAsync(Arg.Any<ItemTemplateId>(), false, Arg.Any<CancellationToken>()).Returns(row);
        var sut = new ItemTemplateController(repository, World(1), Microsoft.Extensions.Options.Options.Create(Options))
        { ControllerContext = Context() };

        ItemTemplateDto dto = Dto<ItemTemplateDto>(await sut.Get(3, CancellationToken.None));

        Assert.Equal(("TownPortalScroll", (uint?)3000u, (uint?)30000u, "scroll", (uint?)7u),
            (dto.UseScript, dto.UseCastTimeMs, dto.UseCooldownMs, dto.UseCooldownGroup, dto.UseValue));
    }

    private static ICurrentWorld World(ushort id)
    {
        ICurrentWorld world = Substitute.For<ICurrentWorld>();
        world.Id.Returns(new WorldId(id));
        return world;
    }

    private static ControllerContext Context() => new() { HttpContext = new DefaultHttpContext() };

    private static T Dto<T>(IActionResult result) => Assert.IsType<T>(Assert.IsType<OkObjectResult>(result).Value);
}
