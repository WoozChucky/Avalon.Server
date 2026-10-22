using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Loot;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.World;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Quests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// One player fighting through a forest, as a client playing it would: every tick it steers at the nearest live
/// creature with one <see cref="CPlayerInputPacket" /> through the real <see cref="PlayerInputHandler" />, turning away
/// from a wall it walked into (bump and turn), and once the creature is within its basic ability's reach and in sight
/// (<see cref="InSight" />) it stands, faces it and casts the ability through the real <see cref="CastAbilityHandler" />
/// each time the ability is ready; in reach but behind a trunk or a rock, it walks on.
/// It runs from <see cref="ScenarioConnection.UpdateMap" />, inside the instance pass, where production processes a
/// connection's queued packets. One packet of each kind per fighter, mutated each time, so the driver itself allocates
/// nothing.
/// </summary>
/// <remarks>
/// <para>
/// It reads the forest directly rather than through the world-state packets a client decodes: the creatures are the
/// forest's own (<see cref="FightForest" />), and the player's position, velocity, cooldown and death are its
/// character's. Its decisions are those of the load-test fighter (<c>tools/Avalon.LoadTest</c>): the class's basic
/// ability (Cleave 200, Spark Bolt 210, Quick Shot 220, Smite 230: no cost, a 0.8 s cooldown), a stop a metre inside
/// the ability's reach, or a fifth of a short reach (a warrior stops at 2 m, outside the 1.5 m a creature keeps from
/// what it fights, or it would chase it for ever), a Cursor ability aimed at the creature's position, a Movement
/// ability along the facing, turned to the creature by the input's yaw. A cast is handled in the same map update as
/// the tick's input, so inside the input's dispatch (<see cref="ScenarioConnection.UpdateMap" />), not one of its own.
/// </para>
/// <para>
/// <b>Lag compensation.</b> Every cast names the tick the fighter was drawing, <see cref="ViewLagTicks" /> behind the
/// world's (<c>CCastAbilityPacket.ViewTick</c>), and its connection reports a 60 ms round trip
/// (<see cref="ScenarioConnection.ScenarioRoundTrip" />), as a real client's would (#1082): its instant hits are judged
/// against where the creatures stood that many ticks ago, its projectiles' sweeps a one-way delay less, and the
/// creatures' telegraphed hits on it wait its one-way delay for it to step out, so the allocation gate measures both.
/// </para>
/// <para>
/// <b>Healing.</b> A cleric first looks after its party (<see cref="Enter" />): when Mending Circle
/// (<see cref="HealAbility" />) is ready, it has the mana and a living member within the heal's reach is under
/// <see cref="HealBelow" /> of its health, it casts the circle at the most hurt one's feet through the same cast
/// handler, walking or standing; otherwise it fights like the others. The heal goes through the instance's combat
/// service like any hit: health, the heal broadcast, and threat for the healing on every creature fighting the healed
/// (#961).
/// </para>
/// <para>
/// <b>Quest.</b> Each fighter holds the scenarios' kill quest (<see cref="ScenarioReferenceData.KillQuest" />): accepted
/// at its first step, and once ready to hand in, abandoned and accepted again (<see cref="KeepQuest" />). The instance
/// gives it kill credit on every kill the party shares, and the quest flush sends its updates, lines and markers.
/// </para>
/// <para>
/// <b>Loot.</b> Before it looks for a creature, a fighter walks to the nearest drop it may take within
/// <see cref="LootSearchRadius" /> (reserved for it, or free for all by now) and asks for it through the real
/// <see cref="LootPickupHandler" />; when its bag is down to <see cref="BagRoomSlots" /> free slots it destroys the lowest
/// occupied one through the real <see cref="ItemDestroyHandler" />. Its bag starts near full
/// (<see cref="ForestCombatScenario.BagFill" />), so both happen in every minute.
/// </para>
/// <para>
/// <b>Death.</b> A dead player sends nothing, as the input handler drops a dead character's input. A client would ask to
/// respawn in town (<c>CMSG_RESPAWN_AT_TOWN</c>) and walk back in, which a scenario cannot do, since it cannot move a
/// character between instances. Instead, <see cref="ReviveAfterTicks" /> after it died the fighter is revived at the
/// forest's entry through the instance's own <see cref="ICombatService.RevivePlayer" />: the dead flag cleared, a
/// quarter of its health (<c>CombatConfig.ReviveHealthFraction</c>), and the revive broadcast to the instance. The
/// delay stands for the trip to town and back; the forest keeps three fighters for the whole run, and the run stays
/// deterministic.
/// </para>
/// <para>
/// Bump and turn: a step that asked to move but moved less than 0.1 m/s ran into a wall. The fighter then turns 90 to
/// 270 degrees away (from a seeded <see cref="Random" />, so the run is deterministic) and holds that heading for a
/// second before steering at the creature again.
/// </para>
/// </remarks>
public sealed class LoopFighter
{
    /// <summary>How long a dead fighter lies dead before it is revived at the entry: 10 s at 60 Hz.</summary>
    public const int ReviveAfterTicks = 600;

