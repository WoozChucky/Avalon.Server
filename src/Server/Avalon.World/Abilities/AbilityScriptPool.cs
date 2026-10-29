using System.Diagnostics;
using System.Reflection;
using Avalon.Common.Telemetry;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities;

/// <summary>
/// One instance's reusable ability scripts (#1181 Part D, the owner's decision of 2026-10-28), kept by its
/// <see cref="InstanceAbilityCastSystem" /> and used on its tick alone, so a warm cast of a pooled type allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it pools.</b> Scripts of the types registered with <see cref="Pools{T}" />, each type with its own free stack,
/// keyed by the type the script manager answers for a cast: <c>ProjectileAbilityScript</c>, <c>CircleAbilityScript</c> and
/// <c>ConeAbilityScript</c> (#1238), each with its own copy of the ability and its own guid wrapper. And the hit sets those
/// projectiles share (<see cref="ProjectileHits" />). A script type registers by deriving from
/// <see cref="PooledAbilityScript" />, whose <c>Launch</c> and <c>Clear</c> are the reset contract every pooled type keeps.
/// A circle or a cone finishes inside its <c>Prepare</c>, so it never joins the running list: it is retired as it fires,
/// and, since its guid never reaches a view, free again at once (registered with <c>seenByViews</c> false).
/// </para>
/// <para>
/// <b>Nothing may hold a script's copy of the ability past its cast.</b> The copy is cleared when the script is handed
/// back, and made again for its next cast. The holders walked for #1238: the auras a hit places keep the id and the
/// metadata (<c>AuraSource.Of</c>), never the copy; the fired, impact and damage packets copy what they send when they are
/// encoded; and the dodge wait's held hits (<see cref="PendingHits" />), which outlive the cast by up to six ticks, keep a
/// copy of their own.
/// </para>
/// <para>
/// <b>No container services.</b> A pooled type is made by the factory it registered with (<see cref="Make" />), which
/// passes the four cast arguments and nothing from the container, where the activator binds constructor services. So
/// <see cref="Pools{T}" /> refuses, with an <see cref="InvalidOperationException" /> as the cast system is built, a type
/// with any constructor but one public constructor taking the four cast arguments (<see cref="IAbility" />,
/// <see cref="IUnit" />, <see cref="AbilityAim" />, <see cref="IAbilityArena" />): a second one taking a service, even
/// marked <c>[ActivatorUtilitiesConstructor]</c>, would be the activator's choice while the pool kept calling the first, and
/// the service would be missing on pooled casts alone. Checked once per type. A seeded script name answering any other
/// type is built by the activator, unpooled.
/// </para>
/// <para>
/// <b>Where each one goes back.</b> <see cref="Retire" /> when a prepared script is done: as it fires for one that finished
/// inside its <c>Prepare</c> (every circle and cone), or as it leaves the cast system's running list, finished with its
/// last state taken (the sweep, or <c>DropFinished</c>), its caster gone (<c>CancelScriptsOf</c>), or its creature caster
/// dead or walking home. A type seen by views (a projectile) is free again only after two whole view rounds; one whose
/// guid no view ever holds (a circle, a cone) is free at once, as a returned one is. <see cref="Return" /> for one never
/// prepared, free at once: a queued cast that ends without firing (moved, dead, a creature gone, crowd
/// control, <c>CancelCasts</c>). A script whose <c>Prepare</c>, <c>Update</c> or <c>State</c> threw is handed to
/// neither, and left to the collector with what it holds.
/// </para>
/// <para>
/// <b>Ownership</b>, as an aura system's pool of auras: a script this pool made names it (<see cref="PooledAbilityScript.Pool" />),
/// and only this pool takes it back. One built anywhere else, by the activator (a cast whose ability is not a
/// <see cref="GameAbility" />, a script name that answers another type) or by a test through the public constructor, is
/// left to the collector. Every script handed back is cleared at once (<see cref="PooledAbilityScript.Clear" />), so a
/// pooled script keeps nothing alive: no caster, no metadata (none from before a reload), no unit's guid.
/// </para>
/// <para>
/// <b>The cap.</b> Each type keeps at most <see cref="InstanceAbilityCastSystem.PooledScripts" /> free and retired scripts,
/// and the hit sets as many: one handed back past it is left to the collector, its hit set released, and counted
/// (<c>world.ability_scripts.pool_drops</c>, <see cref="DiagnosticsConfig.World.AbilityScriptPoolDrops" />), since the
/// next cast of its type builds one anew. So an instance keeps what its own busiest moment had in flight, up to some
/// 110 KB of projectiles.
/// </para>
/// <para>
/// <b>The guid.</b> A projectile's id is new for every flight (<c>Prepare</c> writes the next id into its own wrapper,
/// <c>ObjectGuid.Set</c>), so an id is never reused (#981). Its wrapper is, so it must not change while anything still
/// holds it for the flight before. Every holder of a world object's guid wrapper, walked for #1181 Part D:
/// </para>
/// <list type="bullet">
///   <item>each view's <c>EntityTrackingSystem</c> keeps the wrapper it was handed (<c>TrackedEntity.Guid</c>, keyed by its
///   raw id) until it tells the removal, at the first view round after the projectile left the world objects, and its
///   <c>_pendingRemovals</c> only within that update;</item>
///   <item>each view's new, updated and removed lists (<c>CharacterCharacterGameState</c>) until that view's next decision
///   or hold, on the next tick; its <c>_forgotten</c> list after a start over, whose removal is encoded at once
///   (<c>SInstanceStateRemovePacket.Create</c>) and never read again;</item>
///   <item>the instance's <c>_frameDirtyFields</c>, keyed by the wrapper, emptied at the next tick's snapshot;</item>
///   <item>a view started over (a client entering another instance, <c>AddCharacter</c>'s reset, which a move makes on the
///   tick with the removal; a broadcast that threw, <c>StartOver</c>) encodes its removal at once;</item>
///   <item>the packets (impact, state add, update and remove) copy the raw id when they are encoded, at once; the state
///   assembler and the running list's index are keyed by the raw id and emptied when the broadcast or the list changes.</item>
/// </list>
/// <para>
/// So a retired script waits for two whole rounds (<see cref="IAbilityArena.ViewRound" />): the first after it left
/// tells every view here its removal, and the second outlasts every list that round filled, each emptied by the next
/// tick's decision or hold. Some 200 ms of pool life per projectile.
/// </para>
/// </remarks>
internal sealed class AbilityScriptPool(IAbilityArena arena)
{
    /// <summary>A new script of a pooled type, naming <paramref name="pool" /> as its pool, over its own cast copy.</summary>
    public delegate PooledAbilityScript Make(AbilityScriptPool pool, GameAbility own, IUnit caster, AbilityAim aim,
        IAbilityArena arena);

