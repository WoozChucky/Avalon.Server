using System.Reflection;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// A quest script is modding surface (#433): its hooks hand it read-only views, never a live creature or instance,
/// so its one write is IQuestContext.Advance on its own Scripted objectives.
/// </summary>
public class QuestScriptSurfaceShould
{
    private static readonly HashSet<Type> ReadOnlyParameters =
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
                Assert.True(ReadOnlyParameters.Contains(parameter.ParameterType),
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

    [Fact]
    public void Copy_a_creature_into_its_view_without_sharing_its_guid()
    {
        var live = new ObjectGuid(ObjectType.Creature, 42);
        ICreature creature = Substitute.For<ICreature>();
        ICreatureMetadata metadata = Substitute.For<ICreatureMetadata>();
        metadata.Id.Returns(new CreatureTemplateId(704));
        creature.Metadata.Returns(metadata);
        creature.Guid.Returns(live);
        creature.Name.Returns("Boar");
        creature.Level.Returns((ushort)3);
        creature.CurrentHealth.Returns(0u);

        var view = QuestCreatureView.From(creature);

        Assert.Equal((704ul, "Boar", (ushort)3, true), (view.TemplateId.Value, view.Name, view.Level, view.IsDead));
        Assert.Equal(live.RawValue, view.Guid.RawValue);
        Assert.NotSame(live, view.Guid);
    }

    [Fact]
    public void Copy_an_instance_into_its_view()
    {
        var id = Guid.NewGuid();
        IMapInstance instance = Substitute.For<IMapInstance>();
        instance.InstanceId.Returns(id);
        instance.TemplateId.Returns(new MapTemplateId(2));
        instance.MapType.Returns(MapType.Normal);

        Assert.Equal(new QuestInstanceView(id, new MapTemplateId(2), MapType.Normal), QuestInstanceView.From(instance));
    }
}
