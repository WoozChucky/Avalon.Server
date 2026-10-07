using Avalon.Common.Accounts;

namespace Avalon.Api.Distribution;

/// <summary>
/// Who may use a build channel. Each channel mirrors a world (live = Asthoria, ptr = the Public Test
/// Realm, dev = Development) and uses the same rule the world list does, AccessLevels.ForWorld:
/// access levels are flags, so a PTR account is not "above" a Player one and an ordinal check would
/// let PTR (32) into dev (Admin, 4).
/// </summary>
public static class ChannelAccess
{
    public static bool Allows(Channel channel, AccountAccessLevel caller) =>
        AccessLevels.ForWorld(Required(channel)).Allows(caller);

    /// <summary>The channels <paramref name="caller" /> may list; an anonymous caller sees live only.</summary>
    public static IReadOnlyList<Channel> Visible(AccountAccessLevel? caller) =>
        caller is { } level
            ? Enum.GetValues<Channel>().Where(c => Allows(c, level)).ToList()
            : [Channel.Live];

    private static AccountAccessLevel Required(Channel channel) => channel switch
    {
        Channel.Live => AccountAccessLevel.Player,
        Channel.Ptr => AccountAccessLevel.PTR,
        Channel.Dev => AccountAccessLevel.Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };
}
