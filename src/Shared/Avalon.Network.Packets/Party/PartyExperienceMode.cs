namespace Avalon.Network.Packets.Party;

/// <summary>How a party shares a kill's experience. Append-only.</summary>
public enum PartyExperienceMode : byte
{
    Unknown = 0,

    /// <summary>Equal shares, plus the party bonus. The default.</summary>
    Even = 1,

    /// <summary>Shares by level, plus the party bonus.</summary>
    LevelWeighted = 2,
}
