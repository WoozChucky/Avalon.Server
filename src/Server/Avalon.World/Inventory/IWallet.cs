namespace Avalon.World.Inventory;

/// <summary>One character's gold. Tick thread only; all or nothing.</summary>
public interface IWallet
{
    ulong Balance { get; }

    /// <summary>Against the balance less any gold held for a trade (#1175).</summary>
    WalletResult CanSpend(ulong gold);

    WalletResult TrySpend(ulong gold);

    /// <summary>
    /// Refused whole, with GoldCapReached, if the balance would pass the configured maximum, counting gold a
    /// completing trade is about to pay (#1175) as already in it.
    /// </summary>
    WalletResult TryAddGold(ulong gold);

    /// <summary>
    /// Gives back <paramref name="gold" /> that <see cref="TrySpend" /> took earlier in the same handler call, on the
    /// same tick, when the work it paid for changed nothing. Never refused: it restores a balance the character held a
    /// moment ago, so neither the cap nor a trade's incoming gold applies. Any other gold goes through
    /// <see cref="TryAddGold" />. Internal, so only Avalon.World's own handlers reach it: being past the cap, it is no
    /// way to add gold for anything else.
    /// </summary>
    internal void GiveBack(ulong gold);
}
