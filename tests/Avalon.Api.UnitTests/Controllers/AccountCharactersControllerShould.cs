using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Api.Services;
using Avalon.Api.UnitTests.Authentication;
using Avalon.Common.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;

namespace Avalon.Api.UnitTests.Controllers;

public class AccountCharactersControllerShould
{
    [Fact]
    public async Task Ask_for_the_callers_characters_at_the_callers_level()
    {
        IAuthContext auth = Substitute.For<IAuthContext>();
        auth.Account.Returns(ApiAuthHost.MakeAccount());
        IAccountCharactersService service = Substitute.For<IAccountCharactersService>();
        CharacterListDto expected = new();
        service.GetAsync(Arg.Any<AccountId>(), Arg.Any<AccountAccessLevel>(), Arg.Any<CancellationToken>()).Returns(expected);
        ClaimsPrincipal user = new(new ClaimsIdentity([new Claim(ClaimTypes.GroupSid, nameof(AccountAccessLevel.Player))], "test"));
        AccountCharactersController sut = new(auth, service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
        };

        CharacterListDto result = await sut.GetAll(CancellationToken.None);

        Assert.Same(expected, result);
        await service.Received(1).GetAsync(Arg.Is<AccountId>(a => a.Value == ApiAuthHost.AccountIdValue),
            AccountAccessLevel.Player, Arg.Any<CancellationToken>());
    }
}
