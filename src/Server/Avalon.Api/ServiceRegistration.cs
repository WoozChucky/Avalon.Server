using System.Reflection;
using System.Security.Claims;
using System.Text;
using Avalon.Api.Authentication;
using Avalon.Api.Authentication.AV;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Services;
using Avalon.Common.Telemetry;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.ResourceDetectors.Container;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Avalon.Api;

public static class ServiceRegistration
{
    public static void AddInfrastructure(this IServiceCollection services, ApplicationConfig config)
    {
        DatabaseManager.RegisterMappings();
        
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IMFAService, MFAService>();
        services.AddSingleton<IReplicatedCache, ReplicatedCache>();
        services.AddScoped<IMFAHashService, MFAHashService>();
        services.AddScoped<INotificationService, NotificationService>();
        
        var connectionString = $"Server={config.Database!.Auth!.Host}; " +
                               $"Port={config.Database.Auth.Port}; " +
                               $"Database={config.Database.Auth.Database}; " +
                               $"userid={config.Database.Auth.Username}; " +
                               $"Pwd={config.Database.Auth.Password};" +
                               "AllowZeroDateTime=True;" +
                               "ConvertZeroDateTime=True;";
        services.AddScoped<IAccountRepository>(_ => new AccountRepository(connectionString));
        services.AddScoped<IMFASetupRepository>(_ => new MFASetupRepository(connectionString));
        services.AddScoped<IDeviceRepository>(_ => new DeviceRepository(connectionString));
        
        services.AddScoped<IJwtUtils, JwtUtils>();
    }

    public static void AddTelemetry(this IServiceCollection services, ApplicationConfig config)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(builder =>
            {
                builder
                    .AddService(DiagnosticsConfig.Api.ServiceName)
                    .AddAttributes(new Dictionary<string, object>()
                    {
                        {"Host", Environment.MachineName },
                        {"OS", Environment.OSVersion.VersionString },
                        {"SystemPageSize", Environment.SystemPageSize.ToString() },
                        {"ProcessorCount", Environment.ProcessorCount.ToString() },
                        {"UserDomainName", Environment.UserDomainName },
                        {"UserName", Environment.UserName },
                        {"Version", Environment.Version.ToString() },
                        {"WorkingSet", Environment.WorkingSet.ToString() },
                        {"Application", Assembly.GetExecutingAssembly().GetName().Name! },
                    })
                    .AddDetector(new ContainerResourceDetector());
            })
            .WithMetrics(builder =>
            {
                builder
                    .AddMeter(DiagnosticsConfig.Api.Meter.Name)
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(options =>
                    {
                        options.Protocol = OtlpExportProtocol.Grpc;
                        options.Endpoint = new Uri("http://192.168.1.227:4317");
                    });
            })
            .WithTracing(builder =>
            {
                builder
                    .AddHttpClientInstrumentation()
                    .AddAspNetCoreInstrumentation()
                    .AddSqlClientInstrumentation()
                    .AddOtlpExporter(options =>
                    {
                        options.Protocol = OtlpExportProtocol.Grpc;
                        options.Endpoint = new Uri("http://192.168.1.227:4317");
                    });
            });
    }
    
    public static void AddAuth(this IServiceCollection services, ApplicationConfig config)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(x =>
        {
            x.SaveToken = true;
            x.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // try to fetch from authorization header, if not found, try from cookie
                    if (context.Request.Headers.TryGetValue(HeaderNames.Authorization, out var token))
                    {
                        context.Token = token.ToString().Split(" ")[1];
                    }
                    else if (context.Request.Cookies.TryGetValue(AuthConstants.CookieName, out var cookie))
                    {
                        context.Token = cookie;
                    }
                    return Task.CompletedTask;
                }
            };
            
            x.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = config.Authentication!.Issuer,
                ValidateIssuer = config.Authentication.ValidateIssuer,
            
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(config.Authentication.IssuerSigningKey)),
                ValidateIssuerSigningKey = config.Authentication.ValidateIssuerKey,
            
                ValidAudience = config.Authentication.Audience,
                ValidateAudience = config.Authentication.ValidateAudience,
            
                ValidateLifetime = false,
                ClockSkew = TimeSpan.FromMinutes(config.Authentication.ClockSkewInMinutes)
            };
            
            x.Validate(JwtBearerDefaults.AuthenticationScheme);
        })
        .AddScheme<AvalonAuthenticationSchemeOptions, AvalonAuthenticationHandler>(
            AvalonAuthenticationSchemeOptions.SchemeName, 
            AvalonAuthenticationSchemeOptions.SchemeName,
            options => { }
        );
    
        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme, AvalonAuthenticationSchemeOptions.SchemeName)
                .RequireAuthenticatedUser()
                .AddRequirements(new AvalonAuthRequirement())
                .Build();
        
            options.AddPolicy(AvalonRoles.Console, policy => policy
                .RequireClaim(ClaimTypes.GroupSid, AvalonRoles.Console)
                .Combine(options.DefaultPolicy)
            );
            
            options.AddPolicy(AvalonRoles.Admin, policy => policy
                .RequireClaim(ClaimTypes.GroupSid, AvalonRoles.Admin, AvalonRoles.Console)
                .Combine(options.DefaultPolicy)
            );
            
            options.AddPolicy(AvalonRoles.GameMaster, policy => policy
                .RequireClaim(ClaimTypes.GroupSid, AvalonRoles.GameMaster, AvalonRoles.Admin, AvalonRoles.Console)
                .Combine(options.DefaultPolicy)
            );
            
            options.AddPolicy(AvalonRoles.Player, policy => policy
                .RequireClaim(ClaimTypes.GroupSid, AvalonRoles.Player, AvalonRoles.GameMaster, AvalonRoles.Admin, AvalonRoles.Console)
                .Combine(options.DefaultPolicy)
            );
        
        });

        services.AddScoped<IAuthContext, AuthContext>();
        services.AddScoped<IAuthorizationHandler, AvalonAuthHandler>();
    }
    
    public static void AddSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new() { Title = "Avalon Api", Version = "v1" });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = @"JWT Authorization header using the Bearer scheme. <br>
                          Enter 'Bearer' [space] and then your token in the text input below.
                          <br>Example: 'Bearer 12345abcdef'",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement()
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        },
                        Scheme = "oauth2",
                        Name = "Bearer",
                        In = ParameterLocation.Header,
                    },
                    new List<string>()
                }
            });
        });
    }
}
