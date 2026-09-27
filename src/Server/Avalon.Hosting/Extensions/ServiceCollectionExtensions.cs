using System;
using System.Linq;
using System.Reflection;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Avalon.Hosting.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>Every EF Core category, <c>Database.Command</c> included, starts with this.</summary>
    public const string EntityFrameworkCategory = "Microsoft.EntityFrameworkCore";

    private const string MESSAGE_TEMPLATE =
        "[{Timestamp:HH:mm:ss.fff}][{ThreadId}][{Level:u3}]{Message:lj} {NewLine:1}{Exception:1}";

    public static IServiceCollection AddCoreServices(this IServiceCollection services,
        IConfiguration configuration, ComponentType component)
    {
        services.AddCustomLogging(configuration);
        services.AddOptions<HostingConfiguration>().BindConfiguration("Hosting")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IPacketManager>(provider =>
        {
            Type[] packetTypes = typeof(Packet).Assembly.GetExportedTypes().Where(type =>
            {
                PacketAttribute? packetAttribute = type.GetCustomAttribute<PacketAttribute>();
                bool hasPacketAttribute = packetAttribute != null;
                if (!hasPacketAttribute)
                {
                    return false;
                }

                if (packetAttribute!.HandleOn != component)
                {
                    return false;
                }

                return type.IsClass &&
                       type.GetFields(BindingFlags.Public | BindingFlags.Static)
                           .Any(field => field.FieldType == typeof(NetworkPacketType));
            }).ToArray();

            Type[] handlerTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Where(x => !x.IsDynamic)
                .SelectMany(x => x.ExportedTypes)
                .Where(x =>
                    x.IsAssignableTo(typeof(IPacketHandlerNew)) &&
                    x is {IsClass: true, IsAbstract: false, IsInterface: false})
                .OrderBy(x => x.FullName)
                .ToArray();
            return ActivatorUtilities.CreateInstance<PacketManager>(provider, packetTypes, handlerTypes);
        });
        services.AddSingleton<IPacketReader, PacketReader>(provider =>
        {
            Type[] packetTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Where(x => !x.IsDynamic)
                .SelectMany(x => x.ExportedTypes.Where(type =>
                {
                    PacketAttribute? packetAttribute = type.GetCustomAttribute<PacketAttribute>();
                    bool hasPacketAttribute = packetAttribute != null;
                    if (!hasPacketAttribute)
                    {
                        return false;
                    }

                    if (packetAttribute!.HandleOn != component)
                    {
                        return false;
                    }

                    return type.IsClass &&
                           type.GetFields(BindingFlags.Public | BindingFlags.Static)
                               .Any(field => field.FieldType == typeof(NetworkPacketType));
                })).ToArray();

            return ActivatorUtilities.CreateInstance<PacketReader>(provider, [packetTypes]);
        });

        return services;
    }

    public static IServiceCollection AddCustomLogging(this IServiceCollection services, IConfiguration configuration)
    {
        LoggerConfiguration config = new();

        // add minimum log level for the instances. EF at Warning (#558): its command log is one
        // Information entry per statement. Serilog:MinimumLevel:Override can raise it again.
        config.MinimumLevel.Debug()
            .MinimumLevel.Override(EntityFrameworkCategory, LogEventLevel.Warning)
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

        config.Enrich.FromLogContext();

        // add assembly information
        // TODO: uncomment if needed
        //config.Enrich.WithAssemblyName() // {AssemblyName}
        //    .Enrich.WithAssemblyVersion(true) // {AssemblyVersion}
        //    .Enrich.WithAssemblyInformationalVersion();

        // add exception information
        config.Enrich.WithExceptionData();

        // sink to console
        config.WriteTo.Console(outputTemplate: MESSAGE_TEMPLATE, applyThemeToRedirectedOutput: true);

        config.ReadFrom.Configuration(configuration);

        // finally, create the logger
        services.AddLogging(x =>
        {
            x.ClearProviders();
            x.AddSerilog(config.CreateLogger());
        });

        // The same floor for every provider, not only Serilog (#558): the OpenTelemetry log
        // exporter the auth and world servers add after this is a provider of its own, which
        // Serilog's overrides never reach. Inserted first, so any Logging:LogLevel rule for the
        // category, read from configuration, still wins it (for Development).
        services.Configure<LoggerFilterOptions>(options => options.Rules.Insert(0,
            new LoggerFilterRule(null, EntityFrameworkCategory, LogLevel.Warning, null)));
        return services;
    }
}
