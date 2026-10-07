using Avalon.World;
using Avalon.World.GameAuth;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class GameContextRevocationShould
{
    [Fact]
    public void A_delayed_notice_cannot_disconnect_a_different_context_or_grant_authority()
    {
        using var current = WorldAdmissionConnection.Create();
        GameSessionLease lease = WorldAdmissionConnection.Lease(); current.PublishAdmission(lease);
        Assert.Equal(0, WorldServer.NotifyGameContextRevocation([current], "42|" + Guid.NewGuid().ToString("N")));
        Assert.Equal(0, WorldServer.NotifyGameContextRevocation([current], "43|" + lease.GameContextId.ToString("N")));
        Assert.Equal(0, WorldServer.NotifyGameContextRevocation([current], "042|" + lease.GameContextId.ToString("N")));
        Assert.Equal(0, WorldServer.NotifyGameContextRevocation([current], new string('a', 10000)));
        Assert.Equal(1, WorldServer.NotifyGameContextRevocation([current], "42|" + lease.GameContextId.ToString("N")));
        Assert.True(lease.IsActive);
        Assert.False(current.IsGameplayAuthorized); // A notification does not bypass the protocol handshake.
        Assert.Equal(1, WorldServer.NotifyGameContextRevocation([current], "42|" + Guid.Empty.ToString("N")));
    }
}
