using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Movement;
using Avalon.World.Handlers;
using Avalon.World.Maps.Navigation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// One player walking a loop, as a client holding a direction would: every tick one <see cref="CPlayerInputPacket" />
/// steering around a circle of <see cref="Radius" /> about a centre, handed to the real <see cref="PlayerInputHandler" />,
/// which moves the character over the instance's navmesh and sends the real <c>SPlayerStateAckPacket</c> through the
/// connection's cipher. It runs from <see cref="ScenarioConnection.UpdateMap" />, inside the instance pass, where
/// production processes a connection's queued input. One packet per player, mutated each tick, so the driver itself
/// allocates nothing.
/// </summary>
public sealed class LoopWalker
{
    /// <summary>The loop's radius.</summary>
    public const float Radius = 8f;

    /// <summary>The inputs a walker sends over the measured windows: one per tick.</summary>
    public const uint MeasuredInputs = ScenarioMeasurement.Windows * ScenarioMeasurement.WindowTicks;

    private readonly PlayerInputHandler _handler;
    private readonly Vector3 _centre;
    private readonly CPlayerInputPacket _packet = new();

    private LoopWalker(PlayerInputHandler handler, Vector3 centre)
    {
        _handler = handler;
        _centre = centre;
    }

    /// <summary>The handler every walker of the world shares, over the world's real registry, as production has one.</summary>
    public static PlayerInputHandler Handler(ScenarioWorld world) =>
        new(NullLogger<PlayerInputHandler>.Instance, world.Host);

    /// <summary>A walker around <paramref name="centre" />, its step ready to be a connection's map update.</summary>
    public static Action<ScenarioConnection> Around(PlayerInputHandler handler, Vector3 centre) =>
        new LoopWalker(handler, centre).Step;

    /// <summary>
    /// The point of the loop at <paramref name="angle" /> (radians, from +x towards +z), on the navmesh's ground;
    /// throws when it is off the navmesh, so a scenario never starts a player in a wall.
    /// </summary>
    public static Vector3 LoopPoint(MapNavigator navigator, Vector3 centre, float angle)
    {
        var raw = new Vector3(centre.x + Radius * MathF.Cos(angle), centre.y, centre.z + Radius * MathF.Sin(angle));
        if (navigator.FindGround(raw, out Vector3 ground) != NavmeshGroundKind.Under)
            throw new InvalidOperationException($"Loop point ({raw.x}, {raw.z}) is off the navmesh");

        return ground;
    }

    /// <summary>One tick's input: the loop's tangent (anticlockwise), pulled back onto the circle when off it.</summary>
    private void Step(ScenarioConnection connection)
    {
        Vector3 position = connection.Character!.Position;
        float dx = position.x - _centre.x;
        float dz = position.z - _centre.z;
        float r = MathF.Sqrt(dx * dx + dz * dz);
        float outX = r > 1e-3f ? dx / r : 1f;
        float outZ = r > 1e-3f ? dz / r : 0f;

        // A metre off the circle steers 45 degrees back towards it; on it, the direction is the tangent.
        float pull = Math.Clamp(Radius - r, -1f, 1f);
        float dirX = -outZ + outX * pull;
        float dirZ = outX + outZ * pull;
        float length = MathF.Sqrt(dirX * dirX + dirZ * dirZ);

        _packet.Seq++;
        _packet.DirX = dirX / length;
        _packet.DirZ = dirZ / length;
        _packet.YawDeg = (ushort)((MathF.Atan2(dirX, dirZ) * (180f / MathF.PI) + 360f) % 360f);

        _handler.Execute(connection, _packet);
    }
}
