using Avalon.World.Public.Scripts;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>What the sample quest script saw and what it should do; a test registers one in the container.</summary>
public sealed class QuestScriptRecorder
{
    public List<string> Calls { get; } = [];
    public bool AllowAccept { get; set; } = true;
    public string? ThrowIn { get; set; }

    /// <summary>With <see cref="ThrowIn" />: throw only for this quest's hooks (null: for every quest's).</summary>
    public uint? ThrowOnlyFor { get; set; }

    /// <summary>Each hook that had a context, with its quest.</summary>
    public List<(uint Quest, string Hook)> Seen { get; } = [];
    public (uint Objective, uint Amount)? AdvanceOnKill { get; set; }
    public List<bool> AdvanceResults { get; } = [];
    public IQuestContext? LastContext { get; set; }
    public IQuestCharacter? LastCharacter { get; set; }
    public QuestCreatureView? LastCreature { get; set; }
    public QuestInstanceView? LastInstance { get; set; }

    /// <summary>Runs inside OnStageStarted, after it is recorded: a test drives progress from inside a stage start.</summary>
    public Action<IQuestContext, int>? OnStage { get; set; }
}

/// <summary>
/// The quest script the tests use (#433). Public, and constructible from an empty container (the recorder is
/// optional), because the constructibility test builds it through the production container, and ScriptManager finds
/// it too. ScriptManager keys quest scripts by short type name across every loaded assembly, so no other test script
/// may be called SampleQuestScript.
/// </summary>
public sealed class SampleQuestScript(QuestScriptRecorder? recorder = null) : QuestScript
{
    private readonly QuestScriptRecorder _recorder = recorder ?? new QuestScriptRecorder();

    private void Hook(string name, IQuestContext? context)
    {
        _recorder.Calls.Add(name);
        if (context is not null)
        {
            _recorder.LastContext = context;
            _recorder.Seen.Add((context.QuestId, name));
        }

        if (_recorder.ThrowIn == name && (_recorder.ThrowOnlyFor is null || _recorder.ThrowOnlyFor == context?.QuestId))
            throw new InvalidOperationException($"{name} failed on purpose");
    }

    public override bool CanAccept(IQuestCharacter character)
    {
        _recorder.LastCharacter = character;
        Hook("CanAccept", null);
        return _recorder.AllowAccept;
    }

    public override void OnAccepted(IQuestContext context) => Hook("OnAccepted", context);

    public override void OnStageStarted(IQuestContext context, int stage)
    {
        Hook($"OnStageStarted:{stage}", context);
        _recorder.OnStage?.Invoke(context, stage);
    }

    public override void OnCreatureKilled(IQuestContext context, QuestCreatureView creature)
    {
        _recorder.LastCreature = creature;
        Hook("OnCreatureKilled", context);
        if (_recorder.AdvanceOnKill is { } advance)
            _recorder.AdvanceResults.Add(context.Advance(advance.Objective, advance.Amount));
    }

    public override void OnInteract(IQuestContext context, QuestCreatureView creature)
    {
        _recorder.LastCreature = creature;
        Hook("OnInteract", context);
    }

    public override void OnEnterInstance(IQuestContext context, QuestInstanceView instance)
    {
        _recorder.LastInstance = instance;
        Hook("OnEnterInstance", context);
    }
}
