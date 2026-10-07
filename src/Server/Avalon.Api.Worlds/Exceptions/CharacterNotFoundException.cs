namespace Avalon.Api.Worlds.Exceptions;

/// <summary>
/// The character a request names is gone (#757): deleted between the endpoint's lookup and its write. Answered as the
/// endpoint answers a request for a missing character, MVC's 404 client-error ProblemDetails.
/// </summary>
public sealed class CharacterNotFoundException : Exception
{
    public CharacterNotFoundException() : base("Character not found")
    {
    }
}
