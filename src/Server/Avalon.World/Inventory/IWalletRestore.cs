namespace Avalon.World.Inventory;

/// <summary>
/// The one way to give gold back past the cap (#1181), kept off <see cref="IWallet" /> and internal, so only
/// Avalon.World's own handlers reach it, by asking the wallet the economy gave them for it. <see cref="CharacterWallet" />
/// implements it.
/// </summary>
internal interface IWalletRestore
{
    /// <summary>
    /// Gives back <paramref name="gold" /> that <see cref="IWallet.TrySpend" /> took earlier in the same handler call, on
    /// the same tick, when the work it paid for changed nothing. Never refused: it restores a balance the character held
    /// a moment ago, so neither the cap nor a trade's incoming gold applies. Any other gold goes through
    /// <see cref="IWallet.TryAddGold" />.
    /// </summary>
    void GiveBack(ulong gold);
}
