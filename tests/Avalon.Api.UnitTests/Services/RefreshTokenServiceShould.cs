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
}
