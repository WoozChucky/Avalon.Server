using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Entities;
using Avalon.World.Quests;

namespace Avalon.World.Doodads;

/// <summary>
/// <see cref="IDoodadViewContext" /> over one question of one viewer about one doodad: opened before
/// <see cref="DoodadScript.CanSee" /> and closed after it, so <see cref="DoodadVisibility" /> keeps one and asks every
/// script through it without allocating. Tick thread only, as the view decisions that ask it are.
/// </summary>
public sealed class DoodadViewContext : IDoodadViewContext
{
    private Doodad? _doodad;
    private CharacterEntity? _viewer;
    private QuestCatalog? _quests;
    private bool _dataGateAllows;

    public DoodadTemplateId TemplateId => Doodad.Template.Id;

    public Vector3 Position => Doodad.Position;

    public DoodadState State => Doodad.State;

    public bool DataGateAllows => _doodad is not null ? _dataGateAllows : throw Closed();

    public string ViewerName => Viewer.Name;

    public ushort ViewerLevel => Viewer.Level;

    public CharacterClass ViewerClass => Viewer.Class;

    private Doodad Doodad => _doodad ?? throw Closed();

    private CharacterEntity Viewer => _viewer ?? throw Closed();

    public bool ViewerHolds(uint questId) => Viewer.Quests.IsActive(questId);

    public bool ViewerCompleted(uint questId) => Viewer.Quests.IsCompleted(questId);

    public bool ViewerOnObjective(uint questId, uint objectiveId) =>
        DoodadGates.OnObjective(Viewer.Quests, _quests ?? throw Closed(), questId, objectiveId);

    /// <summary>Points the context at one viewer's question about one doodad, whose data gate answered <paramref name="dataGateAllows" />.</summary>
    internal void Open(Doodad doodad, CharacterEntity viewer, QuestCatalog quests, bool dataGateAllows)
    {
        _doodad = doodad;
        _viewer = viewer;
        _quests = quests;
        _dataGateAllows = dataGateAllows;
    }

    /// <summary>Lets go of the doodad and the viewer, so a script that kept the context reads nothing through it.</summary>
    internal void Close()
    {
        _doodad = null;
        _viewer = null;
        _quests = null;
        _dataGateAllows = false;
    }

    private static InvalidOperationException Closed() =>
        new("A doodad view context is valid only during the CanSee call it was handed to.");
}
