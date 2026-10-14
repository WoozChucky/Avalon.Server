using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Public.Enums;
using Avalon.World.Scripts.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #975: through <c>World.Update</c>, the instances' state broadcasts are spread over the ticks of an interval, each
/// instance in its own slot, every 100 ms. They used to go out together on one tick, and each instance's timer dropped
/// its remainder, so they came every seventh tick of just under 1/60 s, not every sixth.
/// </summary>
public class BroadcastScheduleShould
{
    private static readonly MapTemplateId s_mapId = new(2);

    // Tick lengths as the tick loop measures them: alternately 50 µs under and over 1/60 s.
    private static readonly TimeSpan s_under = TimeSpan.FromTicks(166_166);
    private static readonly TimeSpan s_over = TimeSpan.FromTicks(167_166);
    private static readonly TimeSpan s_stall = TimeSpan.FromMilliseconds(250);

    private const int Ticks = 1200;
    private const int StallTick = 800;
    private const int LeaveTick = 300;
    private const int ReturnTick = 350;

    private sealed class SilentReloader : IScriptHotReloader
    {
        public void Update(out List<Type> scriptTypes) => scriptTypes = [];

        public event ScriptsHotReloadedEventHandler? ScriptsHotReloaded;

        public void Start() => ScriptsHotReloaded?.Invoke([]);

        public void Stop() { }
    }

    [Fact]
    public async Task Broadcast_each_instance_in_its_own_slot_every_interval_and_once_after_a_stall()
    {
        // As many instances as there are slots, built together and published on the same tick: each must take a tick of
        // its own. One of them empties for a while and must come back in its slot.
        var instances = new MapInstance[BroadcastSchedule.Slots];
        var clients = new MapInstanceClient[instances.Length];
        for (int i = 0; i < instances.Length; i++)
        {
            instances[i] = TestMapInstances.Build(NewWorld());
            clients[i] = Join(instances[i], 975_100 + (uint)i);
            clients[i].Character.PowerType = PowerType.Mana;
            clients[i].Character.Power = 100;
        }

        try
        {
            IAvalonMapManager mapManager = Substitute.For<IAvalonMapManager>();
            mapManager.Templates.Returns([new MapTemplate { Id = s_mapId, MapType = MapType.Normal }]);
            IChunkLayoutInstanceFactory factory = Substitute.For<IChunkLayoutInstanceFactory>();
            factory.BuildAsync(default!, default, default).ReturnsForAnyArgs(instances[0], instances[1..]);
            Avalon.World.World world = await ScriptHotReloadPollingShould.BuildWorldAsync(
                new SilentReloader(), intervalSeconds: 600, mapManager, factory);
            for (int i = 0; i < instances.Length; i++)
                _ = world.InstanceRegistry.GetOrCreateNormalInstanceAsync(975_100 + (uint)i, s_mapId);

            var broadcastTicks = new List<int>[instances.Length];
            for (int i = 0; i < instances.Length; i++)
                broadcastTicks[i] = [];

            for (int tick = 0; tick < Ticks; tick++)
            {
                if (tick == LeaveTick)
                    instances[0].RemoveCharacter(clients[0].Connection);
                if (tick == ReturnTick)
                    instances[0].AddCharacter(clients[0].Connection);

                // A change every tick, so every broadcast carries an update to the character's own client.
                foreach (MapInstanceClient client in clients)
                    client.Character.CurrentPower = (uint)(tick % 2 == 0 ? 10 : 90);

                world.Update(tick == StallTick ? s_stall : tick % 2 == 0 ? s_under : s_over);

                for (int i = 0; i < clients.Length; i++)
                {
                    if (clients[i].Sent.Exists(p => p.Header.Type == NetworkPacketType.SMSG_WORLD_STATE_UPDATE))
                        broadcastTicks[i].Add(tick);
                    foreach (OutboundPacket packet in clients[i].Sent)
                        packet.Release();
                    clients[i].Sent.Clear();
                }
            }

            // The stall: every instance is owed a broadcast, and makes exactly one, on the stall's tick.
            Assert.All(broadcastTicks, ticks => Assert.Contains(StallTick, ticks));

            // Every other tick: at most one instance broadcasts, the one whose slot it is; the empty one came back in its
            // own, and none caught up on the stall with broadcasts on the ticks after it.
            var byTick = broadcastTicks.SelectMany((ticks, instance) => ticks.Select(tick => (tick, instance)))
                .Where(b => b.tick != StallTick)
                .GroupBy(b => b.tick)
                .Where(g => g.Count() > 1)
                .Select(g => $"tick {g.Key}: instances {string.Join(", ", g.Select(b => b.instance))}")
                .ToList();
            Assert.Empty(byTick);

            // Each instance's broadcasts are six ticks apart, 100 ms, outside the stall and its empty stretch.
            for (int i = 0; i < broadcastTicks.Length; i++)
            {
                List<int> ticks = broadcastTicks[i];
                for (int b = 1; b < ticks.Count; b++)
                {
                    bool acrossStall = ticks[b] == StallTick || ticks[b - 1] == StallTick;
                    bool acrossAbsence = i == 0 && ticks[b - 1] < LeaveTick && ticks[b] >= ReturnTick;
                    if (!acrossStall && !acrossAbsence)
                    {
                        Assert.True(ticks[b] - ticks[b - 1] == 6,
                            $"instance {i} broadcast on ticks {ticks[b - 1]} and {ticks[b]}");
                    }
                }

                // Ten a second: one per six ticks over the run, less the stretch the first instance was empty.
                int expected = (Ticks - (i == 0 ? ReturnTick - LeaveTick : 0)) / 6;
                Assert.InRange(ticks.Count, expected - 2, expected + 2);
            }
        }
        finally
        {
            foreach (MapInstance instance in instances)
                instance.Dispose();
        }
    }
}
