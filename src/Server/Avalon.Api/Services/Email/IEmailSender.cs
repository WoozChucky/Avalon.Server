namespace Avalon.Api.Services.Email;

/// <summary>
/// Sends one plain-text email (#510). Registered only when <c>Application:Email:Sender</c> names a
/// sender, so whether the api can send email at all is whether this is registered. It returns once
/// the email is handed on, or throws; the caller decides what a failure means.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string textBody, CancellationToken ct);
}
