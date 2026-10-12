using System.Runtime.CompilerServices;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Microsoft.Extensions.DependencyInjection;

namespace Avalon.World.Abilities;

/// <summary>
/// Builds ability scripts from the cast system's four arguments, (ability, caster, aim, arena), with anything else a
/// constructor asks for resolved from the container (#925). A script type's constructor is found and bound once, into
/// an <see cref="ObjectFactory" /> every cast system shares; a cast then costs the factory call, not a reflection
/// search and a <c>params</c> array, which is what <c>ActivatorUtilities.CreateInstance</c> cost on every cast.
/// </summary>
/// <remarks>
/// <para>
/// <b>Binding.</b> A factory binds by the declared types below, not by the runtime types of what it is handed: a
/// constructor must take each of the four through a parameter of that type or of one it is assignable to. One that
/// declares a narrower type (the concrete ability, a creature, the map instance) is refused, where per-cast
/// activation would have built it whenever the runtime argument happened to fit.
/// <c>AbilityScriptConstructibilityShould</c> builds every seeded script through this class.
/// </para>
/// <para>
/// <b>Script types that go away.</b> The factories are held weakly by their type, so nothing here keeps a script
/// assembly loaded, and a factory leaves with its type. The caller asks <c>IScriptManager</c> for the type on every
/// cast, so a type the manager no longer names is never built, whatever is still held here.
/// </para>
/// <para>
/// <b>Making a factory</b> compiles the constructor call: about half a millisecond for a type, and some ten more for
/// the first in a process. <c>ScriptManager.Load</c> asks for each type's with <see cref="Prepare" />, off the tick;
/// a type that reaches a cast any other way has its factory made by that cast.
/// </para>
/// <para>
/// <b>Threads.</b> The factories are shared and safe to share. The argument array is not: each cast system owns one
/// activator, and so one array, used on its instance's tick alone.
/// </para>
/// </remarks>
public sealed class AbilityScriptActivator
{
    private static readonly Type[] s_argumentTypes =
        [typeof(IAbility), typeof(IUnit), typeof(AbilityAim), typeof(IAbilityArena)];

    private static readonly ConditionalWeakTable<Type, ObjectFactory> s_factories = new();

    private readonly object?[] _arguments = new object?[4];

    /// <summary>
    /// Makes <paramref name="scriptType" />'s factory now, so its first cast does not. Throws
    /// <see cref="InvalidOperationException" /> for a type whose constructors do not fit the four arguments.
    /// </summary>
    public static void Prepare(Type scriptType) => FactoryOf(scriptType);

    /// <summary>
    /// A new <paramref name="scriptType" /> script; null when the type is not an <see cref="AbilityScript" />. Throws
    /// what the constructor throws, and <see cref="InvalidOperationException" /> for a type whose constructors do not
    /// fit the four arguments or want a service the container lacks. A type that cannot be bound leaves no factory
    /// behind, so it is tried afresh on the next cast.
    /// </summary>
    public AbilityScript? Build(IServiceProvider services, Type scriptType, IAbility ability, IUnit caster,
        AbilityAim aim, IAbilityArena arena)
    {
        ObjectFactory factory = FactoryOf(scriptType);

        // A factory reads every argument before the constructor's body runs, so a constructor that casts through the
        // same cast system finds the array free.
        object?[] arguments = _arguments;
        arguments[0] = ability;
        arguments[1] = caster;
        arguments[2] = aim;
        arguments[3] = arena;
        try
        {
            return factory(services, arguments) as AbilityScript;
        }
        finally
        {
            // Not kept between casts: the array would hold the last caster, ability and arena alive.
            Array.Clear(arguments);
        }
    }

    private static ObjectFactory FactoryOf(Type scriptType) =>
        s_factories.GetValue(scriptType, static type => ActivatorUtilities.CreateFactory(type, s_argumentTypes));
}
