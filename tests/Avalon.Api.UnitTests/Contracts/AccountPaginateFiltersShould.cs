using Avalon.Api.Contract;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Xunit;

namespace Avalon.Api.UnitTests.Contracts;

/// <summary>
/// #503 follow-up: emails are stored normalised, so a filter compared as typed missed an account
/// searched for as <c>Player@Avalon.Monster</c>.
/// </summary>
public class AccountPaginateFiltersShould
{
    private static readonly Account s_stored = new()
    {
        Id = new AccountId(7),
        Username = "PLAYER",
        Email = "player@avalon.monster",
        Salt = [1],
        Verifier = [2],
        JoinDate = DateTime.UtcNow,
    };

    [Theory]
    [InlineData("player@avalon.monster", true)]
    [InlineData(" Player@Avalon.MONSTER ", true)]
    [InlineData("other@avalon.monster", false)]
    public void Match_an_email_in_any_case_and_no_other(string email, bool matches) =>
        Assert.Equal(matches, new AccountPaginateFilters { Email = email }.GetFilter().Compile()(s_stored));
}
