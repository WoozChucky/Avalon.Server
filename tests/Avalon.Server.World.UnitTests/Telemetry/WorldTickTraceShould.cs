using System.Diagnostics.Tracing;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Testing.Scenarios;

namespace Avalon.Server.World.UnitTests.Telemetry;

[Collection("World tick trace")]
public class WorldTickTraceShould
{
    [Fact]
    public void Attribute_real_visibility_work_to_instances_and_the_same_tick()
    {
        using var world = new ScenarioWorld();
        MapInstance crowded = world.AddInstance();
        MapInstance sparse = world.AddInstance();
        CharacterEntity mover = world.NewCharacter(2);
        world.Join(crowded, new ScenarioConnection(world.NewCharacter(1), world.Scheduler, sealPayloads: false), new Vector3(0, 0, 0));
        world.Join(crowded, new ScenarioConnection(mover, world.Scheduler, sealPayloads: false), new Vector3(1, 0, 0));
        world.Join(sparse, new ScenarioConnection(world.NewCharacter(3), world.Scheduler, sealPayloads: false), new Vector3(0, 0, 0));
        for (int i = 0; i < 12; i++)
            world.Tick();
        long bytesBefore = world.Connections.Sum(c => c.BytesWritten);
        int packetsBefore = world.Connections.Sum(c => c.Sent);
        using var listener = new Capture();
        for (int i = 0; i < 12; i++)
        {
            mover.Position = new Vector3(1 + (i % 2) * 0.1f, 0, 0);
            world.Tick();
        }

        var batches = listener.Events.Where(e => e.Name == "InstanceBatch").ToList();
        var instances = listener.Events.Where(e => e.Name == "InstanceTick").ToList();
        Assert.Equal(12, batches.Count);
        Assert.Equal(24, instances.Count);
        Assert.DoesNotContain(listener.Events, e => e.Name == "EventSourceMessage");
        foreach ((string Name, Dictionary<string, long> Values) batch in batches)
        {
            var records = instances.Where(e => e.Values["tickId"] == batch.Values["tickId"]).ToList();
            Assert.Equal(2, records.Count);
            // The views are decided on each instance's broadcast tick (#981): every player checks every player there.
            Assert.All(records, e => Assert.Equal(e.Values["broadcast"] * e.Values["players"] * e.Values["players"],
                e.Values["candidateChecks"]));
            Assert.All(records, e => Assert.Equal(e.Values["candidateChecks"], e.Values["visiblePairs"]));
            Assert.All(records, e => Assert.Equal(1, e.Values["completed"]));
            Assert.All(records, e => Assert.Equal(0, e.Values["added"] + e.Values["removed"]));
            Assert.All(records, e => Assert.True(e.Values["durationTicks"] >=
                e.Values["visibilityTicks"] + e.Values["trackingTicks"]));
        }
        Assert.Contains(instances, e => e.Values["broadcast"] == 1);
        Assert.Contains(instances, e => e.Values["broadcast"] == 0);
        Assert.Equal(world.Connections.Sum(c => c.BytesWritten) - bytesBefore, instances.Sum(e => e.Values["stateBytes"]));
        Assert.Equal(world.Connections.Sum(c => c.Sent) - packetsBefore, instances.Sum(e => e.Values["statePackets"]));
        Assert.Contains(instances, e => e.Values["updated"] > 0 && e.Values["stateEncodingTicks"] > 0);
        Assert.All(instances.Where(e => e.Values["broadcast"] == 0), e => Assert.Equal(0, e.Values["updated"]));
    }

    [Fact]
    public void Count_the_packets_the_map_pass_took_for_each_instance_on_its_own_tick()
    {
        using var world = new ScenarioWorld();
        MapInstance busy = world.AddInstance();
        MapInstance quiet = world.AddInstance();
        using var queue = new ReceiveQueueFixture();
        CharacterEntity character = world.NewCharacter(1);
        character.Data!.Map = 1;
        character.InstanceId = busy.InstanceId;
        queue.Connection.Character = character;
        busy.AddCharacter(queue.Connection);
        world.Join(quiet, world.NewCharacter(2), new Vector3(0, 0, 0));
        world.Tick();

        using var listener = new Capture();
        for (int i = 0; i < 3; i++)
            queue.Connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, new CPlayerInputPacket());
        world.Tick();
        world.Tick();

        long busyLow = BitConverter.ToInt64(busy.InstanceId.ToByteArray(), 0);
        var ticks = listener.Events.Where(e => e.Name == "InstanceTick")
            .GroupBy(e => e.Values["tickId"])
            .Select(tick => tick.ToDictionary(e => e.Values["instanceLow"] == busyLow, e => e.Values["mapPackets"]))
            .ToList();
        Assert.Equal(3, queue.Dispatched);
        Assert.Equal(2, ticks.Count);
        Assert.Equal(3, ticks[0][true]);
        Assert.Equal(0, ticks[0][false]);
        Assert.Equal(0, ticks[1][true]);
    }

    [Fact]
    public void Mark_an_interrupted_instance_incomplete_and_reset_its_next_record()
    {
        using var world = new ScenarioWorld();
        MapInstance instance = world.AddInstance();
        bool fail = true;
        world.Join(instance, world.NewCharacter(1), new Vector3(0, 0, 0), _ =>
        {
            if (fail)
            {
                fail = false;
                throw new InvalidOperationException("interrupted input");
            }
        });
        using var listener = new Capture();
        Assert.Throws<InvalidOperationException>(() => instance.Update(ScenarioWorld.Dt));
        instance.Update(ScenarioWorld.Dt);
        var records = listener.Events.Where(e => e.Name == "InstanceTick").ToList();
        Assert.Equal(2, records.Count);
        Assert.Equal(0, records[0].Values["completed"]);
        Assert.Equal(1, records[1].Values["completed"]);
        Assert.Equal(1, records[1].Values["candidateChecks"]);
        Assert.Equal(1, records[1].Values["visiblePairs"]);
        Assert.Equal(1, records[1].Values["added"]);
    }

    private sealed class Capture : EventListener
    {
        public List<(string Name, Dictionary<string, long> Values)> Events { get; } = [];

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Avalon-World-Tick")
                EnableEvents(eventSource, EventLevel.Informational);
        }

        protected override void OnEventWritten(EventWrittenEventArgs data)
        {
            if (data.EventSource.Name != "Avalon-World-Tick")
                return;
            var values = new Dictionary<string, long>(StringComparer.Ordinal);
            if (data.EventId != 0 && data.Payload is not null && data.PayloadNames is not null)
            {
                for (int i = 0; i < data.Payload.Count; i++)
                    values.Add(data.PayloadNames[i], (long)data.Payload[i]!);
            }
            Events.Add((data.EventName ?? string.Empty, values));
        }
    }
}
