using Avalon.Api;
using Avalon.Api.Config;
using Avalon.Api.ExceptionHandlers;

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
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseHsts();
    }
    
    app.UseHttpsRedirection();
    
    app.UseRouting();

    app.UseExceptionHandler(_ => { });
    
    app.UseCors(x => x
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader()
    );
    
    app.UseAuthentication();

    app.UseAuthorization();

    app.MapControllers();
}

app.Run();
