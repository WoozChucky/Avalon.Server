using Avalon.Domain.Characters;
using Avalon.World.Entities;

namespace Avalon.World.Inventory;

/// <summary><see cref="IWallet" /> over <c>Character.Gold</c>. The one writer of a character's gold.</summary>
public sealed class CharacterWallet(CharacterEntity owner, ulong maxGold) : IWallet
{
    private Character Row => owner.Data
        ?? throw new InvalidOperationException("A character without a row has no wallet.");

    public ulong Balance => Row.Gold;

    public WalletResult CanSpend(ulong gold) =>
        gold <= Row.Gold ? WalletResult.Ok : WalletResult.NotEnoughGold;

    public WalletResult TrySpend(ulong gold)
    {
        WalletResult result = CanSpend(gold);
        if (result != WalletResult.Ok || gold == 0)
            return result;

        Row.Gold -= gold;
        Changed();
        return WalletResult.Ok;
    }

    public WalletResult TryAddGold(ulong gold)
    {
        if (gold == 0)
            return WalletResult.Ok;

        // Written so nothing can wrap: a balance already at or above a (lowered) cap refuses every
        // addition, and the headroom subtraction only runs when it cannot underflow.
        ulong balance = Row.Gold;
        if (balance >= maxGold || gold > maxGold - balance)
            return WalletResult.GoldCapReached;

        Row.Gold = balance + gold;
        Changed();
        return WalletResult.Ok;
    }

    private void Changed()
    {
        owner.SaveState.GoldChanged();
        owner.ClientChanges.RecordGold();
    }
}
