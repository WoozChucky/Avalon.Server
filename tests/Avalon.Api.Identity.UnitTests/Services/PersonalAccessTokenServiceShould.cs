using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Hosting.Exceptions;
using Avalon.Api.Identity.Services;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Services;

public class PersonalAccessTokenServiceShould
{
    private readonly IPersonalAccessTokenRepository _repo = Substitute.For<IPersonalAccessTokenRepository>();
    private readonly ISecureRandom _random = Substitute.For<ISecureRandom>();
    private static readonly DateTimeOffset s_fixedNow = DateTime.Parse("2026-04-22Z").ToUniversalTime();
    private readonly TimeProvider _time = new FakeTimeProvider(s_fixedNow);

    private PersonalAccessTokenService MakeSut() => new(_repo, _random, _time);

    /// <summary>A current-password check that passed a moment ago.</summary>
    private static readonly Reauthenticated s_proof = new(new AccountId(7), 0);

    [Fact]
    public async Task MintSelf_DefaultsRolesToCallerRoles_WhenRequestedRolesOmitted()
    {
        _random.GetBytes(32).Returns(Enumerable.Repeat((byte)0xAA, 32).ToArray());
        _repo.CreateUnlessCredentialsChangedAsync(Arg.Any<PersonalAccessToken>(), Arg.Any<AccountId>(),
                 Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(ci => ci.Arg<PersonalAccessToken>());

        PersonalAccessTokenService sut = MakeSut();
        MintResult result = await sut.MintSelfAsync(
            callerId: new AccountId(7),
            callerRoles: AccountAccessLevel.Player | AccountAccessLevel.GameMaster,
            name: "ci",
            expiresAt: null,
            requestedRoles: null,
            s_proof,
            CancellationToken.None);

        Assert.Equal(AccountAccessLevel.Player | AccountAccessLevel.GameMaster, result.Roles);
        Assert.StartsWith("avp_", result.Token);
        Assert.Equal(8, result.Prefix.Length);
    }

    [Fact]
    public async Task MintSelf_Throws_WhenRequestedRolesSupersetOfCaller()
    {
        PersonalAccessTokenService sut = MakeSut();
        await Assert.ThrowsAsync<BusinessException>(() => sut.MintSelfAsync(
            callerId: new AccountId(7),
            callerRoles: AccountAccessLevel.Player,
            name: "ci",
            expiresAt: null,
            requestedRoles: AccountAccessLevel.Admin,
            s_proof,
            CancellationToken.None));
    }

    [Fact]
    public async Task MintAdmin_AcceptsRolesBeyondTargetButWithinCaller()
    {
        _random.GetBytes(32).Returns(new byte[32]);
        _repo.CreateUnlessCredentialsChangedAsync(Arg.Any<PersonalAccessToken>(), Arg.Any<AccountId>(),
                 Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(ci => ci.Arg<PersonalAccessToken>());

        PersonalAccessTokenService sut = MakeSut();
        MintResult result = await sut.MintAdminAsync(
            callerRoles: AccountAccessLevel.Admin | AccountAccessLevel.GameMaster | AccountAccessLevel.Player,
            targetAccountId: new AccountId(7),
            name: "svc",
            expiresAt: null,
            requestedRoles: AccountAccessLevel.GameMaster,
            s_proof,
            CancellationToken.None);

        Assert.Equal(AccountAccessLevel.GameMaster, result.Roles);
    }

    [Fact]
    public async Task MintAdmin_Throws_WhenRequestedRolesExceedCaller()
    {
        PersonalAccessTokenService sut = MakeSut();
        await Assert.ThrowsAsync<BusinessException>(() => sut.MintAdminAsync(
            callerRoles: AccountAccessLevel.Admin,
            targetAccountId: new AccountId(7),
            name: "svc",
            expiresAt: null,
            requestedRoles: AccountAccessLevel.Console,
            s_proof,
            CancellationToken.None));
    }

    [Fact]
    public async Task Mint_Throws_WhenExpiryBeyondMaxLifetime()
    {
        PersonalAccessTokenService sut = MakeSut();
        DateTime tooFar = s_fixedNow.UtcDateTime.AddDays(400);
        await Assert.ThrowsAsync<BusinessException>(() => sut.MintSelfAsync(
            callerId: new AccountId(7),
            callerRoles: AccountAccessLevel.Player,
            name: "ci",
            expiresAt: tooFar,
            requestedRoles: null,
            s_proof,
            CancellationToken.None));
    }

    [Fact]
    public async Task FindByRawToken_HashesAndDelegates()
    {
        _repo.FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
             .Returns((PersonalAccessToken?)null);

        PersonalAccessTokenService sut = MakeSut();
        await sut.FindByRawTokenAsync("avp_abcdef", CancellationToken.None);

        byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes("avp_abcdef"));
        await _repo.Received(1).FindByHashAsync(
            Arg.Is<byte[]>(h => h.SequenceEqual(expected)),
            Arg.Any<CancellationToken>());
    }
}

internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
