namespace Avalon.Domain.World;

/// <summary>
/// What choosing a dialogue option does besides moving the conversation (spec #463). Stored as a
/// number, so values are only ever appended.
/// </summary>
public enum DialogueOptionAction
{
    /// <summary>Opens the character's bank. A creature whose dialogue offers this is a banker.</summary>
    OpenBank = 0,

    /// <summary>Opens the NPC's shop (#432). A creature whose dialogue offers this is a vendor.</summary>
    OpenShop = 1,
}
