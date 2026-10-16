using Avalon.Api.Testing;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.WorldPlaces;

/// <summary>The place and queue scenarios (#967) against the in-memory store, as CI runs them.</summary>
public sealed class MemoryWorldPlaceStoreShould
{
    [Theory]
    [MemberData(nameof(WorldPlaceScenarios.Names), MemberType = typeof(WorldPlaceScenarios))]
    public Task Keep_the_place_and_queue_rules(string scenario) =>
        WorldPlaceScenarios.RunAsync(scenario, (clock, config) => new MemoryWorldPlaceStore(clock, config), new PlaceIds(1, 1));
}
