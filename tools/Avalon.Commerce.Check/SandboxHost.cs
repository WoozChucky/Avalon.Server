using System.Security.Claims;
using System.Text.Encodings.Web;
using Avalon.Api;
using Avalon.Api.Commerce;
using Avalon.Api.Contract;
using Avalon.Api.Contract.Commerce;
using Avalon.Api.Middlewares;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using Avalon.Configuration;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

/// <summary>Local, one-actor fixture host. Authentication is deliberately a fixture; payments/storage are real.</summary>
internal static class SandboxHost
{
    public static async Task RunAsync(IDbContextFactory<AuthDbContext> factory, IReplicatedCache cache, TimeProvider clock)
    {
        while (await RunHostAsync(factory, cache, clock))
            Console.WriteLine("Restarting the isolated fixture host and payment worker; disposable storage is retained.");
    }

    private static async Task<bool> RunHostAsync(IDbContextFactory<AuthDbContext> factory, IReplicatedCache cache, TimeProvider clock)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddEnvironmentVariables("AVALON_COMMERCE_SANDBOX_");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Application:Commerce:Enabled"] = "true",
            ["Application:Commerce:PaymentEnvironment"] = "sandbox",
            ["Application:Commerce:LicenseEnvironment"] = "development",
            ["Application:StoreAuthentication:Environment"] = "development",
            ["Application:StoreAuthentication:SteamIdentityPrefix"] = "avalon-auth-dev",
        });
        var config = builder.Configuration.GetSection("Application:Commerce").Get<CommerceConfiguration>() ?? new();
        var authentication = new StoreAuthenticationConfiguration { Environment = "development", SteamIdentityPrefix = "avalon-auth-dev" };
        if (config.PublicSiteOrigin != "https://localhost" || !new CommerceOptionsValidator(builder.Environment, Options.Create(authentication), [StripePaymentProvider.Registration]).Validate(null, config).Succeeded)
            throw new CheckFailure("The fixture requires complete sandbox settings and PublicSiteOrigin=https://localhost. No credentials were logged.");
        builder.WebHost.UseUrls("http://127.0.0.1:5216", "https://localhost:443");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(factory); builder.Services.AddSingleton(cache); builder.Services.AddSingleton(clock);
        builder.Services.AddSingleton<IOptions<StoreAuthenticationConfiguration>>(Options.Create(authentication));
        builder.Services.AddSingleton<IPurchaseRepository, PurchaseRepository>();
        builder.Services.AddCommerce();
        builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("Fixture", _ => { });
        builder.Services.AddAuthorization();
        var accounts = new AccountRepository(factory);
        var actor = await accounts.FindByUserNameAsync("COMMERCEBUYER") ?? await accounts.CreateAsync(new Account
        {
            Username = "COMMERCEBUYER",
            Email = "buyer@example.test",
            EmailVerifiedAt = clock.GetUtcNow().UtcDateTime,
            Salt = [1],
            Verifier = [2],
            JoinDate = clock.GetUtcNow().UtcDateTime,
            AccessLevel = AccountAccessLevel.Player | AccountAccessLevel.Admin
        });
        await using var app = builder.Build();
        var restart = false;
        app.UseMiddleware<ExceptionHandlerMiddleware>();
        app.Use(async (ctx, next) => { ctx.Response.Headers.CacheControl = "no-store"; await next(); });
        app.UseAuthentication(); app.UseAuthorization();
        var api = app.MapGroup("/api");
        // Fixture-only recovery control; never registered by the application API.
        api.MapPost("/__fixture/restart", (HttpContext ctx, IHostApplicationLifetime lifetime) =>
        {
            if (ctx.Connection.RemoteIpAddress is not { } address || !System.Net.IPAddress.IsLoopback(address))
                return Results.NotFound();
            restart = true;
            ctx.Response.OnCompleted(() => { lifetime.StopApplication(); return Task.CompletedTask; });
            return Results.Accepted();
        }).RequireAuthorization();
        AuthenticateResponse Reply() => new() { Token = FixtureAuthentication.Token, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), Status = Avalon.Api.Contract.AuthenticationResponseStatus.Success };
        api.MapPost("/account/authenticate", (HttpContext ctx) => { ctx.Response.Cookies.Append(FixtureAuthentication.Cookie, "fixture", new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/api" }); return Reply(); });
        api.MapPost("/account/refresh", (HttpContext ctx) => ctx.Request.Cookies[FixtureAuthentication.Cookie] == "fixture" ? Results.Ok(Reply()) : Results.Unauthorized());
        api.MapPost("/account/logout", (HttpContext ctx) => { ctx.Response.Cookies.Delete(FixtureAuthentication.Cookie, new CookieOptions { Path = "/api", Secure = true }); return Results.NoContent(); });
        api.MapGet("/account", () => new AccountDto { Id = actor.Id.Value, Username = actor.Username, Email = actor.Email!, EmailVerifiedAt = actor.EmailVerifiedAt, AccessLevel = (Avalon.Api.Contract.AccountAccessLevel)(int)actor.AccessLevel }).RequireAuthorization();
        api.MapGet("/account/game-license", (IPurchaseService service, HttpContext ctx) => service.GetLicenseStatusAsync(actor.Id, ctx.RequestAborted)).RequireAuthorization();
        api.MapGet("/account/purchases/{id:guid}", (Guid id, IPurchaseService service, HttpContext ctx) => service.GetOrderAsync(actor.Id, id, ctx.RequestAborted)).RequireAuthorization();
        api.MapPost("/account/purchases/checkout", (IPurchaseService service, HttpContext ctx) => service.CreateCheckoutAsync(actor.Id, actor.CredentialsVersion, "127.0.0.1", ctx.RequestAborted)).RequireAuthorization();
        api.MapGet("/admin/purchases", ([AsParameters] PurchaseSearch search, IPurchaseAdministrationService service, HttpContext ctx) => service.SearchAsync(search, ctx.RequestAborted)).RequireAuthorization();
        api.MapGet("/admin/purchases/{id:guid}", (Guid id, IPurchaseAdministrationService service, HttpContext ctx) => service.GetAsync(id, ctx.RequestAborted)).RequireAuthorization();
        api.MapPost("/admin/purchases/{id:guid}/refund", (Guid id, FullRefundRequest body, IPurchaseAdministrationService service, HttpContext ctx) => service.RequestFullRefundAsync(actor.Id, id, body.PaymentAttemptId, body.Reason, ctx.RequestAborted)).RequireAuthorization();
        api.MapPost("/admin/purchases/{id:guid}/retry", async (Guid id, IPurchaseAdministrationService service, HttpContext ctx) => { await service.RetryReconciliationAsync(actor.Id, id, ctx.RequestAborted); return Results.Accepted(); }).RequireAuthorization();
        app.MapPost("/payments/notifications/stripe", async (IPaymentNotificationService service, HttpContext ctx) =>
        {
            using var body = new MemoryStream(); var buffer = new byte[8192];
            while (true) { var count = await ctx.Request.Body.ReadAsync(buffer, ctx.RequestAborted); if (count == 0) break; if (body.Length + count > CommercePolicy.MaximumNotificationBytes) return Results.BadRequest(); await body.WriteAsync(buffer.AsMemory(0, count), ctx.RequestAborted); }
            var result = await service.AcceptAsync(StripePaymentProvider.ProviderName, body.ToArray(), ctx.Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase), ctx.RequestAborted);
            return result == NotificationAcceptance.Accepted ? Results.Ok() : Results.BadRequest();
        });
        api.MapGet("/account/email/verification", () => new { emailVerifiedAt = actor.EmailVerifiedAt, deliveryAvailable = false, resendAvailableAt = (DateTime?)null }).RequireAuthorization();
        api.MapGet("/account/links/steam/consolidations/pending", () => new { consolidation = (object?)null }).RequireAuthorization();
        api.MapGet("/pat", () => Array.Empty<object>()).RequireAuthorization();
        api.MapGet("/client/auth/sessions", () => Array.Empty<object>()).RequireAuthorization();
        api.MapGet("/mfa/status", () => new { enrolled = false }).RequireAuthorization();
        api.MapMethods("/{**unsupported}", ["GET", "POST", "PUT", "PATCH", "DELETE"], () => Results.NotFound());
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "tools", "Avalon.Commerce.Check"))) root = root.Parent;
        var publicDist = root is null ? "" : Path.GetFullPath(Path.Combine(root.FullName, "..", "Avalon.Dashboard", "apps", "public", "dist"));
        if (!Directory.Exists(publicDist)) throw new CheckFailure("Build the public Dashboard before starting the fixture UI.");
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(publicDist) });
        app.MapFallback(async ctx => { ctx.Response.ContentType = "text/html"; await ctx.Response.SendFileAsync(Path.Combine(publicDist, "index.html")); });
        Console.WriteLine("Local Stripe fixture: https://localhost. Sign in with the COMMERCEBUYER dummy fixture; no real account password is used.");
        Console.WriteLine("Forward sandbox Stripe notifications to http://127.0.0.1:5216/payments/notifications/stripe. This host does not test native SRP, launcher handoff or map entry.");
        await app.RunAsync();
        return restart;
    }
}
internal sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string Token = "commerce-sandbox-fixture";
    internal const string Cookie = "avalon-commerce-fixture";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.Authorization == "Bearer " + Token
        ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "fixture")], Scheme.Name)), Scheme.Name)) : AuthenticateResult.NoResult());
}
