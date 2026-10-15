using System.Net;
using System.Net.Http.Json;
using Avalon.Api.Contract;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Testing;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;
using static Avalon.Api.Testing.ApiTestHost;

namespace Avalon.Api.Hosting.UnitTests.Middlewares;

/// <summary>
/// The API's own line behind the edge proxy (audit of 2026-10-15: the API processed a plaintext login): a request a
/// trusted proxy forwarded over http is refused before any endpoint runs, while a call straight to the pod over cluster
/// http, one forwarded as https, and one whose forwarded scheme comes from a peer that is not trusted are served. Every
/// response says nosniff, one to a request that arrived over https carries HSTS, and a login's token is never cached.
/// The host runs as production does; loopback, the test host's default peer, is a trusted proxy.
/// </summary>
public sealed class TransportSecurityShould
{
    [Theory]
    // A world server, a probe or the balance proxy calling the pod over cluster http: no forwarded scheme.
    [InlineData(null, null, null, true, false)]
    [InlineData(null, "https", null, true, true)]
    [InlineData(null, "http", null, false, false)]
    // A peer that is not a trusted proxy names no scheme, as it names no address.
    [InlineData("203.0.113.9", "http", null, true, false)]
    // Application:ForwardedHeaders:RefusePlaintext turns the refusal off.
    [InlineData(null, "http", "false", true, false)]
    public async Task Refuse_a_login_forwarded_over_plaintext_and_serve_the_rest_with_the_transport_headers(
        string? peer, string? forwardedProto, string? refusePlaintext, bool served, bool hsts)
    {
        await using ApiTestHost host = await StartAsync(ApiServices.All, new ApiTestHostOptions
        {
            Settings = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Application:ForwardedHeaders:RefusePlaintext"] = refusePlaintext,
            },
        });
        host.Accounts.Authenticate(Arg.Any<AuthenticateRequest>(), Arg.Any<IPAddress>(), Arg.Any<CancellationToken>())
            .Returns((new AuthenticateResponse { Status = AuthenticationResponseStatus.Success, Token = "access" }, null, 0));

        // A host the HSTS middleware does not skip, as it skips localhost.
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://api.avalon.example/account/authenticate")
        {
            Content = JsonContent.Create(new { username = "caller", password = TestPasswords.Valid }),
        };
        if (peer is not null) request.Headers.Add(PeerHeader, peer);
        if (forwardedProto is not null) request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        using HttpResponseMessage response = await host.Client.SendAsync(request);

        Assert.Equal(["nosniff"], response.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal(hsts ? ["max-age=31536000"] : [], Values(response, "Strict-Transport-Security"));
        if (served)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Contains("no-cache", Values(response, "Pragma"));
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(ForwardedPlaintextRefusal.Detail, (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Detail);
            await host.Accounts.DidNotReceiveWithAnyArgs().Authenticate(default!, default!, default);
        }
    }

    private static string[] Values(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out IEnumerable<string>? values) ? values.ToArray() : [];
}
