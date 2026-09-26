using System.Net;
using System.Net.Http.Json;
using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Authentication;

/// <summary>
/// #478 review, owner decision: registration took any password, empty included, and no username
/// or email was required. The request is now validated like a password change: username, email
/// and password are required, and the password is at least 8 characters once trimmed, the form
/// it is hashed in. An invalid request is a 400 validation error and nothing is registered.
/// </summary>
public sealed class RegistrationValidationShould : IAsyncLifetime
{
    private ApiAuthHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await ApiAuthHost.StartAsync();
        _host.Accounts.Register(Arg.Any<RegisterRequest>(), Arg.Any<string>(), Arg.Any<IPAddress>(), Arg.Any<CancellationToken>())
            .Returns((new RegisterResponse { Token = "jwt" }, new AccountId(ApiAuthHost.AccountIdValue)));
        _host.Refresh.IssueAsync(Arg.Any<AccountId>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new RefreshIssueResult("refresh", DateTime.UtcNow.AddDays(30), Guid.NewGuid()));
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<HttpResponseMessage> RegisterAsync(object body) =>
        _host.Client.PostAsJsonAsync("/account/register", body);

    private async Task AssertRefusedAsync(object body)
    {
        using HttpResponseMessage response = await RegisterAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _host.Accounts.DidNotReceiveWithAnyArgs().Register(default!, default!, default!, default);
    }

    [Fact]
    public Task Refuse_an_empty_password() =>
        AssertRefusedAsync(new { username = "newplayer", email = "new@avalon.monster", password = "" });

    [Fact]
    public Task Refuse_a_password_of_seven_characters_once_trimmed() =>
        AssertRefusedAsync(new { username = "newplayer", email = "new@avalon.monster", password = "  1234567   " });

    [Fact]
    public Task Refuse_a_missing_username() =>
        AssertRefusedAsync(new { email = "new@avalon.monster", password = "a strong one" });

    [Fact]
    public Task Refuse_a_missing_email() =>
        AssertRefusedAsync(new { username = "newplayer", password = "a strong one" });

    /// <summary>
    /// Owner decision (#487 re-review): a username is 3 to 16 ASCII letters, digits or underscores,
    /// checked as sent, before it is trimmed or upper-cased.
    /// </summary>
    [Theory]
    [InlineData("ab")]                   // too short
    [InlineData("abcdefghijklmnopq")]    // 17: too long
    [InlineData("bad-name")]
    [InlineData("bad name")]
    [InlineData(" padded")]
    [InlineData("tab\t")]
    [InlineData("\u00FCmlaut")]           // not ASCII
    [InlineData("new\nline")]
    [InlineData("trailing\n")]
    public Task Refuse_a_username_outside_the_allowed_characters_and_length(string username) =>
        AssertRefusedAsync(new { username, email = "new@avalon.monster", password = TestPasswords.Valid });

    [Theory]
    [InlineData("abc")]
    [InlineData("Good_Name_16char")]
    [InlineData("player_01")]
    public async Task Register_a_username_inside_the_rule(string username)
    {
        using HttpResponseMessage response = await RegisterAsync(
            new { username, email = "new@avalon.monster", password = TestPasswords.Valid });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Say_what_a_username_must_be()
    {
        using HttpResponseMessage response = await RegisterAsync(
            new { username = "bad-name", email = "new@avalon.monster", password = TestPasswords.Valid });

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("3 to 16", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_a_valid_request()
    {
        using HttpResponseMessage response = await RegisterAsync(
            new { username = "newplayer", email = "new@avalon.monster", password = "a strong one" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _host.Accounts.Received(1).Register(Arg.Any<RegisterRequest>(), Arg.Any<string>(), Arg.Any<IPAddress>(),
            Arg.Any<CancellationToken>());
    }
}
