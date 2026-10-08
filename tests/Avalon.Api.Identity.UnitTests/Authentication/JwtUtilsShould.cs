using System.IdentityModel.Tokens.Jwt;
using System.Text.Json.Nodes;
using Avalon.Api.Hosting.Authentication.Jwt;
using Avalon.Api.Identity.Authentication.Jwt;
using Avalon.Api.Identity.Config;
using Avalon.Api.Testing;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Authentication;

public class JwtUtilsShould
{
    private static readonly AuthenticationConfig s_config = new()
    {
        SigningKey = ApiTestHost.SigningKey,
        SigningKeyId = ApiTestHost.SigningKeyId,
        Issuer = "Avalon Authentication System",
        Audience = "https://api.avalon.monster",
        ValidateIssuer = true,
        ValidateAudience = true,
        ClockSkewInMinutes = 1,
        AccessTokenLifetimeMinutes = 15,
    };

    /// <summary>
    /// The payloads JwtUtils wrote into HS256 tokens before #801 (main at 48516373) for this account, as a website token
    /// and as a launcher token, without the token's id and times. The Dashboard and the launcher read them.
    /// </summary>
    private const string Hs256WebsitePayload =
        """{"nameid":"7","name":"CALLER","email":"caller@avalon.monster","cver":3,"groupsid":["Player","Admin"],"iss":"Avalon Authentication System","aud":"https://api.avalon.monster"}""";

    private const string Hs256LauncherPayload =
        """{"nameid":"7","name":"CALLER","email":"caller@avalon.monster","cver":3,"launcher_family":"0f8fad5b-d9cb-469f-a165-70867728950e","groupsid":["Player","Admin"],"iss":"Avalon Authentication System","aud":"https://api.avalon.monster"}""";

    private static readonly Guid s_launcherFamily = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    private static Account MakeAccount() => new()
    {
        Id = new AccountId(7),
        Username = "CALLER",
        Email = "caller@avalon.monster",
        Salt = [1],
        Verifier = [2],
        JoinDate = DateTime.UtcNow,
        AccessLevel = AccountAccessLevel.Player | AccountAccessLevel.Admin,
        CredentialsVersion = 3,
    };

    /// <summary>
    /// Moving the tokens to ES256 changes their header, never their payload (#801): an ES256 token carries what the
    /// HS256 token for the same account carried (the subject, the name, the email, the credentials version, the launcher
    /// family, every role, the issuer and the audience), and the same lifetime.
    /// </summary>
    [Fact]
    public void Write_the_payload_the_HS256_tokens_carried_into_ES256_tokens()
    {
        var sut = new JwtUtils(s_config, JwtKeys.Create(s_config, signsTokens: true));
        Account account = MakeAccount();

        foreach ((string token, string hs256) in new[]
                 {
                     (sut.GenerateJwtToken(account), Hs256WebsitePayload),
                     (sut.GenerateLauncherJwtToken(account, s_launcherFamily), Hs256LauncherPayload),
                 })
        {
            JwtSecurityToken es256 = new JwtSecurityTokenHandler().ReadJwtToken(token);
            Assert.Equal((SecurityAlgorithms.EcdsaSha256, ApiTestHost.SigningKeyId), (es256.Header.Alg, es256.Header.Kid));

            JsonObject payload = JsonNode.Parse(Base64UrlEncoder.Decode(es256.RawPayload))!.AsObject();
            Assert.Equal(15 * 60, payload["exp"]!.GetValue<long>() - payload["iat"]!.GetValue<long>());
            foreach (string varying in new[] { "jti", "nbf", "exp", "iat" })
                Assert.True(payload.Remove(varying), varying);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(hs256), payload), payload.ToJsonString());
        }
    }
}
