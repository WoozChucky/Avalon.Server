using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Spells;
using Avalon.World.Public.Units;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Scripts;

public class SpellScriptCloneShould
{
    // ──────────────────────────────────────────────
    // Minimal concrete stub — does NOT override Clone()
    // ──────────────────────────────────────────────

    private sealed class StubSpellScript(ISpell spell, IUnit caster, IUnit? target)
        : SpellScript(spell, caster, target)
    {
        public override object State { get; set; } = null!;
        public override Vector3 Position { get; set; }
        public override Vector3 Velocity { get; set; }
        public override Vector3 Orientation { get; set; }
        public override ObjectGuid Guid { get; set; }
        public override void Prepare() { }
        protected override bool ShouldRun() => true;
        // intentionally no Clone() override — uses base implementation

        public List<SpellScript> Chain => ChainedScripts;
        public ISpell ExposedSpell => Spell;
    }

    private static StubSpellScript MakeScript(ISpell? spell = null, IUnit? caster = null)
        => new(spell ?? Substitute.For<ISpell>(), caster ?? Substitute.For<IUnit>(), null);

    // ──────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────

    [Fact]
    public void Clone_ReturnsSameSpellReference()
    {
        var spell = Substitute.For<ISpell>();
        var caster = Substitute.For<IUnit>();
        var original = new StubSpellScript(spell, caster, null);

        var clone = (StubSpellScript)original.Clone();

        Assert.NotSame(original, clone);
        // Spell is shared data: same reference on both
        Assert.Same(original.ExposedSpell, clone.ExposedSpell);
    }

    [Fact]
    public void Clone_HasIndependentChainedScriptsList()
    {
        var original = MakeScript();
        var chained = MakeScript();
        original.Chain(chained);

        var clone = (StubSpellScript)original.Clone();

        // Both reference the same chained entries at clone time
        Assert.Single(clone.Chain);
        // Adding to clone's chain does not affect original
        clone.Chain(MakeScript());
        Assert.Single(original.Chain);
        Assert.Equal(2, clone.Chain.Count);
    }

    [Fact]
    public void Clone_MutatingCloneChain_DoesNotAffectOriginal()
    {
        var original = MakeScript();
        original.Chain(MakeScript());
        original.Chain(MakeScript());

        var clone = (StubSpellScript)original.Clone();
        clone.Chain.Clear();

        // Original chain is unaffected
        Assert.Equal(2, original.Chain.Count);
        Assert.Empty(clone.Chain);
    }

    [Fact]
    public void Clone_SubclassWithoutOverride_ReturnsCorrectType()
    {
        var original = MakeScript();

        var clone = original.Clone();

        Assert.IsType<StubSpellScript>(clone);
        Assert.NotSame(original, clone);
    }

    [Fact]
    public void Clone_ChainsAreRecursivelyCloned()
    {
        var original = MakeScript();
        var inner = MakeScript();
        original.Chain(inner);

        var clone = (StubSpellScript)original.Clone();

        // The chained entry in the clone must itself be a separate instance
        Assert.NotSame(
            original.Chain[0],
            clone.Chain[0]);
    }
}
