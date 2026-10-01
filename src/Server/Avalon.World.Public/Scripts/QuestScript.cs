using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;

namespace Avalon.World.Public.Scripts;

/// <summary>
/// Custom logic for one quest (#433), named by its template's ScriptName and found by type name. One instance per
/// quest, shared by every character that has it, so it keeps no per-character state: everything per character is
/// in the <see cref="IQuestContext" /> a hook is handed. Hooks run on the tick, only while the character has the quest
/// active. A hook that throws is logged and the quest goes on. Built from DI only: its constructor may take services
/// (an ILoggerFactory, say) and nothing else.
/// </summary>
public abstract class QuestScript
{
    /// <summary>Can only narrow: the data's own rules are checked first, and false hides the quest.</summary>
    public virtual bool CanAccept(IQuestCharacter character) => true;

    public virtual void OnAccepted(IQuestContext context) { }

    /// <summary>Called for stage 0 right after <see cref="OnAccepted" />, and for each later stage as it starts.</summary>
    public virtual void OnStageStarted(IQuestContext context, int stage) { }

    /// <summary>A creature died and this character shared the kill.</summary>
    public virtual void OnCreatureKilled(IQuestContext context, ICreature creature) { }

    /// <summary>This character opened a conversation with an NPC.</summary>
    public virtual void OnInteract(IQuestContext context, ICreature creature) { }

    /// <summary>This character arrived in an instance (a login, a portal, a respawn).</summary>
    public virtual void OnEnterInstance(IQuestContext context, IMapInstance instance) { }
}
