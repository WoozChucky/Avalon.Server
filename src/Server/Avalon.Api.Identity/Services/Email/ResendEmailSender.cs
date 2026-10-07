using System.Net.Http.Headers;
using System.Text.Json;
using Avalon.Api.Identity.Config;
using Avalon.Api.Identity.Exceptions;

namespace Avalon.Api.Identity.Services.Email;

/// <summary>One bounded send to a fixed provider; unknown outcomes are not retried.</summary>
public sealed class ResendEmailSender(HttpClient http, EmailConfig config) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string textBody, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(textBody);
        if (to.AsSpan().IndexOfAny('\r', '\n') >= 0 || subject.AsSpan().IndexOfAny('\r', '\n') >= 0)
            throw new ArgumentException("Mail headers cannot contain line breaks.");
        ct.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ResendApiKey);
        request.Content = JsonContent.Create(new
        {
            from = string.IsNullOrEmpty(config.FromName) ? config.From : $"{config.FromName} <{config.From}>",
            to = new[] { to },
            subject,
            text = textBody,
        });
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new EmailDeliveryException();
            using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("id", out JsonElement id)
                || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
            {
                throw new EmailDeliveryException();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            // Provider bodies/transport exceptions can contain credentials and message tokens.
            throw new EmailDeliveryException();
        }
    }
}