    /// <summary>How long a heading turned away from a wall is held: 1 s at 60 Hz.</summary>
    private const int DetourTicks = 60;

    /// <summary>A step that asked to move and moved less than 0.1 m/s ran into a wall.</summary>
    private const float BlockedSpeedSquared = 0.01f;

    /// <summary>How far inside its ability's reach a fighter stops, at most: 1 m ...</summary>
    private const float ReachMargin = 1f;

    /// <summary>... or this share of a short reach.</summary>
    private const float ReachMarginShare = 0.2f;

    /// <summary>
    /// A creature is in sight when a walkable ray from the fighter to it ends within this of it, on the ground plane: a
    /// projectile stops where such a ray does (ProjectileAbilityScript), and a creature at the edge of the eroded mesh
    /// still counts.
    /// </summary>
    private const float SightMargin = 1f;

    /// <summary>
    /// How far behind the world's tick a fighter says it was drawing when it casts (<c>CCastAbilityPacket.ViewTick</c>,
    /// #1082): 10 ticks, about 167 ms. A real client's cast reaches the server its render delay plus a whole round trip
    /// after the tick it names (one way for the state to reach it, one way for the cast to come back): 120 ms plus the
    /// scenario's 60 ms, about 11 ticks. A tick short of that changes nothing the scenario measures (docs/benchmarks.md).
    /// </summary>
    public const int ViewLagTicks = 10;

    /// <summary>A cleric's heal: Mending Circle, an ally circle at the aim point (4 m, 15 m reach, 8 s, 25 mana).</summary>
    public const uint HealAbility = 232u;

    /// <summary>
    /// A cleric heals a party member under this share of its health. At level 1 one Mending Circle heals about 65
    /// health: a quarter of a warrior's, two fifths of a hunter's or a cleric's, a little over half a wizard's. A
    /// member under 70% has lost at least 30%, so most of a heal lands rather than topping up health already full,
    /// and a member is healed well before it is about to die; with the heal's 8 s cooldown and the threshold, the
    /// cleric still spends nearly all of its casts attacking.
    /// </summary>
    public const float HealBelow = 0.7f;

    /// <summary>
    /// How far a fighter looks for a drop it may take: a little past the longest basic reach (Quick Shot, 25 m), so a
    /// ranged fighter finds what fell where its target died, and not across the forest.
    /// </summary>
    public const float LootSearchRadius = 26f;

    /// <summary>How close on the ground plane it walks before it asks: inside the pickup's 5 m (3D) with room for a slope.</summary>
    public const float PickupReach = 3.5f;

    /// <summary>How long it walks at one drop before giving up on it (a drop past a wall): 10 s at 60 Hz.</summary>
    public const int LootGiveUpTicks = 600;

    /// <summary>The bag is near full at this many free slots or fewer, and one slot is destroyed.</summary>
    public const int BagRoomSlots = 2;

