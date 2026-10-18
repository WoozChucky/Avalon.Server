using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public;

namespace Avalon.World.Doodads;

/// <summary>
/// One doodad placed in an instance: a static world object a player clicks to use (a chest, a shrine, a quest object),
/// never a combat target. Placed as the instance is built, off the tick, and changed on the tick only; its state is the
/// instance's and is never persisted. World-side, not the modding API: nothing on it grants anything to a script.
/// </summary>
/// <remarks>
/// It never moves: its placement is fixed at construction, which is what lets <see cref="DoodadIndex" /> file it once.
/// The <see cref="IWorldObject" /> setters therefore throw rather than leave the index behind.
/// </remarks>
public sealed class Doodad : IWorldObject
{
    private DoodadState _state = DoodadState.Ready;

    /// <param name="guid">An <see cref="ObjectType.Doodad" /> guid, its id from <c>IObject.GenerateId()</c>.</param>
    /// <param name="template">The template it was placed from.</param>
    /// <param name="position">Where it stands, its Y already on the ground.</param>
    /// <param name="yawDegrees">Its facing, about the vertical axis.</param>
    /// <param name="visibleGate">The spawn's own visibility gate; null takes the template's.</param>
    /// <param name="usableGate">The spawn's own usability gate; null takes the template's.</param>
    public Doodad(ObjectGuid guid, DoodadTemplateView template, Vector3 position, float yawDegrees,
        DoodadGate? visibleGate = null, DoodadGate? usableGate = null)
    {
        Guid = guid;
        Template = template;
        Position = position;
        Orientation = new Vector3(0f, yawDegrees, 0f);

        // A solid doodad is never hidden: the catalog refuses a gated solid template, and a spawn's gate on one is
        // dropped here too, so no placement path can make an unseen wall.
        if (!template.Solid)
        {
            VisibleGate = visibleGate ?? template.Visible;
            UsableGate = usableGate ?? template.Usable;
        }
    }

    public ObjectGuid Guid { get; }

    /// <summary>The template it was placed from, fixed at spawn: a reload reaches the doodads placed after it only.</summary>
    public DoodadTemplateView Template { get; }

    public Vector3 Position { get; }

    /// <summary>Its facing: yaw in degrees on Y, the other two 0.</summary>
    public Vector3 Orientation { get; }

    /// <summary>Always zero: a doodad never moves.</summary>
    public Vector3 Velocity => Vector3.zero;

    /// <summary>
    /// Ready, Used or Despawned. Every change raises <see cref="ViewVersion" />, and one to Ready, however it came, clears
    /// <see cref="RespawnAt" />. Written on the tick, or by its instance's build before publication (OnSpawn).
    /// </summary>
    public DoodadState State
    {
        get => _state;
        internal set
        {
            if (_state == value)
                return;

            _state = value;
            ViewVersion++;
            if (value == DoodadState.Ready)
                RespawnAt = null;
        }
    }

    /// <summary>
    /// When a spent doodad is ready again; null while it is ready, a script having made it so early included, and when
    /// it never respawns. A respawn scheduled for another time than this one is dropped when it comes up (DoodadRespawns).
    /// </summary>
    public DateTime? RespawnAt { get; internal set; }

    /// <summary>
    /// Raised by every <see cref="State" /> change and by <see cref="RefreshVisibility" />: a viewer whose cached answer
    /// for this doodad holds another version asks its rules again at its next view decision, and no viewer asks before.
    /// </summary>
    public int ViewVersion { get; private set; }

    /// <summary>The effective visibility gate, fixed at spawn: the spawn's own when it set one, else the template's.</summary>
    public DoodadGate? VisibleGate { get; }

    /// <summary>The effective usability gate, fixed at spawn: the spawn's own when it set one, else the template's.</summary>
    public DoodadGate? UsableGate { get; }

    /// <summary>Whether some viewers may not see it, so a view decision must ask its rules rather than assume it seen.</summary>
    public bool HasViewRule => VisibleGate is not null || AsksScript;

    /// <summary>
    /// Whether its script decides who sees it (<see cref="DoodadTemplateView.ScriptCanSee" />): never for a solid one,
    /// which everyone sees, however its template was built.
    /// </summary>
    public bool AsksScript => Template.ScriptCanSee && !Template.Solid;

    /// <summary>False once despawned: dropped from every view, and described to no one.</summary>
    public bool Visible => State != DoodadState.Despawned;

    /// <summary>Whether <c>MapInstance.DoodadChanged</c> has queued its dirty mark for the next broadcast already.</summary>
    internal bool ChangeQueued { get; set; }

    /// <summary>
    /// Raises <see cref="ViewVersion" /> alone, for a rule only a script knows changed (IDoodadContext.RefreshVisibility):
    /// every viewer asks its rules again, the script's CanSee included, at its next view decision, and none before. Tick
    /// thread only.
    /// </summary>
    internal void RefreshVisibility() => ViewVersion++;

    ObjectGuid IObject.Guid
    {
        get => Guid;
        set => throw Immovable(nameof(IObject.Guid));
    }

    Vector3 IWorldObject.Position
    {
        get => Position;
        set => throw Immovable(nameof(IWorldObject.Position));
    }

    Vector3 IWorldObject.Velocity
    {
        get => Velocity;
        set => throw Immovable(nameof(IWorldObject.Velocity));
    }

    Vector3 IWorldObject.Orientation
    {
        get => Orientation;
        set => throw Immovable(nameof(IWorldObject.Orientation));
    }

    private InvalidOperationException Immovable(string member) =>
        new($"Doodad {Guid} cannot have its {member} written: a doodad is fixed where it was placed.");
}
