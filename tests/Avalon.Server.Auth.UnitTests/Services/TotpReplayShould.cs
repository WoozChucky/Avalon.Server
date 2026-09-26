using System.Security.Cryptography;
using System.Text;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OtpNet;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// #471: a TOTP code could be used again for as long as it stayed inside the verify window, which
/// was ±2 steps (about ±60 s). The last accepted step is now stored on the account's MFA row, a
/// code is refused unless its step is later, and the window is ±1. These run the real repository
/// over a real relational schema, because the refusal of a replay is a conditional write.
/// </summary>
public sealed class TotpReplayShould : IDisposable
{
    private const string Hash = "mfa-hash";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<AuthDbContext> _options;
    private readonly IDbContextFactory<AuthDbContext> _factory = Substitute.For<IDbContextFactory<AuthDbContext>>();
    private readonly MfaSetupRepository _mfa;
    private readonly AccountRepository _accounts;
    private readonly IMFAHashService _hashService = Substitute.For<IMFAHashService>();
    private readonly ISecureRandom _random = Substitute.For<ISecureRandom>();

    public TotpReplayShould()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;
        _factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_ => new AuthDbContext(_options));
        _factory.CreateDbContext().Returns(_ => new AuthDbContext(_options));
        using (var context = new AuthDbContext(_options))
        {
            context.Database.EnsureCreated();
        }

        _mfa = new MfaSetupRepository(_factory);
        _accounts = new AccountRepository(_factory);
        _random.GetBytes(Arg.Any<int>()).Returns(ci => RandomNumberGenerator.GetBytes(ci.Arg<int>()));
        // Each verify here stands for a fresh login's hash, so each one wins its hash (#478); the
        // refusals under test come from the step, not from a spent hash.
        _hashService.TryConsumeAsync(Hash, Arg.Any<AccountId>()).Returns(true);
    }

    public void Dispose() => _connection.Dispose();

    private MFAService Service() => new(NullLoggerFactory.Instance, _mfa, _hashService, _random,
        Substitute.For<Avalon.Infrastructure.IReplicatedCache>());

    private async Task<(AccountId Id, byte[] Secret)> EnrolledAccountAsync()
    {
        Account account = await _accounts.CreateAsync(new Account
        {
            Username = "TOTPUSER",
            Email = "totp@example.com",
            Salt = new byte[16],
            Verifier = Encoding.UTF8.GetBytes("unused"),
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
        });
        byte[] secret = KeyGeneration.GenerateRandomKey(32);
        await _mfa.CreateAsync(new MFASetup
        {
            AccountId = account.Id,
            Secret = secret,
            RecoveryCode1 = [],
            RecoveryCode2 = [],
            RecoveryCode3 = [],
            Status = MfaSetupStatus.Confirmed,
            CreatedAt = DateTime.UtcNow,
            ConfirmedAt = DateTime.UtcNow,
        });
        _hashService.GetAccountIdAsync(Hash).Returns(account.Id);
        return (account.Id, secret);
    }

    [Fact]
    public async Task Refuse_a_code_the_second_time_it_is_used()
    {
        (_, byte[] secret) = await EnrolledAccountAsync();
        string code = new Totp(secret).ComputeTotp();
        MFAService service = Service();

        MFAVerifyResult first = await service.VerifyMFAAsync(Hash, code);
        MFAVerifyResult second = await service.VerifyMFAAsync(Hash, code);

        Assert.True(first.Success);
        Assert.False(second.Success);
    }

    /// <summary>
    /// Behind the current step on purpose: a code ahead of it would drift into the window if the
    /// clock crossed a step boundary mid-test, one behind only drifts further out.
    /// </summary>
    [Fact]
    public async Task Refuse_a_code_two_steps_from_now()
    {
        (_, byte[] secret) = await EnrolledAccountAsync();
        string code = new Totp(secret).ComputeTotp(DateTime.UtcNow.AddSeconds(-60));

        MFAVerifyResult result = await Service().VerifyMFAAsync(Hash, code);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Accept_a_code_one_step_from_now()
    {
        (_, byte[] secret) = await EnrolledAccountAsync();
        string code = new Totp(secret).ComputeTotp(DateTime.UtcNow.AddSeconds(-30));

        MFAVerifyResult result = await Service().VerifyMFAAsync(Hash, code);

        Assert.True(result.Success);
    }

    /// <summary>An earlier code than the last one accepted is as stale as the same code.</summary>
    [Fact]
    public async Task Refuse_an_older_code_after_a_newer_one_was_accepted()
    {
        (_, byte[] secret) = await EnrolledAccountAsync();
        var totp = new Totp(secret);
        string older = totp.ComputeTotp(DateTime.UtcNow.AddSeconds(-30));
        string current = totp.ComputeTotp();
        MFAService service = Service();

        Assert.True((await service.VerifyMFAAsync(Hash, current)).Success);
        MFAVerifyResult result = await service.VerifyMFAAsync(Hash, older);

        Assert.False(result.Success);
    }

    /// <summary>The code that confirmed enrolment is spent: it cannot then complete a login.</summary>
    [Fact]
    public async Task Refuse_at_login_the_code_that_confirmed_enrolment()
    {
        Account account = await _accounts.CreateAsync(new Account
        {
            Username = "TOTPUSER",
            Email = "totp@example.com",
            Salt = new byte[16],
            Verifier = Encoding.UTF8.GetBytes("unused"),
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
        });
        _hashService.GetAccountIdAsync(Hash).Returns(account.Id);
        MFAService service = Service();
        Assert.True((await service.SetupMFAAsync(account, "Avalon")).Success);
        MFASetup pending = (await _mfa.FindByAccountIdAsync(account.Id))!;
        string code = new Totp(pending.Secret).ComputeTotp();

        Assert.True((await service.ConfirmMFAAsync(account.Id, code)).Success);
        MFAVerifyResult result = await service.VerifyMFAAsync(Hash, code);

        Assert.False(result.Success);
    }

    /// <summary>The stored step only moves forward, and only on a confirmed row.</summary>
    [Fact]
    public async Task Accept_each_totp_step_only_once_and_only_in_order()
    {
        (AccountId id, _) = await EnrolledAccountAsync();
        Guid row = (await _mfa.FindByAccountIdAsync(id))!.Id;

        Assert.True(await _mfa.TryAcceptTotpStepAsync(row, 100));
        Assert.False(await _mfa.TryAcceptTotpStepAsync(row, 100));
        Assert.False(await _mfa.TryAcceptTotpStepAsync(row, 99));
        Assert.True(await _mfa.TryAcceptTotpStepAsync(row, 101));
        Assert.Equal(101, (await _mfa.FindByAccountIdAsync(id))!.LastAcceptedTotpStep);
    }

    [Fact]
    public async Task Refuse_a_confirm_code_two_steps_from_now()
    {
        Account account = await _accounts.CreateAsync(new Account
        {
            Username = "TOTPUSER",
            Email = "totp@example.com",
            Salt = new byte[16],
            Verifier = Encoding.UTF8.GetBytes("unused"),
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
        });
        MFAService service = Service();
        Assert.True((await service.SetupMFAAsync(account, "Avalon")).Success);
        MFASetup pending = (await _mfa.FindByAccountIdAsync(account.Id))!;
        string code = new Totp(pending.Secret).ComputeTotp(DateTime.UtcNow.AddSeconds(-60));

        MFAConfirmResult result = await service.ConfirmMFAAsync(account.Id, code);

        Assert.False(result.Success);
        Assert.Equal(MFAOperationResult.InvalidCode, result.Status);
    }
}
