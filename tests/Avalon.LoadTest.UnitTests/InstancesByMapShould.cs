using Avalon.LoadTest.Ramp;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class InstancesByMapShould
{
    [Fact]
    public void Tell_an_older_world_from_missing_data_and_read_a_map_type_never_ticked_as_none()
    {
        var both = new Dictionary<string, double> { ["Town"] = 7, ["Normal"] = 4.5 };

        // A world build without the instance updates: it reports its ticks (the count's anchor) and no update.
        Assert.Equal(InstancesReadout.NotExported, InstancesByMap.From(true, 0, both).Readout);
        // Nothing from the world, a failed query, an empty rate, or no tick to divide by says nothing about its build.
        Assert.Equal(InstancesByMap.Unknown, InstancesByMap.From(true, null, both));
        Assert.Equal(InstancesByMap.Unknown, InstancesByMap.From(false, null, both));
        Assert.Equal(InstancesByMap.Unknown, InstancesByMap.From(true, 2, null));
        Assert.Equal(InstancesByMap.Unknown, InstancesByMap.From(true, 2, new Dictionary<string, double>()));
        Assert.Equal(InstancesByMap.Unknown,
            InstancesByMap.From(true, 2, new Dictionary<string, double> { ["Town"] = double.PositiveInfinity }));

        Assert.Equal(new InstancesByMap(InstancesReadout.Reported, 7, 4.5), InstancesByMap.From(true, 2, both));
        // The town ticks as long as the world does; a forest never entered has no series to rate: none, not unknown.
        Assert.Equal(new InstancesByMap(InstancesReadout.Reported, 7, 0),
            InstancesByMap.From(true, 1, new Dictionary<string, double> { ["Town"] = 7 }));
    }
}
