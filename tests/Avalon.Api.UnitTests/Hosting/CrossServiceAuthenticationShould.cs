using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Identity;
using Avalon.Api.Identity.Authentication.Jwt;
using Avalon.Api.Testing;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// An access token identity mints is accepted by every other service, each running alone in its own process (#794,
/// design section 4.1): the process validates it with the public key every process is given, holding no private key
/// (#801), and reloads its account from the auth database they share, as a request to any of its endpoints behind a role
/// policy would. A token signed with another private key under the same key id, or one past its lifetime, is refused
/// there.
/// </summary>
public sealed class CrossServiceAuthenticationShould
{
    private const string PlayerOnly = "/player-only";

    public static TheoryData<string> OtherServices =>
        new(ApiServices.All.Where(service => service != IdentityApi.Service).Select(service => service.Name));

    [Theory]
    [MemberData(nameof(OtherServices))]
    public async Task Accept_the_token_identity_mints_and_refuse_a_forged_or_expired_one(string service)
    {
        Account account = ApiTestHost.MakeAccount();
        IAccountRepository accounts = Substitute.For<IAccountRepository>();
        accounts.FindByIdAsync(Arg.Is<AccountId>(id => id.Value == account.Id.Value), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(account);

        string minted = await MintAsync(account, ApiTestHost.SigningKey);
        using var another = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string forged = await MintAsync(account, another.ExportPkcs8PrivateKeyPem());
        string expired = ApiTestHost.MintCustom(DateTime.UtcNow.AddMinutes(-30), DateTime.UtcNow.AddMinutes(-10));

        await using WebApplication process = ApiProcess.Build(ApiServices.All.Single(candidate => candidate.Name == service),
            services => services.AddSingleton(accounts));
        process.MapGet(PlayerOnly, () => "ok").RequireAuthorization(AvalonRoles.Player);
        await process.StartAsync();
        using HttpClient client = process.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, minted));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, forged));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, expired));
    }

    /// <summary>
    /// The access token an identity process holding <paramref name="signingKey"/> mints for <paramref name="account"/>,
    /// under the key id the other processes list identity's public key under.
    /// </summary>
    private static async Task<string> MintAsync(Account account, string signingKey)
    {
        await using WebApplication identity = ApiProcess.Build(IdentityApi.Service,
            settings: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Application:Authentication:SigningKey"] = signingKey,
                [$"Application:Authentication:ValidationKeys:{ApiTestHost.SigningKeyId}"] = null,
            });
        using IServiceScope scope = identity.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtUtils>().GenerateJwtToken(account);
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, PlayerOnly);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await client.SendAsync(request);
        return response.StatusCode;
    }
}
