using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalon.Common.Converters;
using Avalon.Hosting.Extensions;
using Avalon.Network.Packets.Abstractions.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Avalon.Hosting;

public static class AvalonHostBuilder
{
    public static Task<HostApplicationBuilder> CreateHostAsync(string[] args, ComponentType component)
    {
        // workaround for https://github.com/dotnet/project-system/issues/3619
        string? assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            // may be null in single file deployment
            Directory.SetCurrentDirectory(Path.GetDirectoryName(assemblyPath)!);
        }

        HostApplicationBuilder host = new(args);
        host.Services.Configure<ConsoleLifetimeOptions>(opts => opts.SuppressStatusMessages = true);
        host.Services.AddCoreServices(host.Configuration, component);

        host.Services.AddSingleton<JsonSerializerOptions>(_ =>
        {
            JsonSerializerOptions jsonOptions = new();
            jsonOptions.Converters.Add(new ValueObjectJsonConverterFactory());
            return jsonOptions;
        });

        return Task.FromResult(host);
    }

    public static async Task RunAsync<T>(IHost host, CancellationToken cancellationToken = default) =>
        await host.RunAsync(cancellationToken);
}
