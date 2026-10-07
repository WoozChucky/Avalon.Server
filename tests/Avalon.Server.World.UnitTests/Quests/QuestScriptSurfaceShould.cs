using System.Reflection;
using Avalon.World.Public.Scripts;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// A quest script is modding surface (#433): its hooks hand it read-only views, never a live creature or instance,
/// so its one write is IQuestContext.Advance on its own Scripted objectives.
/// </summary>
public class QuestScriptSurfaceShould
{
    private static readonly HashSet<Type> s_readOnlyParameters =
        [typeof(IQuestContext), typeof(IQuestCharacter), typeof(QuestCreatureView), typeof(QuestInstanceView), typeof(int)];

    [Fact]
    public void Hand_its_hooks_only_read_only_views()
    {
        MethodInfo[] hooks = typeof(QuestScript).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotEmpty(hooks);
        foreach (MethodInfo hook in hooks)
        {
            foreach (ParameterInfo parameter in hook.GetParameters())
            {
                Assert.True(s_readOnlyParameters.Contains(parameter.ParameterType),
                    $"{hook.Name}({parameter.ParameterType.Name} {parameter.Name}) hands a script something it could change");
            }
        }
    }

    [Theory]
    [InlineData(typeof(QuestCreatureView))]
    [InlineData(typeof(QuestInstanceView))]
    [InlineData(typeof(IQuestCharacter))]
    public void Expose_no_setter_but_init_on_its_views(Type view)
    {
        foreach (PropertyInfo property in view.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            MethodInfo? setter = property.SetMethod;
            Assert.True(setter is null || setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)),
                $"{view.Name}.{property.Name} has a setter");
        }
    }
}
