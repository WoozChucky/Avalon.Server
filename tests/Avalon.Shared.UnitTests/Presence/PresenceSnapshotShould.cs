using Avalon.Infrastructure;
using Avalon.Infrastructure.Presence;
using Xunit;

namespace Avalon.Shared.UnitTests.Presence;

public class PresenceSnapshotShould
{
    private static WorldPresenceSnapshot Sample() => new(
        WorldId: 1,
        CapturedAt: new DateTime(2026, 9, 16, 16, 22, 31, DateTimeKind.Utc),
        Instances:
        [
            new InstancePresenceSnapshot(
                InstanceId: Guid.Parse("8f3c1d2e-0000-0000-0000-000000000001"),
                TemplateId: 12,
                Seed: -1044266558,
                MapType: "Normal",
                ConfigVersion: "a91f3c7e",
                OwnerCharacterId: 4417,
                Characters:
                [
                    new CharacterPresenceSnapshot(
                        CharacterId: 4417, Name: "Nym", Class: "Wizard",
                        X: 412.5f, Y: 0f, Z: -87.25f, Orientation: 1.57f,
                        Level: 34, CurrentHealth: 812, Health: 1200,
                        MoveState: "Running", InCombat: true, Dead: false,
                        LastSeen: new DateTime(2026, 9, 16, 16, 22, 31, DateTimeKind.Utc))
                ])
        ]);

    [Fact]
    public void Should_round_trip_through_json()
    {
        WorldPresenceSnapshot original = Sample();

        string json = PresenceJson.Serialize(original);
        WorldPresenceSnapshot? back = PresenceJson.Deserialize<WorldPresenceSnapshot>(json);

        Assert.NotNull(back);
        Assert.Equal(original.WorldId, back!.WorldId);
        Assert.Single(back.Instances);
        Assert.Equal(-1044266558, back.Instances[0].Seed);
        Assert.Equal("a91f3c7e", back.Instances[0].ConfigVersion);
        Assert.Equal("Nym", back.Instances[0].Characters[0].Name);
        Assert.Equal(412.5f, back.Instances[0].Characters[0].X);
    }

    [Fact]
    public void Should_use_camel_case_property_names()
    {
        string json = PresenceJson.Serialize(Sample());

        Assert.Contains("\"worldId\"", json);
        Assert.Contains("\"configVersion\"", json);
        Assert.DoesNotContain("\"WorldId\"", json);
    }

    [Fact]
    public void Should_round_trip_the_character_index()
    {
        var index = new CharacterPresenceIndex(
            WorldId: 7, InstanceId: Guid.Parse("8f3c1d2e-0000-0000-0000-000000000001"));

        CharacterPresenceIndex? back =
            PresenceJson.Deserialize<CharacterPresenceIndex>(PresenceJson.Serialize(index));

        Assert.NotNull(back);
        Assert.Equal((ushort)7, back!.WorldId);
    }

    [Fact]
    public void Should_return_null_for_malformed_json()
    {
        Assert.Null(PresenceJson.Deserialize<WorldPresenceSnapshot>("{not json"));
    }

    [Fact]
    public void Should_build_namespaced_cache_keys()
    {
        Assert.Equal("world:3:presence", CacheKeys.WorldPresence(3));
        Assert.Equal("presence:character:4417", CacheKeys.CharacterPresenceIndex(4417));
        Assert.Equal(TimeSpan.FromSeconds(5), CacheKeys.PresenceTtl);
    }
}
