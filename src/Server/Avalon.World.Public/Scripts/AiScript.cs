using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Scripts;

/// <summary>
/// A creature's AI. The instance calls <see cref="Update" /> on its tick and tells the script of what happens to its
/// creature through the event hooks (<see cref="OnHit" />, <see cref="OnAttacked" />, <see cref="OnEnteredRange" />,
/// <see cref="OnCharacterLeft" />).
/// </summary>
/// <remarks>
/// <para>
/// <b>When <see cref="Update" /> is called (#979).</b> By default on every tick of the creature's instance while
/// anyone is in it. A script whose work comes only from its events and from times it can name declares so with
/// <see cref="UpdatesWhenDue" />; the instance then calls it only while it is active, at its due time, and after an
/// event, and spends nothing on it in between. The declaration grants nothing: such a script hears every event it would
/// hear otherwise, and is called at least whenever its own answer says it has work.
/// </para>
/// <para>
/// For a script that declared it, after each <see cref="Update" /> the instance reads <see cref="NextUpdateIn" />:
/// </para>
/// <list type="bullet">
/// <item><see cref="TimeSpan.Zero" /> (or less): active, called on the next tick, as every script is by default.</item>
/// <item>A positive time: waiting, called on the first tick at which that much creature time
/// (<see cref="ISimulationContext.CreatureClock" />) has passed since this Update.</item>
/// <item><see cref="Timeout.InfiniteTimeSpan" />: waiting for an event alone.</item>
/// </list>
/// <para>
/// A waiting script is woken, and called on the instance's next creature step (the same tick's, when the event comes
/// before its turn in that step), by any of: a hit (<see cref="OnHit" />), an attack that dealt nothing, such as a
/// dodge or a harmful aura (<see cref="OnAttacked" />), a character leaving the instance (<see cref="OnCharacterLeft" />),
/// its due time, a new script or patrol path given to its creature, and its creature joining an instance. A character
/// coming within range is noticed by the script's own search, run when it is due (the built-in scripts search once a
/// second); <see cref="OnEnteredRange" /> is a script's own notification, called from within an Update. Writing a
/// script's <see cref="State" /> from outside wakes nothing.
/// </para>
/// <para>
/// <see cref="Update" />'s <c>deltaTime</c> is always the tick's own: a waiting script is not called on the ticks it
/// waits through and is not handed them later. A script that times something across them reads
/// <see cref="ISimulationContext.CreatureClock" />, which counts every tick its instance updated its creatures.
/// <see cref="UpdatesWhenDue" /> is read once, when the creature joins its instance or is given this script.
/// </para>
/// </remarks>
public abstract class AiScript(ICreature creature, ISimulationContext context)
{
    protected ICreature Creature { get; } = creature;
    protected ISimulationContext Context { get; } = context;

    protected List<AiScript> ChainedScripts { get; } = new();

    public abstract object State { get; set; }

    /// <summary>
    /// Whether this script needs <see cref="Update" /> only while it is active, at its due time and after an event
    /// (#979), rather than on every tick. False by default: a script written against the per-tick contract keeps it. A
    /// script that answers true answers <see cref="NextUpdateIn" /> honestly after every Update; see the class remarks
    /// for the wake rules. A subclass inherits the answer, so a script that opts in and can be subclassed answers for
    /// its own type only, unless a subclass's Update is bound by the same rules.
    /// </summary>
    public virtual bool UpdatesWhenDue => false;

    /// <summary>
    /// For a script that <see cref="UpdatesWhenDue" />, read after each <see cref="Update" />: how much creature time may
    /// pass before it needs its next Update. <see cref="TimeSpan.Zero" /> (the default): the next tick.
    /// <see cref="Timeout.InfiniteTimeSpan" />: only after an event. Never read for a script updated every tick.
    /// </summary>
    public virtual TimeSpan NextUpdateIn => TimeSpan.Zero;

    protected AiScript Chain(AiScript script)
    {
        ChainedScripts.Add(script);
        return this;
    }

    public virtual void Update(TimeSpan deltaTime)
    {
        foreach (AiScript script in ChainedScripts)
        {
            if (script.ShouldRun())
            {
                script.Update(deltaTime);
            }
        }
    }

    protected abstract bool ShouldRun();

    public virtual void OnHit(IUnit attacker, uint damage)
    {
        foreach (AiScript script in ChainedScripts)
        {
            script.OnHit(attacker, damage);
        }
    }

    /// <summary>
    /// <paramref name="attacker" /> attacked this creature and the hit dealt nothing: it was dodged (#506).
    /// The creature should fight back as if hit, but has taken no damage. A notification only: it grants
    /// nothing. Forwarded to chained scripts, as <see cref="OnHit" /> is.
    /// </summary>
    public virtual void OnAttacked(IUnit attacker)
    {
        foreach (AiScript script in ChainedScripts)
        {
            script.OnAttacked(attacker);
        }
    }

    public virtual void OnEnteredRange(ICharacter character)
    {
        foreach (AiScript script in ChainedScripts)
        {
            script.OnEnteredRange(character);
        }
    }

    /// <summary>
    /// <paramref name="character" /> has left this creature's instance (#546). A notification only: it
    /// grants nothing. Forwarded to chained scripts, as <see cref="OnHit" /> is.
    /// </summary>
    public virtual void OnCharacterLeft(ICharacter character)
    {
        foreach (AiScript script in ChainedScripts)
        {
            script.OnCharacterLeft(character);
        }
    }
}
