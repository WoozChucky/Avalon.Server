using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalon.Hosting.Extensions;
using Avalon.Hosting.PluginTypes;
using Avalon.Network.Packets.Abstractions.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Weikio.PluginFramework.Abstractions;
using Weikio.PluginFramework.Catalogs;

namespace Avalon.Hosting;

public static class AvalonHostBuilder
{
    public static async Task<HostApplicationBuilder> CreateHostAsync(string[] args, ComponentType component)
    {
        // workaround for https://github.com/dotnet/project-system/issues/3619
        var assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            // may be null in single file deployment
            Directory.SetCurrentDirectory(Path.GetDirectoryName(assemblyPath)!);
        }

        // init plugins if any
        IPluginCatalog pluginCatalog = new EmptyPluginCatalog();
        if (Directory.Exists("plugins"))
        {
            pluginCatalog = new FolderPluginCatalog("plugins", cfg =>
            {
                var sampleType = typeof(IConnectionLifetimeListener);
                var types = sampleType.Assembly.GetExportedTypes()
                    .Where(x => x.Namespace == sampleType.Namespace)
                    .ToArray();
                foreach (var type in types)
                {
                    cfg.Implements(type);
                }
            });
            await pluginCatalog.Initialize();
        }

        var host = new HostApplicationBuilder(args);
        host.Services.Configure<ConsoleLifetimeOptions>(opts => opts.SuppressStatusMessages = true);
        host.Services.AddCoreServices(pluginCatalog, host.Configuration, component);

        var serviceCollectionPluginTypes = pluginCatalog.GetPlugins()
            .FindAll(x => typeof(IServiceCollectionPlugin).IsAssignableFrom(x.Type))
            .Select(x => x.Type)
            .ToArray();
        foreach (var serviceCollectionPluginType in serviceCollectionPluginTypes)
        {
            try
            {
                var serviceCollectionPlugin =
                    (IServiceCollectionPlugin) Activator.CreateInstance(serviceCollectionPluginType)!;
                serviceCollectionPlugin.ModifyServiceCollection(host.Services);
            }
            catch (Exception e)
            {
                // The application will crash / not start if a service plugin throws an exception
                // this is by design. They shall only modify the services and not have side effects
                Console.WriteLine(e);
                throw;
            }
        }

        return host;
    }

    public static async Task RunAsync<T>(IHost host)
    {
        await Task.WhenAll(host.Services.GetRequiredService<IEnumerable<IPluginCatalog>>()
            .Select(x => x.Initialize()));
        var pluginExecutor = host.Services.GetRequiredService<PluginExecutor>();
        await pluginExecutor.ExecutePlugins<ISingletonPlugin>(x => x.InitializeAsync());
        await host.RunAsync();
    }
}
