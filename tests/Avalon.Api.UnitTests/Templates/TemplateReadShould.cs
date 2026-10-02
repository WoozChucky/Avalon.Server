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
using Microsoft.Extensions.Options;
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
        var repository = Substitute.For<IItemTemplateRepository>();
        var row = new ItemTemplate { Id = new ItemTemplateId(1), Name = "Sword" };
        repository.FindByIdAsync(Arg.Any<ItemTemplateId>(), false, Arg.Any<CancellationToken>()).Returns(row);
        var sut = new ItemTemplateController(repository, World(world), Microsoft.Extensions.Options.Options.Create(Options))
            { ControllerContext = Context() };

        var dto = Dto<ItemTemplateDto>(await sut.Get(1, CancellationToken.None));

        Assert.Equal(TemplateVersion.Of(row), dto.Version);
        Assert.Equal($"\"{dto.Version}\"", sut.Response.Headers.ETag.ToString());
        Assert.Equal(editable, dto.Editable);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, false)]
    public async Task Ability_get_returns_version_etag_and_editable(ushort world, bool editable)
    {
        var repository = Substitute.For<IAbilityTemplateRepository>();
        var row = new AbilityTemplate { Id = new AbilityId(1), Name = "Cleave", ScriptName = "s" };
        repository.FindByIdAsync(Arg.Any<AbilityId>(), false, Arg.Any<CancellationToken>()).Returns(row);
        var sut = new AbilityTemplateController(repository, World(world), Microsoft.Extensions.Options.Options.Create(Options))
            { ControllerContext = Context() };

        var dto = Dto<AbilityTemplateDto>(await sut.Get(1, CancellationToken.None));

        Assert.Equal(TemplateVersion.Of(row), dto.Version);
        Assert.Equal($"\"{dto.Version}\"", sut.Response.Headers.ETag.ToString());
        Assert.Equal(editable, dto.Editable);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(9, false)]
    public async Task Creature_get_returns_version_etag_and_editable(ushort world, bool editable)
    {
        var repository = Substitute.For<ICreatureTemplateRepository>();
        var row = new CreatureTemplate { Id = new CreatureTemplateId(1), Name = "Wolf" };
        repository.FindByIdAsync(Arg.Any<CreatureTemplateId>(), false, Arg.Any<CancellationToken>()).Returns(row);
        var sut = new CreatureTemplateController(repository, World(world), Microsoft.Extensions.Options.Options.Create(Options))
            { ControllerContext = Context() };

        var dto = Dto<CreatureTemplateDto>(await sut.Get(1, CancellationToken.None));

        Assert.Equal(TemplateVersion.Of(row), dto.Version);
        Assert.Equal($"\"{dto.Version}\"", sut.Response.Headers.ETag.ToString());
        Assert.Equal(editable, dto.Editable);
    }

    [Fact]
    public async Task Item_list_carries_the_version_and_editable_on_each_row()
    {
        var repository = Substitute.For<IItemTemplateRepository>();
        var row = new ItemTemplate { Id = new ItemTemplateId(1), Name = "Sword" };
        repository.PaginateAsync(Arg.Any<EntityPaginateFilter<ItemTemplate>>(), false, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ItemTemplate>(1, 50, 1, [row]));
        var sut = new ItemTemplateController(repository, World(1), Microsoft.Extensions.Options.Options.Create(Options))
            { ControllerContext = Context() };

        var page = await sut.List(1, 50, CancellationToken.None);

        Assert.Equal(TemplateVersion.Of(row), page.Items.Single().Version);
        Assert.True(page.Items.Single().Editable);
    }

    [Fact]
    public async Task Item_get_returns_the_use_fields()
    {
        var repository = Substitute.For<IItemTemplateRepository>();
        var row = new ItemTemplate
        {
            Id = new ItemTemplateId(3), Name = "Town Portal Scroll", UseScript = "TownPortalScroll", UseCastTimeMs = 3000,
            UseCooldownMs = 30000, UseCooldownGroup = "scroll", UseValue = 7,
        };
        repository.FindByIdAsync(Arg.Any<ItemTemplateId>(), false, Arg.Any<CancellationToken>()).Returns(row);
        var sut = new ItemTemplateController(repository, World(1), Microsoft.Extensions.Options.Options.Create(Options))
            { ControllerContext = Context() };

        var dto = Dto<ItemTemplateDto>(await sut.Get(3, CancellationToken.None));

        Assert.Equal(("TownPortalScroll", (uint?)3000u, (uint?)30000u, "scroll", (uint?)7u),
            (dto.UseScript, dto.UseCastTimeMs, dto.UseCooldownMs, dto.UseCooldownGroup, dto.UseValue));
    }

    private static ICurrentWorld World(ushort id)
    {
        var world = Substitute.For<ICurrentWorld>();
        world.Id.Returns(new WorldId(id));
        return world;
    }

    private static ControllerContext Context() => new() { HttpContext = new DefaultHttpContext() };

    private static T Dto<T>(IActionResult result) => Assert.IsType<T>(Assert.IsType<OkObjectResult>(result).Value);
}
