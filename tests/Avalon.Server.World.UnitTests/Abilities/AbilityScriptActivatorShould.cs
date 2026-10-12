using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Abilities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>
/// The activator keeps one factory per script type for the life of the process, shared by every cast system (#925).
/// A factory holds its type, so held any other way than weakly it would keep a reloaded script's old assembly loaded
/// for good.
/// </summary>
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
    /// Emits a script type into a collectible assembly and builds it twice, the second time through the factory the
    /// first one left. Not inlined, so nothing of it is still on the caller's stack when the caller collects.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference BuildAScriptFromACollectibleAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("CollectibleAbilityScripts"), AssemblyBuilderAccess.RunAndCollect);
        TypeBuilder type = assembly.DefineDynamicModule("Scripts")
            .DefineType("CollectibleAbilityScript", TypeAttributes.Public | TypeAttributes.Class, typeof(InertAbilityScript));
        ILGenerator il = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, s_castArguments)
            .GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, typeof(InertAbilityScript).GetConstructors().Single());
        il.Emit(OpCodes.Ret);
        Type scriptType = type.CreateType();

        var activator = new AbilityScriptActivator();
        for (int cast = 0; cast < 2; cast++)
        {
            AbilityScript? built = activator.Build(Substitute.For<IServiceProvider>(), scriptType,
                Substitute.For<IAbility>(), Substitute.For<IUnit>(), default, Substitute.For<IAbilityArena>());
            Assert.IsType(scriptType, built);
        }

        return new WeakReference(scriptType);
    }

    /// <summary>A script that does nothing: the base the emitted type needs, so it has only a constructor to emit.</summary>
    public class InertAbilityScript(IAbility ability, IUnit caster, AbilityAim aim) : AbilityScript(ability, caster, aim)
    {
        public override object State { get; set; } = null!;
        public override Vector3 Position { get; set; }
        public override Vector3 Velocity { get; set; }
        public override Vector3 Orientation { get; set; }
        public override ObjectGuid Guid { get; set; } = null!;

        public override void Prepare()
        {
        }

        protected override bool ShouldRun() => false;
    }
}
