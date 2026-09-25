using Avalon.World.Entities;
using Avalon.World.Inventory;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Inventory;

public class CharacterWalletShould
{
    [Fact]
    public void Spend_what_the_character_has_and_refuse_more()
    {
        CharacterEntity character = New(money: 100);
        var wallet = new CharacterWallet(character, maxMoney: 1_000);

        Assert.Equal(WalletResult.Ok, wallet.CanSpend(100));
        Assert.Equal(WalletResult.NotEnoughMoney, wallet.TrySpend(101));
        Assert.Equal(100UL, wallet.Balance);
        Assert.Equal(WalletResult.Ok, wallet.TrySpend(40));
        Assert.Equal(60UL, wallet.Balance);
        Assert.Equal(60UL, character.Data!.Money);
    }

    [Fact]
    public void Cap_additions_at_the_configured_maximum()
    {
        var wallet = new CharacterWallet(New(money: 990), maxMoney: 1_000);

        Assert.Equal(WalletResult.Ok, wallet.TryAddMoney(10));
        Assert.Equal(WalletResult.MoneyCapReached, wallet.TryAddMoney(1));
        Assert.Equal(1_000UL, wallet.Balance);
    }

    /// <summary>Review Focus 3.</summary>
    [Fact]
    public void Refuse_an_addition_past_the_largest_balance_without_wrapping()
    {
        var wallet = new CharacterWallet(New(money: ulong.MaxValue - 5), maxMoney: ulong.MaxValue);

        Assert.Equal(WalletResult.MoneyCapReached, wallet.TryAddMoney(10));
        Assert.Equal(ulong.MaxValue - 5, wallet.Balance);
    }

    /// <summary>Review Focus 5.</summary>
    [Fact]
    public void Refuse_additions_to_a_balance_already_above_a_lowered_cap()
    {
        var wallet = new CharacterWallet(New(money: 800), maxMoney: 500);

        Assert.Equal(WalletResult.MoneyCapReached, wallet.TryAddMoney(1));
        Assert.Equal(800UL, wallet.Balance);
        Assert.Equal(WalletResult.Ok, wallet.TrySpend(300));
        Assert.Equal(500UL, wallet.Balance);
    }

    [Fact]
    public void Mark_money_dirty_and_tell_the_client_only_when_the_balance_moved()
    {
        CharacterEntity character = New(money: 10);
        var wallet = new CharacterWallet(character, maxMoney: 100);

        wallet.TrySpend(11);
        wallet.TryAddMoney(0);
        wallet.TrySpend(0);
        Assert.False(character.SaveState.MoneyDirty);
        Assert.False(character.ClientChanges.MoneyChanged);

        wallet.TryAddMoney(5);
        Assert.True(character.SaveState.MoneyDirty);
        Assert.True(character.ClientChanges.MoneyChanged);
    }

    [Fact]
    public void Read_the_cap_from_configuration()
    {
        Assert.Equal(9_999_999_999UL, new Avalon.World.Configuration.GameConfiguration().MaxMoney);
    }
}
