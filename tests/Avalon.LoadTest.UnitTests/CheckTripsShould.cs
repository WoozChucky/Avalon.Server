using Avalon.LoadTest.Bots;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class CheckTripsShould
{
    /// <summary>
    /// Each row: how each member's trips end, in order (C completed, D died, F failed), whether each member may set out
    /// again after its last one, and the verdict once every member is done.
    /// </summary>
    public static TheoryData<string[], bool[], CheckTripsVerdict> Rows => new()
    {
        { ["C"], [false], CheckTripsVerdict.Passed },
        { ["DDC", "C"], [false, false], CheckTripsVerdict.Passed },
        { ["DD", "C"], [true, false], CheckTripsVerdict.Pending },
        { ["DDD", "C"], [false, false], CheckTripsVerdict.DiedThrice },
        { ["DF", "C"], [false, false], CheckTripsVerdict.Failed },
        { ["DDD", "F"], [false, false], CheckTripsVerdict.Failed },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Send_a_member_that_died_again_and_pass_once_every_member_completed_a_trip(string[] trips, bool[] goesAgain,
        CheckTripsVerdict verdict)
    {
        var check = new CheckTrips(trips.Length);
        for (int member = 0; member < trips.Length; member++)
        {
            foreach (char end in trips[member])
            {
                Assert.True(check.MaySetOut(member));
                check.Ended(member, end switch
                {
                    'C' => TripEnd.Completed,
                    'D' => TripEnd.Died,
                    _ => TripEnd.Failed,
                });
            }
        }

        Assert.Equal(goesAgain, Enumerable.Range(0, trips.Length).Select(check.MaySetOut));
        Assert.Equal(verdict, check.Verdict);
        Assert.Equal(verdict != CheckTripsVerdict.Pending, check.AllDone.IsCompleted);
    }
}
