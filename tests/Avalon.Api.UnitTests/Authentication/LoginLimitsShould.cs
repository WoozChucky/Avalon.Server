using Avalon.Api;
using Avalon.Api.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Avalon.Api.UnitTests.Authentication;

/// <summary>
/// The api's login limits (#478) are bound directly, not through validated options, so startup
/// checks them itself: a limit below one would refuse every login, or never count one, and the
/// Auth server refuses the same values under its own section.
/// </summary>
public class LoginLimitsShould
{
    private static void StartWith(string setting, string value)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"Application:Authentication:{setting}"] = value,
            })
            .Build();
        ApplicationConfig config = ApiConfiguration.Bind(configuration);

        new ServiceCollection().AddInfrastructure(config);
    }

    [Theory]
    [InlineData(nameof(AuthenticationConfig.MaxFailedLoginAttempts))]
    [InlineData(nameof(AuthenticationConfig.LockoutDurationMinutes))]
    [InlineData(nameof(AuthenticationConfig.MaxFailedLoginsPerSource))]
    [InlineData(nameof(AuthenticationConfig.FailedLoginSourceWindowMinutes))]
    [InlineData(nameof(AuthenticationConfig.MaxFailedMfaAttempts))]
    public void Refuse_to_start_with_a_limit_below_one(string setting)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StartWith(setting, "0"));

        Assert.Contains($"Application:Authentication:{setting}", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_with_the_defaults()
    {
        StartWith(nameof(AuthenticationConfig.Issuer), "Avalon");
    }
}
