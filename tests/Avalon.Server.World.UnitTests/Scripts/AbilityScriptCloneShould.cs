using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Scripts;

public class AbilityScriptCloneShould
{
    // ──────────────────────────────────────────────
    // Minimal concrete stub — does NOT override Clone()
    // ──────────────────────────────────────────────

    private sealed class StubAbilityScript(IAbility ability, IUnit caster, AbilityAim aim)
        : AbilityScript(ability, caster, aim)
    {
        public override object State { get; set; } = null!;
        public override Vector3 Position { get; set; }
        public override Vector3 Velocity { get; set; }
        public override Vector3 Orientation { get; set; }
        public override ObjectGuid Guid { get; set; }
        public override void Prepare() { }
        protected override bool ShouldRun() => true;
        // intentionally no Clone() override — uses base implementation

        public new List<AbilityScript> Chain => ChainedScripts;
        public IAbility ExposedAbility => Ability;
    }

    private static StubAbilityScript MakeScript(IAbility? ability = null, IUnit? caster = null)
        => new(ability ?? Substitute.For<IAbility>(), caster ?? Substitute.For<IUnit>(), default);

    // ──────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────

    [Fact]
    public void Clone_ReturnsSameAbilityReference()
    {
        IAbility ability = Substitute.For<IAbility>();
        IUnit caster = Substitute.For<IUnit>();
        var original = new StubAbilityScript(ability, caster, default);

        var clone = (StubAbilityScript)original.Clone();

        Assert.NotSame(original, clone);
        // Ability is shared data: same reference on both
        Assert.Same(original.ExposedAbility, clone.ExposedAbility);
    }

    [Fact]
    public void Clone_HasIndependentChainedScriptsList()
    {
        StubAbilityScript original = MakeScript();
        StubAbilityScript chained = MakeScript();
        original.Chain(chained);

        var clone = (StubAbilityScript)original.Clone();

        // Both reference the same chained entries at clone time
        Assert.Single(clone.Chain);
        // Adding to clone's chain does not affect original
        clone.Chain(MakeScript());
        Assert.Single(original.Chain);
        Assert.Equal(2, clone.Chain.Count);
    }

    [Fact]
    public void Clone_ChainsAreRecursivelyCloned()
    {
        StubAbilityScript original = MakeScript();
        StubAbilityScript inner = MakeScript();
        original.Chain(inner);

        var clone = (StubAbilityScript)original.Clone();

        // The chained entry in the clone must itself be a separate instance
        Assert.NotSame(
            original.Chain[0],
            clone.Chain[0]);
    }
}
