using System.Security.Claims;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Worlds.Controllers;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Worlds.UnitTests.Controllers;

public class ItemTemplateControllerShould
{
    private readonly IItemTemplateRepository _repository = Substitute.For<IItemTemplateRepository>();

    private ItemTemplateController MakeSut(ClaimsPrincipal user) =>
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
    public async Task Get_Returns200_WhenFound()
    {
        _repository
            .FindByIdAsync(Arg.Any<ItemTemplateId>(), false, Arg.Any<CancellationToken>())
            .Returns(new ItemTemplate { Id = new ItemTemplateId(1), Name = "Sword" });

        ItemTemplateController sut = MakeSut(User(7, AvalonRoles.Player));
        IActionResult result = await sut.Get(1, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }
}