    private readonly Dictionary<Type, Kind> _kinds = [];

    // Retired in the order they left, each with the round it left in: the rounds only grow, so the oldest is first.
    private readonly Queue<Retired> _retired = new();
    private readonly Stack<ProjectileHits> _freeHits = new();

    /// <summary>
    /// Pools scripts of type <typeparamref name="T" />, each new one made by <paramref name="make" />. One
    /// <paramref name="seenByViews" /> becomes a world object a view may hold the guid of, so a retired one waits two whole
    /// view rounds; any other is free again as it is retired. Throws for a type with any constructor but the one taking the
    /// four cast arguments (the remarks' "No container services").
    /// </summary>
    public void Pools<T>(Make make, bool seenByViews) where T : PooledAbilityScript
    {
        CastConstructorOnly<T>.Require();
        _kinds.Add(typeof(T), new Kind(make, seenByViews));
    }

    /// <summary>
    /// A script of <paramref name="scriptType" /> for a new cast: a free one launched (<see cref="PooledAbilityScript.Launch" />),
    /// the retired ones whose rounds have passed freed first, else a new one over a cast copy of <paramref name="held" />.
    /// Null for a type this pool does not pool.
    /// </summary>
    public PooledAbilityScript? Take(Type scriptType, GameAbility held, IUnit caster, AbilityAim aim)
    {
        if (!_kinds.TryGetValue(scriptType, out Kind? kind))
            return null;

        Recycle();
        if (kind.Free.TryPop(out PooledAbilityScript? script))
        {
            script.Returned = false;
            script.Launch(held, caster, aim);
            return script;
        }

        PooledAbilityScript made = kind.Make(this, held.CloneForCast(), caster, aim, arena);
        Debug.Assert(ReferenceEquals(made.Pool, this), "A pooled script must name the pool that made it");
        return made;
    }

    /// <summary>A script of this pool's, never prepared, free again at once. Any other script is left as it is.</summary>
    public void Return(AbilityScript script)
    {
        if (HandBack(script) is { } back)
            Free(back);
    }

