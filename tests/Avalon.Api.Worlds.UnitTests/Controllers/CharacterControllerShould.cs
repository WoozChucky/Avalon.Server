using System.Security.Claims;
using Avalon.Api.Contract;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Worlds.Controllers;
using Avalon.Api.Worlds.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Domain.Characters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Worlds.UnitTests.Controllers;

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

    /// <summary>Calls one endpoint for character 42, as <paramref name="user"/>.</summary>
    private Task<IActionResult> Call(string endpoint, ClaimsPrincipal user)
    {
        CharacterController sut = MakeSut(user);
        return endpoint switch
        {
            nameof(CharacterController.GetById) => sut.GetById(42, CancellationToken.None),
            nameof(CharacterController.Patch) => sut.Patch(42, new CharacterPatchDto { Name = "x" }, CancellationToken.None),
            nameof(CharacterController.GetInventory) => sut.GetInventory(42, CancellationToken.None),
            nameof(CharacterController.GetStats) => sut.GetStats(42, CancellationToken.None),
            nameof(CharacterController.GetQuests) => sut.GetQuests(42, CancellationToken.None),
            nameof(CharacterController.GetAuras) => sut.GetAuras(42, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null),
        };
    }

    private void CharacterIsRefusedTo(ClaimsPrincipal user)
    {
        Character ch = MakeChar(99);
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>()).Returns(ch);
        _authz.AuthorizeAsync(user, ch, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());
    }

    [Theory]
    [InlineData(nameof(CharacterController.GetById))]
    [InlineData(nameof(CharacterController.Patch))]
    [InlineData(nameof(CharacterController.GetInventory))]
    [InlineData(nameof(CharacterController.GetStats))]
    [InlineData(nameof(CharacterController.GetQuests))]
    [InlineData(nameof(CharacterController.GetAuras))]
    public async Task Answer_404_for_a_missing_character(string endpoint)
    {
        _service.GetCharacterByIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        Assert.IsType<NotFoundResult>(await Call(endpoint, User(7, AvalonRoles.Player)));
    }

    /// <summary>A player may not learn that another player's character exists, and nothing past it is read.</summary>
    [Theory]
    [InlineData(nameof(CharacterController.GetById))]
    [InlineData(nameof(CharacterController.GetInventory))]
    [InlineData(nameof(CharacterController.GetStats))]
    [InlineData(nameof(CharacterController.GetQuests))]
    [InlineData(nameof(CharacterController.GetAuras))]
    public async Task Answer_404_to_a_player_the_character_is_refused_to(string endpoint)
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        CharacterIsRefusedTo(user);

        Assert.IsType<NotFoundResult>(await Call(endpoint, user));
        Assert.All(_service.ReceivedCalls(),
            call => Assert.Equal(nameof(ICharacterService.GetCharacterByIdAsync), call.GetMethodInfo().Name));
    }

    [Theory]
    [InlineData(nameof(CharacterController.GetById))]
    [InlineData(nameof(CharacterController.GetQuests))]
    [InlineData(nameof(CharacterController.GetAuras))]
    public async Task Answer_403_to_a_game_master_the_character_is_refused_to(string endpoint)
    {
        ClaimsPrincipal user = User(99, AvalonRoles.GameMaster);
        CharacterIsRefusedTo(user);

        Assert.IsType<ForbidResult>(await Call(endpoint, user));
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
}
