using Avalon.Common.ValueObjects;

namespace Avalon.Balance.Data;

/// <summary>A creature script's kit: its basic attack, its specials in rotation order, and the specials it casts only from range.</summary>
public sealed record CreatureKit(AbilityId Basic, IReadOnlyList<AbilityId> Specials, IReadOnlySet<AbilityId> RangedOnly);

/// <summary>
/// Each creature script's kit, by script type name as ScriptManager names them. A static table so the tool does not
/// read Avalon.World's script types; the world test <c>CreatureKitParityShould</c> keeps it equal to the scripts.
/// </summary>
public static class CreatureKits
{
    private static readonly IReadOnlySet<AbilityId> None = new HashSet<AbilityId>();

    private static CreatureKit Kit(uint basic, params uint[] specials) =>
        new(new AbilityId(basic), specials.Select(s => new AbilityId(s)).ToArray(), None);

    /// <summary>
    /// The kits, read once and never written: lookups run for every creature of every fight, from parallel rows.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, CreatureKit> ByScript =
        new Dictionary<string, CreatureKit>(StringComparer.Ordinal)
        {
            // ThornbackBoarScript: new(Gore, Trample)
            ["ThornbackBoarScript"] = Kit(300, 301),
            // GreyFenWolfScript: new(Bite, RavenousClaw)
            ["GreyFenWolfScript"] = Kit(302, 303),
            // BlightflySwarmlingScript: new(Sting, BlightSpit). BlightSpit is cast only while Sting cannot reach; in the
            // simulator everyone is in melee, so it never is.
            ["BlightflySwarmlingScript"] = new CreatureKit(
                new AbilityId(304), [new AbilityId(305)], new HashSet<AbilityId> { new(305) }),
            // HuskOfTheWoldScript: new(Slam, RottingBurst)
            ["HuskOfTheWoldScript"] = Kit(306, 307),
            // BramblemawAlphaScript: new(Maul, HowlingRoar, RendingFrenzy)
            ["BramblemawAlphaScript"] = Kit(308, 310, 309),
            // OldTuskrootScript: new(TuskGore, Earthsplitter, ThornVolley)
            ["OldTuskrootScript"] = Kit(311, 312, 313),
            // MotherBrambleScript: new(BrambleLash, BrambleNova, Thornspray)
            ["MotherBrambleScript"] = Kit(314, 315, 316),
        };
}