    // The drops given up on that a fighter remembers.
    private const int GivenUpDrops = 16;

    private readonly PlayerInputHandler _input;
    private readonly CastAbilityHandler _cast;
    private readonly TimeProvider _clock;
    private readonly WorldTicks _ticks;
    private readonly double _gcdMs;
    private readonly Random _rng;
    private readonly IAbility _ability;
    private readonly bool _cursorAim;
    private readonly float _stopDistance;
    private readonly CPlayerInputPacket _inputPacket = new();
    private readonly Vector3Dto _aimPoint = new();
    private readonly CCastAbilityPacket _castPacket;

    // A cleric's heal (Mending Circle 232), its aim and its packet; null for every other class.
    private readonly IAbility? _heal;
    private readonly Vector3Dto _healPoint = new();
    private readonly CCastAbilityPacket? _healPacket;

    // The kill quest: its abandon, the service its accept starts it through, and the kills it had counted at the last step.
    private readonly QuestAbandonHandler _abandon;
    private readonly QuestService _quests;
    private readonly CQuestAbandonPacket _abandonPacket = new() { QuestId = ScenarioReferenceData.KillQuest };
    private uint _questCounted;

    // Loot: the pickup and destroy handlers, their packets, the drop being walked to and for how long, and the drops given
    // up on (a ring: a drop out of reach, or one refused), so a fighter never walks at the same one for ever.
    private readonly LootPickupHandler _pickup;
    private readonly ItemDestroyHandler _destroy;
    private readonly CLootPickupPacket _pickupPacket = new();
    private readonly CItemDestroyPacket _destroyPacket = new() { Container = InventoryType.Bag };
    private readonly ulong[] _givenUp = new ulong[GivenUpDrops];
    private int _givenUpNext;
    private ulong _lootTarget;
    private int _lootTicks;

    private FightForest? _forest;
    private ICharacter[] _party = [];
    private bool _moved;
    private bool _detouring;
    private float _detourYaw;
    private int _detourLeft;
    private int _deadFor;

    /// <param name="handlers">The handlers every fighter of the world shares (<see cref="Handlers" />).</param>
    /// <param name="character">The fighter's character, holding its class's basic ability.</param>
    /// <param name="seed">Seeds its turns away from walls.</param>
    public LoopFighter(FighterHandlers handlers, ICharacter character, int seed)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(character);
        (_input, _cast, _clock, _ticks, _gcdMs) = (handlers.Input, handlers.Cast, handlers.Clock, handlers.Ticks, handlers.GcdMs);
        (_abandon, _quests) = (handlers.QuestAbandon, handlers.Quests);
        (_pickup, _destroy) = (handlers.LootPickup, handlers.ItemDestroy);
        _rng = new Random(seed);

        uint abilityId = BasicAbility(character.Class);
        _ability = character.Spells[new AbilityId(abilityId)]
                   ?? throw new InvalidOperationException($"{character.Name} has no basic ability {abilityId}");
        AbilityMetadata meta = _ability.Metadata;
        _cursorAim = meta.AimMode == AbilityAimMode.Cursor;
        _stopDistance = meta.Reach - MathF.Min(ReachMargin, ReachMarginShare * meta.Reach);
        _castPacket = new CCastAbilityPacket { AbilityId = abilityId, GroundPos = _cursorAim ? _aimPoint : null };

