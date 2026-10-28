using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World;
using Avalon.World.Abilities;
using Avalon.World.Auras;
using Avalon.World.Talents;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Talents;

/// <summary>
/// The talent tree (#1181) is checked whole: a tree breaking any rule is refused, naming its first problem, and holds no
/// node, while each class whose start resolves keeps its starter, so a bad deploy leaves every character one ability.
/// The tree here: the four class starts on a ring (1-4), a chain off the Warrior's start (10 small, 11 ability, 12
/// notable) and a fork pair of capstones off it (20, 21), each shaping the chain's cone (110).
/// </summary>
public class TalentCatalogShould
{
    private const uint WarriorStarter = 100;
    private const uint WizardStarter = 101;
    private const uint ChainAbility = 110;

    [Theory]
    [InlineData("class without a start", "the Cleric has no start")]
    [InlineData("start on a small node", "a Small node, not a Start")]
    [InlineData("start no class owns", "Start node 5 is no class's start")]
    [InlineData("two classes on one start", "the Hunter and the Cleric both start on node 3")]
    [InlineData("edge to a missing node", "names node 99, which the tree does not hold")]
    [InlineData("link on a small node", "node 10 is a Small node and cannot link an ability")]
    [InlineData("ability node without a link", "Ability node 11 links no ability")]
    [InlineData("link to a refused ability", "links ability 110, which the ability catalog refused")]
    [InlineData("link to a missing ability", "links ability 999, which the ability catalog does not hold")]
    [InlineData("one ability on two nodes", "ability 110 is linked by both node 4 and node 11")]
    [InlineData("fork group of one capstone", "fork group 1 holds 1 capstones")]
    [InlineData("more on attack speed", "AttackSpeed cannot be More")]
    [InlineData("range on strength", "Strength cannot have a range")]
    [InlineData("local armour", "LocalIncreasedArmor is local to the item")]
    [InlineData("notable without a name", "Notable node 12 has no name")]
    [InlineData("keystone rule on a notable", "node 12 is a Notable node and cannot carry a keystone rule")]
    [InlineData("modifier on a small node", "node 10 is a Small node and cannot shape an ability")]
    [InlineData("modifier on an ability no node grants", "node 20 shapes ability 104, which no node grants")]
    [InlineData("capstone shaping two abilities", "capstone 20 shapes both ability 110 and ability 100")]
    [InlineData("cone reach on a projectile", "node 20's ConeReachPct modifier: ")]
    [InlineData("explosion on a piercing projectile", "node 20's ImpactExplosion modifier: ")]
    [InlineData("node no start reaches", "node 30 is not reached from the Warrior's start")]
    public void Refuse_the_whole_tree_naming_the_first_problem(string row, string reasonPart)
    {
        var tree = new TreeRows();
        List<AbilityTemplate> abilities = Abilities();
        switch (row)
        {
            case "class without a start":
                tree.Starts.RemoveAll(s => s.Class == CharacterClass.Cleric);
                break;
            case "start on a small node":
                tree.Node(1).Kind = TalentNodeKind.Small;
                break;
            case "start no class owns":
                tree.Nodes.Add(new TalentNode { Id = 5, Kind = TalentNodeKind.Start, Name = "Spare start" });
                tree.Edges.Add(new TalentEdge { NodeA = 4, NodeB = 5 });
                tree.Abilities.Add(Link(5, 102));
                break;
            case "two classes on one start":
                tree.Starts.Single(s => s.Class == CharacterClass.Cleric).NodeId = 3;
                break;
            case "edge to a missing node":
                tree.Edges.Add(new TalentEdge { NodeA = 12, NodeB = 99 });
                break;
            case "link on a small node":
                tree.Abilities.Add(Link(10, 103));
                break;
            case "ability node without a link":
                tree.Abilities.RemoveAll(a => a.NodeId == 11);
                break;
            case "link to a refused ability":
                abilities.Single(a => a.Id.Value == ChainAbility).Reach = -1f;
                break;
            case "link to a missing ability":
                tree.Abilities.Single(a => a.NodeId == 11).AbilityId = new AbilityId(999);
                break;
            case "one ability on two nodes":
                tree.Abilities.Single(a => a.NodeId == 4).AbilityId = new AbilityId(ChainAbility);
                break;
            case "fork group of one capstone":
                tree.Node(21).ForkGroupId = null;
                break;
            case "more on attack speed":
                tree.Stats.Add(Line(10, StatType.AttackSpeed, TalentStatKind.More, 5));
                break;
            case "range on strength":
                tree.Stats.Add(Line(12, StatType.Strength, TalentStatKind.Added, 5, valueMax: 7));
                break;
            case "local armour":
                tree.Stats.Add(Line(10, StatType.LocalIncreasedArmor, TalentStatKind.Added, 10));
                break;
            case "notable without a name":
                tree.Node(12).Name = null;
                break;
            case "keystone rule on a notable":
                tree.Node(12).Keystone = KeystoneRule.NoCrits;
                break;
            case "modifier on a small node":
                tree.Modifiers.Add(Modifier(10, TalentModifierKind.ConeArcPct, ChainAbility, 10));
                break;
            case "modifier on an ability no node grants":
                abilities.Add(AbilityTestData.Cone(104));
                tree.Modifiers.Single(m => m.NodeId == 20).AbilityId = new AbilityId(104);
                break;
            case "capstone shaping two abilities":
                tree.Modifiers.Add(Modifier(20, TalentModifierKind.ConeArcPct, WarriorStarter, 10));
                break;
            case "cone reach on a projectile":
                abilities[abilities.FindIndex(a => a.Id.Value == ChainAbility)] = AbilityTestData.Projectile(ChainAbility);
                break;
            case "explosion on a piercing projectile":
                abilities[abilities.FindIndex(a => a.Id.Value == ChainAbility)] =
                    AbilityTestData.Projectile(ChainAbility, pierce: true);
                TalentAbilityModifier explosion = tree.Modifiers.Single(m => m.NodeId == 20);
                explosion.Kind = TalentModifierKind.ImpactExplosion;
                explosion.Value2 = 50;
                break;
            case "node no start reaches":
                tree.Nodes.Add(new TalentNode { Id = 30, Kind = TalentNodeKind.Small });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(row), row, null);
        }

