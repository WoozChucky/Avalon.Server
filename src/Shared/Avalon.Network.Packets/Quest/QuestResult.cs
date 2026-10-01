namespace Avalon.Network.Packets.Quest;

/// <summary>The answer to a quest request (#433). Append-only; 0 is what a payload without the field decodes as.</summary>
public enum QuestResult
{
    Unknown = 0,
    Ok = 1,
    /// <summary>The NPC does not offer that quest to this character now (or the quest does not exist).</summary>
    NotAvailable = 2,
    /// <summary>The character does not have that quest.</summary>
    NotActive = 3,
    /// <summary>The quest is not ready to turn in, or its items are no longer all in the bag.</summary>
    NotReady = 4,
    LogFull = 5,
    /// <summary>The reward items would not fit in the bag, even after the quest items leave it.</summary>
    BagFull = 6,
    /// <summary>The reward money would pass the character's gold cap.</summary>
    MoneyCap = 7,
    /// <summary>The NPC is past the dialogue leash; the conversation was ended.</summary>
    TooFar = 8,
    /// <summary>
    /// No open conversation with that NPC, or it is gone or dead, or the character is dead; for a turn-in, also an
    /// NPC that does not take that quest back.
    /// </summary>
    NoConversation = 9,
    /// <summary>
    /// The request threw (logged; whatever it had already changed stays changed, nothing is rolled back), or a
    /// turn-in's reward item can no longer be paid (a data fault, logged; the turn-in changed nothing).
    /// </summary>
    Error = 10,
}
