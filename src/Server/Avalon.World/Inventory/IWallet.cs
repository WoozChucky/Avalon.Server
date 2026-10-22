namespace Avalon.World.Inventory;

/// <summary>One character's gold. Tick thread only; all or nothing.</summary>
public interface IWallet
{
    ulong Balance { get; }

    /// <summary>Against the balance less any gold held for a trade (#1175).</summary>
    WalletResult CanSpend(ulong gold);

    WalletResult TrySpend(ulong gold);

    /// <summary>Refused whole, with GoldCapReached, if the balance would pass the configured maximum.</summary>
    WalletResult TryAddGold(ulong gold);
}