        TalentCatalog catalog = Catalog(tree, abilities);

        Assert.False(catalog.IsLoaded);
        Assert.Equal(0, catalog.Count);
        Assert.Contains(reasonPart, catalog.Refusal, StringComparison.Ordinal);
        Assert.StartsWith("talent tree refused", catalog.Describe(), StringComparison.Ordinal);
        Assert.Equal(WizardStarter, catalog.StarterOf(CharacterClass.Wizard)?.Value);
        Assert.Equal(row == "start on a small node" ? null : WarriorStarter, catalog.StarterOf(CharacterClass.Warrior)?.Value);

        // A walk on the refused tree reaches nothing rather than throwing.
        Span<ulong> reached = [ulong.MaxValue];
        catalog.WalkHeld(catalog.StartIndexOf(CharacterClass.Warrior), [], reached, []);
        Assert.Equal(0UL, reached[0]);
    }

    /// <summary>
    /// Spec 5.2.12 (Part D): no capstone lengthens or deepens a stun, a root, a silence or a slow. Over the seeded tree,
    /// with Wide Arc's (306) modifier pointed at each crowd-control ability, checked against the seeded abilities and
    /// auras.
    /// </summary>
    [Theory]
    [InlineData(204u, TalentModifierKind.AuraDurationPct)]  // Shield Bash: Stunned
    [InlineData(204u, TalentModifierKind.AuraStacks)]
    [InlineData(224u, TalentModifierKind.AuraDurationPct)]  // Binding Shot: Bound
    [InlineData(214u, TalentModifierKind.AuraStacks)]       // Frost Bind: Frozen
    [InlineData(235u, TalentModifierKind.AuraDurationPct)]  // Hush: Silenced
    [InlineData(235u, TalentModifierKind.AuraStacks)]
    [InlineData(223u, TalentModifierKind.AuraDurationPct)]  // Crippling Shot: Crippled, a slow
    [InlineData(223u, TalentModifierKind.AuraStacks)]
    public void Refuse_a_capstone_that_lengthens_or_deepens_crowd_control(uint abilityId, TalentModifierKind kind)
    {
        StaticData data = ScenarioReferenceData.Shared.Data;
        TalentTreeRows rows = Avalon.Database.World.Seeding.TalentSeed.Rows();
        rows = rows with
        {
            Modifiers =
            [
                .. rows.Modifiers.Select(m => m.NodeId == 306 ? Modifier(306, kind, abilityId, 50) : m),
            ],
        };

        var catalog = new TalentCatalog(rows, data.Abilities, data.Auras, NullLoggerFactory.Instance);

        Assert.False(catalog.IsLoaded);
        Assert.Contains("node 306's", catalog.Refusal, StringComparison.Ordinal);
        Assert.Contains("lengthens or deepens crowd control", catalog.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Walk_the_held_nodes_linked_to_the_start()
    {
        TalentCatalog catalog = Catalog(new TreeRows(), Abilities());
        Assert.True(catalog.IsLoaded, catalog.Refusal);

        int start = catalog.StartIndexOf(CharacterClass.Warrior);
        Span<ulong> held = stackalloc ulong[catalog.BitWords];
        Span<ulong> reached = stackalloc ulong[catalog.BitWords];
        Span<int> queue = stackalloc int[catalog.Count];

        Hold(catalog, held, 10, 12);
        catalog.WalkHeld(start, held, reached, queue);
        Assert.Equal([1, 10], Ids(catalog, reached));

        Hold(catalog, held, 11);
        catalog.WalkHeld(start, held, reached, queue);
        Assert.Equal([1, 10, 11, 12], Ids(catalog, reached));
    }

    private static TalentCatalog Catalog(TreeRows tree, List<AbilityTemplate> abilities)
    {
        var rows = new TalentTreeRows(tree.Nodes, tree.Edges, tree.Stats, tree.Abilities, tree.ForkGroups, tree.Starts)
        {
            Modifiers = tree.Modifiers,
        };
        return new TalentCatalog(rows, new AbilityCatalog(abilities, NullLoggerFactory.Instance), AuraCatalog.Empty,
            NullLoggerFactory.Instance);
    }

    private static List<AbilityTemplate> Abilities() =>
    [
        AbilityTestData.Cone(WarriorStarter), AbilityTestData.Cone(WizardStarter), AbilityTestData.Cone(102),
        AbilityTestData.Cone(103), AbilityTestData.Cone(ChainAbility),
    ];

    private static void Hold(TalentCatalog catalog, Span<ulong> held, params int[] nodeIds)
    {
        foreach (int id in nodeIds)
        {
            Assert.True(catalog.TryIndexOf(id, out int index));
            held[index >> 6] |= 1UL << index;
        }
    }

    private static int[] Ids(TalentCatalog catalog, ReadOnlySpan<ulong> reached)
    {
        List<int> ids = [];
        for (int i = 0; i < catalog.Count; i++)
        {
            if ((reached[i >> 6] & (1UL << i)) != 0)
                ids.Add(catalog.NodeIdAt(i));
        }

        return [.. ids];
    }

    private static TalentNodeAbility Link(int nodeId, uint abilityId) =>
        new() { NodeId = nodeId, AbilityId = new AbilityId(abilityId) };

    private static TalentAbilityModifier Modifier(int nodeId, TalentModifierKind kind, uint abilityId, int value) =>
        new() { NodeId = nodeId, Kind = kind, AbilityId = new AbilityId(abilityId), Value = value };

    private static TalentNodeStat Line(int nodeId, StatType stat, TalentStatKind kind, int value, int? valueMax = null) =>
        new() { NodeId = nodeId, Stat = stat, Kind = kind, Value = value, ValueMax = valueMax };

    /// <summary>A valid tree, its rows mutable for each case to break one rule.</summary>
    private sealed class TreeRows
    {
        public List<TalentNode> Nodes { get; } =
        [
            new() { Id = 1, Kind = TalentNodeKind.Start, Name = "Warrior start" },
            new() { Id = 2, Kind = TalentNodeKind.Start, Name = "Wizard start" },
            new() { Id = 3, Kind = TalentNodeKind.Start, Name = "Hunter start" },
            new() { Id = 4, Kind = TalentNodeKind.Start, Name = "Cleric start" },
            new() { Id = 10, Kind = TalentNodeKind.Small },
            new() { Id = 11, Kind = TalentNodeKind.Ability, Name = "Chain ability" },
            new() { Id = 12, Kind = TalentNodeKind.Notable, Name = "Chain notable" },
            new() { Id = 20, Kind = TalentNodeKind.Capstone, Name = "Left capstone", ForkGroupId = 1 },
            new() { Id = 21, Kind = TalentNodeKind.Capstone, Name = "Right capstone", ForkGroupId = 1 },
        ];

        public List<TalentEdge> Edges { get; } =
        [
            new() { NodeA = 1, NodeB = 2 }, new() { NodeA = 2, NodeB = 3 }, new() { NodeA = 3, NodeB = 4 },
            new() { NodeA = 1, NodeB = 4 }, new() { NodeA = 1, NodeB = 10 }, new() { NodeA = 10, NodeB = 11 },
            new() { NodeA = 11, NodeB = 12 }, new() { NodeA = 1, NodeB = 20 }, new() { NodeA = 1, NodeB = 21 },
        ];

        public List<TalentNodeStat> Stats { get; } =
        [
            Line(10, StatType.Strength, TalentStatKind.Added, 5),
            Line(12, StatType.IncreasedAreaDamage, TalentStatKind.More, 10),
            Line(12, StatType.AddedColdDamageToAttacks, TalentStatKind.Added, 2, valueMax: 4),
        ];

        public List<TalentNodeAbility> Abilities { get; } =
        [
            Link(1, WarriorStarter), Link(2, WizardStarter), Link(3, 102), Link(4, 103), Link(11, ChainAbility),
        ];

        public List<TalentAbilityModifier> Modifiers { get; } =
        [
            Modifier(20, TalentModifierKind.ConeReachPct, ChainAbility, 10),
            Modifier(21, TalentModifierKind.ConeArcPct, ChainAbility, 10),
        ];

        public List<TalentForkGroup> ForkGroups { get; } = [new() { Id = 1 }];

        public List<TalentClassStart> Starts { get; } =
        [
            new() { Class = CharacterClass.Warrior, NodeId = 1 }, new() { Class = CharacterClass.Wizard, NodeId = 2 },
            new() { Class = CharacterClass.Hunter, NodeId = 3 }, new() { Class = CharacterClass.Cleric, NodeId = 4 },
        ];

        public TalentNode Node(int id) => Nodes.Single(n => n.Id == id);
    }
}
