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
}

public enum WalletResult
{
    Ok,
    NotEnoughMoney,
    MoneyCapReached,
}
