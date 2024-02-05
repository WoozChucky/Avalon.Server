using Avalon.Api;
using Avalon.Api.Config;
using Avalon.Api.Middlewares;
using Avalon.Api.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

var builder = WebApplication.CreateBuilder(args);

var configuration = builder
    .Configuration
    .AddEnvironmentVariables().
    Build();

//Add support to logging with SERILOG
builder.Host.UseSerilog((context, hostConfig) =>
{
    hostConfig
        .MinimumLevel.Verbose()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware]", LogEventLevel.Fatal)
        .Enrich.FromLogContext()
        .WriteTo.Console(LogEventLevel.Debug,
            outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] [{SourceContext}] -> {Message}{NewLine}{Exception}",
            theme: AnsiConsoleTheme.Sixteen, applyThemeToRedirectedOutput: true);
});

var services = builder.Services;

// Add services to the container.
{
    var applicationConfig = new ApplicationConfig();
    configuration.Bind("Application", applicationConfig);
    services.AddSingleton(applicationConfig);
    services.AddSingleton(applicationConfig.Environment!);
    services.AddSingleton(applicationConfig.Authentication!);
    services.AddSingleton(applicationConfig.Database!);
    services.AddSingleton(applicationConfig.Notification!);
    
    services.Configure<CookiePolicyOptions>(options =>
    {
        options.MinimumSameSitePolicy = SameSiteMode.None;
    });
    
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

    app.UseMiddleware<ExceptionHandlerMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();
    
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });
    
    app.UseSwagger();
    app.UseSwaggerUI();
    
    app.UseRouting();
    
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

var logger = app.Services.GetRequiredService<ILogger<Program>>();

var workerServices = app.Services.GetServices<IWorkerService>();

var cts = new CancellationTokenSource();
foreach (var workerService in workerServices)
{
    logger.LogInformation("Starting worker {Worker}", workerService.GetType().Name);
    workerService.StartWorker(cts.Token);
}

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    logger.LogError(eventArgs.ExceptionObject as Exception, "Unhandled exception");
    Environment.Exit(1);
};
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    cts.Cancel();
    logger.LogInformation("Exited successfully");
};
Console.CancelKeyPress += (_, _) =>
{
    logger.LogInformation("Ctrl+C was pressed, stopping application...");
};

app.Run();
