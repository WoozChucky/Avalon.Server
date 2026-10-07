using Avalon.Api.Hosting.Worlds;
using Xunit;

namespace Avalon.Api.Hosting.UnitTests.Hosting;

/// <summary>A process provides what any of its services needs (#794, design section 2.2).</summary>
public sealed class ApiServiceNeedsShould
{
    [Fact]
    public void Provide_what_any_service_needs()
    {
        var union = ApiServiceNeeds.Union(
        [
            new ApiServiceNeeds(Redis: false, WorldDatabaseParts.Characters, AuthSchemaRole.Owner, WorldRoutes: false),
            new ApiServiceNeeds(Redis: true, WorldDatabaseParts.World, AuthSchemaRole.Reader, WorldRoutes: true),
        ]);

        Assert.Equal(new ApiServiceNeeds(true, WorldDatabaseParts.Both, AuthSchemaRole.Owner, true), union);
    }

    [Fact]
    public void Provide_nothing_and_read_the_auth_schema_for_no_service()
    {
        Assert.Equal(new ApiServiceNeeds(false, WorldDatabaseParts.None, AuthSchemaRole.Reader, false),
            ApiServiceNeeds.Union([]));
    }
}
