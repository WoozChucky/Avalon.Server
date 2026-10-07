using System.Security.Claims;
using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Worlds.Controllers;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Worlds.UnitTests.Controllers;

public class AbilityTemplateControllerShould
{
    private readonly IAbilityTemplateRepository _repository = Substitute.For<IAbilityTemplateRepository>();

    private AbilityTemplateController MakeSut(ClaimsPrincipal user) =>
        new(_repository, Substitute.For<Avalon.Api.Hosting.Worlds.ICurrentWorld>(),
            Microsoft.Extensions.Options.Options.Create(new Avalon.Api.Worlds.Templates.TemplateEditingOptions()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };

    private static ClaimsPrincipal User(long accountId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, accountId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new(new ClaimsIdentity(claims, "test", ClaimTypes.NameIdentifier, ClaimTypes.Role));
    }

    [Fact]
    public async Task List_ReturnsPage()
    {
        _repository
            .PaginateAsync(Arg.Any<EntityPaginateFilter<AbilityTemplate>>(), false, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<AbilityTemplate>(1, 50, 0, new List<AbilityTemplate>()));

        AbilityTemplateController sut = MakeSut(User(7, AvalonRoles.Player));
        PagedResult<AbilityTemplateDto> result = await sut.List(1, 50, CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Get_Returns404_WhenMissing()
    {
        _repository
            .FindByIdAsync(Arg.Any<AbilityId>(), false, Arg.Any<CancellationToken>())
            .Returns((AbilityTemplate?)null);

        AbilityTemplateController sut = MakeSut(User(7, AvalonRoles.Player));
        IActionResult result = await sut.Get(1, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Get_Returns200_WhenFound()
    {
        _repository
            .FindByIdAsync(Arg.Any<AbilityId>(), false, Arg.Any<CancellationToken>())
            .Returns(new AbilityTemplate { Id = new AbilityId(1), Name = "Fireball", ScriptName = "fireball.cs" });

        AbilityTemplateController sut = MakeSut(User(7, AvalonRoles.Player));
        IActionResult result = await sut.Get(1, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    /// <summary>The pool a cost is spent from reaches the REST contract as a named value (#652).</summary>
    [Theory]
    [InlineData(Avalon.Network.Packets.State.PowerType.None, "None")]
    [InlineData(Avalon.Network.Packets.State.PowerType.Mana, "Mana")]
    [InlineData(Avalon.Network.Packets.State.PowerType.Fury, "Fury")]
    [InlineData(Avalon.Network.Packets.State.PowerType.Energy, "Energy")]
    public async Task Get_ReturnsTheCostPowerType(Avalon.Network.Packets.State.PowerType pool, string json)
    {
        _repository
            .FindByIdAsync(Arg.Any<AbilityId>(), false, Arg.Any<CancellationToken>())
            .Returns(new AbilityTemplate
            {
                Id = new AbilityId(1),
                Name = "Fireball",
                ScriptName = "fireball.cs",
                Cost = 5,
                CostPowerType = pool,
            });

        AbilityTemplateController sut = MakeSut(User(7, AvalonRoles.Player));
        IActionResult result = await sut.Get(1, CancellationToken.None);

        AbilityTemplateDto dto = Assert.IsType<AbilityTemplateDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(5u, dto.Cost);
        Assert.Equal(pool.ToString(), dto.CostPowerType.ToString());
        Assert.Contains($"\"costPowerType\":\"{json}\"",
            JsonSerializer.Serialize(dto, JsonSerializerOptions.Web));
    }

    /// <summary>The contract enum is cast from the domain one, so the two must agree name for value.</summary>
    [Fact]
    public void Mirror_every_power_type_by_name_and_value()
    {
        IEnumerable<(string Name, int Value)> domain = Enum.GetValues<Avalon.Network.Packets.State.PowerType>()
            .Select(p => (Name: p.ToString(), Value: (int)p));
        IEnumerable<(string Name, int Value)> contract = Enum.GetValues<Avalon.Api.Contract.PowerType>()
            .Select(p => (Name: p.ToString(), Value: (int)p));

        Assert.Equal(domain, contract);
    }
}
