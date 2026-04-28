using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.World.Public.Characters;
using Avalon.World.Respawn;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.World;

public class DeSpawnPlayerAsyncShould
{
    [Fact]
    public async Task Persist_character_at_town_with_full_HP_when_dying_at_logout()
    {
        // Logout-while-dead must call Revive() on the live entity (clears IsDead, restores HP)
        // and rewrite the dbCharacter row to point at the resolved town with full HP. No DB
        // column for IsDead exists — the flag is purely runtime state and is always cleared
        // before persistence. Character.Map is ushort; Character.Health is int (max HP, no
        // separate CurrentHealth column).

        var resolver = Substitute.For<IRespawnTargetResolver>();
        resolver.ResolveTownAsync(new MapTemplateId(2), Arg.Any<CancellationToken>())
            .Returns(new MapTemplateId(1));

        // dbCharacter uses ushort Map and int Health (max HP only — no CurrentHealth column).
        var dbCharacter = new Character { Id = new CharacterId(1), Map = 2, Health = 100 };

        var charEntity = Substitute.For<ICharacter>();
        charEntity.IsDead.Returns(true);
        charEntity.Map.Returns(new MapId(2));
        charEntity.Health.Returns(100u);

        await Avalon.World.World.ApplyDeathLogoutAsync(charEntity, dbCharacter, resolver, CancellationToken.None);

        // Live entity is revived (Revive() invoked once).
        charEntity.Received(1).Revive();
        // Persisted row points at town map 1 with full HP.
        Assert.Equal((ushort)1, dbCharacter.Map);
        Assert.Equal(100, dbCharacter.Health);
    }
}
