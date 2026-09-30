namespace Avalon.Network.Packets.Party;

/// <summary>The answer to one party request, or an invite's end. Append-only; 0 is what a payload without the field reads as.</summary>
public enum PartyResult : byte
{
    Unknown = 0,
    Ok = 1,
    NotLeader = 2,
    NotFound = 3,
    AlreadyInParty = 4,
    PartyFull = 5,
    InvitePending = 6,
    NoInvite = 7,
    InCombat = 8,
    OnCooldown = 9,
    Self = 10,
    NotInParty = 11,
    InviteExpired = 12,
    InviteDeclined = 13,

    /// <summary>A request the server cannot read: an experience mode of Unknown.</summary>
    Invalid = 14,
}
