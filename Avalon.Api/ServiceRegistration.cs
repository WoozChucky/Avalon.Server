using System.Security.Claims;
using System.Text;
using Avalon.Api.Authentication;
using Avalon.Api.Config;
using Avalon.Api.Services;
using Avalon.Database;
using Avalon.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace Avalon.Api;

public static class ServiceRegistration
{
    public static void AddInfrastructure(this IServiceCollection services, ApplicationConfig config)
    {
        DatabaseManager.RegisterMappings();
        
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IAccountRepository>(_ => new AccountRepository(config.Database.AuthConnectionString));
        
        services.AddScoped<IJwtUtils, JwtUtils>();
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
            
            x.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = config.Authentication.Issuer,
                ValidateIssuer = config.Authentication.ValidateIssuer,
            
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(config.Authentication.IssuerSigningKey)),
                ValidateIssuerSigningKey = config.Authentication.ValidateIssuerKey,
            
                ValidAudience = config.Authentication.Audience,
                ValidateAudience = config.Authentication.ValidateAudience,
            
                ValidateLifetime = false,
                ClockSkew = TimeSpan.FromMinutes(config.Authentication.ClockSkewInMinutes)
            };
            
            x.Validate(JwtBearerDefaults.AuthenticationScheme);
        });
    
        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new AvalonAuthRequirement())
                .Build();
        
            options.AddPolicy("Admin", policy => policy
                .RequireClaim(ClaimTypes.GroupSid, "admin", "Admin")
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
