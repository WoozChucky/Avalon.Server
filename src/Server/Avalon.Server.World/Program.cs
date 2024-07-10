using System.Reflection;
using Avalon.Common.Cryptography;
using Avalon.Common.Telemetry;
using Avalon.Database.Character;
using Avalon.Hosting;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Configuration;
using Avalon.Metrics;
using Avalon.Network;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Network.Packets.Internal;
using Avalon.Network.Packets.Internal.Deserialization;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Tcp;
using Avalon.Server.World.Configuration;
using Avalon.Server.World.Extensions;
using Avalon.Server.World.Logging;
using Avalon.World;
using Avalon.World.Database;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace Avalon.Server.World
{
    internal class Program
    {
        private static CancellationTokenSource CancellationTokenSource { get; set; } = null!;
        private static IServiceProvider ServiceProvider { get; set; } = null!;
        private static IConfigurationRoot Configuration { get; set; } = null!;
        private static IAvalonInfrastructure Infrastructure { get; set; } = null!;
        private static ILogger<Program> Logger { get; set; } = null!;
        private static IMetricsManager MetricsManager { get; set; } = null!;
        private static AppConfiguration AppConfiguration { get; set; } = null!;
        
        private static async Task Main(string[] args)
        {
            //TestBuild("dungeon.obj", RcPartition.WATERSHED);
            //return;
            
            var hostBuilder = await AvalonHostBuilder.CreateHostAsync(args, ComponentType.World);
            hostBuilder.Services.AddWorldServices();
            hostBuilder.Services.AddHostedService<WorldServer>();
            hostBuilder.Services.AddSingleton<IWorldServer>(provider =>
                provider.GetServices<IHostedService>().OfType<WorldServer>().Single());
        
            var host = hostBuilder.Build();
        
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var characterDb = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
                var worldDb = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
                var logger = host.Services.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Migrating database if necessary...");
                await characterDb.Database.MigrateAsync();
                await worldDb.Database.MigrateAsync();
            
                var cache = scope.ServiceProvider.GetRequiredService<IReplicatedCache>();
                await cache.ConnectAsync();
            }

            await AvalonHostBuilder.RunAsync<Program>(host);
        }
        
        private static float m_cellSize = 0.3f;
        private static float m_cellHeight = 0.2f;
        private static float m_agentHeight = 2.0f;
        private static float m_agentRadius = 0.6f;
        private static float m_agentMaxClimb = 0.9f;
        private static float m_agentMaxSlope = 45.0f;
        private static int m_regionMinSize = 8;
        private static int m_regionMergeSize = 20;
        private static float m_edgeMaxLen = 12.0f;
        private static float m_edgeMaxError = 1.3f;
        private static int m_vertsPerPoly = 6;
        private static float m_detailSampleDist = 6.0f;
        private static float m_detailSampleMaxError = 1.0f;
        private static RcPartition m_partitionType = RcPartition.WATERSHED;
        
        public static void TestBuild(string filename, RcPartition partitionType)
        {
            m_partitionType = partitionType;
            IInputGeomProvider geomProvider = SimpleInputGeomProvider.LoadFile(filename);
            long time = RcFrequency.Ticks;
            RcVec3f bmin = geomProvider.GetMeshBoundsMin();
            RcVec3f bmax = geomProvider.GetMeshBoundsMax();
            RcContext m_ctx = new RcContext();
            //
            // Step 1. Initialize build config.
            //

            // Init build configuration from GUI
            RcConfig cfg = new RcConfig(
                partitionType,
                m_cellSize, m_cellHeight,
                m_agentMaxSlope, m_agentHeight, m_agentRadius, m_agentMaxClimb,
                m_regionMinSize, m_regionMergeSize,
                m_edgeMaxLen, m_edgeMaxError,
                m_vertsPerPoly,
                m_detailSampleDist, m_detailSampleMaxError,
                true, true, true,
                SampleAreaModifications.SAMPLE_AREAMOD_GROUND, true);
            RcBuilderConfig bcfg = new RcBuilderConfig(cfg, bmin, bmax);

            //
            // Step 2. Rasterize input polygon soup.
            //

            // Allocate voxel heightfield where we rasterize our input data to.
            RcHeightfield m_solid = new RcHeightfield(bcfg.width, bcfg.height, bcfg.bmin, bcfg.bmax, cfg.Cs, cfg.Ch, cfg.BorderSize);

            foreach (RcTriMesh geom in geomProvider.Meshes())
            {
                float[] verts = geom.GetVerts();
                int[] tris = geom.GetTris();
                int ntris = tris.Length / 3;

                // Allocate array that can hold triangle area types.
                // If you have multiple meshes you need to process, allocate
                // and array which can hold the max number of triangles you need to process.

                // Find triangles which are walkable based on their slope and rasterize them.
                // If your input data is multiple meshes, you can transform them here, calculate
                // the are type for each of the meshes and rasterize them.
                int[] m_triareas = RcRecast.MarkWalkableTriangles(m_ctx, cfg.WalkableSlopeAngle, verts, tris, ntris, cfg.WalkableAreaMod);
                RcRasterizations.RasterizeTriangles(m_ctx, verts, tris, m_triareas, ntris, m_solid, cfg.WalkableClimb);
            }

            //
            // Step 3. Filter walkable surfaces.
            //

            // Once all geometry is rasterized, we do initial pass of filtering to
            // remove unwanted overhangs caused by the conservative rasterization
            // as well as filter spans where the character cannot possibly stand.
            RcFilters.FilterLowHangingWalkableObstacles(m_ctx, cfg.WalkableClimb, m_solid);
            RcFilters.FilterLedgeSpans(m_ctx, cfg.WalkableHeight, cfg.WalkableClimb, m_solid);
            RcFilters.FilterWalkableLowHeightSpans(m_ctx, cfg.WalkableHeight, m_solid);

            //
            // Step 4. Partition walkable surface to simple regions.
            //

            // Compact the heightfield so that it is faster to handle from now on.
            // This will result more cache coherent data as well as the neighbours
            // between walkable cells will be calculated.
            RcCompactHeightfield m_chf = RcCompacts.BuildCompactHeightfield(m_ctx, cfg.WalkableHeight, cfg.WalkableClimb, m_solid);

            // Erode the walkable area by agent radius.
            RcAreas.ErodeWalkableArea(m_ctx, cfg.WalkableRadius, m_chf);

            // (Optional) Mark areas.
            /*
             * ConvexVolume vols = m_geom->GetConvexVolumes(); for (int i = 0; i < m_geom->GetConvexVolumeCount(); ++i)
             * RcMarkConvexPolyArea(m_ctx, vols[i].verts, vols[i].nverts, vols[i].hmin, vols[i].hmax, (unsigned
             * char)vols[i].area, *m_chf);
             */

            // Partition the heightfield so that we can use simple algorithm later
            // to triangulate the walkable areas.
            // There are 3 partitioning methods, each with some pros and cons:
            // 1) Watershed partitioning
            // - the classic Recast partitioning
            // - creates the nicest tessellation
            // - usually slowest
            // - partitions the heightfield into nice regions without holes or
            // overlaps
            // - the are some corner cases where this method creates produces holes
            // and overlaps
            // - holes may appear when a small obstacles is close to large open area
            // (triangulation can handle this)
            // - overlaps may occur if you have narrow spiral corridors (i.e
            // stairs), this make triangulation to fail
            // * generally the best choice if you precompute the navmesh, use this
            // if you have large open areas
            // 2) Monotone partioning
            // - fastest
            // - partitions the heightfield into regions without holes and overlaps
            // (guaranteed)
            // - creates long thin polygons, which sometimes causes paths with
            // detours
            // * use this if you want fast navmesh generation
            // 3) Layer partitoining
            // - quite fast
            // - partitions the heighfield into non-overlapping regions
            // - relies on the triangulation code to cope with holes (thus slower
            // than monotone partitioning)
            // - produces better triangles than monotone partitioning
            // - does not have the corner cases of watershed partitioning
            // - can be slow and create a bit ugly tessellation (still better than
            // monotone)
            // if you have large open areas with small obstacles (not a problem if
            // you use tiles)
            // * good choice to use for tiled navmesh with medium and small sized
            // tiles
            long time3 = RcFrequency.Ticks;

            if (m_partitionType == RcPartition.WATERSHED)
            {
                // Prepare for region partitioning, by calculating distance field
                // along the walkable surface.
                RcRegions.BuildDistanceField(m_ctx, m_chf);
                // Partition the walkable surface into simple regions without holes.
                RcRegions.BuildRegions(m_ctx, m_chf, cfg.MinRegionArea, cfg.MergeRegionArea);
            }
            else if (m_partitionType == RcPartition.MONOTONE)
            {
                // Partition the walkable surface into simple regions without holes.
                // Monotone partitioning does not need distancefield.
                RcRegions.BuildRegionsMonotone(m_ctx, m_chf, cfg.MinRegionArea, cfg.MergeRegionArea);
            }
            else
            {
                // Partition the walkable surface into simple regions without holes.
                RcRegions.BuildLayerRegions(m_ctx, m_chf, cfg.MinRegionArea);
            }

            
            //
            // Step 5. Trace and simplify region contours.
            //

            // Create contours.
            RcContourSet m_cset = RcContours.BuildContours(m_ctx, m_chf, cfg.MaxSimplificationError, cfg.MaxEdgeLen, RcBuildContoursFlags.RC_CONTOUR_TESS_WALL_EDGES);
            
            //
            // Step 6. Build polygons mesh from contours.
            //

            // Build polygon navmesh from the contours.
            RcPolyMesh m_pmesh = RcMeshs.BuildPolyMesh(m_ctx, m_cset, cfg.MaxVertsPerPoly);

            //
            // Step 7. Create detail mesh which allows to access approximate height
            // on each polygon.
            //

            RcPolyMeshDetail m_dmesh = RcMeshDetails.BuildPolyMeshDetail(m_ctx, m_pmesh, m_chf, cfg.DetailSampleDist,
                cfg.DetailSampleMaxError);
            long time2 = RcFrequency.Ticks;
            Console.WriteLine(filename + " : " + partitionType + "  " + (time2 - time) / TimeSpan.TicksPerMillisecond + " ms");
            Console.WriteLine("           " + (time3 - time) / TimeSpan.TicksPerMillisecond + " ms");
            SaveObj(filename.Substring(0, filename.LastIndexOf('.')) + "_" + partitionType + "_detail.obj", m_dmesh);
            SaveObj(filename.Substring(0, filename.LastIndexOf('.')) + "_" + partitionType + ".obj", m_pmesh);
            foreach (var rtt in m_ctx.ToList())
            {
                Console.WriteLine($"{rtt.Key} : {rtt.Millis} ms");
            }
        }
        
        private static void SaveObj(string filename, RcPolyMesh mesh)
        {
            try
            {
                string path = Path.Combine("test-output", filename);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using StreamWriter fw = new StreamWriter(path);
                for (int v = 0; v < mesh.nverts; v++)
                {
                    fw.Write("v " + (mesh.bmin.X + mesh.verts[v * 3] * mesh.cs) + " "
                             + (mesh.bmin.Y + mesh.verts[v * 3 + 1] * mesh.ch) + " "
                             + (mesh.bmin.Z + mesh.verts[v * 3 + 2] * mesh.cs) + "\n");
                }

                for (int i = 0; i < mesh.npolys; i++)
                {
                    int p = i * mesh.nvp * 2;
                    fw.Write("f ");
                    for (int j = 0; j < mesh.nvp; ++j)
                    {
                        int v = mesh.polys[p + j];
                        if (v == RcRecast.RC_MESH_NULL_IDX)
                        {
                            break;
                        }

                        fw.Write((v + 1) + " ");
                    }

                    fw.Write("\n");
                }

                fw.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private static void SaveObj(string filename, RcPolyMeshDetail dmesh)
        {
            try
            {
                string filePath = Path.Combine("test-output", filename);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                using StreamWriter fw = new StreamWriter(filePath);
                for (int v = 0; v < dmesh.nverts; v++)
                {
                    fw.Write(
                        "v " + dmesh.verts[v * 3] + " " + dmesh.verts[v * 3 + 1] + " " + dmesh.verts[v * 3 + 2] + "\n");
                }

                for (int m = 0; m < dmesh.nmeshes; m++)
                {
                    int vfirst = dmesh.meshes[m * 4];
                    int tfirst = dmesh.meshes[m * 4 + 2];
                    for (int f = 0; f < dmesh.meshes[m * 4 + 3]; f++)
                    {
                        fw.Write("f " + (vfirst + dmesh.tris[(tfirst + f) * 4] + 1) + " "
                                 + (vfirst + dmesh.tris[(tfirst + f) * 4 + 1] + 1) + " "
                                 + (vfirst + dmesh.tris[(tfirst + f) * 4 + 2] + 1) + "\n");
                    }
                }

                fw.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private static void ConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            Logger.LogWarning("[Ctrl+C] was caught, stopping application...");
            CancellationTokenSource.Cancel();
        }
        
        private static void OnUnhandledExceptionOccurred(object sender, UnhandledExceptionEventArgs e)
        {
            Logger.LogError(e.ExceptionObject as Exception, "Unhandled exception");
            CancellationTokenSource.Cancel();
        }
        
        private static void OnProcessExit(object? sender, EventArgs e)
        {
            Logger.LogInformation("Exited successfully");
        }

        private static void ConfigureConfiguration(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", false, false)
                .AddEnvironmentVariables();
            
            if (args.Length > 0)
            {
                builder.AddCommandLine(args);
            }
            
            Configuration = builder.Build();

            AppConfiguration = new AppConfiguration();
            
            Configuration.Bind(AppConfiguration);
        }
        
        private static void ConfigureDependencyInjection()
        {
            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder
                    .AddSerilog(new LoggerConfiguration()
                        .MinimumLevel.Verbose()
                        .Enrich.FromLogContext()
                        .Enrich.WithThreadId()
                        .Enrich.With<LayerEnricher>() // ({SourceContext})
                        .WriteTo.Console(LogEventLevel.Debug, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] [{ThreadId}] [{Layer}] -> {Message}{NewLine}{Exception}", theme: AnsiConsoleTheme.Sixteen, applyThemeToRedirectedOutput: true)
                        .CreateLogger()
                    )
                    .SetMinimumLevel(LogLevel.Debug);
                if (AppConfiguration.Metrics!.Export)
                {
                    builder.AddOpenTelemetry(options =>
                    {
                        options.SetResourceBuilder(
                                ResourceBuilder
                                    .CreateDefault()
                                    .AddService(DiagnosticsConfig.Server.ServiceName)
                            )
                            .AddOtlpExporter(options =>
                            {
                                options.Protocol = OtlpExportProtocol.Grpc;
                                options.Endpoint = new Uri("http://192.168.1.227:4317");
                            });
                    });
                }
            });

            if (AppConfiguration.Metrics!.Export)
            {
                services.AddOpenTelemetry()
                    .ConfigureResource(builder =>
                    {
                        builder
                            .AddService(DiagnosticsConfig.Server.ServiceName)
                            .AddAttributes(new Dictionary<string, object>()
                            {
                                {"Host", Environment.MachineName},
                                {"OS", Environment.OSVersion.VersionString},
                                {"SystemPageSize", Environment.SystemPageSize.ToString()},
                                {"ProcessorCount", Environment.ProcessorCount.ToString()},
                                {"UserDomainName", Environment.UserDomainName},
                                {"UserName", Environment.UserName},
                                {"Version", Environment.Version.ToString()},
                                {"WorkingSet", Environment.WorkingSet.ToString()},
                                {"Application", Assembly.GetExecutingAssembly().GetName().Name!},
                            })
                            .AddContainerDetector();
                    })
                    .WithMetrics(builder =>
                    {
                        builder
                            .AddMeter(DiagnosticsConfig.Server.Meter.Name)
                            .AddRuntimeInstrumentation()
                            .AddProcessInstrumentation()
                            .AddOtlpExporter(options =>
                            {
                                options.Protocol = OtlpExportProtocol.Grpc;
                                options.Endpoint = new Uri("http://192.168.1.227:4317");
                            });
                    })
                    .WithTracing(builder =>
                    {
                        builder
                            .AddOtlpExporter(options =>
                            {
                                options.Protocol = OtlpExportProtocol.Grpc;
                                options.Endpoint = new Uri("http://192.168.1.227:4317");
                            });
                    });   
            }
            
            services.AddSingleton(AppConfiguration);
            services.AddSingleton(AppConfiguration.Infrastructure!);
            services.AddSingleton(AppConfiguration.NetworkDaemon!);
            services.AddSingleton(AppConfiguration.NetworkDaemon!.Tcp!);
            services.AddSingleton(AppConfiguration.Metrics);
            services.AddSingleton(AppConfiguration.Database!);
            services.AddSingleton(AppConfiguration.Cache!);
            services.AddSingleton(AppConfiguration.Game!);

            if (AppConfiguration.Metrics.Enabled)
            {
                services.AddSingleton<IMetricsManager, MetricsManager>();
            }
            else
            {
                services.AddSingleton<IMetricsManager, FakeMetricsManager>();
            }
            
            services.AddSingleton<IAvalonTcpServer, AvalonTcpServer>();
            services.AddSingleton<IPacketDeserializer, NetworkPacketDeserializer>();
            services.AddSingleton<IPacketSerializer, NetworkPacketSerializer>();
            services.AddSingleton<IPacketRegistry, PacketRegistry>();
            services.AddSingleton<ICryptoManager, CryptoManager>();
            services.AddSingleton<CancellationTokenSource>(s => new CancellationTokenSource());
            services.AddScoped<IReplicatedCache, ReplicatedCache>(provider =>
            {
                var options = provider.GetRequiredService<IOptionsSnapshot<CacheConfiguration>>();
                return new ReplicatedCache(provider.GetRequiredService<ILoggerFactory>(), options);
            });
            
            ServiceProvider = services.BuildServiceProvider();
            
            Logger = ServiceProvider.GetService<ILogger<Program>>() ?? throw new InvalidOperationException();
            Infrastructure = ServiceProvider.GetService<IAvalonInfrastructure>() ?? throw new InvalidOperationException();
            CancellationTokenSource = ServiceProvider.GetService<CancellationTokenSource>() ?? throw new InvalidOperationException();
            MetricsManager = ServiceProvider.GetService<IMetricsManager>() ?? throw new InvalidOperationException();
        }
    }
}
