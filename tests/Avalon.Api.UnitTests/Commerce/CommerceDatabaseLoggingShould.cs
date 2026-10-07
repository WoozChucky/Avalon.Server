using Avalon.Api.Commerce;
using Avalon.Configuration;
using Avalon.Database.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Commerce;

public sealed class CommerceDatabaseLoggingShould
{
    [Fact]
    public void Isolated_commerce_disables_sensitive_EF_logging_even_in_Development()
    {
        var host = Substitute.For<IHostEnvironment>(); host.EnvironmentName = Environments.Development;
        var services = new ServiceCollection();
        services.AddSingleton(host);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddAvalonDatabases();
        services.AddCommerce();
        services.AddSingleton<IOptions<CommerceConfiguration>>(Options.Create(new CommerceConfiguration { Enabled = true }));
        using var provider = services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<IOptions<DatabaseConfiguration>>().Value.EnableSensitiveDataLogging);
    }
}
