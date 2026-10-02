namespace Avalon.Api.Exceptions;

/// <summary>
/// A rename refused because the character is in the world (#757, owner decision): its world server holds the name in
/// memory for the session, so the rename waits until the character is logged out. Answered as 409 ProblemDetails.
/// </summary>
public sealed class CharacterOnlineException : Exception
{
    public const string Detail = "Character is online; rename it while logged out.";

    public CharacterOnlineException() : base(Detail)
    {
    }
}
