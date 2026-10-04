using System.Security.Claims;
using AspNet.Security.OpenId;
using AspNet.Security.OpenId.Steam;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Authentication;

public static class SteamWebLinkRegistration
{
    public const string TransactionProperty = "avalon.steam-link.transaction";
    public static string CookieName(Guid id) => "__Host-AvalonSteamLink-" + id.ToString("N");
    public static void AddSteamWebLink(this IServiceCollection services)
    {
        services.AddOptions<SteamWebLinkOptions>().BindConfiguration(SteamWebLinkOptions.Section);
        services.AddScoped<SteamWebLinkStore>();
        services.AddAuthentication().AddCookie("AvalonSteamLinkUnused", o =>
        {
            o.Cookie.Name = "__Host-AvalonSteamLinkUnused"; o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }).AddSteam(SteamWebLinkOptions.Scheme, o =>
        {
            o.SignInScheme = "AvalonSteamLinkUnused";
            o.CallbackPath = SteamWebLinkOptions.CallbackPath;
            o.Configuration = new() { AuthenticationEndpoint = SteamWebLinkOptions.ProviderEndpoint };
            o.ApplicationKey = null; o.UserInformationEndpoint = string.Empty; o.Attributes.Clear();
            o.Backchannel = new HttpClient(new SteamOpenIdBackchannelHandler()) { Timeout = TimeSpan.FromSeconds(5) };
            o.CorrelationCookie.Name = "__Host-AvalonSteamCorrelation.";
            o.CorrelationCookie.Path = "/"; o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
            o.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(5);
            o.Events.OnRedirectToIdentityProvider = context =>
            {
                var trusted = context.HttpContext.RequestServices.GetRequiredService<IOptions<SteamWebLinkOptions>>().Value;
                trusted.Validate();
                context.Properties.Items[OpenIdAuthenticationConstants.Properties.ReturnTo] = trusted.CallbackUrl;
                context.ProtocolMessage.Realm = trusted.CallbackUrl[..^SteamWebLinkOptions.CallbackPath.Length];
                context.ProtocolMessage.ReturnTo = QueryHelpers.AddQueryString(trusted.CallbackUrl, "state", context.Options.StateDataFormat!.Protect(context.Properties));
                return Task.CompletedTask;
            };
            o.Events.OnTicketReceived = async context =>
            {
                context.HandleResponse(); // Never sign in a Steam principal or mint an Avalon bearer token.
                var services = context.HttpContext.RequestServices;
                var trusted = services.GetRequiredService<IOptions<SteamWebLinkOptions>>().Value;
                var ok = false; Guid? transaction = null;
                if (context.Properties?.Items.TryGetValue(TransactionProperty, out var value) == true && Guid.TryParseExact(value, "N", out var id))
                {
                    transaction = id;
                    var cookie = context.Request.Cookies[CookieName(id)] ?? string.Empty;
                    var store = services.GetRequiredService<SteamWebLinkStore>();
                    var record = await store.ReadCallbackAsync(id, cookie, context.HttpContext.RequestAborted);
                    var root = record is null ? null : await services.GetRequiredService<IAccountRepository>().FindByIdAsync(record.AccountId, false, context.HttpContext.RequestAborted);
                    var claimed = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (root is not null && claimed is not null && context.HttpContext.Items["steam-link.nonce"] is string nonce)
                        ok = await store.VerifyAsync(id, cookie, root, claimed, nonce, context.HttpContext.RequestAborted);
                }
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Redirect(trusted.ResultUrl(transaction, !ok));
            };
            o.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Redirect(context.HttpContext.RequestServices.GetRequiredService<IOptions<SteamWebLinkOptions>>().Value.ResultUrl(null));
                return Task.CompletedTask;
            };
        });
        services.AddOptions<SteamAuthenticationOptions>(SteamWebLinkOptions.Scheme).Configure<GameAuthCryptography>((options, crypto) =>
            options.StateDataFormat = new SteamOpenIdStateFormat(crypto));
    }
}
