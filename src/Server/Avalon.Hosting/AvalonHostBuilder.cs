using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalon.Hosting.Extensions;
using Avalon.Hosting.PluginTypes;
using Avalon.Network.Packets.Abstractions.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Avalon.Hosting;

public static class AvalonHostBuilder
{
    public static Task<HostApplicationBuilder> CreateHostAsync(string[] args, ComponentType component)
    {
        // workaround for https://github.com/dotnet/project-system/issues/3619
        var assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            // may be null in single file deployment
            Directory.SetCurrentDirectory(Path.GetDirectoryName(assemblyPath)!);
        }

        var host = new HostApplicationBuilder(args);
        host.Services.Configure<ConsoleLifetimeOptions>(opts => opts.SuppressStatusMessages = true);
        host.Services.AddCoreServices(host.Configuration, component);

        return Task.FromResult(host);
    }

    public static async Task RunAsync<T>(IHost host)
    {
        await host.RunAsync();
    }
}
