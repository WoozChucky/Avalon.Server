namespace Avalon.Infrastructure.GameAuth;

public static class GameContextAuthorizationWindow
{
    public static DateTime? Deadline(GameContextRecord context)
    {
        if (context.AuthorizationValidUntil is not { } until) return null;
        if (context.AbsoluteExpiresAt < until) until = context.AbsoluteExpiresAt;
        if (context.IdentityValidUntil is { } identity && identity < until) until = identity;
        return until;
    }
}
