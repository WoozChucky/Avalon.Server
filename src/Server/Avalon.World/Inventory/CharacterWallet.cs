using Avalon.Domain.Characters;
using Avalon.World.Entities;

namespace Avalon.World.Inventory;

/// <summary>
/// <see cref="IWallet" /> over <c>Character.Gold</c>. The one writer of a character's gold. Gold offered in a trade
/// (#1175) cannot be spent, and gold a completing trade is about to pay counts toward the cap.
/// </summary>
public sealed class CharacterWallet(CharacterEntity owner, ulong maxGold) : IWallet
{
    /// <summary>The gold cap it was made under: an addition past it is refused.</summary>
    public ulong MaxGold => maxGold;

    private Character Row => owner.Data
        ?? throw new InvalidOperationException("A character without a row has no wallet.");

    public ulong Balance => Row.Gold;

    /// <summary>Only what is not held for a trade (#1175) can be spent.</summary>
    public WalletResult CanSpend(ulong gold) =>
        gold <= Spendable ? WalletResult.Ok : WalletResult.NotEnoughGold;

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

        // Nothing can wrap: a balance already at or above a (lowered) cap refuses every addition. Gold a
        // completing trade is about to pay (#1175) counts as already in the balance.
        ulong balance = Row.Gold;
        if (!owner.Locks.CanReceiveGold(balance, gold, maxGold))
            return WalletResult.GoldCapReached;

        Row.Gold = balance + gold;
        Changed();
        return WalletResult.Ok;
    }

    void IWallet.GiveBack(ulong gold)
    {
        if (gold == 0)
            return;

        // The balance it makes was the balance before the spend, so it fits wherever that did; checked, so a misuse
        // (gold this wallet never spent) throws rather than wraps.
        Row.Gold = checked(Row.Gold + gold);
        Changed();
    }

    private ulong Spendable => owner.Locks.Any ? owner.Locks.Spendable(Row.Gold) : Row.Gold;

    private void Changed()
    {
        owner.SaveState.GoldChanged();
        owner.ClientChanges.RecordGold();
    }
}
