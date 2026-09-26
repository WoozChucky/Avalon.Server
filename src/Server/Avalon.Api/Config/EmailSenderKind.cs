namespace Avalon.Api.Config;

/// <summary>Which email sender the api uses (#510), bound from <c>Application:Email:Sender</c>.</summary>
public enum EmailSenderKind
{
    /// <summary>No sender. Nothing is sent, so email change stays off (501).</summary>
    None = 0,

    /// <summary>
    /// Development only: every email is written as an .eml file into
    /// <see cref="EmailConfig.PickupDirectory"/>. Startup refuses it in any other environment.
    /// </summary>
    Pickup = 1,
}
