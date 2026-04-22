using System.Security.Claims;
using Avalon.Common.ValueObjects;

namespace Avalon.Api.Authentication;

public static class ClaimsPrincipalExtensions
{
    public static AccountId AccountId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("missing sub claim");
        return new AccountId(long.Parse(raw));
    }

    // Role claims are emitted per set flag in AccountAccessLevel. An Admin principal
    // does NOT automatically satisfy IsInRole("GameMaster") unless that flag is set too.
    // This helper walks the hierarchy explicitly.
    private static readonly string[] Ladder =
        { AvalonRoles.Player, AvalonRoles.GameMaster, AvalonRoles.Admin, AvalonRoles.Console };

    public static bool HasRoleAtLeast(this ClaimsPrincipal user, string minRole)
    {
        var minIdx = Array.IndexOf(Ladder, minRole);
        if (minIdx < 0) return false;

        for (var i = minIdx; i < Ladder.Length; i++)
            if (user.IsInRole(Ladder[i])) return true;

        return false;
    }
}
