using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Configuration;
using Avalon.Server.Auth.Handlers;
using Avalon.Infrastructure.Login;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OtpNet;
using ProtoBuf;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// An account has one MFA row (#470). With two, login read whichever one the database returned
/// first, and a <see cref="MfaSetupStatus.Setup"/> row let a password-only login through an
/// enrolled account. These run the real repository over a real relational schema, because the
/// guarantee lives in the schema and in the SQL, not in the service.
/// </summary>
public sealed class MfaSetupRowShould : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private readonly AuthSqlite _database = new();
    private readonly AccountRepository _accounts;
    private readonly MfaSetupRepository _mfa;
    private readonly IMFAHashService _hashService = Substitute.For<IMFAHashService>();
    private readonly ISecureRandom _random = Substitute.For<ISecureRandom>();

    public MfaSetupRowShould()
    {
        _accounts = new AccountRepository(_database);
        _mfa = new MfaSetupRepository(_database);
        _random.GetBytes(Arg.Any<int>()).Returns(ci => RandomNumberGenerator.GetBytes(ci.Arg<int>()));
        _hashService.GenerateHashAsync(Arg.Any<Account>()).Returns("mfa-hash");
    }

    public void Dispose() => _database.Dispose();

    /// <summary>
    /// SQLite returns rows for one account in insertion order, so the two orders are the two rows a
    /// lookup could return. Whatever rows end up stored, a password-only login must be refused with
    /// MFA_REQUIRED exactly when the account holds a confirmed row.
    /// </summary>
    [Theory]
    [InlineData(MfaSetupStatus.Confirmed, MfaSetupStatus.Setup)]
    [InlineData(MfaSetupStatus.Setup, MfaSetupStatus.Confirmed)]
    public async Task Require_mfa_at_login_whenever_the_account_holds_a_confirmed_row(
        MfaSetupStatus first, MfaSetupStatus second)
    {
        Account account = await _accounts.CreateAsync(NewAccount());

        await TryInsertAsync(Row(account.Id, first));
        await TryInsertAsync(Row(account.Id, second));

        bool enrolled;
        await using (AuthDbContext context = _database.CreateDbContext())
        {
            enrolled = await context.MfaSetups.AnyAsync(
                m => m.AccountId == account.Id && m.Status == MfaSetupStatus.Confirmed);
        }

        AuthResult? result = await LogInWithPasswordOnlyAsync();

        Assert.Equal(enrolled ? AuthResult.MFA_REQUIRED : AuthResult.SUCCESS, result);
    }

    /// <summary>
    /// Both setups read "no row" before either writes, the shape of two concurrent /mfa/setup calls.
    /// </summary>
    [Fact]
    public async Task Leave_one_row_when_two_setups_race()
    {
        Account account = await _accounts.CreateAsync(NewAccount());
        var gated = new GatedMfaSetupRepository(_mfa);
        MFAService service = Service(gated);

        Task<MFASetupResult> first = service.SetupMFAAsync(account, "Avalon");
        await gated.FirstReadDone.WaitAsync(Bound);

        MFASetupResult second = await service.SetupMFAAsync(account, "Avalon").WaitAsync(Bound);
        gated.Release();
        MFASetupResult firstResult = await first.WaitAsync(Bound);

        Assert.True(firstResult.Success);
        Assert.True(second.Success);

        await using AuthDbContext context = _database.CreateDbContext();
        List<MFASetup> rows = await context.MfaSetups.Where(m => m.AccountId == account.Id).ToListAsync();
        MFASetup row = Assert.Single(rows);
        Assert.Equal(MfaSetupStatus.Setup, row.Status);
    }

    /// <summary>
    /// Both confirms read the row while it is still in Setup and both verify the same code. The
    /// codes shown by the confirm that succeeds are the ones that must be stored: a second write
    /// must not replace them with codes nobody was shown.
    /// </summary>
    [Fact]
    public async Task Keep_the_recovery_codes_that_were_shown_when_a_confirm_is_submitted_twice()
    {
        Account account = await _accounts.CreateAsync(NewAccount());
        Assert.True((await Service(_mfa).SetupMFAAsync(account, "Avalon")).Success);
        MFASetup pending = (await _mfa.FindByAccountIdAsync(account.Id))!;
        string code = new Totp(pending.Secret).ComputeTotp();

        var gated = new GatedMfaSetupRepository(_mfa);
        MFAService service = Service(gated);

        Task<MFAConfirmResult> first = service.ConfirmMFAAsync(account.Id, code);
        await gated.FirstReadDone.WaitAsync(Bound);

        MFAConfirmResult second = await service.ConfirmMFAAsync(account.Id, code).WaitAsync(Bound);
        gated.Release();
        MFAConfirmResult firstResult = await first.WaitAsync(Bound);

        MFAConfirmResult winner = Assert.Single(new[] { firstResult, second }, r => r.Success);

        MFASetup stored = (await _mfa.FindByAccountIdAsync(account.Id))!;
        Assert.Equal(MfaSetupStatus.Confirmed, stored.Status);
        Assert.True(MFARecoveryCodes.Matches(winner.RecoveryCodes![0], stored.RecoveryCode1));
        Assert.True(MFARecoveryCodes.Matches(winner.RecoveryCodes![1], stored.RecoveryCode2));
        Assert.True(MFARecoveryCodes.Matches(winner.RecoveryCodes![2], stored.RecoveryCode3));
    }

    private MFAService Service(IMfaSetupRepository repository) =>
        new(NullLoggerFactory.Instance, repository, _hashService, _random, Substitute.For<Avalon.Infrastructure.IReplicatedCache>());

    private async Task TryInsertAsync(MFASetup row)
    {
        try
        {
            await _mfa.CreateAsync(row);
        }
        catch (DbUpdateException)
        {
            // The schema refused it. What matters is the login outcome for whatever was stored.
        }
    }

    private async Task<AuthResult?> LogInWithPasswordOnlyAsync()
    {
        IAuthConnection connection = Substitute.For<IAuthConnection>();
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.RemoteEndPoint.Returns("127.0.0.1:12345");

        var handler = new CAuthHandler(NullLoggerFactory.Instance, _accounts, Substitute.For<IReplicatedCache>(),
            _hashService, _mfa, Options.Create(new AuthConfiguration { MaxFailedLoginAttempts = 5 }), new BCryptPasswordVerifier());

        await handler.ExecuteAsync(new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "mfauser", Password = "correct_password" },
            Connection = connection,
        });

        NetworkPacket? sent = connection.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IAuthConnection.Send))
            .Select(c => c.GetArguments()[0])
            .OfType<NetworkPacket>()
            .LastOrDefault();
        if (sent == null) return null;

        using var stream = new MemoryStream(sent.Payload);
        return Serializer.Deserialize<SAuthResultPacket>(stream).Result;
    }

    private static MFASetup Row(AccountId accountId, MfaSetupStatus status) => new()
    {
        AccountId = accountId,
        Secret = KeyGeneration.GenerateRandomKey(32),
        RecoveryCode1 = [],
        RecoveryCode2 = [],
        RecoveryCode3 = [],
        Status = status,
        CreatedAt = DateTime.UtcNow,
        ConfirmedAt = status == MfaSetupStatus.Confirmed ? DateTime.UtcNow : DateTime.MinValue,
    };

    private static Account NewAccount() => new()
    {
        Username = "MFAUSER",
        Email = "mfauser@example.com",
        Salt = new byte[16],
        Verifier = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword("correct_password")),
        SessionKey = [],
        LastIp = "127.0.0.1",
        LastAttemptIp = string.Empty,
        MuteBy = string.Empty,
        MuteReason = string.Empty,
        JoinDate = DateTime.UtcNow,
        LastLogin = DateTime.UtcNow,
    };

    /// <summary>
    /// Forwards to the real repository, but holds the first account lookup after it has read, until
    /// released. That puts a second caller's whole operation between the first caller's read and
    /// its write, one at a time, so the single SQLite connection is never used concurrently.
    /// </summary>
    private sealed class GatedMfaSetupRepository(IMfaSetupRepository inner) : IMfaSetupRepository
    {
        private readonly TaskCompletionSource _firstReadDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;

        public Task FirstReadDone => _firstReadDone.Task;

        public void Release() => _release.TrySetResult();

        public async Task<MFASetup?> FindByAccountIdAsync(AccountId accountId, CancellationToken cancellationToken = default)
        {
            MFASetup? row = await inner.FindByAccountIdAsync(accountId, cancellationToken);
            if (Interlocked.Increment(ref _reads) == 1)
            {
                _firstReadDone.TrySetResult();
                await _release.Task.WaitAsync(Bound, cancellationToken);
            }

            return row;
        }

        public Task<PagedResult<MFASetup>> PaginateAsync(EntityPaginateFilter<MFASetup> filter, bool track = false,
            CancellationToken cancellationToken = default) => inner.PaginateAsync(filter, track, cancellationToken);

        public Task<List<MFASetup>> FindAllAsync(bool track = false, CancellationToken cancellationToken = default) =>
            inner.FindAllAsync(track, cancellationToken);

        public Task<MFASetup?> FindByIdAsync(Guid id, bool track = false, CancellationToken cancellationToken = default) =>
            inner.FindByIdAsync(id, track, cancellationToken);

        public Task<List<MFASetup>> FindByAsync(Expression<Func<MFASetup, bool>> predicate,
            CancellationToken cancellationToken = default) => inner.FindByAsync(predicate, cancellationToken);

        public Task<MFASetup> CreateAsync(MFASetup entity, CancellationToken cancellationToken = default) =>
            inner.CreateAsync(entity, cancellationToken);

        public Task<List<MFASetup>> CreateAsync(List<MFASetup> entities, CancellationToken cancellationToken = default) =>
            inner.CreateAsync(entities, cancellationToken);

        public Task<MFASetup> UpdateAsync(MFASetup entity, CancellationToken cancellationToken = default) =>
            inner.UpdateAsync(entity, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(id, cancellationToken);

        public Task<bool> UpsertPendingAsync(MFASetup pending, CancellationToken cancellationToken = default) =>
            inner.UpsertPendingAsync(pending, cancellationToken);

        public Task<bool> ResetConfirmedAsync(Guid id, AccountId accountId, DateTime now,
            CancellationToken cancellationToken = default) =>
            inner.ResetConfirmedAsync(id, accountId, now, cancellationToken);

        public Task<bool> TryConfirmAsync(Guid id, byte[] verifiedSecret, byte[] recoveryCode1, byte[] recoveryCode2,
            byte[] recoveryCode3, DateTime confirmedAt, long acceptedTotpStep, CancellationToken cancellationToken = default) =>
            inner.TryConfirmAsync(id, verifiedSecret, recoveryCode1, recoveryCode2, recoveryCode3, confirmedAt,
                acceptedTotpStep, cancellationToken);

        public Task<bool> TryAcceptTotpStepAsync(Guid id, long step, CancellationToken cancellationToken = default) =>
            inner.TryAcceptTotpStepAsync(id, step, cancellationToken);

        public Task DeletePendingAsync(Guid id, byte[] secret, CancellationToken cancellationToken = default) =>
            inner.DeletePendingAsync(id, secret, cancellationToken);
    }

    /// <summary>
    /// The Auth model over one in-memory SQLite connection held open for the fixture's life; the
    /// connection is the database, and every context the repositories open is handed it.
    /// </summary>
    private sealed class AuthSqlite : IDbContextFactory<AuthDbContext>, IDisposable
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");
        private readonly DbContextOptions<AuthDbContext> _options;

        public AuthSqlite()
        {
            _connection.Open();
            _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;
            using AuthDbContext context = CreateDbContext();
            context.Database.EnsureCreated();
        }

        public AuthDbContext CreateDbContext() => new(_options);

        public Task<AuthDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());

        public void Dispose() => _connection.Dispose();
    }
}
