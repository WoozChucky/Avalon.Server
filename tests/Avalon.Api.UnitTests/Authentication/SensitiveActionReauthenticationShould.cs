using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Avalon.Api.Services;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using NSubstitute;
using Xunit;
using static Avalon.Api.UnitTests.Authentication.ApiAuthHost;

namespace Avalon.Api.UnitTests.Authentication;

/// <summary>
/// #478 and #483: a session alone could enrol an authenticator (<c>/mfa/setup</c>) or mint a
/// personal access token that outlives it (<c>POST /pat</c>, <c>POST /pat/admin</c>), so a stolen
/// access token, good for fifteen minutes, could be turned into a year-long credential or lock the
/// owner out of their second factor. Each now needs the current password, checked by the login
/// policy. Runs the real controllers over HTTP.
/// </summary>
public sealed class SensitiveActionReauthenticationShould : IAsyncLifetime
{
    private const string Password = "correct horse";

    private ApiAuthHost _host = null!;
    private Account _account = null!;

    public async Task InitializeAsync()
    {
        _host = await ApiAuthHost.StartAsync();
        _account = MakeAccount(AccountAccessLevel.Player | AccountAccessLevel.Admin);
        _account.Verifier = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(Password, BCrypt.Net.BCrypt.GenerateSalt(4)));
        _host.AccountNowIs(_account);
        _host.AccountRepository.FindByIdAsync(Arg.Is<AccountId>(id => id.Value == AccountIdValue), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(_account);
        _host.Mfa.SetupMFAAsync(Arg.Any<Account>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MFASetupResult(true, "otpauth://totp/x", MFAOperationResult.Success));
        var minted = new MintResult(new PersonalAccessTokenId(1), "cli", "avp_token", "avp_toke", DateTime.UtcNow.AddDays(30),
            AccountAccessLevel.Player);
        _host.Pats.MintSelfAsync(Arg.Any<AccountId>(), Arg.Any<AccountAccessLevel>(), Arg.Any<string>(),
            Arg.Any<DateTime?>(), Arg.Any<AccountAccessLevel?>(), Arg.Any<Reauthenticated>(), Arg.Any<CancellationToken>()).Returns(minted);
        _host.Pats.MintAdminAsync(Arg.Any<AccountAccessLevel>(), Arg.Any<AccountId>(), Arg.Any<string>(),
            Arg.Any<DateTime?>(), Arg.Any<AccountAccessLevel>(), Arg.Any<Reauthenticated>(), Arg.Any<CancellationToken>()).Returns(minted);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<HttpResponseMessage> PostAsync(string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Mint(_account));
        return await _host.Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> SetupAsync(string? password) =>
        PostAsync("/mfa/setup", password is null ? new { } : new { currentPassword = password });

    private Task<HttpResponseMessage> MintAsync(string? password) =>
        PostAsync("/pat", password is null ? new { name = "cli" } : new { name = "cli", currentPassword = password });

    private Task<HttpResponseMessage> MintAdminAsync(string? password) =>
        PostAsync("/pat/admin", password is null
            ? new { accountId = 9, name = "ops", roles = 1 }
            : new { accountId = 9, name = "ops", roles = 1, currentPassword = password });

    private Task AssertNoSetupAsync() =>
        _host.Mfa.DidNotReceiveWithAnyArgs().SetupMFAAsync(default!, default!, default);

    private Task AssertNoMintAsync() =>
        _host.Pats.DidNotReceiveWithAnyArgs().MintSelfAsync(default!, default, default!, default, default, default, default);

    private Task AssertFailureCountedAsync() =>
        _host.AccountRepository.Received(1).RecordFailedLoginAsync(Arg.Is<AccountId>(id => id.Value == AccountIdValue),
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task Refuse_mfa_setup_without_the_current_password()
    {
        using HttpResponseMessage response = await SetupAsync(null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNoSetupAsync();
    }

    [Fact]
    public async Task Refuse_mfa_setup_with_a_wrong_password_and_count_it_as_a_failed_login()
    {
        using HttpResponseMessage response = await SetupAsync("wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNoSetupAsync();
        await AssertFailureCountedAsync();
    }

    [Fact]
    public async Task Start_mfa_setup_with_the_current_password()
    {
        using HttpResponseMessage response = await SetupAsync(Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _host.Mfa.Received(1).SetupMFAAsync(Arg.Any<Account>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// #478 review: the reset used to enrol a fresh authenticator on the spot, with recovery codes
    /// and no password. It now only resets (and revokes); enrolling again is <c>POST /mfa/setup</c>,
    /// which needs the password.
    /// </summary>
    [Fact]
    public async Task Reset_mfa_without_enrolling_a_new_authenticator()
    {
        _host.Mfa.ResetMFAAsync(Arg.Any<AccountId>(), Arg.Any<int>(), "a", "b", "c", Arg.Any<CancellationToken>())
            .Returns(new MFAResetResult(true, MFAOperationResult.Success));

        using HttpResponseMessage response = await PostAsync("/mfa/reset",
            new { recoveryCode1 = "a", recoveryCode2 = "b", recoveryCode3 = "c" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertNoSetupAsync();
    }

    [Fact]
    public async Task Refuse_to_mint_a_personal_access_token_without_the_current_password()
    {
        using HttpResponseMessage response = await MintAsync(null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNoMintAsync();
    }

    [Fact]
    public async Task Refuse_to_mint_a_personal_access_token_with_a_wrong_password_and_count_it()
    {
        using HttpResponseMessage response = await MintAsync("wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNoMintAsync();
        await AssertFailureCountedAsync();
    }

    [Fact]
    public async Task Mint_a_personal_access_token_with_the_current_password()
    {
        using HttpResponseMessage response = await MintAsync(Password);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>The admin route mints for any account, the admin's own included, so it needs the admin's password too.</summary>
    [Fact]
    public async Task Refuse_to_mint_an_admin_token_without_the_admins_current_password()
    {
        using HttpResponseMessage response = await MintAdminAsync(null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await _host.Pats.DidNotReceiveWithAnyArgs().MintAdminAsync(default, default!, default!, default, default, default, default);
    }

    [Fact]
    public async Task Mint_an_admin_token_with_the_admins_current_password()
    {
        using HttpResponseMessage response = await MintAdminAsync(Password);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
