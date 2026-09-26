namespace Avalon.Api.Exceptions;

/// <summary>
/// A refresh token presented again inside <c>RefreshTokenService.RotationGrace</c> of the rotation
/// that replaced it, while that rotation's child is still unused (#495 review): a second tab or a
/// client retry. Answered 401, without revoking the family, publishing a disconnect or clearing the
/// refresh cookie, which by now holds the winner's token.
/// </summary>
public sealed class RefreshAlreadyRotatedException : UnauthorizedAccessException
{
    public RefreshAlreadyRotatedException() : base("Refresh token already rotated")
    {
    }
}
