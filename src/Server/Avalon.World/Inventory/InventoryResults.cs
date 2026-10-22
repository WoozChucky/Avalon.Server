namespace Avalon.World.Inventory;

public enum InventoryAddResult
{
    Ok,
    InventoryFull,
    UnknownTemplate,
    UniqueAlreadyOwned,
}

public enum InventoryRemoveResult
{
    Ok,
    NotEnough,
    NotFound,

    /// <summary>The instance is offered in a trade (#1175).</summary>
    Locked,
}

public enum WalletResult
{
    Ok,
    NotEnoughGold,
    GoldCapReached,
}
