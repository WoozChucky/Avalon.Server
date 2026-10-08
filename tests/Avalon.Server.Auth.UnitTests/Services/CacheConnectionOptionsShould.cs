using Avalon.Infrastructure;
using Avalon.Infrastructure.Configuration;
using StackExchange.Redis;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// Each API service signs in to Redis as its own ACL user (#803); a host that names none signs in as the
/// default user, as before.
/// </summary>
public class CacheConnectionOptionsShould
{
    [Fact]
    public void Sign_in_as_the_configured_user_and_as_the_default_user_without_one()
    {
        ConfigurationOptions named = ReplicatedCache.ConnectionOptions(
            new CacheConfiguration { Host = "redis:6379", Username = "api-identity", Password = "pw" });
        ConfigurationOptions unnamed = ReplicatedCache.ConnectionOptions(
            new CacheConfiguration { Host = "redis:6379", Password = "pw" });

        Assert.Equal("api-identity", named.User);
        Assert.Equal("pw", named.Password);
        Assert.Null(unnamed.User);
        Assert.Equal("pw", unnamed.Password);
    }
}
