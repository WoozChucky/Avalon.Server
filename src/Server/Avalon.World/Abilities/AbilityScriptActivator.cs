using System.Reflection;
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
/// activation would have built it whenever the runtime argument happened to fit. And a script has exactly one public
/// constructor that accepts the four, or marks one <c>[ActivatorUtilitiesConstructor]</c>: with two that fit, a
/// factory is refused, where per-cast activation picked the longer.
/// <c>AbilityScriptConstructibilityShould</c> builds every seeded script through this class.
/// </para>
/// <para>
/// <b>The aim's box.</b> A factory takes its arguments as objects, so the aim, a struct, goes in boxed. Each activator
/// keeps one box and writes every cast's aim into it; the factory unboxes it into the constructor's
/// <see cref="AbilityAim" /> parameter, a copy, so each script keeps the aim it was built with. A constructor that
/// took the aim as a reference (<c>object</c>, <c>ValueType</c>, an interface the aim implements) would be handed
/// the box itself, and read every later cast's aim in it: a script type with such a parameter on a public
/// constructor is refused. The aim is written into the box through <c>Unsafe.Unbox</c>, which replaces the boxed
/// value whole although <see cref="AbilityAim" /> is a readonly struct. That is safe here because nothing else holds
/// the box: it never leaves this activator but as the factory's argument, the factory copies the aim out of it on
/// the same thread right after the write, and no reader can meet a half-written value.
/// <c>InstanceAbilityCastSystemShould.Fire_each_script_with_the_aim_of_its_own_cast</c> pins it.
/// </para>
/// <para>
/// <b>A build inside a build.</b> A factory evaluates the constructor's parameters left to right, the ones it
/// resolves from the container among them. A service resolved between two of the four could itself build a script
/// through this activator, and would then overwrite the box and clear the array under the outer build. So a build
/// started while another is running on the same activator takes an array and a box of its own.
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
/// <b>Threads.</b> The factories are shared and safe to share. The argument array and the aim's box are not: each
/// cast system owns one activator, and so one of each, used on its instance's tick alone.
/// </para>
/// </remarks>
public sealed class AbilityScriptActivator
{
    private static readonly Type[] s_argumentTypes =
        [typeof(IAbility), typeof(IUnit), typeof(AbilityAim), typeof(IAbilityArena)];

    private static readonly ConditionalWeakTable<Type, ObjectFactory> s_factories = new();

    private readonly object _aim = default(AbilityAim);
    private readonly object?[] _arguments;
    private bool _building;

    public AbilityScriptActivator() => _arguments = [null, null, _aim, null];

    /// <summary>
    /// Makes <paramref name="scriptType" />'s factory now, so its first cast does not. Throws
    /// <see cref="InvalidOperationException" /> for a type whose constructors do not fit the four arguments, or would
    /// take the aim boxed.
    /// </summary>
    public static void Prepare(Type scriptType) => FactoryOf(scriptType);

    /// <summary>
    /// A new <paramref name="scriptType" /> script; null when the type is not an <see cref="AbilityScript" />. Throws
    /// what the constructor throws, and <see cref="InvalidOperationException" /> for a type whose constructors do not
    /// fit the four arguments, would take the aim boxed, or want a service the container lacks. A type that cannot be
    /// bound leaves no factory behind, so it is tried afresh on the next cast.
    /// </summary>
    public AbilityScript? Build(IServiceProvider services, Type scriptType, IAbility ability, IUnit caster,
        AbilityAim aim, IAbilityArena arena)
    {
        ObjectFactory factory = FactoryOf(scriptType);

        // The kept array and box belong to the build that is running: its factory may not have read them all yet
        // (it resolves services between them). A build started meanwhile brings its own.
        if (_building)
        {
            return factory(services, [ability, caster, aim, arena]) as AbilityScript;
        }

        object?[] arguments = _arguments;
        Unsafe.Unbox<AbilityAim>(_aim) = aim;
        arguments[0] = ability;
        arguments[1] = caster;
        arguments[3] = arena;
        _building = true;
        try
        {
            return factory(services, arguments) as AbilityScript;
        }
        finally
        {
            // Not kept between casts: the array would hold the last ability, caster and arena alive. The aim's box
            // stays in its slot: it holds no reference.
            arguments[0] = null;
            arguments[1] = null;
            arguments[3] = null;
            _building = false;
        }
    }

    private static ObjectFactory FactoryOf(Type scriptType) => s_factories.GetValue(scriptType, CreateFactory);

    private static ObjectFactory CreateFactory(Type scriptType)
    {
        foreach (ConstructorInfo constructor in scriptType.GetConstructors())
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                if (!parameter.ParameterType.IsValueType && parameter.ParameterType.IsAssignableFrom(typeof(AbilityAim)))
                {
                    throw new InvalidOperationException(
                        $"{scriptType} declares the constructor parameter '{parameter.Name}' as {parameter.ParameterType}, " +
                        $"which could be handed the aim in the box every cast shares. Declare the aim as {nameof(AbilityAim)}.");
                }
            }
        }

        return ActivatorUtilities.CreateFactory(scriptType, s_argumentTypes);
    }
}
