namespace Avalon.World;

/// <summary>
/// The server's side of a character leave (#663). Lives in Avalon.World, outside Avalon.World.Public,
/// so the modding API can read <c>IWorldConnection.LeaveInProgress</c> but never start or end one.
/// </summary>
public interface ICharacterLeaveControl
{
    /// <summary>Marks a leave as under way. False when one already is, and nothing changes.</summary>
    bool TryBeginLeave();

    /// <summary>Ends the leave under way, whether it succeeded or not.</summary>
    void EndLeave();

    /// <summary>
    /// Forgets what the connection knew about the character it held: the last input sequence, the
    /// target, a respawn in flight and an open conversation. What belongs to the account (locale,
    /// access level) and to the connection (time sync) stays.
    /// </summary>
    void ResetCharacterState();
}
