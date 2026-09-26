namespace Avalon.Domain.World;

/// <summary>
/// What choosing a dialogue option does besides moving the conversation (spec #463). Stored as a
/// number, so values are only ever appended. Vendors (#432) add theirs here.
/// </summary>
public enum DialogueOptionAction
{
    /// <summary>Opens the character's bank. A creature whose dialogue offers this is a banker.</summary>
    OpenBank = 0,
}
