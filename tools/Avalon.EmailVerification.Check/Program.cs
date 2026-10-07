using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Middlewares;
using Avalon.Api.Services.Email;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using Account = Avalon.Domain.Auth.Account;

// Opt-in only: no appsettings, credentials or live hosts are consulted.
try
{
    string rawConnection = Environment.GetEnvironmentVariable("AVALON_TEST_AUTH_DATABASE")
        ?? throw new CheckFailureException("Set AVALON_TEST_AUTH_DATABASE for the explicitly disposable database.");
    var connection = new NpgsqlConnectionStringBuilder(rawConnection);
    if (connection.Database != "avalon_email_verification_test" || connection.Host is not ("127.0.0.1" or "localhost" or "::1"))
        throw new CheckFailureException("Refusing any host/database except loopback avalon_email_verification_test.");
    var factory = new FixtureFactory(connection.ConnectionString);
    var accounts = new AccountRepository(factory);
    var repo = new AccountEmailVerificationRepository(factory);
    DateTime now = DateTime.UtcNow;
    const string first = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string replacement = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    if (!args.Contains("--serve"))
    {
        await using AuthDbContext db = factory.CreateDbContext();
        await db.Database.OpenConnectionAsync();
        await using DbCommand empty = db.Database.GetDbConnection().CreateCommand();
        empty.CommandText = "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'";
        Check(Convert.ToInt64(await empty.ExecuteScalarAsync()) == 0, "The disposable database must be empty; no tables are deleted by this checker.");
        string[] migrations = db.Database.GetMigrations().ToArray();
        int index = Array.FindIndex(migrations, x => x.EndsWith("_AccountEmailVerification", StringComparison.Ordinal));
        Check(index > 0, "Verification migration missing.");
        IMigrator migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[index - 1]);
        // The historical seed is an existing pre-change account. Select only columns that existed then.
        var old = await db.Accounts.AsNoTracking().Select(a => new { a.Id, a.Salt, a.Verifier, a.AccessLevel, a.CredentialsVersion, a.SessionEpoch }).FirstAsync();
        await migrator.MigrateAsync(migrations[index]);
        Account? upgraded = await accounts.FindByIdAsync(old.Id);
        Check(upgraded!.EmailVerifiedAt is null && upgraded.Salt.SequenceEqual(old.Salt) && upgraded.Verifier.SequenceEqual(old.Verifier)
            && upgraded.AccessLevel == old.AccessLevel && upgraded.CredentialsVersion == old.CredentialsVersion && upgraded.SessionEpoch == old.SessionEpoch,
            "Migration changed credentials, authority or verification of a pre-existing account.");
        Console.WriteLine("PASS historical migration preserves credentials/roles and leaves email unverified");

        Account race = await accounts.CreateAsync(NewAccount("EMAILRACE", now));
        EmailVerificationIssueResult[] issued = await Task.WhenAll(Issue(race, first, now), Issue(race, replacement, now));
        Check(issued.Count(x => x == EmailVerificationIssueResult.Issued) == 1 && issued.Count(x => x == EmailVerificationIssueResult.Cooldown) == 1,
            "Concurrent issuers must have one winner and one cooldown.");
        string digest = (await repo.FindAsync(race.Id, default))!.TokenHash;
        bool[] consumed = await Task.WhenAll(repo.ConsumeAsync(race.Id, digest, now, default), repo.ConsumeAsync(race.Id, digest, now, default));
        Check(consumed.Count(x => x) == 1, "Concurrent consumers must have exactly one winner.");
        Console.WriteLine("PASS cross-connection concurrent issuance and single consumption");

        Account late = await accounts.CreateAsync(NewAccount("EMAILREPLACE", now));
        await Issue(late, first, now);
        await Issue(late, replacement, now.AddSeconds(60));
        bool[] cleanup = await Task.WhenAll(repo.InvalidateAsync(late.Id, first, default), repo.ConsumeAsync(late.Id, replacement, now.AddSeconds(61), default));
        Check(!cleanup[0] && cleanup[1] && (await repo.FindAsync(late.Id, default))!.TokenHash == replacement,
            "An old send failure affected its replacement.");
        Console.WriteLine("PASS late send cleanup preserves current replacement proof");

        Account failed = await accounts.CreateAsync(NewAccount("EMAILFAILED", now));
        await Issue(failed, first, now);
        Check(await repo.InvalidateAsync(failed.Id, first, default) && !await repo.ConsumeAsync(failed.Id, first, now, default), "Failed send proof remained usable.");
        Check((await accounts.FindByIdAsync(failed.Id))!.EmailVerifiedAt is null, "Failure verified the address.");
        Console.WriteLine("PASS failed-send invalidation leaves account unverified");

        Account pickupAccount = await accounts.CreateAsync(NewAccount("EMAILPICKUP", now));
        string pickupDir = Path.Combine(Path.GetTempPath(), "avalon-email-check-" + Guid.NewGuid().ToString("N"));
        var config = new EmailConfig { Sender = EmailSenderKind.Pickup, From = "noreply@example.test", VerificationSiteOrigin = "http://localhost:5173", PickupDirectory = pickupDir };
        var service = new AccountEmailVerificationService(accounts, repo, BudgetCache(), config, TimeProvider.System, new PickupEmailSender(config, TimeProvider.System));
        await service.RequestAsync(pickupAccount.Id, "127.0.0.1", default);
        string mailPath = Directory.GetFiles(pickupDir, "*.eml").Single();
        string body = await File.ReadAllTextAsync(mailPath);
        Match match = System.Text.RegularExpressions.Regex.Match(body, @"#token=([A-Za-z0-9_-]{43})");
        Check(match.Success, "Pickup mail did not contain a bounded fragment link.");
        Check((await service.GetStatusAsync(pickupAccount.Id, default)).EmailVerifiedAt is null, "Opening mail verified the account.");
        await service.ConfirmAsync(pickupAccount.Id, match.Groups[1].Value, default);
        Check((await service.GetStatusAsync(pickupAccount.Id, default)).EmailVerifiedAt is not null, "Explicit Pickup confirmation did not verify.");
        Check(!await db.GameLicenses.AnyAsync(), "Email verification minted a license.");
        File.Delete(mailPath); Directory.Delete(pickupDir);
        await accounts.CreateAsync(NewAccount("EMAILSMOKE", now));
        Console.WriteLine("PASS real Pickup mail -> explicit confirmation -> verified account, no game license");
        Console.WriteLine("All PostgreSQL checks executed successfully. Remove only the dedicated test container when finished.");
        return;
    }

    // Optional UI smoke host. Only the fixture sign-in is fake; mail, repositories and verification are real.
    Account smoke = await accounts.FindByUserNameAsync("EMAILSMOKE") ?? throw new CheckFailureException("Run the checker first to create its smoke fixture.");
    WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
    builder.WebHost.UseUrls("http://127.0.0.1:5214");
    builder.Logging.ClearProviders(); // Never log request bodies, proof URLs or mail bodies.
    builder.Services.AddAuthentication("Smoke").AddScheme<AuthenticationSchemeOptions, SmokeAuthentication>("Smoke", _ => { });
    builder.Services.AddAuthorization();
    var mailConfig = new EmailConfig
    {
        Sender = EmailSenderKind.Pickup,
        From = "noreply@example.test",
        VerificationSiteOrigin = "http://localhost:5173",
        PickupDirectory = Path.Combine(Path.GetTempPath(), "avalon-email-smoke-pickup")
    };
    var smokeService = new AccountEmailVerificationService(accounts, repo, BudgetCache(), mailConfig, TimeProvider.System, new PickupEmailSender(mailConfig, TimeProvider.System));
    WebApplication app = builder.Build();
    app.UseMiddleware<ExceptionHandlerMiddleware>(); app.UseAuthentication(); app.UseAuthorization();
    app.MapPost("/account/authenticate", (HttpContext ctx) => { ctx.Response.Cookies.Append("email-smoke", "fixture", new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict }); return Reply(); });
    app.MapPost("/account/refresh", (HttpContext ctx) => ctx.Request.Cookies["email-smoke"] == "fixture" ? Results.Ok(Reply()) : Results.Unauthorized());
    app.MapGet("/account", async () => { Account a = (await accounts.FindByIdAsync(smoke.Id))!; return new AccountDto { Id = a.Id.Value, Username = a.Username, Email = a.Email!, EmailVerifiedAt = a.EmailVerifiedAt }; }).RequireAuthorization();
    app.MapGet("/account/email/verification", () => smokeService.GetStatusAsync(smoke.Id, default)).RequireAuthorization();
    app.MapPost("/account/email/verification", async (HttpContext ctx) => { await smokeService.RequestAsync(smoke.Id, "127.0.0.1", ctx.RequestAborted); return Results.Accepted(); }).RequireAuthorization();
    app.MapPost("/account/email/verification/confirm", async (AccountEmailVerificationConfirmRequest proof, HttpContext ctx) => { await smokeService.ConfirmAsync(smoke.Id, proof.Token, ctx.RequestAborted); return Results.NoContent(); }).RequireAuthorization();
    app.MapGet("/account/links/steam/consolidations/pending", () => new { state = "none" }).RequireAuthorization();
    app.MapGet("/smoke/email-link", async (HttpContext ctx) =>
    {
        if (ctx.Request.Cookies["email-smoke"] != "fixture") return Results.Unauthorized();
        string latest = Directory.GetFiles(mailConfig.PickupDirectory!, "*.eml").OrderByDescending(File.GetLastWriteTimeUtc).First();
        string mail = await File.ReadAllTextAsync(latest);
        Match link = System.Text.RegularExpressions.Regex.Match(mail, @"http://localhost:5173/account/email/verify#token=[A-Za-z0-9_-]{43}");
        Check(link.Success, "Pickup link not found.");
        return Results.Redirect(link.Value);
    });
    Console.WriteLine("Fixture UI host: http://127.0.0.1:5214. Use VITE_API_TARGET=http://127.0.0.1:5214 and the EMAILSMOKE fixture (dummy sign-in only).");
    await app.RunAsync();

    Task<EmailVerificationIssueResult> Issue(Account a, string hash, DateTime at) => repo.IssueAsync(a.Id, a.Email!, a.CredentialsVersion, hash, at, at.AddMinutes(30), TimeSpan.FromSeconds(60), default);
    static Account NewAccount(string name, DateTime joined) => new() { Username = name, Email = name.ToLowerInvariant() + "@example.test", Salt = [1, 2], Verifier = [3, 4], JoinDate = joined };
    static void Check(bool condition, string message) { if (!condition) throw new CheckFailureException(message); }
    static AuthenticateResponse Reply() => new() { Token = "email-smoke-fixture", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds(), Status = AuthenticationResponseStatus.Success };
    static IReplicatedCache BudgetCache()
    {
        IReplicatedCache cache = Substitute.For<IReplicatedCache>();
        var counts = new ConcurrentDictionary<string, long>();
        cache.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>()).Returns(call => counts.AddOrUpdate(call.ArgAt<string>(0), 1, (_, count) => count + 1));
        return cache;
    }
}
catch (CheckFailureException ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}
catch (Exception ex)
{
    // Provider errors can contain connection details or proof data. Report only the type.
    Console.Error.WriteLine($"Checker failed ({ex.GetType().Name}); no exception details were logged.");
    Environment.ExitCode = 1;
}
internal sealed class CheckFailureException(string message) : Exception(message);
internal sealed class FixtureFactory(string connection) : IDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(connection).Options);
}
internal sealed class SmokeAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
        Request.Headers.Authorization == "Bearer email-smoke-fixture"
            ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "fixture")], Scheme.Name)), Scheme.Name))
            : AuthenticateResult.NoResult());
}
