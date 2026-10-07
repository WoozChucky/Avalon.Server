using Avalon.Common.GameAuth;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Persistence;

namespace Avalon.Server.World.UnitTests.Persistence;

public sealed class GameplaySnapshotShould
{
    [Fact]
    public void Preserve_the_admitted_writer_in_an_off_thread_snapshot_after_the_entity_changes()
    {
        CharacterEntity character = TestCharacters.New();
        var authority = new GameplayWriteAuthority(character.Data!.AccountId, Guid.NewGuid(), 7);
        character.BindGameplayAuthority(authority);
        character.Data.Money = 25;
        var snapshot = CharacterSaveSnapshot.Take(character);
        character.Data.Money = 99;
        Assert.Same(authority, snapshot.Batch.Authority);
        Assert.Equal(25UL, snapshot.Batch.Row.Money);
        Assert.Equal(7, snapshot.Batch.Authority!.FencingToken);
    }
    [Fact]
    public void Never_relabel_an_old_entity_or_snapshot_as_a_replacement_session()
    {
        CharacterEntity character = TestCharacters.New();
        var old = new GameplayWriteAuthority(character.Data!.AccountId, Guid.NewGuid(), 7);
        var replacement = new GameplayWriteAuthority(character.Data.AccountId, Guid.NewGuid(), 8);
        character.BindGameplayAuthority(old);
        Assert.Throws<InvalidOperationException>(() => character.BindGameplayAuthority(replacement));
        Assert.Throws<InvalidOperationException>(() => CharacterSaveSnapshot.Take(character, replacement));
        Assert.Same(old, CharacterSaveSnapshot.Take(character).Batch.Authority);
    }
}
