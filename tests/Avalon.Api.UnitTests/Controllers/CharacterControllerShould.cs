using System.Security.Claims;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Domain.Characters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Controllers;

public class CharacterControllerShould
{
    private readonly ICharacterService _service = Substitute.For<ICharacterService>();
    private readonly IAuthorizationService _authz = Substitute.For<IAuthorizationService>();
    private readonly CurrentWorld _world = SelectedWorld();

    private static CurrentWorld SelectedWorld()
    {
        CurrentWorld world = new();
        world.Select(new Avalon.Domain.Auth.WorldId(1), "Development");
        return world;
    }

    private CharacterController MakeSut(ClaimsPrincipal user) =>
        new(_service, _authz, _world)
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

    private static Character MakeChar(long accountId) => new()
    {
        Id = new CharacterId(42),
        AccountId = new AccountId(accountId),
        Name = "c",
        CreationDate = DateTime.UtcNow,
    };

    [Fact]
    public async Task GetById_Returns200_WhenAuthzSucceeds()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Character ch = MakeChar(7);
        _service.GetCharacterByIdAsync(new CharacterId(42), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Success());

        CharacterController sut = MakeSut(user);
        IActionResult result = await sut.GetById(42, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetById_Carries_the_requests_world()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Character ch = MakeChar(7);
        _service.GetCharacterByIdAsync(new CharacterId(42), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Success());

        OkObjectResult result = Assert.IsType<OkObjectResult>(await MakeSut(user).GetById(42, CancellationToken.None));

        CharacterDto dto = Assert.IsType<CharacterDto>(result.Value);
        Assert.Equal((ushort)1, dto.WorldId);
        Assert.Equal("Development", dto.WorldName);
    }

    [Fact]
    public async Task GetById_Returns404_WhenEntityMissing()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        CharacterController sut = MakeSut(user);
        IActionResult result = await sut.GetById(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetById_Returns404_WhenAuthzFailsAndCallerIsPlayer()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Character ch = MakeChar(99);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());

