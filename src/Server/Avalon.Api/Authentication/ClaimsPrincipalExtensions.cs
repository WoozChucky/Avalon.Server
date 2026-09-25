using System.Security.Claims;
using Avalon.Common.Accounts;
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

    /// <summary>
    /// The caller's <see cref="AccountAccessLevel"/>, folded back from the one GroupSid claim per set
    /// flag that JwtUtils and AvalonAuthenticationHandler emit. Only a claim whose value is exactly a
    /// flag's name counts, as with the authorization policies' RequireClaim; anything else, numeric
    /// strings included, is ignored. A principal with no such claim has level 0, which enters no world.
    /// </summary>
    public static AccountAccessLevel AccessLevel(this ClaimsPrincipal user)
    {
        AccountAccessLevel level = 0;

        foreach (var flag in Enum.GetValues<AccountAccessLevel>())
            if (user.HasClaim(ClaimTypes.GroupSid, flag.ToString()))
                level |= flag;

        return level;
    }

    // Role claims are emitted per set flag in AccountAccessLevel. An Admin principal
    // does NOT automatically satisfy IsInRole("GameMaster") unless that flag is set too.
    // This helper walks the hierarchy explicitly. Tournament and PTR sit on the Player rung, below
    // GameMaster: they are players with exactly the Player permission set (#447), so they satisfy
    // "at least Player" and nothing higher. Keep them after Player and before GameMaster.
    private static readonly string[] Ladder =
    {
        AvalonRoles.Player, AvalonRoles.Tournament, AvalonRoles.PTR,
        AvalonRoles.GameMaster, AvalonRoles.Admin, AvalonRoles.Console
    };

    public static bool HasRoleAtLeast(this ClaimsPrincipal user, string minRole)
    {
        var minIdx = Array.IndexOf(Ladder, minRole);
        if (minIdx < 0) return false;

        for (var i = minIdx; i < Ladder.Length; i++)
            if (user.IsInRole(Ladder[i])) return true;

        return false;
    }
}
