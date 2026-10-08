using System.Globalization;
using System.Security.Cryptography;
using Avalon.LocalDevelopment;
using Projects;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<ContainerResource> redis = builder
    .AddContainer("redis", "redis", "latest")
    // The official image takes its password as an argument and ignores a REDIS_PASSWORD variable; every server sends 123.
    .WithArgs("--requirepass", "123")
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

// The local Development world (#523): the id the api's world databases below are keyed by, and the one world
// whose templates admins may edit live (Application:Templates:EditableWorlds). Production worlds stay out of it.
const int DevWorldId = LocalCertificates.WorldId;

// The access tokens' ES256 key pair (#801), made afresh on every run, so no user secret is needed: the private half goes
// to the api, which runs identity, the one service that signs, and the public half is listed for every service under
// the key id. A token from an earlier run is refused, and the client refreshes.
const string JwtKeyId = "apphost";
using var jwtKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

// The game-auth cryptography's host key (#801): generated once and kept in the AppHost's user secrets (persist), so the
// receipts, proofs and Steam OpenID states it protects survive a restart.
IResourceBuilder<ParameterResource> gameAuthHostKey = builder.AddParameter(
    "game-auth-host-key",
    new GenerateParameterDefault { MinLength = 48, Special = false },
    secret: true,
    persist: true);

// The world's three private TLS leaves (docs/development-setup.md), made afresh on every run like the token keys, into
// the temp folder: the world's own (players pin it from the join reply, so a new one each run costs nothing), the
// world's client leaf on the api's game workload listener, and that listener's. The api and the world get each other's
// pins below.
LocalCertificates certificates = await LocalCertificates.CreateAsync(
    Path.Combine(Path.GetTempPath(), "avalon-apphost"), TimeSpan.FromDays(30), CancellationToken.None);

// The api's listeners. Named Kestrel endpoints replace the launch profile's URLs once the game workload listener is
// declared, so the public ones are declared too: https for the game client, which talks only https, and http for the
// browser tools. Unproxied, so each port is the api's own.
const int ApiHttpsPort = 7166;
const string ApiHttpUrl = "http://localhost:5210";
const string ApiHttpsUrl = "https://localhost:7166";

IResourceBuilder<ProjectResource> apiProject = builder
    .AddProject<Avalon_Api>("api")
    .WithEndpoint("http", endpoint => endpoint.IsProxied = false)
    .WithHttpsEndpoint(port: ApiHttpsPort, name: "https", isProxied: false)
    .WithEndpoint(port: LocalCertificates.GameInternalPort, scheme: "https", name: "game-internal", isProxied: false)
    .WithEnvironment("Kestrel__Endpoints__PublicHttp__Url", ApiHttpUrl)
    .WithEnvironment("Kestrel__Endpoints__Public__Url", ApiHttpsUrl)
    .WithEnvironment("Kestrel__Endpoints__GameInternal__Url", LocalCertificates.GameInternalUrl)
    .WithEnvironment("Kestrel__Endpoints__GameInternal__Certificate__Path", certificates.ApiInternal.Path)
    .WithEnvironment("Kestrel__Endpoints__GameInternal__Certificate__Password", certificates.ApiInternal.Password)
    // The one world the game admission allocates: its server, the name its TLS leaf is for, and the two pins.
    .WithEnvironment("Application__GameWorkloads__Servers__0__ServerId", LocalCertificates.ServerId)
    .WithEnvironment("Application__GameWorkloads__Servers__0__WorldId", DevWorldId.ToString(CultureInfo.InvariantCulture))
    .WithEnvironment("Application__GameWorkloads__Servers__0__TlsServerName", LocalCertificates.TlsServerName)
    .WithEnvironment("Application__GameWorkloads__Servers__0__TlsCertificateSha256", certificates.WorldTls.Sha256)
    .WithEnvironment("Application__GameWorkloads__Servers__0__ClientCertificateSha256", certificates.Workload.Sha256)
    .WithEnvironment("Application__Authentication__SigningKey", Convert.ToBase64String(jwtKey.ExportPkcs8PrivateKey()))
    .WithEnvironment("Application__Authentication__SigningKeyId", JwtKeyId)
    .WithEnvironment($"Application__Authentication__ValidationKeys__{JwtKeyId}",
        Convert.ToBase64String(jwtKey.ExportSubjectPublicKeyInfo()))
    .WithEnvironment("Application__GameAuth__HostKey", gameAuthHostKey)
    // The Development world, the same local databases the api's appsettings.Development.json names.
    .WithEnvironment($"Database__Worlds__{DevWorldId}__World__ConnectionString",
        "Server=localhost;Port=5432;Database=world;User Id=postgres;Password=123;")
    .WithEnvironment($"Database__Worlds__{DevWorldId}__Characters__ConnectionString",
        "Server=localhost;Port=5432;Database=characters;User Id=postgres;Password=123;")
    .WithEnvironment("Application__Templates__EditableWorlds__0", DevWorldId.ToString(CultureInfo.InvariantCulture))
    // The public site's local dev server (Avalon.Dashboard apps/public, vite), for link previews' og:url.
    .WithEnvironment("Application__PublicSiteUrl", "http://localhost:5173")
    .WithEnvironment("Application__Balance__SharedSecret", balanceSecret)
    .WithEnvironment("Application__Balance__Url", balanceService.GetEndpoint("http"))
    // Healthy once it serves, which is after it migrated the auth database: the servers below wait for that.
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WaitFor(redis)
    .WaitFor(postgresql)
    .WaitFor(balanceService);

// The auth server waits for the api so the two never migrate the auth database at once.
builder
    .AddProject<Avalon_Server_Auth>("auth")
    .WithEndpoint(21000, 21000, "tcp", "tcp", isProxied: false, isExternal: true)
    .WaitFor(redis)
    .WaitFor(postgresql)
    .WaitFor(apiProject);

// The world reads the auth database (its world row, maintenance), which it never migrates, so it waits for the api
// too. It creates its own World and Characters databases; the api found them missing at its start on a fresh
// Postgres and picks them up by itself once they exist (WorldDatabaseRecheck), with no restart.
builder
    .AddProject<Avalon_Server_World>("world")
    .WithEndpoint(21001, 21001, "tcp", "tcp", isProxied: false, isExternal: true)
    .WithEnvironment("Hosting__Security__CertificatePath", certificates.WorldTls.Path)
    .WithEnvironment("Hosting__Security__CertificatePassword", certificates.WorldTls.Password)
    .WithEnvironment("World__Admission__ApiUrl", LocalCertificates.GameInternalUrl + "/")
    .WithEnvironment("World__Admission__ServerId", LocalCertificates.ServerId)
    .WithEnvironment("World__Admission__ClientCertificatePath", certificates.Workload.Path)
    .WithEnvironment("World__Admission__ClientCertificatePassword", certificates.Workload.Password)
    .WithEnvironment("World__Admission__ApiCertificateSha256", certificates.ApiInternal.Sha256)
    .WaitFor(redis)
    .WaitFor(postgresql)
    .WaitFor(apiProject);

await builder.Build().RunAsync(CancellationToken.None);
