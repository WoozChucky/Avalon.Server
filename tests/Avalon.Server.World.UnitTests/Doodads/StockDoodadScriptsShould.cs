using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auras;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.Loot;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Auras;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Doodads;
using Avalon.World.Doodads.Scripts;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Parties;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Loot.LootTestData;

namespace Avalon.Server.World.UnitTests.Doodads;

/// <summary>
/// The stock chest, shrine and fountain, each used through the real interact handler in a real instance: the chest drops
/// its table as a kill drops, the shrine's outcome is drawn at use and lands on the players in its radius alone, and the
/// fountain restores its share, capped, to the living. The doodad stands at x 10 with a 10 m radius. Doodad templates 3-5,
/// character ids 107_0xx and auras 8-10 are this class's.
/// </summary>
public class StockDoodadScriptsShould
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Vector3 s_doodadAt = new(10f, 0f, 0f);

    private const uint IdA = 107_001, IdB = 107_002, IdC = 107_003, IdD = 107_004;

    /// <summary>
    /// Countdown: A, B and C form a party, A and B enter its instance, and A leaves the party there, so A uses the chest
    /// in its leave countdown: as for a kill, nobody is eligible, and the party instance's drops are free for all at once.
    /// Member in countdown: B leaves instead, so A uses it with B in its countdown beside it: B is no longer a member, so
    /// only A is eligible, as for a kill. #1179: the chest's blade rolls its copy as it drops, at an item level drawn from
    /// the use within the chest's depth band (3-5, the draw 4); an authored chest (solo, placed without a band) on a map
    /// with no level range rolls item level 1, drawing none.
    /// </summary>
    [Theory]
    [InlineData("solo")]
    [InlineData("authored")]
    [InlineData("party")]
    [InlineData("countdown")]
    [InlineData("member in countdown")]
    public async Task Drop_a_chests_table_at_its_feet_as_a_kill_drops(string mode)
    {
        bool party = mode == "party", countdown = mode == "countdown", memberCountdown = mode == "member in countdown";
        bool authored = mode == "authored";
        var partyId = new PartyId(70);
        PartyTestWorld? parties = null;
        PartyClient? partyA = null, partyB = null;
        if (countdown || memberCountdown)
        {
            parties = new PartyTestWorld();
            partyA = parties.Online(IdA);
            partyB = parties.Online(IdB);
            parties.Form(partyA, partyB, parties.Online(IdC));
            partyId = parties.Parties.PartyOf(IdA)!.Id;
        }

        // In the party instance: the first drop goes to the second eligible member, the second to the first.
        ScriptedCombatRandom draws = new ScriptedCombatRandom().Longs(1, 0);
        bool solo = mode is "solo" or "authored";
        using Fixture f = await Fixture.CreateAsync(owner: solo ? IdA : null,
            ownerParty: solo ? null : partyId, draws: draws, parties: parties?.Parties);
        Doodad chest = f.Place(Template(3, typeof(LootDoodadScript), loot: 1, gold: 5),
            authored ? null : new LevelRange(3, 5));
        f.ScriptRandom.NextInt64(3, 6).Returns(4L);
        MapInstanceClient a = f.Join(partyA?.Character ?? TestCharacters.New(IdA, time: f.Time), new Vector3(6f, 0f, 0f));
        MapInstanceClient b = f.Join(partyB?.Character ?? TestCharacters.New(IdB, time: f.Time), new Vector3(30f, 0f, 30f));
        MapInstanceClient stranger = f.Join(IdD, new Vector3(8f, 0f, 0f));
        if (party)
        {
            // B far off counts, as a party member anywhere in the instance; C, in another instance, never does.
            SetParty(a.Character, partyId);
            SetParty(b.Character, partyId);
            SetParty(TestCharacters.New(IdC), partyId);
        }

        if (countdown || memberCountdown)
        {
            uint leaving = countdown ? IdA : IdB;
            parties!.Instances.Owned[f.Instance.InstanceId] = partyId;
            Assert.Equal(PartyResult.Ok, parties.Parties.Leave(leaving));
            Assert.True(parties.Parties.InCountdown(leaving));
        }

        f.Use(a, chest);

        Assert.Equal(2, f.Instance.Drops.Count);   // the sword and the pile: Assert.All passes on nothing
        Assert.All(f.Instance.Drops.All, drop =>
        {
            Assert.Equal(s_now.UtcDateTime + (countdown ? TimeSpan.Zero : new GameConfiguration().LootGracePeriod),
                drop.FreeForAllAt);
            float dx = drop.Position.x - s_doodadAt.x, dz = drop.Position.z - s_doodadAt.z;
            Assert.True(dx * dx + dz * dz <= 1.5f * 1.5f, $"a drop at {drop.Position} is not at the chest's feet");
        });
        if (party)
        {
            Assert.Equal(IdB, f.Instance.Drops.All.Single(d => d.ItemTemplateId == s_blade.Id).OwnerCharacterId);
            Assert.Equal(IdA, f.Instance.Drops.All.Single(d => d.Gold > 0).OwnerCharacterId);
            Assert.Equal([(0L, 1L), (0L, 1L)], draws.WeaponRolls);   // two eligible, each draw
        }
        else
        {
            Assert.All(f.Instance.Drops.All, drop => Assert.Equal(countdown ? null : IdA, drop.OwnerCharacterId));
        }

        foreach (MapInstanceClient client in new[] { a, b, stranger })
        {
            SLootSpawnedPacket spawned = Assert.Single(client.Read<SLootSpawnedPacket>(NetworkPacketType.SMSG_LOOT_SPAWNED));
            Assert.Equal(2, spawned.Drops.Count);
            Assert.Equal(authored ? 1u : 4u, Assert.Single(spawned.Drops, d => d.ItemTemplateId == s_blade.Id.Value).ItemLevel);
        }

        Assert.Equal(authored ? 1 : 4, f.Instance.Drops.All.Single(d => d.ItemTemplateId == s_blade.Id).Rolled!.ItemLevel);
        f.ScriptRandom.Received(authored ? 0 : 1).NextInt64(3, 6);
    }

    [Fact]
    public async Task Apply_the_rolled_shrine_aura_to_every_player_in_its_radius()
    {
        using Fixture f = await Fixture.CreateAsync();
        Doodad shrine = f.Place(Template(4, typeof(ShrineDoodadScript), radius: 10f,
            outcomes: [new(new AuraId(8), 40), new(new AuraId(9), 40), new(new AuraId(10), 20)]));
        f.ScriptRandom.NextInt64(0, 100).Returns(85L);   // past 40 and 80: the third outcome

        MapInstanceClient atDoodad = f.Join(IdA, s_doodadAt);
        MapInstanceClient near = f.Join(IdB, new Vector3(10f, 3f, 9.9f));   // 9.9 m on X/Z, though 10.3 m in 3D
        MapInstanceClient past = f.Join(IdC, new Vector3(10f, 0f, 10.1f));
        MapInstanceClient lost = f.Join(IdD, new Vector3(float.NaN, 0f, 0f));
        f.Ticks(30);

        // Before the use nothing names an outcome: no aura packet names any aura, and the shrine is told as its template.
        foreach (MapInstanceClient client in new[] { atDoodad, near, past, lost })
        {
            Assert.Empty(client.Read<SAuraListPacket>(NetworkPacketType.SMSG_AURA_LIST).SelectMany(p => p.Entries));
            Assert.Empty(client.Read<SAuraUpdatePacket>(NetworkPacketType.SMSG_AURA_UPDATE).SelectMany(p => p.Entries));
        }

        Assert.Equal(4u, Assert.Single(atDoodad.Added(), s => s.Guid == shrine.Guid.RawValue).DoodadTemplateId);

        var boar = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 107_011),
            Metadata = BoarTemplate(null),
            Position = new Vector3(12f, 0f, 0f),
            Health = 50,
            CurrentHealth = 50,
        };
        f.Instance.AddCreature(boar);

        f.Use(atDoodad, shrine);

        foreach (MapInstanceClient reached in new[] { atDoodad, near })
        {
            // From nobody: no caster, no ability, no credit.
            ActiveAura applied = Assert.Single(reached.Character.Auras.All);
            Assert.Equal(new AuraId(10), applied.Id);
            Assert.Equal(0ul, applied.CasterGuid.RawValue);
            Assert.Equal(AuraSource.None, applied.Source);
        }

        Assert.Equal(0, past.Character.Auras.Count);
        Assert.Equal(0, lost.Character.Auras.Count);
        Assert.Equal(0, boar.Auras.Count);
    }

    [Fact]
    public async Task Restore_health_and_power_by_the_fountains_share_in_its_radius()
    {
        using Fixture f = await Fixture.CreateAsync();
        Doodad fountain = f.Place(Template(5, typeof(FountainDoodadScript), radius: 10f, restorePct: 50));
        MapInstanceClient low = f.Join(IdA, new Vector3(6f, 0f, 0f));
        MapInstanceClient high = f.Join(IdB, new Vector3(14f, 0f, 0f));
        MapInstanceClient dead = f.Join(IdC, new Vector3(10f, 0f, 4f));
        Pools(low.Character, current: 40);
        Pools(high.Character, current: 190);
        Pools(dead.Character, current: 0);
        dead.Character.IsDead = true;

        f.Use(low, fountain);

        Assert.Equal((140u, (uint?)140u), (low.Character.CurrentHealth, low.Character.CurrentPower));
        Assert.Equal((200u, (uint?)200u), (high.Character.CurrentHealth, high.Character.CurrentPower));
        Assert.Equal((0u, (uint?)0u), (dead.Character.CurrentHealth, dead.Character.CurrentPower));
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    /// <summary>A one-handed sword base (#1179), alone in its line, so its drop rolls its copy on this template.</summary>
    private static readonly ItemTemplate s_blade = new()
    {
        Id = new ItemTemplateId(210),
        Name = "Blade",
        Class = ItemClass.Weapon,
        SubClass = ItemSubClass.OneHanded,
        Slot = ItemSlotType.MainHand,
        BaseKind = ItemBaseKind.Sword,
        RequiredLevel = 1,
        MaxStackSize = 1,

        // A sound sword (#1179, ItemBaseStatRules): a damage type and its base-stat ranges, or the items area leaves it out.
        DamageType = DamageType.Physical,
        BaseStats =
        [
            new ItemTemplateBaseStat { TemplateId = 210, Stat = ItemBaseStat.DamageMin, Min = 2, Max = 4 },
            new ItemTemplateBaseStat { TemplateId = 210, Stat = ItemBaseStat.DamageMax, Min = 6, Max = 8 },
            new ItemTemplateBaseStat { TemplateId = 210, Stat = ItemBaseStat.AttackTime, Min = 665, Max = 735 },
        ],
    };

    private static DoodadTemplateView Template(uint id, Type script, int? loot = null, int gold = 0, float radius = 0f,
        uint restorePct = 0, IReadOnlyList<DoodadOutcomeView>? outcomes = null) => new(
        new DoodadTemplateId(id), script.Name, "props-kit", "Chest", null, null, null, null,
        DoodadInteraction.Click, 0, 5f, radius, DoodadUseLimit.OncePerInstance, DoodadSpentLook.ShowUsed, null, script,
        loot is { } table ? new LootTableId(table) : null, gold, gold, restorePct, DoodadCollision.None, 0f, 0f, 0f,
        null, null, outcomes ?? [], (uint)(outcomes?.Sum(o => o.Weight) ?? 0), ScriptCanSee: false);

    /// <summary>200 health and 200 mana, each at <paramref name="current" />.</summary>
    private static void Pools(CharacterEntity character, uint current)
    {
        character.Health = 200;
        character.CurrentHealth = current;
        character.PowerType = PowerType.Mana;
        character.Power = 200;
        character.CurrentPower = current;
    }

    /// <summary>The party a character is in, written as the party service writes it.</summary>
    private static void SetParty(CharacterEntity character, PartyId party) =>
        (typeof(CharacterEntity).GetProperty(nameof(CharacterEntity.PartyId))?.SetMethod
         ?? throw new MissingMethodException(nameof(CharacterEntity), "set_PartyId")).Invoke(character, [party]);

    /// <summary>
    /// An instance on a fake clock over open, flat ground, with the container's loot roller and allocator, the party
    /// service when given one (its leave countdowns), auras 8-10 and loot table 1 (one sword), and the use service and
    /// interact handler the container would hand it. The shrine's draws come from <see cref="ScriptRandom" />.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private Fixture(StaticData data, uint? owner, PartyId? ownerParty, ICombatRandom? draws, PartyService? parties)
        {
            Time = new FakeTimeProvider(s_now);
            IWorld world = MapInstanceClients.NewWorld(data);
            var scripts = new DoodadScripts(new ServiceCollection().BuildServiceProvider(), Time,
                NullLogger<DoodadScripts>.Instance);
            var visibility = new DoodadVisibility(scripts);
            var service = new DoodadUseService(scripts, visibility, new QuietWorld(data), Time,
                NullLogger<DoodadUseService>.Instance, random: ScriptRandom);

            IMapNavigator navigator = Substitute.For<IMapNavigator>();
            navigator.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(ci => ci.ArgAt<Vector3>(1));
            navigator.SampleGroundHeight(Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>())
                .Returns(ci => ci.ArgAt<float>(1));

            Instance = TestMapInstances.Build(world, navigator: navigator, time: Time, ownerPartyId: ownerParty,
                parties: parties,
                lootRoller: new LootRoller(new LootRandom(new Random(460)), NullLogger<LootRoller>.Instance),
                itemRoller: new ItemRoller(new LootRandom(new Random(1179)), new ItemIdAllocator(),
                    Options.Create(new GameConfiguration())),
                lootAllocator: new PartyLootAllocator(Options.Create(new GameConfiguration()), Time,
                    draws ?? CombatRandom.Steady),
                doodadVisibility: visibility, doodadUses: service, ownerCharacterId: owner);
            world.InstanceRegistry.GetInstanceById(Instance.InstanceId).Returns(Instance);
            Interact = new InteractHandler(NullLogger<InteractHandler>.Instance, world, doodads: service);
        }

        public FakeTimeProvider Time { get; }
        public ILootRandom ScriptRandom { get; } = Substitute.For<ILootRandom>();
        public MapInstance Instance { get; }
        public InteractHandler Interact { get; }

        public static async Task<Fixture> CreateAsync(uint? owner = null, PartyId? ownerParty = null,
            ICombatRandom? draws = null, PartyService? parties = null)
        {
            // The blade is a base, so it links an implicit (#1179) or the items area leaves it out.
            TestStaticDataRepositories repositories = TestStaticData.Repositories(
                items: () => [.. Items, s_blade],
                loot: LootRepositories.Of(() => [Table(1, Item(1, s_blade))]),
                auras: () => [AuraTestData.Fortified(8), AuraTestData.Fortified(9), AuraTestData.Fortified(10)]);
            StaticData data = await TestStaticData.LoadAsync(
                repositories with { ItemModifiers = ItemModifierRepositories.ImplicitFor(s_blade) });
            return new Fixture(data, owner, ownerParty, draws, parties);
        }

        /// <param name="levels">The depth band its drops roll their item level in; none, as for an authored spawn.</param>
        public Doodad Place(DoodadTemplateView template, LevelRange? levels = null)
        {
            var doodad = new Doodad(new ObjectGuid(ObjectType.Doodad, template.Id.Value), template, s_doodadAt, 0f)
            {
                Levels = levels,
            };
            Instance.AddDoodad(doodad);
            return doodad;
        }

        public MapInstanceClient Join(uint id, Vector3 position) => Join(TestCharacters.New(id, time: Time), position);

        public MapInstanceClient Join(CharacterEntity character, Vector3 position)
        {
            character.Position = position;
            return MapInstanceClients.Join(Instance, character);
        }

        public void Use(MapInstanceClient client, Doodad doodad) =>
            Interact.Execute(client.Connection, new CInteractPacket { TargetGuid = doodad.Guid.RawValue });

        public void Ticks(int count)
        {
            for (int i = 0; i < count; i++)
                Instance.Update(TimeSpan.FromSeconds(1d / 60d));
        }

        public void Dispose() => Instance.Dispose();
    }
}
