using Avalon.Api.Contract;
using Avalon.Api.Worlds.Services;
using Xunit;
using DomainStatus = Avalon.Domain.Auth.WorldStatus;

namespace Avalon.Api.Worlds.UnitTests.Services;

/// <summary>The coarse population a public realm list shows in place of a head count.</summary>
public sealed class PopulationBandShould
{
    [Theory]
    [InlineData(DomainStatus.Online, 29, 90, null, PublicPopulation.Low)]
    [InlineData(DomainStatus.Online, 30, 90, null, PublicPopulation.Medium)]
    [InlineData(DomainStatus.Online, 59, 90, null, PublicPopulation.Medium)]
    [InlineData(DomainStatus.Online, 60, 90, null, PublicPopulation.High)]
    [InlineData(DomainStatus.Online, 89, 90, null, PublicPopulation.High)]
    [InlineData(DomainStatus.Full, 90, 90, null, PublicPopulation.Full)]
    [InlineData(DomainStatus.Online, 120, null, 90, PublicPopulation.High)]
    [InlineData(DomainStatus.Online, 10, null, 90, PublicPopulation.Low)]
    [InlineData(DomainStatus.Online, 0, null, 90, PublicPopulation.Low)]
    [InlineData(DomainStatus.Online, 10, null, null, null)]
    [InlineData(DomainStatus.Offline, 10, 90, null, null)]
    [InlineData(DomainStatus.Maintenance, 10, 90, 90, null)]
    [InlineData(DomainStatus.Online, 10, 90, 30, PublicPopulation.Low)]
    public void Band_a_head_count(DomainStatus status, int taken, int? limit, int? reference, PublicPopulation? expected) =>
        Assert.Equal(expected, PopulationBand.Of(status, taken, limit, reference));
}