        if (character.Class == CharacterClass.Cleric)
        {
            _heal = character.Spells[new AbilityId(HealAbility)]
                    ?? throw new InvalidOperationException($"{character.Name} has no heal {HealAbility}");
            _healPacket = new CCastAbilityPacket { AbilityId = HealAbility, GroundPos = _healPoint };
        }
    }

    /// <summary>The casts the cast handler accepted, heals included.</summary>
    public int Casts { get; private set; }

    /// <summary>The times the fighter died.</summary>
    public int Deaths { get; private set; }

    /// <summary>The heals the cast handler accepted (a cleric's only).</summary>
    public int Heals { get; private set; }

    /// <summary>The accepted heals that raised the health of the party member they were aimed at, in the same call.</summary>
    public int HealsLanded { get; private set; }

    /// <summary>The kill quest accepted, the first time and after every completion.</summary>
    public int QuestsAccepted { get; private set; }

    /// <summary>The kill quest's kill credit: each kill it counted, on either objective.</summary>
    public int QuestCredits { get; private set; }

    /// <summary>The kill quest completed (ready to hand in), each then abandoned and accepted again.</summary>
    public int QuestsCompleted { get; private set; }

    /// <summary>The drops the pickup handler gave the fighter: items and gold.</summary>
    public int Pickups { get; private set; }

    /// <summary>The bag slots the item destroy handler emptied, to keep room for the next pickup.</summary>
    public int ItemsDestroyed { get; private set; }

    /// <summary>The drops given up on: walked at too long, or refused.</summary>
    public int GaveUp { get; private set; }

    /// <summary>
    /// The handlers every fighter of a world shares, over the world's real registry and services, as production has one
    /// of each. The world must have reference data (its quest service).
    /// </summary>
    public static FighterHandlers Handlers(ScenarioWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        QuestService quests = world.Quests
                              ?? throw new ArgumentException("A fighter's world needs reference data (its quest service)", nameof(world));
        ICharacterEconomy economy = world.Economy!;
        var combat = new CombatConfig();
        return new FighterHandlers(
            new PlayerInputHandler(NullLogger<PlayerInputHandler>.Instance, world.Host),
            new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world.Host, combat, world.Clock),
            new QuestAbandonHandler(quests, NullLogger<QuestAbandonHandler>.Instance),
            new LootPickupHandler(NullLogger<LootPickupHandler>.Instance, world.Host, economy, world.Clock, quests),
            new ItemDestroyHandler(NullLogger<ItemDestroyHandler>.Instance, world.Host, economy),
            quests, world.Clock, world.Scheduler.Ticks, combat.GcdMs);
    }

    /// <summary>The class's basic ability: no cost, a 0.8 s cooldown.</summary>
    public static uint BasicAbility(CharacterClass characterClass) => characterClass switch
    {
        CharacterClass.Warrior => 200u, // Cleave: a cone, along the facing
        CharacterClass.Wizard => 210u, // Spark Bolt: a projectile, at the cursor
        CharacterClass.Hunter => 220u, // Quick Shot: a projectile, at the cursor
        CharacterClass.Cleric => 230u, // Smite: a projectile, at the cursor
        _ => throw new ArgumentOutOfRangeException(nameof(characterClass), characterClass, "No basic ability"),
    };

    /// <summary>The step, to be the fighter's connection's map update; it fights once <see cref="Enter" /> named its forest.</summary>
    public void Step(ScenarioConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        FightForest forest = _forest ?? throw new InvalidOperationException("The fighter was given no forest");
        ICharacter character = connection.Character!;

        if (character.IsDead)
        {
            if (_deadFor == 0)
                Deaths++;

            if (++_deadFor >= ReviveAfterTicks)
            {
                forest.Instance.CombatService.RevivePlayer(character, forest.Entry);
                _deadFor = 0;
                _moved = false;
                _detouring = false;
                _lootTarget = 0;
            }

            return;
        }

        KeepQuest(connection);
        KeepBagRoom(connection);
        if (Loot(connection, character, forest))
            return;

        ICreature? target = forest.NearestLive(character.Position);
        if (target is null)
        {
            TryHeal(connection, character);
            return;
        }

        Vector3 position = character.Position;
        Vector3 at = target.Position;
        float dx = at.x - position.x;
        float dz = at.z - position.z;
        float goalYaw = Yaw(dx, dz);

        // Out of reach, or in reach behind a trunk or a rock, which would take the shot: walk on (bump and turn takes it
        // round what is in the way), as a player steps out from behind a tree.
        if (dx * dx + dz * dz > _stopDistance * _stopDistance || !InSight(forest, position, at))
        {
            Walk(connection, goalYaw);
            TryHeal(connection, character);
            return;
        }

        // In reach: standing, facing it, casting when the ability is ready; a cleric heals first when someone needs it.
        _detouring = false;
        Send(connection, 0f, 0f, goalYaw);
        _moved = false;

        if (TryHeal(connection, character))
            return;

        if (Ready(_ability, character))
        {
            _aimPoint.X = at.x;
            _aimPoint.Y = at.y;
            _aimPoint.Z = at.z;
            _castPacket.ViewTick = ViewTick();
            DateTime before = character.LastCastStartTime;
            _cast.Execute(connection, _castPacket);
            if (character.LastCastStartTime != before)
                Casts++;
        }
    }

    /// <summary>
    /// Gives the fighter its forest, once the forest is built, and its party (itself among them), whom a cleric heals:
    /// until then its step throws.
    /// </summary>
    public void Enter(FightForest forest, ICharacter[] party)
    {
        _forest = forest ?? throw new ArgumentNullException(nameof(forest));
        _party = party ?? throw new ArgumentNullException(nameof(party));
    }

    /// <summary>
    /// The kill quest (<see cref="ScenarioReferenceData.KillQuest" />): accepted when the fighter does not hold it, and
    /// once it is ready to hand in, abandoned through the real <see cref="QuestAbandonHandler" /> and accepted again, as
    /// a player farming it would without walking back to town. Its kill credit comes from the instance, on every kill
    /// the party shares; what it has counted is read here.
    /// </summary>
    /// <remarks>
    /// The accept goes through <see cref="QuestService.StartFromItem" />, not <see cref="QuestAcceptHandler" />: the
    /// handler's <see cref="QuestService.Accept" /> requires an open conversation with the quest's giver within the
    /// dialogue leash, and no forest holds a quest giver. <c>StartFromItem</c> runs the same start as an accept
    /// (availability, then stage 0, every count 0, the line, the hooks and the settle) without the conversation and
    /// without re-sending the giver's dialogue; it is answered, as the handler answers, by <see cref="QuestReplies" />
    /// (one SMSG_QUEST_RESULT), the service handed through as state to a static lambda as the handler hands its own.
    /// </remarks>
    private void KeepQuest(ScenarioConnection connection)
    {
        if (connection.Character is not CharacterEntity character)
            return;

        ActiveQuest? held = character.Quests.Get(ScenarioReferenceData.KillQuest);
        if (held is not null)
        {
            uint counted = held.ProgressOf(ScenarioReferenceData.KillQuestBoars)
                           + held.ProgressOf(ScenarioReferenceData.KillQuestWolves);
            if (counted > _questCounted)
                QuestCredits += (int)(counted - _questCounted);
            _questCounted = counted;

            if (held.State != CharacterQuestState.ReadyToTurnIn)
                return;

            QuestsCompleted++;
            _abandon.Execute(connection, _abandonPacket);
            _questCounted = 0;
            if (character.Quests.IsActive(ScenarioReferenceData.KillQuest))
                throw new InvalidOperationException($"{character.Name} could not abandon the kill quest");
        }

        QuestReplies.Handle(connection, NullLogger.Instance, NetworkPacketType.CMSG_QUEST_ACCEPT,
            ScenarioReferenceData.KillQuest, _quests,
            static (c, quests) => quests.StartFromItem(c, ScenarioReferenceData.KillQuest));
        if (!character.Quests.IsActive(ScenarioReferenceData.KillQuest))
            throw new InvalidOperationException($"{character.Name} could not accept the kill quest");
        QuestsAccepted++;
    }

    /// <summary>
    /// Keeps room in the bag: when it has <see cref="BagRoomSlots" /> free slots or fewer, the lowest occupied slot is
    /// destroyed whole through the real <see cref="ItemDestroyHandler" />, as a player clears a full bag, so a pickup is
    /// never refused for a full bag and the run never stalls at a drop.
    /// </summary>
    private void KeepBagRoom(ScenarioConnection connection)
    {
        var character = (CharacterEntity)connection.Character!;
        CharacterInventoryContainer bag = character.Container(InventoryType.Bag);
        if (bag.Capacity - bag.Held.Count > BagRoomSlots)
            return;

        for (byte slot = 0; slot < bag.Capacity; slot++)
        {
            if (!bag.TryGet(slot, out _))
                continue;

            _destroyPacket.RequestId++;
            _destroyPacket.Slot = slot;
            _destroyPacket.Count = null;
            _destroy.Execute(connection, _destroyPacket);
            if (bag.TryGet(slot, out _))
                throw new InvalidOperationException($"{character.Name}'s bag slot {slot} was not destroyed");

            ItemsDestroyed++;
            return;
        }
    }

    /// <summary>
    /// Picks up the nearest drop the fighter may take now (reserved for it, or free for all by now) within
    /// <see cref="LootSearchRadius" />: it walks there, and within <see cref="PickupReach" /> stands and asks for it
    /// through the real <see cref="LootPickupHandler" />. A drop it has walked at for <see cref="LootGiveUpTicks" />, or
    /// one the handler refused, is given up. False, sending nothing, when there is no drop to take.
    /// </summary>
    private bool Loot(ScenarioConnection connection, ICharacter character, FightForest forest)
    {
        GroundLoot? drop = NearestTakeable(character, forest);
        if (drop is null)
        {
            _lootTarget = 0;
            return false;
        }

        ulong raw = drop.Guid.RawValue;
        if (raw != _lootTarget)
        {
            _lootTarget = raw;
            _lootTicks = 0;
        }
        else if (++_lootTicks > LootGiveUpTicks)
        {
            GiveUp(raw);
            return false;
        }

        Vector3 position = character.Position;
        float dx = drop.Position.x - position.x;
        float dz = drop.Position.z - position.z;
        float yaw = Yaw(dx, dz);
        if (dx * dx + dz * dz > PickupReach * PickupReach)
        {
            Walk(connection, yaw);
            TryHeal(connection, character);
            return true;
        }

        _detouring = false;
        Send(connection, 0f, 0f, yaw);
        _moved = false;

        _pickupPacket.LootGuid = raw;
        _pickup.Execute(connection, _pickupPacket);
        if (forest.Drops.TryGet(drop.Guid, out _))
            GiveUp(raw);
        else
            Pickups++;

        _lootTarget = 0;
        TryHeal(connection, character);
        return true;
    }

    /// <summary>The nearest drop within reach of the search the fighter may take now and has not given up; null when none.</summary>
    private GroundLoot? NearestTakeable(ICharacter character, FightForest forest)
    {
        GroundLoot? nearest = null;
        float best = LootSearchRadius * LootSearchRadius;
        uint me = character.Guid.Id;
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        Vector3 position = character.Position;
        foreach (GroundLoot drop in forest.DropsOnGround)
        {
            if (drop.OwnerCharacterId != me && now < drop.FreeForAllAt)
                continue;

            float dx = drop.Position.x - position.x;
            float dz = drop.Position.z - position.z;
            float distance = dx * dx + dz * dz;
            if (distance < best && !GivenUp(drop.Guid.RawValue))
            {
                best = distance;
                nearest = drop;
            }
        }

        return nearest;
    }

    private bool GivenUp(ulong drop) => Array.IndexOf(_givenUp, drop) >= 0;

    private void GiveUp(ulong drop)
    {
        _givenUp[_givenUpNext] = drop;
        _givenUpNext = (_givenUpNext + 1) % _givenUp.Length;
        _lootTarget = 0;
        GaveUp++;
    }

    /// <summary>
    /// A cleric's heal: the party member alive with the smallest share of its health, when that share is under
    /// <see cref="HealBelow" /> and the member stands within the heal's reach, gets a Mending Circle at its feet through
    /// the real cast handler, once the heal is off cooldown, the global cooldown is over and the cleric has the mana. The
    /// circle heals every ally inside it, the cleric too, and gives each creature fighting them threat for the healing.
    /// False, sending nothing, when there is no heal to cast.
    /// </summary>
    private bool TryHeal(ScenarioConnection connection, ICharacter character)
    {
        if (_heal is null || !Ready(_heal, character) || character.CurrentPower < _heal.Metadata.Cost)
            return false;

        ICharacter? hurt = null;
        float lowest = HealBelow;
        float reach = _heal.Metadata.Reach;
        Vector3 position = character.Position;
        foreach (ICharacter member in _party)
        {
            if (member.IsDead || member.Health == 0)
                continue;

            float share = member.CurrentHealth / (float)member.Health;
            float dx = member.Position.x - position.x;
            float dz = member.Position.z - position.z;
            if (share < lowest && dx * dx + dz * dz <= reach * reach)
            {
                lowest = share;
                hurt = member;
            }
        }

        if (hurt is null)
            return false;

        Vector3 at = hurt.Position;
        _healPoint.X = at.x;
        _healPoint.Y = at.y;
        _healPoint.Z = at.z;
        _healPacket!.ViewTick = ViewTick();
        DateTime before = character.LastCastStartTime;
        uint health = hurt.CurrentHealth;
        _cast.Execute(connection, _healPacket);
        if (character.LastCastStartTime == before)
            return false;

        Casts++;
        Heals++;
        if (hurt.CurrentHealth > health)
            HealsLanded++;
        return true;
    }

    /// <summary>
    /// Whether a walkable ray from <paramref name="from" /> to <paramref name="to" /> over the forest's navmesh ends within
    /// <see cref="SightMargin" /> of it: nothing solid stands between. The raycast allocates nothing.
    /// </summary>
    private static bool InSight(FightForest forest, Vector3 from, Vector3 to)
    {
        Vector3 end = forest.Instance.GetNavigatorForPosition(from).RaycastWalkable(from, to);
        float dx = to.x - end.x, dz = to.z - end.z;
        return dx * dx + dz * dz <= SightMargin * SightMargin;
    }

    /// <summary>Steers at <paramref name="goalYaw" />, or along a heading turned away from the wall the last step met.</summary>
    private void Walk(ScenarioConnection connection, float goalYaw)
    {
        ICharacter character = connection.Character!;
        Vector3 velocity = character.Velocity;
        bool blocked = _moved && velocity.x * velocity.x + velocity.z * velocity.z < BlockedSpeedSquared;
        if (blocked)
        {
            _detourYaw = ((_detouring ? _detourYaw : goalYaw) + 90f + (float)_rng.NextDouble() * 180f) % 360f;
            _detourLeft = DetourTicks;
            _detouring = true;
        }
        else if (_detouring && --_detourLeft <= 0)
        {
            _detouring = false;
        }

        float yaw = _detouring ? _detourYaw : goalYaw;
        float radians = yaw * (MathF.PI / 180f);
        Send(connection, MathF.Sin(radians), MathF.Cos(radians), yaw);
        _moved = true;
    }

    private void Send(ScenarioConnection connection, float dirX, float dirZ, float yaw)
    {
        _inputPacket.Seq++;
        _inputPacket.DirX = dirX;
        _inputPacket.DirZ = dirZ;
        _inputPacket.YawDeg = (ushort)((int)yaw % 360);
        _input.Execute(connection, _inputPacket);
    }

    /// <summary>
    /// Whether the cast handler would accept the cast: the ability off cooldown and the global cooldown over, as a client
    /// tracks them, so a fighter sends a cast only when it is ready and no refusal is sent. Read from the ability itself,
    /// held since the start: the character's ability lookups (<c>Spells[id]</c>, <c>Spells.IsCasting</c>) allocate, and
    /// a client checks nothing on the server before it casts. The basic abilities are instant, so never mid-cast.
    /// </summary>
    private bool Ready(IAbility ability, ICharacter character) =>
        ability.CooldownTimer <= 0
        && (_clock.GetUtcNow().UtcDateTime - character.LastCastStartTime).TotalMilliseconds >= _gcdMs;

    /// <summary>
    /// The tick a cast sent now says the fighter was drawing (#1082): <see cref="ViewLagTicks" /> behind the world's tick,
    /// read as the cast is sent, as a client reads its render clock.
    /// </summary>
    private long ViewTick() => _ticks.Completed - ViewLagTicks;

    /// <summary>The yaw, 0 to 360 degrees, that faces along (<paramref name="dx" />, <paramref name="dz" />).</summary>
    private static float Yaw(float dx, float dz)
    {
        float yaw = MathF.Atan2(dx, dz) * (180f / MathF.PI);
        return yaw < 0f ? yaw + 360f : yaw;
    }
}

