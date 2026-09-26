namespace Avalon.Network.Packets.World;

/// <summary>
/// What choosing a dialogue option does besides moving the conversation, carried on
/// <see cref="SDialogueOptionInfo" /> (#522), so a client can hide or mark an option it has not
/// built the window for. It names only an action the server will actually run when the option is
/// chosen; an option that runs none is <see cref="Conversation" />.
/// </summary>
/// <remarks>
/// Values are only ever appended. <b>A client must treat any value it does not know as "do not
/// offer this option"</b>: a newer server may add one whose window the client cannot show.
/// This is the wire's own enum, mapped explicitly from the server's stored DialogueOptionAction
/// (whose numbers differ); the two are never cast one to the other.
/// </remarks>
public enum DialogueOptionKind
{
    /// <summary>
    /// Only moves the conversation, or ends it. Also what an option without the field decodes as.
    /// </summary>
    Conversation = 0,

    /// <summary>Opens the character's bank: the Bank slots follow in SMSG_INVENTORY_UPDATE.</summary>
    OpenBank = 1,

    /// <summary>Opens the NPC's shop: SMSG_VENDOR_LIST follows.</summary>
    OpenShop = 2,
}
