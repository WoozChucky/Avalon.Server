namespace Avalon.Api.Config;

/// <summary>
/// Account email delivery and current-address verification, bound from <c>Application:Email</c>.
/// </summary>
public class EmailConfig
{
    public const string Section = "Application:Email";

    /// <summary><see cref="EmailSenderKind.None"/> unless set.</summary>
    public EmailSenderKind Sender { get; set; } = EmailSenderKind.None;

    /// <summary>
    /// Where the pickup sender writes its .eml files; created when missing, on Unix readable by its
    /// owner only (0700). Under the user's local application data, not the shared temp folder.
    /// </summary>
    public string PickupDirectory { get; set; } =
        Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "avalon-mail");

    /// <summary>The address every email is sent from. Required, and a bare address, when a sender is set.</summary>
    public string? From { get; set; }
    public string? FromName { get; set; }
    /// <summary>Server secret; never logged or emitted into browser configuration.</summary>
    public string? ResendApiKey { get; set; }
    /// <summary>Website origin for verification and email-change links; null leaves current-address verification unavailable.</summary>
    public string? VerificationSiteOrigin { get; set; }
    public int VerificationCooldownSeconds { get; set; } = 60;
    public int MaxVerificationSendsPerAccount { get; set; } = 5;
    public int MaxVerificationSendsPerSource { get; set; } = 20;
}
