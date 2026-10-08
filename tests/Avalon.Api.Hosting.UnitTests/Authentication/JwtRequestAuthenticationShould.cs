using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Hosting.Config;
using Avalon.Api.Identity.Authentication.Jwt;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using static Avalon.Api.Testing.ApiTestHost;
using Avalon.Database.Auth.Repositories;
using Avalon.Api.Testing;

namespace Avalon.Api.Hosting.UnitTests.Authentication;

/// <summary>
/// #480: an access JWT must stop working when it expires, and must never carry more than the
/// account behind it holds now. Every request goes over HTTP through <see cref="ApiTestHost"/>,
/// so the bearer handler, its events and the policies are the real ones.
/// </summary>
public sealed class JwtRequestAuthenticationShould : IAsyncLifetime
{
    private ApiTestHost _host = null!;
    // The repository authentication reads the account through (#794).
    private IAccountRepository Accounts => _host.AccountRepository;

    public async Task InitializeAsync() => _host = await ApiTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static AccountId IsTheCaller => Arg.Is<AccountId>(id => id.Value == AccountIdValue);

    [Fact]
    public async Task Accept_a_live_token_for_an_active_account_and_load_the_account_once()
    {
        Account account = MakeAccount();
        _host.AccountNowIs(account);
        // Identity signs with ES256 under its key id (#801); the service checks it with the public key that id names.
        string token = Mint(account);
        JwtSecurityToken minted = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal((SecurityAlgorithms.EcdsaSha256, SigningKeyId), (minted.Header.Alg, minted.Header.Kid));

        using HttpResponseMessage response = await _host.GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Authentication loads the account; the authorization handler reuses it.
        await Accounts.Received(1).FindByIdAsync(IsTheCaller, Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuse_a_token_that_expired_beyond_the_clock_skew()
    {
        _host.AccountNowIs(MakeAccount());
        DateTime now = DateTime.UtcNow;
        // Expired five minutes ago; the configured skew is one minute.
        string token = MintCustom(now.AddMinutes(-20), now.AddMinutes(-5));

        using HttpResponseMessage response = await _host.GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Accept_a_token_that_expired_within_the_clock_skew()
    {
        _host.AccountNowIs(MakeAccount());
        DateTime now = DateTime.UtcNow;
        string token = MintCustom(now.AddMinutes(-15), now.AddSeconds(-10));

        using HttpResponseMessage response = await _host.GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Deny_the_admin_policy_to_an_admin_token_whose_account_was_demoted()
    {
        string token = Mint(MakeAccount(AccountAccessLevel.Player | AccountAccessLevel.Admin));
        _host.AccountNowIs(MakeAccount(AccountAccessLevel.Player));

        using HttpResponseMessage response = await _host.GetAsync("/admin", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await Accounts.Received().FindByIdAsync(IsTheCaller, Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Carry_only_the_roles_both_the_token_and_the_account_hold()
    {
        // Minted as Player|Admin; since then Admin was removed and GameMaster granted. The token
        // loses Admin at once, and does not gain GameMaster until the account signs in again.
        string token = Mint(MakeAccount(AccountAccessLevel.Player | AccountAccessLevel.Admin));
        _host.AccountNowIs(MakeAccount(AccountAccessLevel.Player | AccountAccessLevel.GameMaster));

        using HttpResponseMessage response = await _host.GetAsync("/roles", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Player", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Let_an_admin_token_through_while_the_account_is_still_admin()
    {
        Account account = MakeAccount(AccountAccessLevel.Player | AccountAccessLevel.Admin);
        _host.AccountNowIs(account);

        using HttpResponseMessage response = await _host.GetAsync("/admin", Mint(account));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(AccountStatus.Banned)]
    [InlineData(AccountStatus.Deactivated)]
    public async Task Refuse_a_token_for_an_account_that_is_no_longer_active(AccountStatus status)
    {
        string token = Mint(MakeAccount());
        _host.AccountNowIs(MakeAccount(status: status));

        using HttpResponseMessage response = await _host.GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await Accounts.Received().FindByIdAsync(IsTheCaller, Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuse_a_token_for_an_account_that_no_longer_exists()
    {
        string token = Mint(MakeAccount());
        _host.AccountNowIs(null);

        using HttpResponseMessage response = await _host.GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-number")]
    [InlineData("-7")]
    public async Task Refuse_a_token_whose_subject_is_missing_or_not_an_account_id(string? subject)
    {
        _host.AccountNowIs(MakeAccount());

        using HttpResponseMessage response = await _host.GetAsync("/player", MintLive(subject));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A token is accepted only when it is ES256 and the public key its key id names checks it (#801). HS256 is refused
    /// even with the key the tokens of before #801 were signed with still configured, both as the ignored
    /// <c>Application:Authentication:IssuerSigningKey</c> and as the game-auth host key, which a deployment from before
    /// #801 gives that key's value: a token signed as those were (HS256 with that key, naming no key id), one naming a key
    /// id, another HMAC algorithm, a key id no service lists, another private key under the listed key id, an HMAC keyed
    /// with the public key (its bytes under its key id, or its text with none), and a token with no signature are all
    /// refused.
    /// </summary>
    [Theory]
    [InlineData("HS256 with the old key, naming no key id, as before #801")]
    [InlineData("HS256 with the old key, naming a key id")]
    [InlineData("HS512 with the old key")]
    [InlineData("a key id no service lists")]
    [InlineData("another private key under the listed key id")]
    [InlineData("an HMAC keyed with the public key's bytes, under its key id")]
    [InlineData("an HMAC keyed with the public key's PEM text, with no key id")]
    [InlineData("alg none")]
    public async Task Refuse_a_token_no_listed_key_signed_with_ES256(string forgery)
    {
        await using ApiTestHost host = await StartWithTheOldKeyAsync();
        host.AccountNowIs(MakeAccount());
        using var another = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = Convert.FromBase64String(PublicKey);
        byte[] oldKey = Encoding.UTF8.GetBytes(HostKey);
        string token = forgery switch
        {
            "HS256 with the old key, naming no key id, as before #801" => MintLive(signing: Hmac(oldKey, null)),
            "HS256 with the old key, naming a key id" => MintLive(signing: Hmac(oldKey, SigningKeyId)),
            "HS512 with the old key" => MintLive(signing: Hmac(oldKey, null, SecurityAlgorithms.HmacSha512)),
            "a key id no service lists" => MintLive(signing: Es256(another, "unknown")),
            "another private key under the listed key id" => MintLive(signing: Es256(another, SigningKeyId)),
            "an HMAC keyed with the public key's bytes, under its key id" => MintLive(signing: Hmac(publicKey, SigningKeyId)),
            "an HMAC keyed with the public key's PEM text, with no key id" =>
                MintLive(signing: Hmac(Encoding.ASCII.GetBytes(PemEncoding.Write("PUBLIC KEY", publicKey)), null)),
            "alg none" => Unsigned(MintLive()),
            _ => throw new ArgumentOutOfRangeException(nameof(forgery), forgery, null),
        };

        using HttpResponseMessage response = await host.GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A key rotation (#801): identity signs with its new key, which it knows without its being listed, while every
    /// service still lists the old public key, so a token signed with either is accepted until the old one is removed.
    /// </summary>
    [Fact]
    public async Task Accept_a_token_signed_with_either_key_during_a_rotation()
    {
        using var next = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await using ApiTestHost host = await ApiTestHost.StartAsync(ApiServices.All, new ApiTestHostOptions
        {
            Settings = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Application:Authentication:SigningKey"] = next.ExportPkcs8PrivateKeyPem(),
                ["Application:Authentication:SigningKeyId"] = "next",
            },
        });
        Account account = MakeAccount();
        host.AccountNowIs(account);
        string signedWithTheNewKey;
        using (IServiceScope scope = host.Services.CreateScope())
            signedWithTheNewKey = scope.ServiceProvider.GetRequiredService<IJwtUtils>().GenerateJwtToken(account);

        using HttpResponseMessage old = await host.GetAsync("/player", Mint(account));
        using HttpResponseMessage current = await host.GetAsync("/player", signedWithTheNewKey);

        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    [Fact]
    public async Task Accept_a_token_sent_in_the_session_cookie()
    {
        Account account = MakeAccount();
        _host.AccountNowIs(account);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/player");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={Mint(account)}");
        using HttpResponseMessage response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // An anonymous endpoint still answers a refused token, but as an anonymous caller.
    [Fact]
    public async Task Treat_a_refused_token_as_anonymous_on_an_anonymous_endpoint()
    {
        string token = Mint(MakeAccount());
        _host.AccountNowIs(MakeAccount(status: AccountStatus.Banned));

        using HttpResponseMessage response = await _host.GetAsync("/anonymous", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("anonymous", await response.Content.ReadAsStringAsync());
    }

    // The database being down must fail closed, and read as "unavailable", not as a server bug.
    [Fact]
    public async Task Answer_503_when_the_account_lookup_hits_a_database_failure()
    {
        Accounts.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new FakeDbException());

        using HttpResponseMessage response = await _host.GetAsync("/player", Mint(MakeAccount()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Never_let_a_request_through_when_the_account_lookup_throws()
    {
        Accounts.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        using HttpResponseMessage response = await _host.GetAsync("/player", Mint(MakeAccount()));

        Assert.False(response.IsSuccessStatusCode);
    }

    // ---------------- Credentials version (#495) ----------------

    private static Account AtVersion(int version)
    {
        Account account = MakeAccount();
        account.CredentialsVersion = version;
        return account;
    }

    /// <summary>
    /// The change moved the account to version 1 in the same second the token was minted at
    /// version 0. A timestamp compared at the second let it through; the version does not.
    /// </summary>
    [Fact]
    public async Task Refuse_a_token_minted_in_the_same_second_as_a_credentials_change()
    {
        string token = Mint(AtVersion(0));
        _host.AccountNowIs(AtVersion(1));

        using HttpResponseMessage response = await _host.GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Accept_the_owners_fresh_login_straight_after_the_change()
    {
        string token = Mint(AtVersion(1));
        _host.AccountNowIs(AtVersion(1));

        using HttpResponseMessage response = await _host.GetAsync("/player", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("one")]
    [InlineData("-1")]
    public async Task Refuse_a_token_whose_credentials_version_is_missing_or_malformed(string? claim)
    {
        _host.AccountNowIs(AtVersion(0));
        DateTime now = DateTime.UtcNow;

        using HttpResponseMessage response = await _host.GetAsync("/player",
            MintCustom(now.AddMinutes(-1), now.AddMinutes(10), credentialsVersion: claim));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A host given the HS256 key of before #801 as it was given then, <c>Application:Authentication:IssuerSigningKey</c>,
    /// beside the game-auth host key that holds the same value (<see cref="ApiTestHost.HostKey"/>).
    /// </summary>
    private static Task<ApiTestHost> StartWithTheOldKeyAsync() => ApiTestHost.StartAsync(ApiServices.All,
        new ApiTestHostOptions
        {
            Settings = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [TokenValidationConfig.IssuerSigningKeySetting] = HostKey,
            },
        });

    private static SigningCredentials Es256(ECDsa key, string keyId) =>
        new(new ECDsaSecurityKey(key) { KeyId = keyId }, SecurityAlgorithms.EcdsaSha256);

    private static SigningCredentials Hmac(byte[] key, string? keyId, string algorithm = SecurityAlgorithms.HmacSha256) =>
        new(new SymmetricSecurityKey(key) { KeyId = keyId }, algorithm);

    /// <summary><paramref name="token"/>'s claims under a header that names no algorithm, with no signature.</summary>
    private static string Unsigned(string token) =>
        $"{Base64UrlEncoder.Encode($$"""{"alg":"none","kid":"{{SigningKeyId}}","typ":"JWT"}""")}.{token.Split('.')[1]}.";

    private sealed class FakeDbException() : DbException("connection refused");
}
