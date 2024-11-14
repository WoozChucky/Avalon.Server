using Projects;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<ContainerResource> redis = builder
    .AddContainer("redis", "redis", "latest")
    .WithEnvironment("REDIS_PASSWORD", "123")
    .WithEndpoint(6379, 6379, "tcp", "tcp", isExternal: true)
    .WithLifetime(ContainerLifetime.Persistent);

IResourceBuilder<ContainerResource> mariadb = builder
    .AddContainer("mariadb", "mariadb", "latest")
    .WithEnvironment("MARIADB_ROOT_PASSWORD", "123")
    .WithEndpoint(3306, 3306, "tcp", "tcp", isExternal: true)
    .WithLifetime(ContainerLifetime.Persistent);

IResourceBuilder<ProjectResource> apiProject = builder
    .AddProject<Avalon_Api>("api")
    .WaitFor(redis)
    .WaitFor(mariadb);

IResourceBuilder<ProjectResource> authServer = builder
    .AddProject<Avalon_Server_Auth>("auth")
    .WaitFor(redis)
    .WaitFor(mariadb);

IResourceBuilder<ProjectResource> worldServer = builder
    .AddProject<Avalon_Server_World>("world")
    .WaitFor(redis)
    .WaitFor(mariadb);

builder.Build().Run();
