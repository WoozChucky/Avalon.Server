using Avalon.Common.ValueObjects;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Abilities;

/// <summary>
/// The abilities a character holds. Every list is the container's own and walked by index, so the per-tick
/// <see cref="Update" />, the cast handler's lookup and <see cref="IsCasting" /> allocate no boxed enumerator, closure or
/// delegate (#880).
/// </summary>
/// <remarks>
/// A talent change (#1181) rebuilds what is held in place: <see cref="StartRebuild" /> hands out a spare list,
/// <see cref="FinishRebuild" /> swaps it with the held one and parks every ability that left, and a later rebuild takes a
/// parked ability back through <see cref="Unpark" />. Once warm, a change allocates nothing, however often a character
/// allocates and refunds. Parking is safe because a cast never holds the character's ability, only its own copy
/// (<see cref="GameAbility.CloneForCast" />). Only an ability built from the generation a rebuild reads
/// (<see cref="TreeAbility.BuiltFrom" />) is parked or taken back, and the parked list is cleared when the ability catalog
/// generation changes, so it holds at most the abilities the tree grants and a re-allocation after a reload or a live
/// edit never brings back an older generation's details.
/// </remarks>
public partial class CharacterAbilityContainer(ILoggerFactory loggerFactory) : ICharacterAbilities
{
    private readonly ILogger<CharacterAbilityContainer> _logger = loggerFactory.CreateLogger<CharacterAbilityContainer>();
    private readonly List<IAbility> _parked = [];
    private List<IAbility> _held = [];
    private List<IAbility> _spare = [];

    // The ability catalog the parked abilities were parked under; null when nothing is parked yet.
    private AbilityCatalog? _parkedGeneration;

    /// <summary>Every ability held, in load order; empty before the first load. World-side (#669).</summary>
    public IReadOnlyList<IAbility> All => _held;

    public IAbility? this[AbilityId abilityId] => Find(_held, abilityId);

    public bool IsCasting
    {
        get
        {
            List<IAbility> held = _held;
            for (int i = 0; i < held.Count; i++)
            {
                if (held[i].Casting)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Replaces what the character holds with a copy of <paramref name="abilities" />, and forgets every parked ability.
    /// World-side only (#1181): the talent tree decides what a character holds
    /// (<see cref="Talents.TalentAbilities.Rebuild" />), so <see cref="ICharacterAbilities" /> has no way in.
    /// </summary>
    public void Load(IReadOnlyCollection<IAbility> abilities)
    {
        _held.Clear();
        _held.AddRange(abilities);
        _parked.Clear();
        _parkedGeneration = null;
        LogLoading(_logger, _held.Count);
    }

    /// <summary>
    /// The list a rebuild fills, empty, in place of the held one until <see cref="FinishRebuild" />; the indexer still
    /// reads what is held meanwhile.
    /// </summary>
    public List<IAbility> StartRebuild()
    {
        _spare.Clear();
        return _spare;
    }

    /// <summary>
    /// The ability built from <paramref name="generation" /> parked for <paramref name="abilityId" />, taken off the
    /// parked list and reset as a new one arrives: ready (cooldown 0), its cast timer its metadata's cast time, not
    /// casting. Null when none from this generation is parked.
    /// </summary>
    public IAbility? Unpark(AbilityId abilityId, AbilityCatalog generation)
    {
        if (!ReferenceEquals(generation, _parkedGeneration))
            return null;

        List<IAbility> parked = _parked;
        for (int i = 0; i < parked.Count; i++)
        {
            IAbility ability = parked[i];
            if (ability.AbilityId != abilityId || !BuiltFrom(ability, generation))
                continue;

            // The last into the hole: nothing shifts.
            int last = parked.Count - 1;
            parked[i] = parked[last];
            parked.RemoveAt(last);

            ability.CooldownTimer = 0f;
            ability.CastTimeTimer = ability.Metadata.CastTime;
            ability.Casting = false;
            return ability;
        }

        return null;
    }

    /// <summary>
    /// The list <see cref="StartRebuild" /> handed out becomes what is held, and every ability no longer held that was
    /// built from <paramref name="generation" /> is parked; the parked list is cleared first when the generation changed.
    /// An ability built from an older generation (one held across a reload) is let go, and is built afresh if it returns.
    /// </summary>
    public void FinishRebuild(AbilityCatalog generation)
    {
        if (!ReferenceEquals(generation, _parkedGeneration))
        {
            _parked.Clear();
            _parkedGeneration = generation;
        }

        List<IAbility> rebuilt = _spare;
        List<IAbility> previous = _held;
        for (int i = 0; i < previous.Count; i++)
        {
            IAbility ability = previous[i];
            if (BuiltFrom(ability, generation) && !Holds(rebuilt, ability))
                _parked.Add(ability);
        }

        _held = rebuilt;
        _spare = previous;
        // The spare keeps no ability alive between rebuilds.
        previous.Clear();
        LogLoading(_logger, rebuilt.Count);
    }

    public void Update(TimeSpan deltaTime)
    {
        List<IAbility> held = _held;
        for (int i = 0; i < held.Count; i++)
            CountDown(held[i], deltaTime);
    }

    private static IAbility? Find(List<IAbility> abilities, AbilityId abilityId)
    {
        for (int i = 0; i < abilities.Count; i++)
        {
            IAbility ability = abilities[i];
            if (ability.AbilityId == abilityId)
                return ability;
        }

        return null;
    }

    private static bool BuiltFrom(IAbility ability, AbilityCatalog generation) =>
        ability is TreeAbility tree && ReferenceEquals(tree.BuiltFrom, generation);

    private static bool Holds(List<IAbility> abilities, IAbility ability)
    {
        for (int i = 0; i < abilities.Count; i++)
        {
            if (ReferenceEquals(abilities[i], ability))
                return true;
        }

        return false;
    }

    private static void CountDown(IAbility ability, TimeSpan deltaTime)
    {
        // #627: a cooldown counts down whatever the cast timer holds, which a shortened cast time never
        // matches with the metadata's. An ability being cast has no cooldown running: a cast is refused
        // while it has one, and the cooldown is set only once the cast fires.
        if (ability.CooldownTimer > 0)
        {
            ability.CooldownTimer -= (float)deltaTime.TotalSeconds;
        }
    }

    // Generated: no params array and no boxed count on each load, whatever the level (#1181: a talent change loads).
    [LoggerMessage(Level = LogLevel.Debug, Message = "Loading {Count} abilities into character")]
    private static partial void LogLoading(ILogger logger, int count);
}
