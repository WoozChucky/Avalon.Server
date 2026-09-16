using System;
using System.IO;
using System.Threading.Tasks;
using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Avalon.Shared.UnitTests.Hosting;

/// <summary>
/// Scope validation is what turns a captured scoped service from a silent defect into a startup
/// failure. Each of these fails if it is switched back off.
/// </summary>
public class AvalonServiceProviderShould
{
    [Fact]
    public void Reject_resolving_a_scoped_service_from_the_root_provider()
    {
        ServiceCollection services = new();
        services.AddScoped<ScopedDependency>();

        ServiceProvider provider = services.BuildServiceProvider(AvalonServiceProvider.Options);

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ScopedDependency>());
    }

    [Fact]
    public void Reject_a_singleton_that_captures_a_scoped_service_when_the_container_is_built()
    {
        ServiceCollection services = new();
        services.AddScoped<ScopedDependency>();
        services.AddSingleton<SingletonHolder>();

        // ValidateOnBuild, not ValidateScopes: the capture is reported at build rather than at the
        // first resolve, which is the difference between a failed startup and a defect in production.
        Assert.Throws<AggregateException>(() => services.BuildServiceProvider(AvalonServiceProvider.Options));
    }

    [Fact]
    public async Task Validate_the_container_the_host_builder_produces()
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.Auth);
            builder.Services.AddScoped<ScopedDependency>();
            builder.Services.AddSingleton<SingletonHolder>();

            Assert.ThrowsAny<Exception>(() => builder.Build());
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }

    private sealed class ScopedDependency;

    private sealed class SingletonHolder(ScopedDependency dependency)
    {
        public ScopedDependency Dependency { get; } = dependency;
    }
}
