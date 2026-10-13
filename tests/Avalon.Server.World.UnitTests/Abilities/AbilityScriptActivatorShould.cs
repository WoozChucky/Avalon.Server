using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Avalon.Common.Mathematics;
using Avalon.World.Abilities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>
/// Run alone: a test that scans every loaded assembly for scripts at the wrong moment (any that loads a ScriptManager
/// or builds a world host) holds the emitted type while it scans, and the type would be found alive.
/// </summary>
[CollectionDefinition(nameof(AbilityScriptActivatorShould), DisableParallelization = true)]
public sealed class AbilityScriptActivatorCollection;

/// <summary>
/// The activator keeps one factory per script type for the life of the process, shared by every cast system (#925).
/// A factory holds its type, so held any other way than weakly it would keep a reloaded script's old assembly loaded
/// for good.
/// </summary>
[Collection(nameof(AbilityScriptActivatorShould))]
public class AbilityScriptActivatorShould
{
    private static readonly Type[] s_castArguments =
        [typeof(IAbility), typeof(IUnit), typeof(AbilityAim), typeof(IAbilityArena)];

    [Fact]
    public void Let_a_script_assembly_unload_once_its_scripts_were_built()
    {
        WeakReference scriptType = BuildAScriptFromACollectibleAssembly();

        for (int i = 0; i < 10 && scriptType.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(scriptType.IsAlive);
    }

    /// <summary>
    /// The aim reaches a constructor out of a box every cast of an instance shares (#925). Taken as a reference, it
    /// would be that box, holding whatever aim was cast last.
    /// </summary>
    [Fact]
    public void Refuse_a_script_that_would_take_the_aim_boxed()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            new AbilityScriptActivator().Build(Substitute.For<IServiceProvider>(), typeof(TakesTheAimBoxed),
                Substitute.For<IAbility>(), Substitute.For<IUnit>(), default, Substitute.For<IAbilityArena>()));

        Assert.Contains(nameof(TakesTheAimBoxed), refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A factory resolves a constructor's services between the four arguments it is handed. A service that builds a
    /// script of its own through the same activator while it is resolved must not write its aim and arena over the
    /// ones the build around it has still to read (#925).
    /// </summary>
    [Fact]
    public void Keep_a_builds_arguments_when_a_service_it_resolves_builds_another()
    {
        var activator = new AbilityScriptActivator();
        var aim = new AbilityAim(new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 1f));
        IAbilityArena arena = Substitute.For<IAbilityArena>();
        var seen = new Seen();
        IServiceProvider services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(Seen)).Returns(_ =>
        {
            activator.Build(services, typeof(Built), Substitute.For<IAbility>(), Substitute.For<IUnit>(),
                new AbilityAim(new Vector3(1f, 0f, 0f), new Vector3(9f, 0f, 9f)), Substitute.For<IAbilityArena>());
            return seen;
        });

        activator.Build(services, typeof(ResolvesBetweenArguments), Substitute.For<IAbility>(),
            Substitute.For<IUnit>(), aim, arena);

        Assert.Equal(aim, seen.Aim);
        Assert.Same(arena, seen.Arena);
    }

    /// <summary>What <see cref="ResolvesBetweenArguments" /> was built with.</summary>
    public sealed class Seen
    {
        public AbilityAim Aim { get; set; }

        public IAbilityArena? Arena { get; set; }
    }

    /// <summary>Asks the container for a service after its caster and before its aim. Not an AbilityScript, for the reason <see cref="Built" /> gives.</summary>
    public sealed class ResolvesBetweenArguments
    {
        public ResolvesBetweenArguments(IAbility ability, IUnit caster, Seen seen, AbilityAim aim, IAbilityArena arena)
        {
            ArgumentNullException.ThrowIfNull(ability);
            ArgumentNullException.ThrowIfNull(caster);
            seen.Aim = aim;
            seen.Arena = arena;
        }
    }

    /// <summary>Not an AbilityScript, for the reason <see cref="Built" /> gives.</summary>
    public sealed class TakesTheAimBoxed(IAbility ability, IUnit caster, object aim, IAbilityArena arena)
    {
        public object[] Taken { get; } = [ability, caster, aim, arena];
    }

    /// <summary>
    /// Emits a type with a script's constructor into a collectible assembly and builds it twice, the second time
    /// through the factory the first one left. Not inlined, so nothing of it is still on the caller's stack when the
    /// caller collects.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference BuildAScriptFromACollectibleAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("CollectibleAbilityScripts"), AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder type = assembly.DefineDynamicModule("Scripts")
            .DefineType("CollectibleAbilityScript", TypeAttributes.Public | TypeAttributes.Class, typeof(Built));
        ILGenerator il = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, s_castArguments)
            .GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Built).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
        Type scriptType = type.CreateType();

        var activator = new AbilityScriptActivator();
        int before = Built.Count;
        for (int cast = 0; cast < 2; cast++)
        {
            activator.Build(Substitute.For<IServiceProvider>(), scriptType, Substitute.For<IAbility>(),
                Substitute.For<IUnit>(), default, Substitute.For<IAbilityArena>());
        }

        Assert.Equal(before + 2, Built.Count);
        return new WeakReference(scriptType);
    }

    /// <summary>
    /// The emitted type's base, counting what was built. Deliberately not an AbilityScript: a ScriptManager that
    /// another test loads meanwhile scans every assembly, and would name the emitted type and keep it alive.
    /// </summary>
    public class Built
    {
        private static int s_count;

        public Built() => Interlocked.Increment(ref s_count);

        /// <summary>A script's constructor, for a build of the base itself.</summary>
        public Built(IAbility ability, IUnit caster, AbilityAim aim, IAbilityArena arena)
            : this() =>
            _ = (ability, caster, aim, arena);

        public static int Count => Volatile.Read(ref s_count);
    }
}
