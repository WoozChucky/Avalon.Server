using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

public class RefreshTokenServiceShould
{
    private readonly IRefreshTokenRepository _repo = Substitute.For<IRefreshTokenRepository>();
    private readonly ISecureRandom _random = Substitute.For<ISecureRandom>();

    public RefreshTokenServiceShould()
    {
        _random.GetBytes(Arg.Any<int>()).Returns(ci => new byte[ci.Arg<int>()]);
        _repo.CreateIfCredentialsCurrentAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    [Fact]
    public async Task Issue_a_time_ordered_family_id()
    {
        var service = new RefreshTokenService(_repo, _random, TimeProvider.System);

        var result = await service.IssueAsync(new AccountId(1L), 0);

        // The family id is not a secret; v7 keeps the (AccountId, FamilyId) index append-friendly.
        Assert.Equal(7, result.FamilyId.Version);
        await _repo.Received(1).CreateIfCredentialsCurrentAsync(
            Arg.Is<RefreshToken>(t => t.FamilyId == result.FamilyId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mark_a_family_with_its_client_and_device_name()
    {
        var service = new RefreshTokenService(_repo, _random, TimeProvider.System);

        await service.IssueLauncherAsync(new AccountId(1L), 0, "MOTHERSHIP");

        await _repo.Received(1).CreateIfCredentialsCurrentAsync(
            Arg.Is<RefreshToken>(t => t.Client == SessionClient.Launcher && t.DeviceName == "MOTHERSHIP"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuse_a_token_of_the_other_client_without_revoking_it()
    {
        var web = new RefreshToken
        {
            AccountId = new AccountId(1L), FamilyId = Guid.NewGuid(), Client = SessionClient.Web,
            Revoked = true, ExpiresAt = DateTime.UtcNow.AddDays(1),
        };
        _repo.FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(web);
        var service = new RefreshTokenService(_repo, _random, TimeProvider.System);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.RotateLauncherAsync("raw", RefreshCaller.From(null, "ua")));

        // Unknown to this client: no theft handling, so a launcher call cannot end the website's session.
        await _repo.DidNotReceiveWithAnyArgs().RevokeFamilyAsync(default);
        await _repo.DidNotReceiveWithAnyArgs().RotateAsync(default!, default!, default);
    }

    [Fact]
    public async Task Carry_the_client_and_device_name_to_the_rotated_child()
    {
        var parent = new RefreshToken
        {
            AccountId = new AccountId(1L), FamilyId = Guid.NewGuid(), Client = SessionClient.Launcher,
            DeviceName = "MOTHERSHIP", ExpiresAt = DateTime.UtcNow.AddDays(1),
        };
        _repo.FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(parent);
        _repo.RotateAsync(Arg.Any<RefreshToken>(), Arg.Any<RefreshToken>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(RefreshRotation.Rotated);
        var service = new RefreshTokenService(_repo, _random, TimeProvider.System);

        await service.RotateLauncherAsync("raw", RefreshCaller.From(null, "ua"));

        await _repo.Received(1).RotateAsync(parent,
            Arg.Is<RefreshToken>(c => c.Client == SessionClient.Launcher && c.DeviceName == "MOTHERSHIP"),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revoke_the_whole_launcher_session_a_token_belongs_to()
    {
        var family = Guid.NewGuid();
        _repo.FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new RefreshToken { AccountId = new AccountId(1L), FamilyId = family, Client = SessionClient.Launcher });
        var service = new RefreshTokenService(_repo, _random, TimeProvider.System);

        await service.RevokeLauncherSessionAsync("raw");

        await _repo.Received(1).RevokeFamilyAsync(family, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Leave_a_website_session_alone_when_its_token_is_presented_to_the_launcher_sign_out()
    {
        _repo.FindByHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new RefreshToken { AccountId = new AccountId(1L), FamilyId = Guid.NewGuid(), Client = SessionClient.Web });
        var service = new RefreshTokenService(_repo, _random, TimeProvider.System);

        await service.RevokeLauncherSessionAsync("web-token");

        await _repo.DidNotReceiveWithAnyArgs().RevokeFamilyAsync(default);
    }
}
