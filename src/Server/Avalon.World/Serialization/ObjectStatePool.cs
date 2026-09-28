using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;

namespace Avalon.World.Serialization;

/// <summary>
/// The <see cref="ObjectState" /> messages of one state broadcast, reused by the next (#640). A
/// broadcast describes every entity in a player's view, a tenth of a second apart for every player, and
/// building each description anew was most of what a busy tick allocated.
/// </summary>
/// <remarks>
/// A packet is serialized when it is created, so its messages are free again as soon as
/// <c>Create</c> returns: <see cref="Reset" /> before describing the next player's view hands them out
/// again. Nothing may keep a message it was handed past that. Every message handed out is cleared
/// first, so it carries only what the writer sets on it, exactly as a new one would. Tick thread only.
/// </remarks>
public sealed class ObjectStatePool
{
    private readonly List<ObjectState> _states = new(32);
    private readonly List<Vec3> _vectors = new(64);
    private int _statesUsed;
    private int _vectorsUsed;

    /// <summary>Every message handed out since the last reset is free to be handed out again.</summary>
    public void Reset()
    {
        _statesUsed = 0;
        _vectorsUsed = 0;
    }

    /// <summary>A message holding only <paramref name="guid" />, as <c>new ObjectState { Guid = guid }</c> would.</summary>
    public ObjectState State(ulong guid)
    {
        ObjectState state;
        if (_statesUsed < _states.Count)
        {
            state = _states[_statesUsed];
            Clear(state);
        }
        else
        {
            state = new ObjectState();
            _states.Add(state);
        }

        _statesUsed++;
        state.Guid = guid;
        return state;
    }

    /// <summary>A triple holding <paramref name="vector" />.</summary>
    public Vec3 Vector(Vector3 vector)
    {
        Vec3 vec;
        if (_vectorsUsed < _vectors.Count)
        {
            vec = _vectors[_vectorsUsed];
        }
        else
        {
            vec = new Vec3();
            _vectors.Add(vec);
        }

        _vectorsUsed++;
        vec.X = vector.x;
        vec.Y = vector.y;
        vec.Z = vector.z;
        return vec;
    }

    /// <summary>
    /// Every member back to what a new message holds. A member added to <see cref="ObjectState" /> must
    /// be cleared here too; <c>ObjectStatePoolShould</c> fails until it is.
    /// </summary>
    public static void Clear(ObjectState state)
    {
        state.Guid = 0;
        state.Position = null;
        state.Velocity = null;
        state.Orientation = null;
        state.MoveState = null;
        state.Health = null;
        state.CurrentHealth = null;
        state.PowerType = null;
        state.Power = null;
        state.CurrentPower = null;
        state.Level = null;
        state.IsDead = null;
        state.Experience = null;
        state.RequiredExperience = null;
        state.CreatureMetadataId = null;
        state.Name = null;
        state.PortalRadius = null;
        state.PortalTargetMapId = null;
        state.PortalRole = null;
        state.CanInteract = null;
        state.PvpEnabled = null;
    }
}
