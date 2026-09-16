using System.Net;
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
            Substitute.For<IRefreshTokenService>(),
            Substitute.For<IDbTransactionRunner<AuthDbContext>>(),
            new AuthenticationConfig());
    }

    [Fact]
    public async Task Persist_the_account_and_its_device_exactly_once()
    {
        (RegisterResponse _, AccountId accountId) = await _service.Register(
            new RegisterRequest { Username = "newplayer", Password = "hunter2", Email = "new@avalon.monster" },
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
            Substitute.For<IRefreshTokenService>(),
            Substitute.For<IDbTransactionRunner<AuthDbContext>>(),
            new AuthenticationConfig());

        await service.Register(
            new RegisterRequest { Username = "navcheck", Password = "hunter2", Email = "nav@avalon.monster" },
            "test-agent",
            IPAddress.Loopback,
            CancellationToken.None);

        Device device = (Device)spy.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IDeviceRepository.CreateAsync))
            .GetArguments()[0]!;

        Assert.Null(device.Account);
        Assert.NotEqual(default, device.AccountId);
    }

    public void Dispose() => _database.Dispose();
}
