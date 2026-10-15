using Avalon.World.GameAuth;

namespace Avalon.Server.World.UnitTests.GameAuth;

/// <summary>
/// Whether a session's end may be skipped once its lease ran out (#1051) turns on whether identity may hold the session
/// past that lease. A heartbeat whose outcome at identity is unknown keeps that possibility for the rest of the session:
/// a request identity still holds may renew it after a later heartbeat was applied.
/// </summary>
public sealed class RenewalLedgerShould
{
    [Theory]
    [InlineData("", false)]
    [InlineData("start", true)]
    [InlineData("start apply", false)]
    [InlineData("start apply start apply", false)]
    [InlineData("start fail", true)]
    [InlineData("start fail start apply", true)]
    [InlineData("start fail start apply start apply", true)]
    public void Settle_only_while_every_renewal_asked_for_was_applied(string steps, bool mayOutlast)
    {
        int state = RenewalLedger.Settled;
        foreach (string step in steps.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (step == "start") RenewalLedger.Started(ref state);
            else if (step == "apply") RenewalLedger.Applied(ref state);
            else RenewalLedger.Unresolved(ref state);
        }

        Assert.Equal(mayOutlast, RenewalLedger.MayOutlastLease(ref state));
    }
}
