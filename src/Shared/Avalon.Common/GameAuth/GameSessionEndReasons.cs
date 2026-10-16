// Stable serialized vocabulary (#967): the world sends these on sessions/end. Keep the literals.
namespace Avalon.Common.GameAuth;

/// <summary>Why a world ended a game session, which decides whether the account's place is held.</summary>
public static class GameSessionEndReasons
{
    /// <summary>The client said goodbye (<c>CMSG_LEAVE_WORLD</c>): the place is released at once.</summary>
    public const string Logout = "logout";

    /// <summary>The connection closed or was lost without a goodbye: the place is held.</summary>
    public const string Drop = "drop";

    /// <summary>Kicked, banned or deactivated.</summary>
    public const string Kicked = "kicked";

    /// <summary>
    /// Released at character select by the world's choice. No world sends it today: a select the world could not finish
    /// (<c>DisconnectReason.SelectTimeout</c>) is reported as <see cref="Drop" />.
    /// </summary>
    public const string SelectTimeout = "select_timeout";

    /// <summary>A maintenance cutoff.</summary>
    public const string Maintenance = "maintenance";

    /// <summary>The world stopped or restarted: after it everyone is equal, so the place is released.</summary>
    public const string Shutdown = "shutdown";

    /// <summary>The world's backstop refused the admission.</summary>
    public const string Backstop = "backstop";

    /// <summary>
    /// Whether a session that ended for <paramref name="reason"/> keeps its place for the held-place grace: a drop, no
    /// reason (a world from before #967), or a reason this identity does not know (a newer world's), each read as a drop.
    /// </summary>
    public static bool HoldsPlace(string? reason) =>
        reason is not (Logout or Kicked or SelectTimeout or Maintenance or Shutdown or Backstop);
}
