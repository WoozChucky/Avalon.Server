namespace Avalon.World.Inventory;

/// <summary>One character's gold, in copper. Tick thread only; all or nothing.</summary>
public interface IWallet
{
    ulong Balance { get; }

    WalletResult CanSpend(ulong copper);

    WalletResult TrySpend(ulong copper);

    /// <summary>Refused whole, with MoneyCapReached, if the balance would pass the configured maximum.</summary>
    WalletResult TryAddMoney(ulong copper);
}
