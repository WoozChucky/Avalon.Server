using System.Net;
using Avalon.Api.Exceptions;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// Registration driven end to end against a real database through the real repositories. Nothing
/// below the service is substituted, because the defect this covers only exists once the entities
/// cross two contexts.
/// </summary>
public class AccountRegistrationShould : IDisposable
{
    private readonly SqliteAuthDatabase _database = new();
    private readonly IDeviceRepository _devices;
    private readonly AccountService _service;

    public AccountRegistrationShould()
    {
        AccountRepository accounts = new(_database);
        _devices = new DeviceRepository(_database);

        _service = new AccountService(
            NullLoggerFactory.Instance,
            accounts,
            Substitute.For<Avalon.Api.Authentication.Jwt.IJwtUtils>(),
            Substitute.For<IMFAHashService>(),
            new MfaSetupRepository(_database),
            _devices,
            Substitute.For<IReplicatedCache>(),
            Substitute.For<ISecureRandom>(),
            Substitute.For<IDbTransactionRunner<AuthDbContext>>(),
            new AuthenticationConfig(),
            TestLogin.Password(new AccountRepository(_database), Substitute.For<IReplicatedCache>()),
            TestLogin.Reauthentication(new AccountRepository(_database), Substitute.For<IReplicatedCache>()));
    }

    [Fact]
    public async Task Persist_the_account_and_its_device_exactly_once()
    {
        (RegisterResponse _, AccountId accountId) = await _service.Register(
            new RegisterRequest { Username = "newplayer", Password = TestPasswords.Valid, Email = "new@avalon.monster" },
            "test-agent",
            IPAddress.Loopback,
            CancellationToken.None);

        await using AuthDbContext context = _database.CreateDbContext();
        Assert.Equal(1, await context.Accounts.CountAsync(a => a.Id == accountId));
        Assert.Equal(1, await context.Devices.CountAsync(d => d.AccountId == accountId));
    }

