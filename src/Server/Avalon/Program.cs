using Projects;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<ContainerResource> redis = builder
    .AddContainer("redis", "redis", "latest")
    .WithEnvironment("REDIS_PASSWORD", "123")
    .WithEndpoint(6379, 6379, "tcp", "tcp", isProxied: false, isExternal: true)
    .WithLifetime(ContainerLifetime.Persistent);

IResourceBuilder<ContainerResource> postgresql = builder
    .AddContainer("postgresql", "postgres", "18")
    .WithEnvironment("POSTGRES_PASSWORD", "123")
    .WithEndpoint(5432, 5432, "tcp", "tcp", isProxied: false, isExternal: true)
    .WithLifetime(ContainerLifetime.Persistent);

// Shared between the api and the balance service: the service requires 32+ characters in X-Balance-Secret.
// Generated once and kept in the AppHost's user secrets (persist), so restarts keep working.
IResourceBuilder<ParameterResource> balanceSecret = builder.AddParameter(
    "balance-secret",
    new GenerateParameterDefault { MinLength = 48, Special = false },
    secret: true,
    persist: true);

IResourceBuilder<ProjectResource> balanceService = builder
    .AddProject<Avalon_Balance_Service>("balance")
    .WithEnvironment("Balance__SharedSecret", balanceSecret);

IResourceBuilder<ProjectResource> apiProject = builder
    .AddProject<Avalon_Api>("api")
    // World 1 (#523), the same local databases the api's appsettings.Development.json names.
    .WithEnvironment("Database__Worlds__1__World__ConnectionString",
        "Server=localhost;Port=5432;Database=world;User Id=postgres;Password=123;")
    .WithEnvironment("Database__Worlds__1__Characters__ConnectionString",
        "Server=localhost;Port=5432;Database=characters;User Id=postgres;Password=123;")
    .WithEnvironment("Application__Balance__SharedSecret", balanceSecret)
    .WithEnvironment("Application__Balance__Url", balanceService.GetEndpoint("http"))
    .WaitFor(redis)
    .WaitFor(postgresql)
    .WaitFor(balanceService);

IResourceBuilder<ProjectResource> authServer = builder
    .AddProject<Avalon_Server_Auth>("auth")
    .WithEndpoint(21000, 21000, "tcp", "tcp", isProxied: false, isExternal: true)
    .WaitFor(redis)
    .WaitFor(postgresql)
    .WaitFor(apiProject);

IResourceBuilder<ProjectResource> worldServer = builder
    .AddProject<Avalon_Server_World>("world")
    .WithEndpoint(21001, 21001, "tcp", "tcp", isProxied: false, isExternal: true)
    .WaitFor(redis)
    .WaitFor(postgresql)
    .WaitFor(authServer);

builder.Build().Run();
