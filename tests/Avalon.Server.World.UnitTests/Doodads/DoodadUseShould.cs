using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Auras;
using Avalon.Server.World.UnitTests.GameAuth;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.ItemUse;
using Avalon.World;
using Avalon.World.Abilities;
using Avalon.World.Auras;
using Avalon.World.ChunkLayouts;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Items;
using Avalon.World.Loot;
using Avalon.World.Persistence;
using Avalon.World.Respawn;
using Avalon.World.Scripts;
using Avalon.World.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

using WireDoodadState = Avalon.Network.Packets.State.DoodadState;
using WorldDoodadState = Avalon.World.Doodads.DoodadState;

namespace Avalon.Server.World.UnitTests.Doodads;

/// <summary>
/// Using a doodad, through the real interact handler and a real instance on a fake clock: every rule that refuses a use
/// drops it silently and allocates nothing; a use runs its script once and spends the doodad for everyone; a cast bar ends
/// on the item cast's rules, and only the first of two casts on one doodad spends it; a script that throws leaves the
/// doodad ready; a spent doodad respawns on the instance's next occupied tick past its time. Doodad templates 1 and 2,
/// character ids 106_0xx and ability 200 are this class's.
/// </summary>
public class DoodadUseShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    private const uint IdA = 106_001, IdB = 106_002, IdC = 106_003, ConeId = 200;

    // A gate no character here meets: quest 906 turned in.
    private static readonly DoodadGate s_neverMet = new(906, DoodadGateCondition.Completed, null);

    // A gate every character here meets but one that has taken quest 906.
    private static readonly DoodadGate s_notStarted = new(906, DoodadGateCondition.NotStarted, null);

    // The doodad stands at x 10; A at x 6 and B at x 14 are both 4 m from it, within its 5 m.
    private static readonly Vector3 s_atA = new(6f, 0f, 0f), s_atB = new(14f, 0f, 0f);

    /// <summary>
    /// A 2 s cast template, so a use the rules should have refused would show as a cast start. The handler drops a dead
    /// user before it reaches the use service, which refuses it again, as it does at a cast's end.
    /// </summary>
    [Theory]
    [InlineData("dead")]
    [InlineData("unknown guid")]
    [InlineData("despawned")]
    [InlineData("interaction none")]
    [InlineData("5.01 m away")]
    [InlineData("user position NaN")]
    [InlineData("not visible")]
    [InlineData("not usable")]
    [InlineData("spent")]
    [InlineData("ability casting")]
    [InlineData("script refuses")]
    public async Task Refuse_a_use_the_rules_forbid(string row)
    {
        using Fixture f = await Fixture.CreateAsync();
        Doodad doodad = f.Place(1, row switch
        {
            "interaction none" => Template(1, castMs: 2000, interaction: DoodadInteraction.None),
            "not visible" => Template(1, castMs: 2000, visible: s_neverMet),
            "not usable" => Template(1, castMs: 2000, usable: s_neverMet),
            _ => Template(1, castMs: 2000),
        });
        MapInstanceClient a = f.Join(IdA, row switch
        {
            "5.01 m away" => new Vector3(4.99f, 0f, 0f),
            "user position NaN" => new Vector3(float.NaN, 0f, 0f),
            _ => s_atA,
        });
        ArrivalProtectionTestData.Apply(a.Character);
        ObjectGuid target = row == "unknown guid" ? new ObjectGuid(ObjectType.Doodad, 999) : doodad.Guid;
        switch (row)
        {
            case "dead":
                a.Character.IsDead = true;
                break;
            case "despawned":
                SetState(doodad, WorldDoodadState.Despawned);
                break;
            case "spent":
                SetState(doodad, WorldDoodadState.Used);
                break;
            case "ability casting":
                GameAbility casting = AbilityTestData.Game(AbilityTestData.Cone(ConeId));
                casting.Casting = true;
                a.Character.Spells.Load([casting]);
                break;
            case "script refuses":
                CountingDoodadScript.Refusal = "Not now.";
                break;
        }

        WorldDoodadState before = doodad.State;
        f.Interact.Execute(a.Connection, new CInteractPacket { TargetGuid = target.RawValue });

        Assert.Equal(0, CountingDoodadScript.Uses);
        Assert.Equal(before, doodad.State);
        Assert.Empty(a.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST));
        Assert.Empty(a.Sent);
        Assert.True(ArrivalProtection.IsProtected(a.Character));   // a refused use keeps it (#1088)

        // The use service the handler hands it to refuses without allocating, once its first refusal is behind it.
        f.Service.Interact(a.Connection, a.Character, f.Instance, target);
        long allocatedBefore = ThreadAllocations.BeforeQuietWindow();
        f.Service.Interact(a.Connection, a.Character, f.Instance, target);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        Assert.Equal(0, CountingDoodadScript.Uses);
    }

    [Fact]
    public async Task Use_an_instant_doodad_once_per_instance()
    {
        using Fixture f = await Fixture.CreateAsync();
        Doodad doodad = f.Place(1, Template(1));
        MapInstanceClient a = f.Join(IdA, s_atA);
        MapInstanceClient b = f.Join(IdB, s_atB);
        f.Ticks(30);
        ArrivalProtectionTestData.Apply(a.Character);

        f.Use(a, doodad);

        Assert.False(ArrivalProtection.IsProtected(a.Character));   // an accepted use ends it (#1088)
        Assert.Equal(1, CountingDoodadScript.Uses);
        Assert.Equal(WorldDoodadState.Used, doodad.State);
        f.Ticks(6);
        foreach (MapInstanceClient viewer in new[] { a, b })
        {
            ObjectState used = Assert.Single(viewer.StateUpdates(), s => s.Guid == doodad.Guid.RawValue);
            Assert.Equal(WireDoodadState.Used, used.DoodadState);
        }

        f.Use(b, doodad);

        Assert.Equal(1, CountingDoodadScript.Uses);
    }

    /// <summary>
    /// Both casts on one tick, both due on one tick: the first to complete spends it, the second ends refused. C stands
    /// beside them, but has taken the quest that hides the doodad: its cast bars, which name the doodad's template, reach
    /// only the viewers that see it.
    /// </summary>
    [Fact]
    public async Task Let_only_the_first_of_two_casts_spend_the_doodad()
    {
        using Fixture f = await Fixture.CreateAsync();
        Doodad doodad = f.Place(1, Template(1, castMs: 2000, visible: s_notStarted));
        MapInstanceClient a = f.Join(IdA, s_atA);
        MapInstanceClient b = f.Join(IdB, s_atB);
        MapInstanceClient c = f.Join(IdC, new Vector3(10f, 0f, 3f));
        c.Character.Quests.Start(906, DateTime.UnixEpoch);
        f.Tick();
        ArrivalProtectionTestData.Apply(a.Character);

        f.Use(a, doodad);
        f.Use(b, doodad);
        Assert.False(ArrivalProtection.IsProtected(a.Character));   // so does a cast bar's start (#1088)

        foreach (MapInstanceClient viewer in new[] { a, b })
        {
            List<SUnitStartCastPacket> starts = viewer.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST);
            Assert.Equal([a.Character.Guid.RawValue, b.Character.Guid.RawValue], starts.Select(s => s.Caster).Order());
            Assert.All(starts, s => Assert.Equal(1ul, s.DoodadTemplateId));
            Assert.All(starts, s => Assert.Equal(2f, s.CastTime));
        }

        f.Time.Advance(TimeSpan.FromSeconds(1.9));
        f.Tick();
        Assert.Equal(0, CountingDoodadScript.Uses);

        f.Time.Advance(TimeSpan.FromSeconds(0.1));
        f.Tick();

        Assert.Equal(1, CountingDoodadScript.Uses);
        Assert.Equal(WorldDoodadState.Used, doodad.State);
        SUnitFinishCastPacket finish =
            Assert.Single(a.Read<SUnitFinishCastPacket>(NetworkPacketType.SMSG_UNIT_FINISH_CAST));
        SCharacterInterruptedCastPacket interrupted =
            Assert.Single(a.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST));
        Assert.Equal(1ul, finish.DoodadTemplateId);
        Assert.Equal(1ul, interrupted.DoodadTemplateId);
        Assert.NotEqual(finish.Caster, interrupted.Caster);
        Assert.Single(b.Read<SUnitFinishCastPacket>(NetworkPacketType.SMSG_UNIT_FINISH_CAST));
        Assert.Single(b.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST));

        Assert.Empty(c.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST));
        Assert.Empty(c.Read<SUnitFinishCastPacket>(NetworkPacketType.SMSG_UNIT_FINISH_CAST));
        Assert.Empty(c.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST));
    }

    /// <summary>B watches A's cast end, out loud, with the doodad's template; damage alone is the item cast's test.</summary>
    [Theory]
    [InlineData("moves")]
    [InlineData("dies")]
    [InlineData("leaves the instance")]
    [InlineData("casts an ability")]
    [InlineData("uses an item that passed its checks")]
    [InlineData("uses another doodad")]
    public async Task End_a_cast_when_the_user_moves_dies_leaves_or_casts(string row)
    {
        using Fixture f = await Fixture.CreateAsync();
        Doodad doodad = f.Place(1, Template(1, castMs: 2000));
        Doodad other = f.Place(2, Template(2, castMs: 2000), new Vector3(10f, 0f, 2f));
        MapInstanceClient a = f.Join(IdA, s_atA);
        MapInstanceClient b = f.Join(IdB, s_atB);
        a.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Cone(ConeId))]);
        a.Character.Container(InventoryType.Bag).Load([TestCharacters.Item(0, ItemUseWorld.Tonic, count: 5)]);
        // B's view holds the doodad, so B hears its cast bar.
        f.Tick();

        f.Use(a, doodad);
        switch (row)
        {
            case "moves":
                a.Character.Position = new Vector3(6.5f, 0f, 0f);
                f.Tick();
                break;
            case "dies":
                a.Character.IsDead = true;
                f.Tick();
                break;
            case "leaves the instance":
                f.Instance.RemoveCharacter(a.Connection);
                break;
            case "casts an ability":
                a.Character.LastCastStartTime = f.Time.GetUtcNow().UtcDateTime.AddSeconds(-10);
                f.Cast.Execute(a.Connection, new CCastAbilityPacket { AbilityId = ConeId });
                break;
            case "uses an item that passed its checks":
                f.Items.Use(a.Connection, a.Character, 1, InventoryType.Bag, 0);
                break;
            case "uses another doodad":
                f.Use(a, other);
                break;
        }

        SCharacterInterruptedCastPacket interrupted = Assert.Single(
            b.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST),
            p => p.Caster == a.Character.Guid.RawValue);
        Assert.Equal(1ul, interrupted.DoodadTemplateId);
        Assert.Equal(0, CountingDoodadScript.Uses);
        Assert.Equal(WorldDoodadState.Ready, doodad.State);

        // Nothing is left to complete later.
        f.Time.Advance(TimeSpan.FromSeconds(2));
        f.Tick();
        Assert.Equal(WorldDoodadState.Ready, doodad.State);
    }

    /// <summary>
    /// A throwing OnUse is logged once per interval; the doodad stays ready, unspent, with no respawn due, not even the one
    /// the script asked for before it threw: a doodad that was never spent never respawns.
    /// </summary>
    [Fact]
    public async Task Leave_the_doodad_ready_when_its_script_throws()
    {
        using Fixture f = await Fixture.CreateAsync();
        CountingDoodadScript.Throws = true;
        CountingDoodadScript.AsksRespawn = TimeSpan.FromSeconds(30);
        Doodad doodad = f.Place(1, Template(1, respawnSeconds: 60));
        MapInstanceClient a = f.Join(IdA, s_atA);

        f.Use(a, doodad);
        f.Use(a, doodad);

        Assert.Equal(2, CountingDoodadScript.Uses);
        Assert.Equal(WorldDoodadState.Ready, doodad.State);
        Assert.Null(doodad.RespawnAt);
        Assert.Single(f.ScriptLog.Errors);
        f.Time.Advance(TimeSpan.FromSeconds(60));
        f.Tick();
        Assert.Equal(0, CountingDoodadScript.Respawns);
    }

    /// <summary>The other way round from the theory above: a doodad use that passed its checks ends the user's item cast.</summary>
    [Fact]
    public async Task End_an_item_cast_when_its_user_uses_a_doodad_that_passed_its_checks()
    {
        using Fixture f = await Fixture.CreateAsync();
        Doodad doodad = f.Place(1, Template(1));
        MapInstanceClient a = f.Join(IdA, s_atA);
        MapInstanceClient b = f.Join(IdB, s_atB);
        a.Character.Container(InventoryType.Bag).Load([TestCharacters.Item(0, ItemUseWorld.Scroll, count: 2)]);
        f.Items.Use(a.Connection, a.Character, 1, InventoryType.Bag, 0);

        f.Use(a, doodad);

        SCharacterInterruptedCastPacket interrupted = Assert.Single(
            b.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST),
            p => p.Caster == a.Character.Guid.RawValue);
        Assert.Equal(ItemUseWorld.Scroll.Id.Value, interrupted.ItemTemplateId);
        Assert.Equal(1, CountingDoodadScript.Uses);
        Assert.Equal(2u, TestCharacters.At(a.Character, InventoryType.Bag, 0).Count);
    }

    [Fact]
    public async Task Respawn_a_spent_doodad_after_its_delay()
    {
        using Fixture f = await Fixture.CreateAsync();
        Doodad doodad = f.Place(1, Template(1, look: DoodadSpentLook.Despawn, respawnSeconds: 60));
        MapInstanceClient a = f.Join(IdA, s_atA);
        f.Ticks(30);
        Assert.Single(a.Added(), s => s.Guid == doodad.Guid.RawValue);

        f.Use(a, doodad);
        f.Ticks(6);

        Assert.Equal(WorldDoodadState.Despawned, doodad.State);
        Assert.Single(a.Removed(), guid => guid == doodad.Guid.RawValue);

        // Nobody is here, so nothing ticks: its time passes, and it waits.
        f.Instance.RemoveCharacter(a.Connection);
        f.Time.Advance(TimeSpan.FromSeconds(60));
        f.Tick();
        Assert.Equal(WorldDoodadState.Despawned, doodad.State);

        a.Sent.Clear();
        f.Instance.AddCharacter(a.Connection);
        f.Tick();

        Assert.Equal(WorldDoodadState.Ready, doodad.State);
        Assert.Equal(1, CountingDoodadScript.Respawns);
        f.Ticks(30);
        Assert.Single(a.Added(), s => s.Guid == doodad.Guid.RawValue);
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static DoodadTemplateView Template(uint id, uint castMs = 0,
        DoodadInteraction interaction = DoodadInteraction.Click, DoodadSpentLook look = DoodadSpentLook.ShowUsed,
        uint? respawnSeconds = null, DoodadGate? visible = null, DoodadGate? usable = null) => new(
        new DoodadTemplateId(id), "Counter", "props-kit", "Crate", null, null, null, null,
        interaction, castMs, 5f, 0f, DoodadUseLimit.OncePerInstance, look, respawnSeconds, typeof(CountingDoodadScript),
        null, 0, 0, 0, DoodadCollision.None, 0f, 0f, 0f, visible, usable, [], 0, ScriptCanSee: false);

    /// <summary>The doodad's state written as the tick writes it, without a use.</summary>
    private static void SetState(Doodad doodad, WorldDoodadState state) =>
        (typeof(Doodad).GetProperty(nameof(Doodad.State))?.SetMethod
         ?? throw new MissingMethodException(nameof(Doodad), "set_State")).Invoke(doodad, [state]);

    /// <summary>
    /// A casting instance (CastAbilityHandler, the shape scripts) on a fake clock, with the use service the container
    /// would hand it and the handler, and an item use service over the Tonic and the Scroll (a 3 s cast). The use service
    /// reads its quest catalog through a world that is no substitute, so a refusal can be pinned at zero bytes.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private Fixture(StaticData data)
        {
            CountingDoodadScript.Reset();
            World = NewWorld(data);
            var scripts = new DoodadScripts(new ServiceCollection().BuildServiceProvider(), Time,
                new Logger<DoodadScripts>(ScriptLog));
            var visibility = new DoodadVisibility(scripts);
            Service = new DoodadUseService(scripts, visibility, new QuietWorld(data), Time,
                NullLogger<DoodadUseService>.Instance, random: new LootRandom(new Random(0)));
            Instance = TestMapInstances.BuildCasting(out CastAbilityHandler cast, world: World, time: Time,
                doodadVisibility: visibility, doodadUses: Service);
            Cast = cast;
            Interact = new InteractHandler(NullLogger<InteractHandler>.Instance, World, doodads: Service);

            IScriptManager itemScripts = Substitute.For<IScriptManager>();
            itemScripts.GetItemScript(nameof(ConsumeOneScript)).Returns(typeof(ConsumeOneScript));
            CharacterEconomy economy = TestEconomy.Over(World);
            var tools = new ItemUseTools(World, economy,
                new TownReturn(NullLogger.Instance, World, Substitute.For<IRespawnTargetResolver>(), InlineDatabaseWork.Instance),
                new MapTeleport(NullLogger<MapTeleport>.Instance, World, Substitute.For<ICharacterSaver>()),
                Substitute.For<ICreaturePlacementService>(), Time, NullLogger<ItemUseTools>.Instance);
            Items = new ItemUseService(tools, itemScripts, Substitute.For<IServiceProvider>(),
                NullLogger<ItemUseService>.Instance);
        }

        public FakeTimeProvider Time { get; } = new();
        public TestLog ScriptLog { get; } = new();
        public IWorld World { get; }
        public MapInstance Instance { get; }
        public DoodadUseService Service { get; }
        public InteractHandler Interact { get; }
        public CastAbilityHandler Cast { get; }
        public ItemUseService Items { get; }

        public static async Task<Fixture> CreateAsync() =>
            new(await TestStaticData.LoadAsync(items: [ItemUseWorld.Tonic, ItemUseWorld.Scroll]));

        /// <summary>A doodad at x 10, unless placed elsewhere, as an instance build places it.</summary>
        public Doodad Place(uint id, DoodadTemplateView template, Vector3? position = null)
        {
            var doodad = new Doodad(new ObjectGuid(ObjectType.Doodad, id), template, position ?? new Vector3(10f, 0f, 0f), 0f);
            Instance.AddDoodad(doodad);
            return doodad;
        }

        public MapInstanceClient Join(uint id, Vector3 position)
        {
            CharacterEntity character = TestCharacters.New(id, time: Time);
            character.Position = position;
            return MapInstanceClients.Join(Instance, character);
        }

        public void Use(MapInstanceClient client, Doodad doodad) =>
            Interact.Execute(client.Connection, new CInteractPacket { TargetGuid = doodad.Guid.RawValue });

        public void Tick() => Instance.Update(s_tick);

        public void Ticks(int count)
        {
            for (int i = 0; i < count; i++)
                Tick();
        }

        public void Dispose()
        {
            Instance.Dispose();
            CountingDoodadScript.Reset();
        }
    }
}
