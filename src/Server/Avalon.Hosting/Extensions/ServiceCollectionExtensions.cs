using System;
using System.Linq;
using System.Reflection;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Hosting.PluginTypes;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Weikio.PluginFramework.Abstractions;
using Weikio.PluginFramework.Microsoft.DependencyInjection;

namespace Avalon.Hosting.Extensions;

public static class ServiceCollectionExtensions
{
    private const string MessageTemplate = "[{Timestamp:HH:mm:ss.fff}][{ThreadId}][{Level:u3}]{Message:lj} " +
                                           "{NewLine:1}{Exception:1}";
    
    public static IServiceCollection AddCoreServices(this IServiceCollection services, IPluginCatalog pluginCatalog,
        IConfiguration configuration, ComponentType component)
    {
        services.AddCustomLogging(configuration);
        services.AddOptions<HostingConfiguration>().BindConfiguration("Hosting");
        services.AddSingleton<IPacketManager>(provider =>
        {
            var packetTypes = typeof(Packet).Assembly.GetExportedTypes().Where(type =>
            {
                var packetAttribute = type.GetCustomAttribute<PacketAttribute>();
                var hasPacketAttribute = packetAttribute != null;
                if (!hasPacketAttribute) return false;
                if (packetAttribute!.HandleOn != component) return false;
                return type.IsClass &&
                       type.GetFields(BindingFlags.Public | BindingFlags.Static)
                           .Any(field => field.FieldType == typeof(NetworkPacketType));
            }).ToArray();
            
            var handlerTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Where(x => !x.IsDynamic)
                .SelectMany(x => x.ExportedTypes)
                .Where(x =>
                    x.IsAssignableTo(typeof(IPacketHandlerNew)) &&
                    x is {IsClass: true, IsAbstract: false, IsInterface: false})
                .OrderBy(x => x.FullName)
                .ToArray();
            return ActivatorUtilities.CreateInstance<PacketManager>(provider, [packetTypes, handlerTypes]);
        });
        services.AddSingleton<IPacketReader, PacketReader>(provider =>
        {
            var packetTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Where(x => !x.IsDynamic)
                .SelectMany(x => x.ExportedTypes.Where(type =>
            {
                var packetAttribute = type.GetCustomAttribute<PacketAttribute>();
                var hasPacketAttribute = packetAttribute != null;
                if (!hasPacketAttribute) return false;
                if (packetAttribute!.HandleOn != component) return false;
                return type.IsClass &&
                       type.GetFields(BindingFlags.Public | BindingFlags.Static)
                           .Any(field => field.FieldType == typeof(NetworkPacketType));
            })).ToArray();
            
            return ActivatorUtilities.CreateInstance<PacketReader>(provider, [packetTypes]);
        });
        services.AddSingleton<PluginExecutor>();
        services.AddPluginFramework()
            .AddPluginCatalog(pluginCatalog)
            .AddPluginType<ISingletonPlugin>()
            .AddPluginType<IConnectionLifetimeListener>()
            .AddPluginType<IGameTickListener>()
            .AddPluginType<IPacketOperationListener>();
        
        return services;
    }
    
    private static IServiceCollection AddCustomLogging(this IServiceCollection services, IConfiguration configuration)
    {
        var config = new LoggerConfiguration();

        // add minimum log level for the instances
        config.MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Query", LogEventLevel.Warning);

        // add destructuring for entities
        config.Destructure.ToMaximumDepth(4)
            .Destructure.ToMaximumCollectionCount(10)
            .Destructure.ToMaximumStringLength(100);

        // add environment variable
        config.Enrich.WithEnvironmentUserName()
            .Enrich.WithMachineName();

        // add process information
        config.Enrich.WithProcessId()
            .Enrich.WithProcessName();
        
        config.Enrich.WithThreadId();

        // add assembly information
        // TODO: uncomment if needed
        //config.Enrich.WithAssemblyName() // {AssemblyName}
        //    .Enrich.WithAssemblyVersion(true) // {AssemblyVersion}
        //    .Enrich.WithAssemblyInformationalVersion();

        // add exception information
        config.Enrich.WithExceptionData();

        // sink to console
        config.WriteTo.Console(outputTemplate: MessageTemplate);

        config.ReadFrom.Configuration(configuration);

        // finally, create the logger
        services.AddLogging(x =>
        {
            x.ClearProviders();
            x.AddSerilog(config.CreateLogger());
        });
        return services;
    }
}