    /// <summary>
    /// The device's principal is named by its foreign key and by nothing else. A navigation would
    /// point at an account the repository already returned and therefore already detached, which
    /// is a second insert of a row that exists.
    /// </summary>
    [Fact]
    public async Task Name_the_account_by_foreign_key_and_not_by_navigation()
    {
        IDeviceRepository spy = Substitute.For<IDeviceRepository>();
        AccountService service = new(
            NullLoggerFactory.Instance,
            new AccountRepository(_database),
            Substitute.For<Avalon.Api.Authentication.Jwt.IJwtUtils>(),
            Substitute.For<IMFAHashService>(),
            new MfaSetupRepository(_database),
            spy,
            Substitute.For<IReplicatedCache>(),
            Substitute.For<ISecureRandom>(),
            Substitute.For<IDbTransactionRunner<AuthDbContext>>(),
            new AuthenticationConfig(),
            TestLogin.Password(new AccountRepository(_database), Substitute.For<IReplicatedCache>()),
            TestLogin.Reauthentication(new AccountRepository(_database), Substitute.For<IReplicatedCache>()));

        await service.Register(
            new RegisterRequest { Username = "navcheck", Password = TestPasswords.Valid, Email = "nav@avalon.monster" },
            "test-agent",
            IPAddress.Loopback,
            CancellationToken.None);

        Device device = (Device)spy.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IDeviceRepository.CreateAsync))
            .GetArguments()[0]!;

        Assert.Null(device.Account);
        Assert.NotEqual(default, device.AccountId);
    }

    /// <summary>
    /// #487: registration checks the username, then inserts. Two registrations of one name that
    /// both pass the check must not both insert: the unique index refuses the second, and its
    /// caller gets the same "taken" answer the check gives.
    /// </summary>
    [Fact]
    public async Task Answer_username_taken_to_the_loser_of_two_concurrent_registrations_of_one_name()
    {
        AccountRepository real = new(_database);
        var gate = new SemaphoreSlim(1, 1);
        int lookups = 0;
        var bothLookedUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Both registrations finish their username check before either inserts. The one SQLite
        // connection is not thread-safe, so the statements themselves take turns.
        IAccountRepository racing = Substitute.For<IAccountRepository>();
        racing.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            Account? found;
            await gate.WaitAsync();
            try { found = await real.FindByUserNameAsync(call.Arg<string>()); }
            finally { gate.Release(); }
            if (Interlocked.Increment(ref lookups) == 2) bothLookedUp.SetResult();
            await bothLookedUp.Task;
            return found;
        });
        racing.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await gate.WaitAsync();
            try { return await real.FindByEmailAsync(call.Arg<string>()); }
            finally { gate.Release(); }
        });
        racing.CreateAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await gate.WaitAsync();
            try { return await real.CreateAsync(call.Arg<Account>()); }
            finally { gate.Release(); }
        });

        AccountService service = new(
            NullLoggerFactory.Instance,
            racing,
            Substitute.For<Avalon.Api.Authentication.Jwt.IJwtUtils>(),
            Substitute.For<IMFAHashService>(),
            new MfaSetupRepository(_database),
            _devices,
            Substitute.For<IReplicatedCache>(),
            Substitute.For<ISecureRandom>(),
            Substitute.For<IDbTransactionRunner<AuthDbContext>>(),
            new AuthenticationConfig(),
            TestLogin.Password(real, Substitute.For<IReplicatedCache>()),
            TestLogin.Reauthentication(real, Substitute.For<IReplicatedCache>()));

        Task<(RegisterResponse, AccountId)> first = service.Register(
            new RegisterRequest { Username = "twin", Password = TestPasswords.Valid, Email = "one@avalon.monster" },
            "test-agent", IPAddress.Loopback, CancellationToken.None);
        Task<(RegisterResponse, AccountId)> second = service.Register(
            new RegisterRequest { Username = "TWIN", Password = TestPasswords.Valid, Email = "two@avalon.monster" },
            "test-agent", IPAddress.Loopback, CancellationToken.None);

        Task[] both = [first, second];
        try { await Task.WhenAll(both); } catch { /* inspected below */ }

        Assert.Single(both, t => t.IsCompletedSuccessfully);
        Task loser = Assert.Single(both, t => t.IsFaulted);
        BusinessException taken = Assert.IsType<BusinessException>(loser.Exception!.InnerException);
        Assert.Equal("Username already exists", taken.Message);

        await using AuthDbContext context = _database.CreateDbContext();
        Assert.Equal(1, await context.Accounts.CountAsync(a => a.Username == "TWIN"));
    }

    private static Task<(RegisterResponse, AccountId)> RegisterAsync(AccountService service, string username, string email) =>
        service.Register(new RegisterRequest { Username = username, Password = TestPasswords.Valid, Email = email },
            "test-agent", IPAddress.Loopback, CancellationToken.None);

    /// <summary>#503 follow-up: the service refuses what the request contract refuses, and does not rely on it.</summary>
    [Theory]
    [InlineData("x")]
    [InlineData("\u00FCser@avalon.monster")]
    [InlineData("   ")]
    public async Task Refuse_to_register_an_email_that_is_not_an_ascii_address(string email)
    {
        BusinessException refused = await Assert.ThrowsAsync<BusinessException>(() => RegisterAsync(_service, "player", email));

        Assert.Equal(AccountEmail.Requirement, refused.Message);
        await using AuthDbContext context = _database.CreateDbContext();
        Assert.Equal(0, await context.Accounts.CountAsync(a => a.Username == "PLAYER"));
    }

    /// <summary>#503: the lookup was exact and the email stored as sent, so these were two accounts.</summary>
    [Fact]
    public async Task Refuse_a_second_registration_whose_email_differs_only_in_case()
    {
        await RegisterAsync(_service, "first", "player@avalon.monster");

        BusinessException refused = await Assert.ThrowsAsync<BusinessException>(() =>
            RegisterAsync(_service, "second", " Player@Avalon.Monster"));

        Assert.Equal("Email already exists", refused.Message);
    }

    [Fact]
    public async Task Store_the_email_trimmed_and_lower_cased()
    {
        (RegisterResponse _, AccountId id) = await RegisterAsync(_service, "mixed", "  Mixed.Case@Avalon.MONSTER ");

        await using AuthDbContext context = _database.CreateDbContext();
        Assert.Equal("mixed.case@avalon.monster", (await context.Accounts.SingleAsync(a => a.Id == id)).Email);
    }

    /// <summary>
    /// #503: two registrations of one email that both pass the check. The unique index refuses the
    /// second insert, and its caller gets the check's answer.
    /// </summary>
    [Fact]
    public async Task Answer_email_taken_to_a_registration_that_loses_to_the_email_index()
    {
        AccountRepository real = new(_database);
        await RegisterAsync(_service, "first", "player@avalon.monster");
        // The check ran before the first registration's insert: it saw the email free.
        IAccountRepository stale = Substitute.For<IAccountRepository>();
        stale.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => real.FindByUserNameAsync(call.Arg<string>()));
        int emailLookups = 0;
        stale.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            ++emailLookups == 1 ? Task.FromResult<Account?>(null) : real.FindByEmailAsync(call.Arg<string>()));
        stale.CreateAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>())
            .Returns(call => real.CreateAsync(call.Arg<Account>()));
        AccountService service = new(
            NullLoggerFactory.Instance, stale, Substitute.For<Avalon.Api.Authentication.Jwt.IJwtUtils>(),
            Substitute.For<IMFAHashService>(), new MfaSetupRepository(_database), _devices,
            Substitute.For<IReplicatedCache>(), Substitute.For<ISecureRandom>(), Substitute.For<IDbTransactionRunner<AuthDbContext>>(), new AuthenticationConfig(),
            TestLogin.Password(real, Substitute.For<IReplicatedCache>()),
            TestLogin.Reauthentication(real, Substitute.For<IReplicatedCache>()));

        BusinessException refused = await Assert.ThrowsAsync<BusinessException>(() =>
            RegisterAsync(service, "second", "PLAYER@avalon.monster"));

        Assert.Equal("Email already exists", refused.Message);
        await using AuthDbContext context = _database.CreateDbContext();
        Assert.Equal(1, await context.Accounts.CountAsync(a => a.Email == "player@avalon.monster"));
    }

    public void Dispose() => _database.Dispose();
}
