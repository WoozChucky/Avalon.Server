using System.Net;
using System.Net.Http.Json;
using Avalon.Api.Contract;
using NSubstitute;
using Xunit;
using static Avalon.Api.UnitTests.Authentication.ApiAuthHost;

namespace Avalon.Api.UnitTests.Authentication;

/// <summary>
/// #478 review: a caller with no peer address (a non-IP transport) got <c>IPAddress.None</c>, so
/// every such caller shared one login source budget: ten failures by one refused them all. The
/// endpoints that spend a source budget now refuse such a caller with 400 instead. Giving each one
/// a key of its own was the alternative, and it would have given them no source budget at all.
/// </summary>
public sealed class AddressLessCallerShould : IAsyncLifetime
{
    private ApiAuthHost _host = null!;

    public async Task InitializeAsync() => _host = await ApiAuthHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<HttpResponseMessage> PostWithoutAddressAsync(string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(NoAddressHeader, "1");
        return await _host.Client.SendAsync(request);
    }

    [Fact]
    public async Task Refuse_a_login_from_a_caller_with_no_address()
    {
        using HttpResponseMessage response = await PostWithoutAddressAsync("/account/authenticate",
            new { username = "caller", password = "pw" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _host.Accounts.DidNotReceiveWithAnyArgs().Authenticate(default!, default!, default);
    }

    [Fact]
    public async Task Refuse_an_mfa_verify_from_a_caller_with_no_address()
    {
        using HttpResponseMessage response = await PostWithoutAddressAsync("/mfa/verify", new { hash = "h", code = "1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _host.Mfa.DidNotReceiveWithAnyArgs().VerifyMFAAsync(default!, default!, default);
    }

    /// <summary>Registration spends the source budget too (#495).</summary>
    [Fact]
    public async Task Refuse_a_registration_from_a_caller_with_no_address()
    {
        using HttpResponseMessage response = await PostWithoutAddressAsync("/account/register",
            new { username = "newcomer", password = TestPasswords.Valid, email = "new@avalon.monster" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _host.Accounts.DidNotReceiveWithAnyArgs().Register(default!, default!, default!, default);
    }

    [Fact]
    public async Task Still_log_in_a_caller_with_an_address()
    {
        _host.Accounts.Authenticate(Arg.Any<AuthenticateRequest>(), IPAddress.Loopback, Arg.Any<CancellationToken>())
            .Returns((new AuthenticateResponse { Status = AuthenticationResponseStatus.RequiresMFA, MfaHash = "h" }, null));

        using HttpResponseMessage response = await _host.Client.PostAsJsonAsync("/account/authenticate",
            new { username = "caller", password = "pw" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
