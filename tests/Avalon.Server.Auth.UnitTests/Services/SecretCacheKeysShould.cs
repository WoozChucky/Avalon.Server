using System.Security.Cryptography;
using System.Text;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Configuration;
using StackExchange.Redis;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// StackExchange.Redis writes the command and key name into its exception messages, and those
/// exceptions are logged, so a key named after a secret puts the secret in the logs on a timeout
/// (#535). Secret-bearing keys are named by the secret's SHA-256, and the connection is told to
/// leave command detail out of its exceptions.
/// </summary>
public class SecretCacheKeysShould
{
    private static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [Fact]
    public void Name_a_world_entry_key_by_its_hash_not_by_the_key()
    {
        string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        string name = CacheKeys.WorldKey(7, secret);

        Assert.Equal($"world:7:keys:{Sha256Hex(secret)}", name);
        Assert.DoesNotContain(secret, name);
    }

    [Fact]
    public void Name_an_mfa_login_hash_key_by_its_hash_not_by_the_hash()
    {
        string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        string name = CacheKeys.MfaReverseHash(secret);

        Assert.Equal($"auth:mfa:hash:{Sha256Hex(secret)}", name);
        Assert.DoesNotContain(secret, name);
    }

    [Fact]
    public void Keep_command_and_key_detail_out_of_redis_exceptions()
    {
        ConfigurationOptions options = ReplicatedCache.ConnectionOptions(
            new CacheConfiguration { Host = "redis:6379", Password = "pw" });

        Assert.False(options.IncludeDetailInExceptions);
        Assert.Equal("pw", options.Password);
        Assert.Contains(options.EndPoints, e => e.ToString()!.Contains("redis", StringComparison.Ordinal));
    }
}
