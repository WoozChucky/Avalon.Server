using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Exceptions;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

public class PersonalAccessTokenServiceShould
{
    private readonly IPersonalAccessTokenRepository _repo = Substitute.For<IPersonalAccessTokenRepository>();
    private readonly ISecureRandom _random = Substitute.For<ISecureRandom>();
    private static readonly DateTimeOffset FixedNow = DateTime.Parse("2026-04-22Z").ToUniversalTime();
    private readonly TimeProvider _time = new FakeTimeProvider(FixedNow);

    private PersonalAccessTokenService MakeSut() => new(_repo, _random, _time);

    [Fact]
    public async Task MintSelf_DefaultsRolesToCallerRoles_WhenRequestedRolesOmitted()
    {
        _random.GetBytes(32).Returns(Enumerable.Repeat((byte)0xAA, 32).ToArray());
        _repo.CreateAsync(Arg.Any<PersonalAccessToken>(), Arg.Any<CancellationToken>())
             .Returns(ci => ci.Arg<PersonalAccessToken>());

        var sut = MakeSut();
        var result = await sut.MintSelfAsync(
            callerId: new AccountId(7),
            callerRoles: AccountAccessLevel.Player | AccountAccessLevel.GameMaster,
            name: "ci",
            expiresAt: null,
            requestedRoles: null,
            CancellationToken.None);

        Assert.Equal(AccountAccessLevel.Player | AccountAccessLevel.GameMaster, result.Roles);
        Assert.StartsWith("avp_", result.Token);
        Assert.Equal(8, result.Prefix.Length);
    }

    [Fact]
    public async Task MintSelf_Throws_WhenRequestedRolesSupersetOfCaller()
    {
        var sut = MakeSut();
        await Assert.ThrowsAsync<BusinessException>(() => sut.MintSelfAsync(
            callerId: new AccountId(7),
            callerRoles: AccountAccessLevel.Player,
            name: "ci",
            expiresAt: null,
            requestedRoles: AccountAccessLevel.Admin,
            CancellationToken.None));
    }

    [Fact]
    public async Task MintAdmin_AcceptsRolesBeyondTargetButWithinCaller()
    {
        _random.GetBytes(32).Returns(new byte[32]);
        _repo.CreateAsync(Arg.Any<PersonalAccessToken>(), Arg.Any<CancellationToken>())
             .Returns(ci => ci.Arg<PersonalAccessToken>());

        var sut = MakeSut();
        var result = await sut.MintAdminAsync(
            callerRoles: AccountAccessLevel.Admin | AccountAccessLevel.GameMaster | AccountAccessLevel.Player,
            targetAccountId: new AccountId(7),
            name: "svc",
            expiresAt: null,
            requestedRoles: AccountAccessLevel.GameMaster,
            CancellationToken.None);

        Assert.Equal(AccountAccessLevel.GameMaster, result.Roles);
    }

    [Fact]
    public async Task MintAdmin_Throws_WhenRequestedRolesExceedCaller()
    {
        var sut = MakeSut();
        await Assert.ThrowsAsync<BusinessException>(() => sut.MintAdminAsync(
            callerRoles: AccountAccessLevel.Admin,
            targetAccountId: new AccountId(7),
            name: "svc",
            expiresAt: null,
            requestedRoles: AccountAccessLevel.Console,
            CancellationToken.None));
    }

    [Fact]
    public async Task Mint_Throws_WhenExpiryBeyondMaxLifetime()
    {
        var sut = MakeSut();
        var tooFar = FixedNow.UtcDateTime.AddDays(400);
        await Assert.ThrowsAsync<BusinessException>(() => sut.MintSelfAsync(
            callerId: new AccountId(7),
            callerRoles: AccountAccessLevel.Player,
            name: "ci",
            expiresAt: tooFar,
            requestedRoles: null,
            CancellationToken.None));
    }

    [Fact]
    public async Task FindByRawToken_HashesAndDelegates()
    {
        _repo.FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
             .Returns((PersonalAccessToken?)null);

        var sut = MakeSut();
        await sut.FindByRawTokenAsync("avp_abcdef", CancellationToken.None);

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes("avp_abcdef"));
        await _repo.Received(1).FindByHashAsync(
            Arg.Is<byte[]>(h => h.SequenceEqual(expected)),
            Arg.Any<CancellationToken>());
    }
}

internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