/// <summary>The packet handlers, services, clock and tick count every <see cref="LoopFighter" /> of a world shares.</summary>
public sealed record FighterHandlers(PlayerInputHandler Input, CastAbilityHandler Cast, QuestAbandonHandler QuestAbandon,
    LootPickupHandler LootPickup, ItemDestroyHandler ItemDestroy,
    QuestService Quests, TimeProvider Clock, WorldTicks Ticks, double GcdMs);

/// <summary>
/// A forest as its fighters see it: the instance, its entry, and its creatures as the forest was built with them, which
/// is every creature it will hold, since creatures never respawn. A creature stays in the list once dead, with no
/// health, after the instance removed its corpse; the list is walked as an array, so a search allocates nothing.
/// </summary>
public sealed class FightForest
{
    private readonly ICreature[] _creatures;
    private readonly int _killedAtStart;

    public FightForest(MapInstance instance)
    {
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        Entry = instance.EntrySpawnWorldPos;
        _creatures = [.. instance.Creatures.Values];
        if (_creatures.Length == 0)
            throw new ArgumentException("The forest has no creatures", nameof(instance));

        _killedAtStart = Dead();

        Drops = instance.Drops;
        DropsOnGround = Drops.All as Dictionary<ulong, GroundLoot>.ValueCollection
                        ?? throw new InvalidOperationException("The ground loot store no longer keeps its drops in a dictionary");
    }