        CharacterController sut = MakeSut(user);
        IActionResult result = await sut.GetById(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetById_Returns403_WhenAuthzFailsAndCallerIsGameMaster()
    {
        ClaimsPrincipal user = User(99, AvalonRoles.GameMaster);
        Character ch = MakeChar(7);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());

        CharacterController sut = MakeSut(user);
        IActionResult result = await sut.GetById(42, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Patch_Returns404_WhenCharacterMissing()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        CharacterController sut = MakeSut(user);
        IActionResult result = await sut.Patch(42, new CharacterPatchDto { Name = "x" }, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Patch_OwnerPlayer_CallsUpdateCosmeticOnly()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Character ch = MakeChar(7);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Success());

        CharacterController sut = MakeSut(user);
        var dto = new CharacterPatchDto { Name = "new", Level = 99 };
        await sut.Patch(42, dto, CancellationToken.None);

        await _service.Received(1).UpdateCosmeticAsync(ch, "new", Arg.Any<CancellationToken>());
        await _service.DidNotReceive().UpdateAnyAsync(Arg.Any<Character>(), Arg.Any<CharacterPatchDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Patch_Admin_CallsUpdateAny()
    {
        ClaimsPrincipal user = User(99, AvalonRoles.Admin);
        Character ch = MakeChar(7);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Success());

        CharacterController sut = MakeSut(user);
        var dto = new CharacterPatchDto { Level = 99 };
        await sut.Patch(42, dto, CancellationToken.None);

        await _service.Received(1).UpdateAnyAsync(ch, dto, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetInventory_Returns200_WhenAuthzSucceeds()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Character ch = MakeChar(7);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Success());
        _service.GetInventoryAsync(new CharacterId(42), Arg.Any<CancellationToken>())
            .Returns(new CharacterInventoryDto { CharacterId = 42 });

        CharacterController sut = MakeSut(user);
        IActionResult result = await sut.GetInventory(42, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetInventory_Returns404_WhenCharacterMissing()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        CharacterController sut = MakeSut(user);
        IActionResult result = await sut.GetInventory(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Paginate_ReturnsMappedResult()
    {
        ClaimsPrincipal user = User(99, AvalonRoles.GameMaster);
        _service.PaginateAsync(Arg.Any<CharacterPaginateFilters>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<Character>(1, 50, 1, new List<Character> { MakeChar(7) }));

        CharacterController sut = MakeSut(user);
        PagedResult<CharacterDto> result = await sut.Paginate(new CharacterPaginateFilters(), CancellationToken.None);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetInventory_Returns404_WhenAuthzFailsAndCallerIsPlayer()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Character ch = MakeChar(99);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());

        CharacterController sut = MakeSut(user);
        IActionResult result = await sut.GetInventory(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetStats_Returns200_WhenAuthzSucceeds()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Character ch = MakeChar(7);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Success());
        _service.GetStatsAsync(new CharacterId(42), Arg.Any<CancellationToken>())
            .Returns(new CharacterStatsDto { CharacterId = 42, MaxHealth = 320 });

        CharacterController sut = MakeSut(user);
        IActionResult result = await sut.GetStats(42, CancellationToken.None);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(320u, Assert.IsType<CharacterStatsDto>(ok.Value).MaxHealth);
    }

    [Fact]
    public async Task GetStats_Returns404_WhenCharacterMissing()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        CharacterController sut = MakeSut(user);
        IActionResult result = await sut.GetStats(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetStats_Returns404_WhenNoStatsSaved()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Character ch = MakeChar(7);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Success());
        _service.GetStatsAsync(new CharacterId(42), Arg.Any<CancellationToken>())
            .Returns((CharacterStatsDto?)null);

        CharacterController sut = MakeSut(user);
        IActionResult result = await sut.GetStats(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetStats_Returns404_WhenAuthzFailsAndCallerIsPlayer()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Character ch = MakeChar(99);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());

        CharacterController sut = MakeSut(user);
        IActionResult result = await sut.GetStats(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _service.DidNotReceive().GetStatsAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetQuests_Returns200_WhenAuthzSucceeds()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Character ch = MakeChar(7);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Success());
        _service.GetQuestLogAsync(new CharacterId(42), Arg.Any<CancellationToken>())
            .Returns(new CharacterQuestLogDto { CharacterId = 42, Completed = [new CharacterCompletedQuestDto { QuestId = 1 }] });

        IActionResult result = await MakeSut(user).GetQuests(42, CancellationToken.None);

        CharacterQuestLogDto log = Assert.IsType<CharacterQuestLogDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(1u, Assert.Single(log.Completed).QuestId);
    }

    [Fact]
    public async Task GetQuests_Returns404_WhenCharacterMissing()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        IActionResult result = await MakeSut(user).GetQuests(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetQuests_Returns404_WhenAuthzFailsAndCallerIsPlayer()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Character ch = MakeChar(99);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());

        IActionResult result = await MakeSut(user).GetQuests(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _service.DidNotReceive().GetQuestLogAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetQuests_Returns403_WhenAuthzFailsAndCallerIsGameMaster()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.GameMaster);
        Character ch = MakeChar(99);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());

        IActionResult result = await MakeSut(user).GetQuests(42, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetAuras_Returns200_WhenAuthzSucceeds()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Character ch = MakeChar(7);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Success());
        _service.GetAurasAsync(new CharacterId(42), Arg.Any<CancellationToken>())
            .Returns(new CharacterAurasDto { CharacterId = 42, Auras = [new CharacterAuraDto { AuraId = 1, RemainingMs = 4000 }] });

        IActionResult result = await MakeSut(user).GetAuras(42, CancellationToken.None);

        CharacterAurasDto auras = Assert.IsType<CharacterAurasDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal((1u, 4000u), (Assert.Single(auras.Auras).AuraId, auras.Auras[0].RemainingMs));
    }

    [Fact]
    public async Task GetAuras_Returns404_WhenCharacterMissing()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        IActionResult result = await MakeSut(user).GetAuras(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _service.DidNotReceive().GetAurasAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAuras_Returns404_WhenAuthzFailsAndCallerIsPlayer()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Character ch = MakeChar(99);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());

        IActionResult result = await MakeSut(user).GetAuras(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _service.DidNotReceive().GetAurasAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAuras_Returns403_WhenAuthzFailsAndCallerIsGameMaster()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.GameMaster);
        Character ch = MakeChar(99);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());

        IActionResult result = await MakeSut(user).GetAuras(42, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }
}
