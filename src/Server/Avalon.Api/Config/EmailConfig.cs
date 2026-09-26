namespace Avalon.Api.Config;

/// <summary>
/// How the api sends email (#510), bound from <c>Application:Email</c>. The one feature that needs
/// it today is email change, which is on only while a sender is configured.
/// </summary>
public class EmailConfig
{
    public const string Section = "Application:Email";

    /// <summary><see cref="EmailSenderKind.None"/> unless set.</summary>
    public EmailSenderKind Sender { get; set; } = EmailSenderKind.None;

    /// <summary>Where the pickup sender writes its .eml files; created when missing.</summary>
    public string PickupDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "avalon-mail");

    /// <summary>The address every email is sent from. Required, and a bare address, when a sender is set.</summary>
    public string? From { get; set; }
}