    public MapInstance Instance { get; }

    /// <summary>The drops on the forest's ground.</summary>
    public GroundLootStore Drops { get; }

    /// <summary>
    /// The same drops as the store's own dictionary's values: walked with their struct enumerator, so a search allocates
    /// nothing, where a walk of <see cref="GroundLootStore.All" /> boxes its enumerator.
    /// </summary>
    public Dictionary<ulong, GroundLoot>.ValueCollection DropsOnGround { get; }

    /// <summary>Where players enter the forest, and where a dead fighter is revived.</summary>
    public Vector3 Entry { get; }

    /// <summary>The creatures the forest was built with.</summary>
    public int Creatures => _creatures.Length;

    /// <summary>The creatures killed since the forest was built.</summary>
    public int Kills => Dead() - _killedAtStart;

    /// <summary>The live creature nearest <paramref name="position" /> on the ground plane; null when none is left.</summary>
    public ICreature? NearestLive(Vector3 position)
    {
        ICreature? nearest = null;
        float best = float.MaxValue;
        foreach (ICreature creature in _creatures)
        {
            if (creature.CurrentHealth == 0)
                continue;

            float dx = creature.Position.x - position.x;
            float dz = creature.Position.z - position.z;
            float distance = dx * dx + dz * dz;
            if (distance < best)
            {
                best = distance;
                nearest = creature;
            }
        }

        return nearest;
    }

    private int Dead()
    {
        int dead = 0;
        foreach (ICreature creature in _creatures)
        {
            if (creature.CurrentHealth == 0)
                dead++;
        }

        return dead;
    }
}
