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

    /// <summary>
    /// #478 review: both hosts spend the same Redis budgets but configure their limits apart, so
    /// each logs its five limits at startup, at Information, for the two logs to be compared.
    /// </summary>
    [Fact]
    public void Log_all_five_limits_and_their_section_at_information()
    {
        var logger = new CapturingLogger();
        var limits = new AuthenticationConfig
        {
            MaxFailedLoginAttempts = 6, LockoutDurationMinutes = 16, MaxFailedLoginsPerSource = 11,
            FailedLoginSourceWindowMinutes = 17, MaxFailedMfaAttempts = 4,
        };

        Avalon.Infrastructure.Login.LoginLimitsValidation.LogAtStartup(logger, limits, "Application:Authentication");

        var (level, message) = Assert.Single(logger.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, level);
        foreach (string expected in new[]
                 {
                     "Application:Authentication", "MaxFailedLoginAttempts=6", "LockoutDurationMinutes=16",
                     "MaxFailedLoginsPerSource=11", "FailedLoginSourceWindowMinutes=17", "MaxFailedMfaAttempts=4",
                 })
            Assert.Contains(expected, message, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<(Microsoft.Extensions.Logging.LogLevel, string)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
