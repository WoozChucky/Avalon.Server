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
/// <b>When <see cref="Update" /> is called (#979).</b> Every script says, through <see cref="NextUpdateIn" />, which the
/// instance reads after each of its Updates, when it next needs one; there is no default. A script that needs every tick
/// answers <see cref="TimeSpan.Zero" />, and is called on every tick of its creature's instance while anyone is in it.
/// One whose work comes only from its events and from times it can name answers how long it can wait, and the instance
/// spends nothing on it in between. The answer grants nothing: a waiting script hears every event it would hear
/// otherwise, and is called at least whenever its own answer says it has work. See <see cref="NextUpdateIn" /> for the
/// answers and the wake rules.
/// </para>
/// </remarks>
public abstract class AiScript(ICreature creature, ISimulationContext context)
{
    protected ICreature Creature { get; } = creature;
    protected ISimulationContext Context { get; } = context;

    protected List<AiScript> ChainedScripts { get; } = new();

    public abstract object State { get; set; }

    /// <summary>
    /// How much creature time may pass before this script needs its next <see cref="Update" /> (#979), read by the
    /// instance after each Update of a creature's own script (a chained script's answer is its owner's to use):
    /// <list type="bullet">
    /// <item><see cref="TimeSpan.Zero" /> (or less): the next tick. A script that needs every tick, as every script had
    /// before #979, answers this always.</item>
    /// <item>A positive time: it waits, and is called on the first tick at which that much creature time
    /// (<see cref="ISimulationContext.CreatureClock" />) has passed since this Update. A time too long for the clock
    /// counts as an endless wait.</item>
    /// <item><see cref="Timeout.InfiniteTimeSpan" />: it waits for an event alone.</item>
    /// </list>
    /// <para>
    /// <b>Wake rules.</b> A waiting script is woken, and called on the instance's next creature step (the same tick's,
    /// when the wake comes before its turn in that step), by any of: a hit (<see cref="OnHit" />), an attack that dealt
    /// nothing, such as a dodge or a harmful aura (<see cref="OnAttacked" />), a character leaving the instance
    /// (<see cref="OnCharacterLeft" />), its due time, a new script or patrol path given to its creature, and its
    /// creature joining an instance; it is woken before it hears the event. A character coming within range is noticed
    /// by the script's own search, run when it is due (the built-in scripts search once a second);
    /// <see cref="OnEnteredRange" /> is a script's own notification, called from within an Update. Writing a script's
    /// <see cref="State" /> from outside wakes nothing.
    /// </para>
    /// <para>
    /// <see cref="Update" />'s <c>deltaTime</c> is always the tick's own: a waiting script is not called on the ticks it
    /// waits through and is not handed them later. A script that times something across them reads
    /// <see cref="ISimulationContext.CreatureClock" />, which counts every tick its instance updated its creatures.
    /// </para>
    /// <para>
    /// A subclass inherits the answer. A script that can wait and can be subclassed answers for its own type only, so a
    /// subclass whose Update is not bound by the same rules gets every tick unless it answers itself, as the built-in
    /// scripts do.
    /// </para>
    /// </summary>
    public abstract TimeSpan NextUpdateIn { get; }

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
