using Avalon.Domain.Characters;
using Avalon.World.Entities;

namespace Avalon.World.Inventory;

/// <summary><see cref="IWallet" /> over <c>Character.Money</c>. The one writer of a character's gold.</summary>
public sealed class CharacterWallet(CharacterEntity owner, ulong maxMoney) : IWallet
{
    private Character Row => owner.Data
        ?? throw new InvalidOperationException("A character without a row has no wallet.");

    public ulong Balance => Row.Money;

    public WalletResult CanSpend(ulong copper) =>
        copper <= Row.Money ? WalletResult.Ok : WalletResult.NotEnoughMoney;

    public WalletResult TrySpend(ulong copper)
    {
        WalletResult result = CanSpend(copper);
        if (result != WalletResult.Ok || copper == 0)
            return result;

        Row.Money -= copper;
        Changed();
        return WalletResult.Ok;
    }

    public WalletResult TryAddMoney(ulong copper)
    {
        if (copper == 0)
            return WalletResult.Ok;

        // Written so nothing can wrap: a balance already at or above a (lowered) cap refuses every
        // addition, and the headroom subtraction only runs when it cannot underflow.
        ulong balance = Row.Money;
        if (balance >= maxMoney || copper > maxMoney - balance)
            return WalletResult.MoneyCapReached;

        Row.Money = balance + copper;
        Changed();
        return WalletResult.Ok;
    }

    private void Changed()
    {
        owner.SaveState.MoneyChanged();
        owner.ClientChanges.RecordMoney();
    }
}
