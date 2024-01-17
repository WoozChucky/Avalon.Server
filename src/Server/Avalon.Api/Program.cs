using Avalon.Api;
using Avalon.Api.Config;
using Avalon.Api.Exceptions.Handlers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder
    .Configuration
    .AddEnvironmentVariables().
    Build();

var services = builder.Services;

// Add services to the container.
{
    var applicationConfig = new ApplicationConfig();
    configuration.Bind("Application", applicationConfig);
    services.AddSingleton(applicationConfig);
    services.AddSingleton(applicationConfig.Environment);
    services.AddSingleton(applicationConfig.Authentication);
    services.AddSingleton(applicationConfig.Database);
    
    services.Configure<CookiePolicyOptions>(options =>
    {
        options.MinimumSameSitePolicy = SameSiteMode.None;
    });

    services.AddExceptionHandler<DefaultExceptionHandler>();
    services.AddHealthChecks();
    services.AddCors();

    services.AddHttpContextAccessor();
    services.AddControllers();

    services.AddTelemetry(applicationConfig);
    services.AddAuth(applicationConfig);
    services.AddSwagger();
    services.AddInfrastructure(applicationConfig);
}

var app = builder.Build();

// Configure the HTTP request pipeline.
{
    if (app.Environment.IsDevelopment())
    {
        
        app.UseDeveloperExceptionPage();
    }
    else
    {
        // app.UseHsts();
    }
    
    app.UseSwagger();
    app.UseSwaggerUI();
    
    app.UseRouting();

    app.UseExceptionHandler(_ => { });
    
    app.UseCors(x => x
        .WithOrigins("http://localhost:4200", "https://avalon.monster", "https://dashboard.avalon.monster")
        .AllowAnyMethod()
        .AllowAnyHeader()
    );
    
    app.UseAuthentication();

    app.UseAuthorization();
    
    app.MapHealthChecks("/health");
    
    app.MapControllers();
}

app.Run();
