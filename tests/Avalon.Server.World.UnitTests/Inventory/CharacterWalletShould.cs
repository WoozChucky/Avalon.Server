using Avalon.World.Entities;
using Avalon.World.Inventory;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Inventory;

public class CharacterWalletShould
{
    [Fact]
    public void Spend_what_the_character_has_and_refuse_more()
    {
        CharacterEntity character = New(gold: 100);
        var wallet = new CharacterWallet(character, maxGold: 1_000);

        Assert.Equal(WalletResult.Ok, wallet.CanSpend(100));
        Assert.Equal(WalletResult.NotEnoughGold, wallet.TrySpend(101));
        Assert.Equal(100UL, wallet.Balance);
        Assert.Equal(WalletResult.Ok, wallet.TrySpend(40));
        Assert.Equal(60UL, wallet.Balance);
        Assert.Equal(60UL, character.Data!.Gold);
    }

    [Fact]
    public void Cap_additions_at_the_configured_maximum()
    {
        var wallet = new CharacterWallet(New(gold: 990), maxGold: 1_000);

        Assert.Equal(WalletResult.Ok, wallet.TryAddGold(10));
        Assert.Equal(WalletResult.GoldCapReached, wallet.TryAddGold(1));
        Assert.Equal(1_000UL, wallet.Balance);
    }

    /// <summary>Review Focus 3.</summary>
    [Fact]
    public void Refuse_an_addition_past_the_largest_balance_without_wrapping()
    {
        var wallet = new CharacterWallet(New(gold: ulong.MaxValue - 5), maxGold: ulong.MaxValue);

        Assert.Equal(WalletResult.GoldCapReached, wallet.TryAddGold(10));
        Assert.Equal(ulong.MaxValue - 5, wallet.Balance);
    }

    /// <summary>Review Focus 5.</summary>
    [Fact]
    public void Refuse_additions_to_a_balance_already_above_a_lowered_cap()
    {
        var wallet = new CharacterWallet(New(gold: 800), maxGold: 500);

        Assert.Equal(WalletResult.GoldCapReached, wallet.TryAddGold(1));
        Assert.Equal(800UL, wallet.Balance);
        Assert.Equal(WalletResult.Ok, wallet.TrySpend(300));
        Assert.Equal(500UL, wallet.Balance);
    }

    [Fact]
    public void Mark_gold_dirty_and_tell_the_client_only_when_the_balance_moved()
    {
        CharacterEntity character = New(gold: 10);
        var wallet = new CharacterWallet(character, maxGold: 100);

        wallet.TrySpend(11);
        wallet.TryAddGold(0);
        wallet.TrySpend(0);
        Assert.False(character.SaveState.GoldDirty);
        Assert.False(character.ClientChanges.GoldChanged);

        wallet.TryAddGold(5);
        Assert.True(character.SaveState.GoldDirty);
        Assert.True(character.ClientChanges.GoldChanged);
    }
}
