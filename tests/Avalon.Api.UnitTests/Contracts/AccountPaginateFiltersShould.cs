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
    private static readonly Account Stored = new()
    {
        Id = new AccountId(7), Username = "PLAYER", Email = "player@avalon.monster", Salt = [1], Verifier = [2],
        JoinDate = DateTime.UtcNow,
    };

    [Theory]
    [InlineData("player@avalon.monster")]
    [InlineData(" Player@Avalon.MONSTER ")]
    public void Match_an_email_in_any_case(string email) =>
        Assert.True(new AccountPaginateFilters { Email = email }.GetFilter().Compile()(Stored));

    [Fact]
    public void Not_match_another_email() =>
        Assert.False(new AccountPaginateFilters { Email = "other@avalon.monster" }.GetFilter().Compile()(Stored));
}
