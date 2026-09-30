namespace Avalon.Network.Packets.Social;

/// <summary>
/// Which channel a chat line belongs to. Append-only. Say is 0 on purpose: a payload written before the
/// field existed reads as Say, which is what every such line was.
/// </summary>
public enum ChatChannel : byte
{
    /// <summary>Said aloud: heard by everyone in the sender's instance.</summary>
    Say = 0,

    /// <summary>To the sender's party, wherever its members are.</summary>
    Party = 1,

    /// <summary>From the server: command answers, party notices, countdowns, scaling.</summary>
    System = 2,

    /// <summary>
    /// A whisper (#717): from one player to one other, wherever each is on the world server. The recipient's line
    /// carries the sender in CharacterName; the sender's own echo carries the recipient in TargetName as well.
    /// </summary>
    Whisper = 3,
}