    /// <summary>
    /// A script of this pool's that was prepared and is done: free again once two whole view rounds have passed for a type
    /// seen by views, at once for any other. Any other script is left as it is.
    /// </summary>
    public void Retire(AbilityScript script)
    {
        if (HandBack(script) is not { } back)
            return;

        if (!back.Kind.SeenByViews)
        {
            Free(back);
            return;
        }

        if (back.Kind.Held >= InstanceAbilityCastSystem.PooledScripts)
        {
            DiagnosticsConfig.World.AbilityScriptPoolDrops.Add(1);
            return;
        }

        back.Kind.Retired++;
        _retired.Enqueue(new Retired(back.Script, back.Kind, arena.ViewRound));
    }

    /// <summary>Frees every retired script whose two view rounds have passed: the cast system's update, first, and every take.</summary>
    public void Recycle()
    {
        uint round = arena.ViewRound;
        while (_retired.TryPeek(out Retired next) && unchecked(round - next.Round) >= 2)
        {
            _retired.Dequeue();
            next.Kind.Retired--;
            next.Kind.Free.Push(next.Script);
        }
    }

    /// <summary>An empty hit set for a new projectile cast, with one lane flying with it.</summary>
    public ProjectileHits TakeHits()
    {
        if (!_freeHits.TryPop(out ProjectileHits? hits))
            hits = new ProjectileHits();

        hits.Lanes = 1;
        return hits;
    }

    /// <summary>One lane fewer flying with <paramref name="hits" />: at none, emptied and free again.</summary>
    public void Release(ProjectileHits hits)
    {
        Debug.Assert(hits.Lanes > 0, "A hit set was released more often than lanes took it");
        if (--hits.Lanes > 0)
            return;

        hits.Units.Clear();
        if (_freeHits.Count < InstanceAbilityCastSystem.PooledScripts)
            _freeHits.Push(hits);
    }

    /// <summary>A script handed back, free at once; past the cap, left to the collector and counted.</summary>
    private static void Free((PooledAbilityScript Script, Kind Kind) back)
    {
        if (back.Kind.Held < InstanceAbilityCastSystem.PooledScripts)
            back.Kind.Free.Push(back.Script);
        else
            DiagnosticsConfig.World.AbilityScriptPoolDrops.Add(1);
    }

    /// <summary>The script cleared and marked returned, with its type's stacks, when it is this pool's; else nothing.</summary>
    private (PooledAbilityScript Script, Kind Kind)? HandBack(AbilityScript script)
    {
        if (script is not PooledAbilityScript pooled || !ReferenceEquals(pooled.Pool, this)
            || !_kinds.TryGetValue(pooled.GetType(), out Kind? kind))
        {
            return null;
        }

        Debug.Assert(!pooled.Returned, "A script handed back to its pool was already handed back");
        if (pooled.Returned)
            return null;

        pooled.Returned = true;
        pooled.Clear();
        return (pooled, kind);
    }

    /// <summary>
    /// One pooled type: how a new one is made, whether a view may hold its guid, its free scripts, and how many of its
    /// scripts are retired.
    /// </summary>
    private sealed class Kind(Make make, bool seenByViews)
    {
        public Make Make { get; } = make;
        public bool SeenByViews { get; } = seenByViews;
        public Stack<PooledAbilityScript> Free { get; } = new();
        public int Retired { get; set; }

        /// <summary>Free and retired: what the cap bounds.</summary>
        public int Held => Free.Count + Retired;
    }

    private readonly record struct Retired(PooledAbilityScript Script, Kind Kind, uint Round);

    /// <summary>
    /// Whether <typeparamref name="T" />'s only constructor is one public constructor taking the four cast arguments, read
    /// once per type: the pool's factory calls that one, and nothing else may be the activator's choice.
    /// </summary>
    private static class CastConstructorOnly<T>
    {
        private static readonly Type[] s_cast = [typeof(IAbility), typeof(IUnit), typeof(AbilityAim), typeof(IAbilityArena)];
        private static readonly string? s_refusal = RefusalOf(typeof(T));

        public static void Require()
        {
            if (s_refusal is not null)
                throw new InvalidOperationException(s_refusal);
        }

        private static string? RefusalOf(Type type)
        {
            ConstructorInfo[] all = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return all is [{ IsPublic: true } only] && only.GetParameters().Select(p => p.ParameterType).SequenceEqual(s_cast)
                ? null
                : $"{type.Name} cannot be pooled: a pooled ability script has exactly one constructor, public, taking " +
                  "(IAbility, IUnit, AbilityAim, IAbilityArena), since the pool builds it with those four alone and " +
                  "no container service.";
        }
    }
}
