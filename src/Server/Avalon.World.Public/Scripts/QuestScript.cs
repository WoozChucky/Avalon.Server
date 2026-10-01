namespace Avalon.World.Public.Scripts;

/// <summary>
/// Custom logic for one quest (#433), named by its template's ScriptName and found by type name. One instance per
/// quest, shared by every character that has it, so it keeps no per-character state: everything per character is
/// in the <see cref="IQuestContext" /> a hook is handed. Hooks run on the tick, only while the character has the quest
/// active. A hook that throws is logged and the quest goes on. Built from DI only, with no runtime arguments: its
/// constructor may take only an ILoggerFactory, an ILogger&lt;T&gt; and a TimeProvider (#738); a script asking for anything
/// else cannot be built, and its quest cannot be accepted. A hook reads the world only through read-only views
/// (QuestCreatureView, QuestInstanceView); its one write is IQuestContext.Advance.
/// </summary>
public abstract class QuestScript
{
    /// <summary>Can only narrow: the data's own rules are checked first, and false hides the quest.</summary>
    public virtual bool CanAccept(IQuestCharacter character) => true;

    public virtual void OnAccepted(IQuestContext context) { }

    /// <summary>Called for stage 0 right after <see cref="OnAccepted" />, and for each later stage as it starts.</summary>
    public virtual void OnStageStarted(IQuestContext context, int stage) { }

    /// <summary>
    /// A creature died and this character shared the kill. The kill's own credit is counted first, so when it
    /// completed the stage the context is already the new stage's (its <see cref="IQuestContext.Stage" /> and counts);
    /// a kill that made the quest ready reaches no hook.
    /// </summary>
    public virtual void OnCreatureKilled(IQuestContext context, QuestCreatureView creature) { }

    /// <summary>This character opened a conversation with an NPC.</summary>
    public virtual void OnInteract(IQuestContext context, QuestCreatureView creature) { }

    /// <summary>
    /// This character arrived in an instance other than the one this hook last ran for: a portal, a respawn, and
    /// every login (nothing about it is saved, so each login is a fresh arrival, even in the same town). Not once per
    /// instance ever: leaving and coming back runs it again. Which instance it last ran for is remembered per
    /// character, not per quest, so a quest accepted while the character is already inside an instance, or one that
    /// goes from ready back to active, hears it only on the next arrival.
    /// </summary>
    public virtual void OnEnterInstance(IQuestContext context, QuestInstanceView instance) { }
}
