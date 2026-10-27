# Skill System

How a player skill (an *ability*) is defined, cast, resolved and shown to clients (#164, #521).

A skill **aims**; it never **targets**. A cast carries a direction (the character's facing) or a point
on the ground (the cursor), never a selected unit. The skill's **shape** (a circle, a cone or a
projectile) then decides which units it affects. There is one generic script per shape, and every
number that makes one skill differ from another is a column on its row.

Terms used below:

- **Caster**: the unit casting the skill: a player, or a creature, whose script picks its abilities (#163).
- **Aim**: what the cast was pointed at, captured when the cast starts (`AbilityAim`): the caster's
  facing, and for a cursor skill the ground point.
- **Body radius**: every unit is a circle on the ground for hit tests (`IUnit.BodyRadius`). A creature
  takes `CreatureTemplate.BodyRadius` (default 0.5 m); a character is a fixed 0.5 m
  (`UnitBody.CharacterRadius`). A unit is hit when its body circle overlaps the shape.
- **2D**: every hit test works on the ground plane (X and Z). Heights are ignored.

---

## 1. Data

Skills are rows of `AbilityTemplate` in the World database (`Avalon.Domain.World`), loaded into
`StaticData` through `AbilityCatalog` (the `/reload abilities` area). `AbilityMetadataMapper` turns a
row into the runtime `AbilityMetadata` (`Avalon.World.Public.Abilities`), and a character's skills
are sent to its client in `SMSG_CHARACTER_ABILITIES` (`AbilityInfo`).

| Column | Type | Unit | Meaning |
|---|---|---|---|
| `Id` | `AbilityId` | | Primary key. Retired ids are never reused, so no client cache confuses an old skill with a new one. |
| `Name` | string | | Display name. |
| `CastTime` | uint | ms | 0 is instant; above 0 the cast waits in the queue first. |
| `Cooldown` | uint | ms | Time before the skill can be cast again, before cooldown recovery (or, for a basic Spell, cast speed). 0 on an Attack makes its weapon pace it (#1181, §3); a basic Attack's is always 0 (#1178). |
| `Cost` | uint | mana | Spent from the caster's mana, the one pool every class has (#1181; see the power rule below). 0 is free. |
| `ScriptName` | string | | The script class that runs the skill: `CircleAbilityScript`, `ConeAbilityScript` or `ProjectileAbilityScript`. |
| `Effects` | `SpellEffect` | | `Damage` for a hostile skill, `Heal` for an ally skill. |
| `AllowedClasses` | list of `CharacterClass` | | Read by nothing since #1181: what makes a class ability is that the talent tree grants it (a class start or a node links it, `TalentNodeAbilities`). The column goes with `StartingSpells`. |
| `ThreatMultiplier`, `HealThreatPerHp`, `TauntDurationMs` | float, float, uint | | Threat rules, unchanged by #164. The two floats must be finite and 0 or more (#529). |
| `Flags` | `AbilityFlags` | | `RequiresInCombat`, `RequiresOutOfCombat`, `Basic` (a class's basic attack, the one ability a silence does not stop), `Unavoidable` (a Hostile ability never rolled against for evasion, #1164). |
| `AnimationId` | uint | | Reserved for the client's cast animation. |
| `AimMode` | `AbilityAimMode` | | `Movement` (0): the caster's facing. `Cursor` (1): a ground point sent with the cast. |
| `Shape` | `AbilityShape` | | `Circle` (0), `Cone` (1), `Projectile` (2). |
| `Anchor` | `AbilityAnchor` | | Circle only: `Caster` (0) centres it on the caster, `AimPoint` (1) on the aim point. |
| `Reach` | float | m | An aim-point circle's furthest centre, a cone's length, a projectile's furthest travel. 0 for a circle on the caster. |
| `Radius` | float | m | A circle's radius; 0 otherwise. |
| `ArcDegrees` | float | degrees | A cone's full width; 0 otherwise. |
| `ProjectileSpeed` | float | m/s | Projectile only; 0 otherwise. |
| `Pierce` | bool | | Projectile only: false ends on the first unit hit; true hits each unit once and flies on. |
| `Affects` | `AbilityAffects` | | `Hostile` (0) damages hostile units; `Ally` (1) heals allies. |
| `MaxTargets` | int | units | The most units the shape affects, nearest first (§5); 0 is no limit, the default. It caps whatever the shape affects: hostiles for a Hostile ability, allies for an Ally one (#1181), and a unit the ability may not affect, every unit hits ignore among them, takes no slot. 0 or more (`CK_AbilityTemplates_MaxTargets`, `AbilityRules`). Exported in the ability catalog and read and edited over REST (`maxTargets` on the template and the public ability), not sent on `AbilityInfo`. |
| `Tags` | `AbilityTags` | | What the skill is (#1178, `[Flags]`): at most one of `Attack` (1), `Spell` (2) and `Heal` (4), plus its delivery, `Area` (8), `Projectile` (16), `Melee` (32). `DamageOverTime` (64) is an aura's tag, never an ability's (`CK_AbilityTemplates_Tags`). The tags pick the base (§4) and the modifiers that scale it. |
| `DamageEffectiveness` | float | percent | What an Attack's weapon roll counts for, and a Spell's added damage; 100 counts it whole. Finite and 0 or more, above 0 for an Attack that deals damage (#1178). |
| `SpellBaseMin`, `SpellBaseMax` | uint, uint | health points | The range a Spell or a Heal rolls at level 1, inclusive; `SpellBaseMax` above 0 for one that deals or heals, or whose aura ticks (#1178). |
| `SpellBasePerLevelPct` | float | percent | Percent of the level-1 spell base added for each caster level above 1, linear. Finite and 0 or more (#1178). |
| `ConvertToType`, `ConvertPct` | `DamageType?`, float | percent | The element an Attack converts `ConvertPct` (0 to 100) of its weapon's physical damage to (#1178). |
| `AddedType`, `AddedMin`, `AddedMax` | `DamageType?`, uint, uint | health points | The skill's built-in added damage, inclusive, rolled after the weapon and never converted; `AddedMax` 0 adds none, and a range above 0 names its type (#1178). |
| `DamageType` | `DamageType?` | | The type its hits and its aura's damage ticks deal (#1178): Physical 0, Fire 1, Cold 2, Lightning 3, Blight 4 (`CK_AbilityTemplates_DamageType`). Null deals the caster's base type, which only a creature-only ability may rely on (a creature's base type is its template's). |

**Legacy `Range`.** The `Range` column (`SpellRange`) stays for schema compatibility and is still sent
in `AbilityInfo.Range`, but no server code reads it: `Reach` replaced it. `AbilityInfo.FacingAngle` is
likewise no longer set, because there is no facing check.

---

## 2. `AbilityCatalog` validation

`AbilityCatalog` (`Avalon.World/Abilities`) checks every row. A bad row is refused with an error that
names it (`ability <id> '<name>': <reason>`), logged at Error, and left out; every other row still
loads. A character holding a refused skill simply does not get it at character select. The rules:

- `Reach`, `Radius`, `ArcDegrees` and `ProjectileSpeed` are finite and 0 or more;
- `AimMode`, `Shape`, `Anchor` and `Affects` are known values;
- `Affects = Ally` only on a circle;
- an Ally ability may not be `Unavoidable` (#1164: it is never evaded anyway);
- a damage type is a known value; an Ally ability names none; a Hostile class ability (one the talent tree grants, #1181: `AbilityRules.Problem(t, granted)`, the world's `AbilityCatalog` given the tree's link ids, the API's edit reading `TalentNodeAbilities`, the simulator the seed's links) whose `Effects` has `Damage` names one, and so does a class ability whose aura deals damage over time (`AuraRules.DamageTypeProblem`: Ignite says Fire) (#1178);
- the offence model (#1178): `DamageEffectiveness` and `SpellBasePerLevelPct` are finite and 0 or more; the tags are known, at most one of Attack, Spell and Heal, and never DamageOverTime; Attack and Spell only on a Hostile ability, Heal only on an Ally one; a Hostile ability that deals damage is an Attack or a Spell, an Ally ability that heals a Heal; Projectile is the tag of a projectile and only of one; only an Attack is Melee; a basic Attack has `Cooldown` 0 (its weapon paces it); a class Attack deals Physical, an element arriving only by its conversion or added damage; `ConvertPct` is 0 to 100; a row with a `ConvertPct` above 0 or any `ConvertToType` converts, and a converting row names an element and is an Attack; `AddedMin` and `SpellBaseMin` are at most their maximums, and added damage names its type; a Spell or a Heal that deals or heals has a `SpellBaseMax` above 0, and an Attack that deals damage a `DamageEffectiveness` above 0. `AuraRules.SourceProblem` adds the aura's side: a class ability applying a damaging aura is an Attack or a Spell, an Ally ability applying a healing aura a Heal, and a Spell or a Heal whose aura ticks has a spell base for it to draw on;
- **circle**: `Radius > 0`; on the aim point it needs `AimMode = Cursor` and `Reach > 0`; on the
  caster it needs `Reach = 0`;
- **cone**: `Reach > 0`, and `ArcDegrees` above 0 and at most 360;
- **projectile**: `Reach > 0`, `ProjectileSpeed > 0` and `AimMode = Cursor`.

---

## 3. Cast flow

A client casts with `CMSG_CAST_ABILITY` (`CCastAbilityPacket { AbilityId, GroundPos?, ViewTick }`;
`ViewTick`, the world tick the client was drawing other units at, is lag compensation's, §4, #1082).
`CastAbilityHandler` checks, in this order, and answers the first refusal with exactly one
`SAbilityNotReadyPacket` naming a `CastRejectReason` (#512). A connection with no character is the one
silent case.

1. **Dead**: the caster is dead.
2. **Stunned**: the caster is stunned (#770, crowd control: see [Auras](auras.md#crowd-control-770)); a stun stops
   the basic attack too.
3. **AlreadyCasting**: a cast-time cast is still in progress (#521 item 4).
4. **Gcd**: the global cooldown (`CombatConfig.GcdMs`, default 200 ms) since the last cast start has
   not run out, measured on the container's `TimeProvider` (#793); the answer carries the time left, rounded up.
5. **NotOwned**: the caster does not hold the skill.
6. **Silenced**: the caster is silenced (#770) and the skill is not its class's basic attack (a row flagged
   `AbilityFlags.Basic`: 200, 210, 220, 230).
7. **Cooldown**: the skill's own cooldown is running; the answer carries the time left, rounded up.
8. **RequiresOutOfCombat** / **RequiresInCombat**: the skill's combat flags.
9. **NoAimPoint**: a cursor skill whose `GroundPos` is missing or has a non-finite component.
10. **The power rule** (`AbilityCost`, #521 item 2, #1181): a cost of 0 is payable by anyone and needs
   no pool. A cost above 0 is spent from mana, the one pool every character has: a caster on
   `PowerType.Mana` holding at least the cost pays it, and anything else is **NotEnoughPower**: too
   little mana, or a caster not on Mana (none is today: creatures cast free, since
   `InstanceAbilityCastSystem` skips the rule for them and they never send a cast request, so a cost
   a live template edit puts on a creature ability is never charged).
11. **The instance**: the caster's instance must exist, and the cast system must accept the cast (its
   script found and built, and the caster's crowd control asked again, #770); otherwise **InternalError**.

`TargetGuid`, a range check and a facing check no longer exist. `TargetNotFound`, `NotFacing` and
`OutOfRange` stay in the enum (values are append-only) but are never sent, and so does `WrongPowerType`
(14), the refusal of a cost from another pool, gone with the other pools (#1181).

**Aim is captured at cast start.** The handler builds the `AbilityAim` (facing from the caster's
`Orientation.y`, and the ground point for a cursor skill) before the cast is accepted, and the shape
script is built with it at once. A cast-time cast therefore fires with the aim it started with. A
script reads the caster's *position* only when it fires, never when it is built.

**Two paths**, both in `InstanceAbilityCastSystem`, both checking the power rule again:

- **Instant** (`CastTime = 0`): the script is built, the cost paid, the cooldown started, the
  finish-cast broadcast, and the script fired, all on the tick the request arrives.
- **Cast-time**: the script is built, the cast joins the queue, the skill is marked `Casting` and the
  cost paid, in that order, so a refused cast spends nothing and never leaves `Casting` set (#521
  item 1). The cast system then sends every client in the instance `SUnitStartCastPacket`, which carries the
  `AbilityId` (#521 item 9), the cast's `CastId`, the `Footprint` it will land on (#648) and the
  `WorldTick` it was taken on (#1082, §6). Each
  tick the cast timer runs down. Moving interrupts it (`SCharacterInterruptedCastPacket`; the cost is
  not refunded). When the timer runs out the cast leaves the queue and fires; a caster who died during
  the cast fires nothing and is free to cast again. The queue is never changed while it is walked
  (#521 item 3).

**A creature casts through the same system (#163).** Its script calls `ISimulationContext.RunInstantAbility`
or `QueueAbility` (which take any `IUnit`) with its own clone of the ability, aimed at its target; there
is no handler and no global cooldown. Its casts are free (the power rule is skipped for an `ICreature`).
It casts with its own pace (`Creature.Pace`, #1178): its cast speed shortens its cast times, its basic waits
its `SwingInterval` (its `BaseAttackTime` over its attack speed), and cooldown recovery shortens any other
cooldown; each speed comes from its auras, from 0 to the cap it spawned with. A creature that dies or turns for
home during a cast has the cast dropped on the next tick, with the interrupt, however much was left, and
any projectile it loosed that is still in flight dropped with it; so does one removed from its instance.
Moving never interrupts a creature's cast: its script stands still for a wind-up, so only a push (a
crowd's separation) could move it. See [creature-system.md](creature-system.md).

**Pace (#1178).** The cast system reads the caster's pace once, when it takes the cast (`CasterPace`: a
character's is fixed at its last stats refresh), through `AbilityPace`, the one rule: the cast time is
`CastTime / (1 + cast speed / 100)`, and the cooldown the cast sets when it fires is worked out then and kept on
the cast (`AbilityInstance.CooldownSeconds`). The cooldown follows four rules (`AbilityPace.Cooldown`), the first that
applies:

1. a creature's basic waits its swing (`SwingInterval`, below);
2. any Attack whose row's `Cooldown` is 0 waits its weapon's attack time (`WeaponDamage.AttackTimeMs`: the attack time
   the main hand's copy rolled, a base stat since #1179, after its local attack speed; the formula's
   `UnarmedAttackTimeMs`, 816, with no weapon) over attack speed (#1181). Every basic Attack (Cleave, Quick Shot, Heavy
   Strike) is one, since a basic Attack's `Cooldown` is validated as 0;
3. any other basic, a basic Spell (Spark Bolt, Smite), waits its row's `Cooldown` (800 ms) over cast speed, still
   instant and with no cast bar (owner decision D1);
4. every other skill waits its row's `Cooldown` over cooldown recovery. Each speed is percentage points, from 0 to its
cap (`AttackSpeedCap`, `CastSpeedCap`, `CooldownRecoveryCap`, 50 each), so a negative one never lengthens a time.
A weapon swap mid-fight changes neither a queued cast's time nor the cooldown it will set, and never a running
cooldown; the next cast follows the new weapon. The global cooldown is no speed's.

Firing starts the cooldown, sends the finish-cast animation, and runs the script's `Prepare`. A circle
or cone resolves completely there; a projectile keeps ticking. A skill that affects nobody is not
refused: its cost and cooldown are spent all the same.

---

## 4. Hit resolution

- **`IHitQuery`** (`Avalon.World/Abilities/Targeting`, implemented by `MapInstance` over its
  characters and creatures) returns the living units whose body circle overlaps a circle, a cone or a
  line segment. A dead unit never appears. Each list is **nearest first**, by the distance from the
  shape's origin (a circle's centre, a cone's apex, a segment's start) to the unit's centre, ties broken
  by the unit's guid. It is World-side, not part of the modding API.
- **Once per cast.** Each unit is affected at most once by one cast (`AbilityEffect`), in that order.
- **Hostility** (`Hostility`):
  - a creature is hostile to a player caster unless it is `Invulnerable`;
  - two players are hostile only when both have PvP on and the map is not a town;
  - nothing is hostile to itself;
  - a creature caster (#163) finds every living player hostile, whatever the map type, and no creature.
- **Allies** are the caster itself and every player not hostile to it. A creature is never an ally, so a
  creature caster's only ally is itself.
- **Damage**: a `Hostile` skill calls `CombatService.ApplyAbilityHit(caster, unit, ability)` (through
  `IAbilityArena.HitForAbility`) on each hostile unit, with the usual threat, encounter, combat tag, death and
  invulnerable rules. Every hostile skill rolls whether it lands on each unit (#1164), damage or not: the unit's
  chance to evade it (its evasion rating against the caster's level, #1178), the dodge wait for a telegraph's players
  (§4), and the units every hit passes over. One whose `Effects` has `Damage` then deals its amount, each unit's hit
  resolving on its own (#506, #1178), typed by the ability's `DamageType`, else the caster's base type:
  its base (below, split by type), then the unit's evasion, the
  caster's crit, the unit's block, then each type's part mitigated on its own (physical by the unit's armour, an
  element by its resistance), floored with a minimum of 1 (0 when evaded); the unit's ward then takes all of it but
  its blight share, and health the rest. One without `Damage` draws the evasion roll alone and, landed, sends nothing
  but its aura; evaded or landed, it counts as combat as an evaded hit does (the encounter, the creature engaging, both
  tagged), whether or not its aura then sticks. Its aura lands only where the hit landed. An ability flagged
  `AbilityFlags.Unavoidable` is never rolled against: no evasion roll, so its damage is never evaded and its aura is
  offered to every unit its shape affects, which never holds a unit every hit ignores (§5; the aura system's own
  refusals still apply). See
  "Combat reads the derived stats" in [World simulation](world-simulation.md).
- **Heal**: an `Ally` skill calls `CombatService.ApplyHeal` on each ally, which restores
  `min(Health, CurrentHealth + heal)`, the heal being its Heal base (below) and then a crit roll,
  never evaded, blocked or reduced by armour or resistance, and never restores ward (#506, #1178). It never lowers health (a unit at or above its maximum
  keeps what it has, #548), never heals a dead unit, and adds heal threat from
  `HealThreatPerHp` when the healed unit is in an encounter. Heal threat counts the health actually
  restored, so overheal adds none (#531).
- **The base (#1178, the offence model; `OffenceMath`, `Avalon.Combat`, shared with the API's amounts and the
  simulator).** The tags decide it (`OffenceMath.DirectBase`, `HealBase`):
  - **An Attack** rolls its caster's weapon: the main hand's damage range, the one its copy rolled (#1179, its
    `DamageMin` and `DamageMax` base stats, [item modifiers](item-modifiers.md#base-stats)), typed by the item's
    `DamageType` (`WeaponDamage` keeps a second range, which no character's weapon fills; a creature's natural range is
    untyped and takes the hit's type), then the weapon's own added damage by type
    (`WeaponDamage.Added`, Physical to Blight: the main hand's local "adds X to Y" lines, #1179), then the attacker's
    added damage to attacks by type (`AttackerCombat.AddedToAttacks`, beside the offence block, Physical to Blight: the
    sum of every worn item's "adds X to Y to Attacks" lines; a range's mask names the types that add anything, and only
    those are drawn), then the skill's built-in added roll when `AddedMax` is above 0, in that order (the
    order is the contract a seeded random sequence relies on). `ConvertPct` of the physical part, the weapon's and the
    added physical damage included, then moves to `ConvertToType`, and the built-in added damage (`AddedType`) joins,
    never converted. The weapon's ranges and its added damage are the main hand's after its local lines: its local
    increased physical damage scales each end of a physical range and of its added physical damage, and its local attack
    speed shortens its attack time, each rounded half away from zero ([inventory](inventory-and-saves.md)); a local line
    on any other item adds nothing. Each part is multiplied by `DamageEffectiveness / 100` (worked out as
    `roll x effectiveness / 100`, so whole percentages stay exact) and by its scale. A character with no main hand
    (every new character: its starting items are potions and a scroll), or with one whose copy rolled no damage above 0,
    rolls the formula's unarmed range, `UnarmedDamageMin..UnarmedDamageMax` (3-6), as physical damage, at the unarmed
    pace. A creature's weapon is its natural `DamageMin..DamageMax`, untyped, so it takes the hit's type.
  - **A Spell** rolls `SpellBaseMin..SpellBaseMax`, inclusive, times `1 + SpellBasePerLevelPct / 100 x (level - 1)`,
    in its damage type, times its scale; then the attacker's added damage to spells by type
    (`AttackerCombat.AddedToSpells`, Fire, Cold and Lightning: every worn item's "adds X to Y to Spells" lines, #1179),
    then the built-in added roll, in that order. It never converts, and its `DamageEffectiveness` (seeded 100) scales
    only added damage, to spells or built-in. Neither the weapon's added damage nor added damage to attacks reaches a
    spell.
  - **A Heal** rolls the same spell base, grown the same way, times `max(0, 1 + increased healing / 100)` and every
    more on healing: neither increased spell damage nor increased damage boosts a heal.
  - **The scale** of a part of type T for a skill with tags G (`OffenceMath.Scale`) is
    `max(0, 1 + sum of increased / 100) x product of every more`, over the attacker's offence keys `Damage`, T, and,
    for each tag G has, `Attack` (and `PhysicalAttack` for a physical part), `Spell`, `Area`, `Projectile`, `Melee`
    and `DamageOverTime`. An increased sum below -100 makes that part nothing (the hit still deals its minimum of 1). A
    character's offence block (`CharacterOffence.Of`) is class-neutral: each point of Strength gives
    `IncreasedPhysicalAttackPerStrength` (3.5) points of increased physical attack damage, each point of Intellect
    `IncreasedSpellPerIntellect` and `IncreasedHealingPerIntellect` (3.5 each), each point of Agility
    `AttackSpeedPerAgility` (0.1) points of attack speed (`CombatFormula`); the gear's `Increased…` stats add flat, and
    auras fold Flat, Increased and More on top ([Auras](auras.md)). A creature's offence block is its auras' alone.
  - **Conversion.** Converted damage takes the modifiers of the type it became plus every untyped, delivery and tag
    modifier, never its original type's. So Strength's increased physical attack damage never boosts Crippling Shot's
    cold half, which grows only with the skill's effectiveness; Binding Shot's built-in 2-4 blight is scaled the same
    way, as blight.
  - **Crit.** A crit multiplies by `HitResolver.CritMultiplier`, `max(0, CritMultiplier + bonus / 100)`: the formula's
    `CritMultiplier` (1.5) plus the attacker's crit multiplier bonus in points (gear `StatType.CritMultiplier`, auras
    `AuraStat.CritMultiplier`; 50 makes a 1.5 crit a 2.0 one). A character's crit chance is the formula's
    `BaseCritPct` (5) plus its gear's, the same for every class; a creature's is its rarity row's.
  - **A damage-over-time aura's** total is rolled once, at application, from its source's base without the source's
    conversion or any added damage (an Attack's source roll is the weapon's two ranges, after its local increased
    physical damage), and scaled with the source's tags plus DamageOverTime ([Auras](auras.md)).
- **Whose screen judges a hit (lag compensation, #1082).** Every hit is positional, and each side of a
  fight sees the other late: a client draws other units its render delay (about 120 ms) plus half its
  round trip behind the server, and what it does reaches the server half a round trip later. The rule:
  **an instant cast and a projectile are judged by the attacker's screen; a hostile telegraph by the
  target's screen.** A creature has no screen: its own casts are always judged on the server's present.
  So in PvE a player's instant shots and the creatures' telegraphs are both judged by that player's
  screen (a creature's instant attack is judged on the present), and in PvP the attacker wins an instant
  shot and the dodger a telegraph. The limits are in `LagCompensation` (`Avalon.World/Abilities`).
- **Rewound hits (the attacker's screen).** A player's instant cast names the tick its client was
  drawing (`CCastAbilityPacket.ViewTick`, its render clock turned back into a tick through the ping's
  anchor, see [the packet protocol](networking-packet-protocol.md)), and its hits are rewound by
  `now - ViewTick` ticks, at most `LagCompensation.MaxRewindTicks` (15, 250 ms). 0 (not sent: a client
  from before #1082) or a tick at or ahead of now rewinds nothing. The cap is the only trust boundary: a
  client may always claim all of it. `CastAbilityHandler` fires an instant cast through the instance's
  `IPlayerCastHost` (internal, World-side), which fixes the cast's `CastView` as it is taken. While the
  cast system runs that cast's `Prepare` it sets the ambient pair `IAbilityArena.HitRewindTicks` and
  `HitRewindExempt` (the caster), and puts back what it found after, so once a cast fired inside
  another's hit returns, the outer cast has its own back. Under a rewind `IHitQuery` tests, and sorts,
  every unit at `PositionOf(unit)`: its history's position that many ticks ago, or its present position
  when the history does not reach back that far (it joined or jumped since). The caster stays where it
  is now: its own inputs reach the server in order before its cast, so where it stands now is where it
  stood on its own screen (a self-including Ally circle must not test it where it was). Liveness and
  membership stay the present's: a unit that died or left since is never hit. The spatial grid holds
  present positions, so a rewound query widens the box it visits by how far a unit can have moved since
  the tick it reads (`SpatialGrid.RewindDrift`): each move measures, on X and on Z, how far the unit is
  from where its history was last recorded, the grid keeps the largest such step of each recorded tick
  for the last 18 ticks, and the box grows by the sum of those of the window plus the largest move since
  the last record. A unit's displacement over the window is at most the sum of its own steps, so none is
  missed however fast it moved, and a window in which little moved costs little more than the present
  (in `forest-combat`, 0.44 m on average, where a fixed 1 m a tick gave 8 m). A jumped unit is tested at
  its present position, so a jump adds nothing; a step that is not finite makes the query scan every
  member. Each candidate's rewound position is read once, and its hit carries its distance from the
  origin into the nearest-first sort. Never rewound: a queued cast (its wind-up is the dodge window and
  its footprint was fixed at its start), a creature's cast, and item use.
- **Position history.** Each character and creature in an instance holds a `PositionHistory`
  (`CharacterEntity.History`, `Creature.History`; never on `IUnit`): a ring of 18 entries keyed by world
  tick (`WorldTicks.Completed`), the 15-tick cap and a margin. `MapInstance` records, in its Step 4b,
  after every move of the tick and before the broadcast, every member that moved since the last record,
  so an entry is the position the broadcast stamped with that tick carries. The units are listed as they
  move: the concrete position setters' call into the spatial grid (`SpatialGrid.Moved`) adds the unit
  to a preallocated list once per tick (a write that leaves its X and Z where they were adds nothing:
  Crowd locomotion writes every agent every tick, standing ones included), and the step
  (`SpatialGrid.RecordMoved`) records that list and reads no other unit (most of a forest's creatures
  are waiting, #979; `forest-combat` records 2 units a tick of 203). An unchanged position writes
  nothing, so a unit standing still keeps its older entries, and a lookup is the newest entry at or
  before the tick. The rings are pooled per instance: one is rented, empty, as a unit joins and returned
  as it leaves, so a unit entering an instance (every transfer, a teleport or a town return into the
  same instance included, leaves and joins again) starts afresh. A join is a jump: `SpatialGrid.Add`
  lists the unit as jumped, so the first Step 4b after it seeds the ring wherever the unit stands then,
  and one that never moves is found there. A map arrival adds the character with the coordinates of the
  map it left and only then writes its spawn (`EnterMapHandler`, `MapTeleport`, `TownReturn`), so a seed
  taken at the add would put it on another map for a query rewound to that tick; a spawn written before
  that Step 4b is not a step either, so it never widens the destination's rewound queries. A jump inside
  the instance clears it: a revive (`CombatService.RevivePlayer`) and a creature's teleport
  (`CrowdLocomotion.Teleport`, `WaypointLocomotion.Teleport`) call the entity's `Jump()` before writing
  the position, which empties the history and lists the unit, so the next Step 4b reseeds it where it
  landed (even where it stood) and a rewind never returns a position from before a jump. Until then, a
  rewound query uses the present position. Recording and lookups allocate nothing (#880), and Step 4b is
  timed as its own phase (`historyTicks`, [tick profiling](tick-profiling.md)). An instance built
  without the world's tick count (tests) keeps no history and rewinds nothing.
- **Dodge wait (the target's screen).** A telegraph reaches a player half a round trip late, and their
  step out of it reaches the server half a round trip after they make it, so a player out of the
  footprint on their own screen when the countdown ended could still be hit. When a queued hostile
  circle or cone fires (a creature's wind-up, or a player's telegraph in PvP), it is tested as before,
  and every unit that is not a player is hit on the spot. Each player it hits is held instead
  (`IAbilityArena.TryDeferHit`, while the cast system sets `DodgeFootprint` around the `Prepare`) for
  that player's one-way delay, `min(LagCompensation.OneWayTicks(RoundTripTime), MaxDodgeWaitTicks)`:
  half the connection's round trip in whole ticks, rounded, at most 6 (about 100 ms). The held hits
  (`PendingHits`, one list per instance) are resolved at the start of the cast system's update on the
  tick they are due, after that tick's inputs, and never on the tick the cast fired. A hit lands, through
  the same `AbilityEffect.Apply` as an immediate one (damage, threat, kill credit), only if the player
  is still alive, still a target of the ability, and still inside the footprint where they stand now;
  otherwise it is a dodge and does nothing. The wait only removes hits: a player outside the footprint
  when it fired was never held, even if they step in. A creature caster that dies or turns for home
  during the wait lands none of its held hits, as its queued casts and projectiles land nothing (#163):
  its death or its turn already took it out of the encounter, and a late hit would put its corpse back
  in or pull its walk home back into the fight. A player caster dying during the wait still lands its
  hits (the cast fired). A caster or a target leaving the instance drops them (`CancelCasts`). The
  fired broadcast goes out at fire. Nothing waits for an Ally cast (a heal or a buff is not a threat to
  step out of), a projectile (its flight is its own dodge window), an instant cast, a creature target, or
  a player whose one-way delay rounds to 0 ticks (no round trip measured, or one under about 17 ms).

---

## 5. The three shapes

**Circle** (`CircleAbilityScript`). On the caster, the centre is the caster's position. On the aim
point, the centre is the point moved toward the caster until it is at most `Reach` away, then pulled
back by `MapNavigator.RaycastWalkable` from the caster, so a blast cannot land behind a wall. A caster
off the navmesh centres it on itself. Every qualifying unit whose body overlaps `Radius` is affected,
once, when it fires.

**`MaxTargets`, every shape (#1181).** A shape whose `MaxTargets` is above 0 affects at most that many units, the
nearest first (the hit query's order). Only units that qualify (`AbilityEffect.Qualifies`) count. For a Hostile
ability that leaves out every unit hits ignore (`CombatService.IgnoresHits`: an invulnerable creature, a corpse, a
creature walking home, a protected arrival) as well as the units it is not hostile to (an invulnerable creature and a
protected arrival already were not), so neither an ally nor a boar walking home standing in front ever takes the
place of the nearest unit it can damage; a unit held for the dodge wait
(§4) counts as affected. A circle or a cone passes it to `AbilityEffect.ApplyToAll`, which stops once it has affected
that many; a piercing projectile ends on its `MaxTargets`-th hit, told as a hit (`ProjectileImpactReason.Hit`), and
one without `Pierce` already ends on its first. A projectile flies through every unit that does not qualify, one hits
ignore included, and a held hit whose target has since come to be ignored is dropped (§4). The same rule means an
`Unavoidable` ability offers its aura only to a unit it may affect: an ignored unit is offered none. Before #1181 the
aura system refused such an aura, except crowd control on a creature walking home, which it resisted and answered
"Immune"; a shape's crowd control now skips that creature and tells nothing (crowd control applied to it directly, by
a Resonance proc, is still resisted and answered "Immune").

**Cone** (`ConeAbilityScript`). The apex is the caster. It points along the caster's facing (a
movement skill) or toward the aim point (a cursor skill; a point on the caster falls back to facing).
A unit is inside when its centre is within `Reach` plus its body radius, and its direction from the
apex is within half of `ArcDegrees`, widened by the angle its body spans at that distance, so a large
body at the edge still counts. A body over the apex is inside whatever the direction, and a 360-degree
cone is a circle. It resolves once, when it fires. Walls do not clip it (an accepted limitation).

**Projectile** (`ProjectileAbilityScript`). A world object (`ObjectType.SpellProjectile`) spawned at
the caster, flying flat toward the aim point at `ProjectileSpeed`. Its velocity is metres per second,
like every other moving object (#424), and it is drawn 0.5 m above the ground it flies along. Each
tick it sweeps its whole step as a segment:

- the step is speed × tick time, capped at the `Reach` still left, so a long tick cannot carry it
  past `Reach` or through a unit;
- the segment is cut by `RaycastWalkable`, so a wall stops it;
- the units whose body the segment touches are taken nearest first. Without `Pierce` it ends on the
  first unit that qualifies, stopping at the point of its step nearest that unit's centre. With `Pierce`
  it hits each unit once and flies on.
- a player's projectile sweeps the units where that player sees them beside it (#1082). The client
  draws projectiles where the server has them, with no delay, but other units its render delay plus one
  way of the round trip behind, so the sweep is rewound for the whole flight by its cast's
  `CastView.ProjectileRewindTicks`: the instant rewind (§4) less the caster's one-way delay (half its
  `RoundTripTime`, in whole ticks), never below 0, fixed when the cast is taken. The caster is exempt
  as for an instant cast, and the stop point is taken at the unit's rewound position
  (`IHitQuery.PositionOf`). A round trip the client inflates only shortens this rewind, never lengthens
  it past the cap. A creature's projectile, and a queued one, sweep the present.

It ends at `Reach`, at a wall (a caster off the navmesh ends it on its first tick), or on a first hit.
It never homes and has no target.

---

## 6. Broadcast

- **Projectiles** are world objects, so clients see them through the ordinary world-state add, update
  and remove packets. A finished projectile is not ticked again and stays until its final state (where
  it stopped, with zero velocity) has gone out in a broadcast, so every client sees it spawn, stop and
  despawn, even one that ended on the tick it was first seen. An instance nobody is in drops its
  finished projectiles at once, since there is nobody to send them to, so a player who enters later
  never sees one frozen where it stopped.
- **Circles and cones** broadcast `SMSG_ABILITY_FIRED` (`SAbilityFiredPacket { CasterGuid, AbilityId,
  Origin, Direction?, Centre?, CastId, Footprint }`) to the instance: a circle carries its centre, a cone its
  direction, and both the cast id and the whole footprint, dimensions included (#648).
- **Footprints (#648).** `AbilityFootprint.Resolve` decides where a cast lands, from its row, its aim and the
  caster's position when the cast started (`AbilityAim.Origin`, stamped by the cast system). The shape scripts
  fire with it and the start broadcast carries it, so a telegraph is never corrected: a creature pushed during
  its wind-up still fires where it began.
- **The countdown's deadline (#1082).** The start carries `WorldTick` (`SUnitStartCastPacket` field 7),
  the world tick the cast was taken on, an item's start included. Through the ping's anchor a client
  turns it into server time and ends the cast bar and the telegraph's countdown at that time plus
  `CastTime`, not at the packet's arrival plus it, so the screen the dodge wait judges a target by (§4)
  shows the real deadline.
- **Damage** arrives as the usual damage packets; `SCharacterDamagePacket.AbilityId` is filled (#521
  item 8), and both carry `Result`, a `HitResult` saying whether the hit crit, was blocked, or was
  evaded (`Dodged`, sent with 0 damage) (#506), and the hit's `Type`, `Types` and `WardAbsorbed` (#1178;
  `Damage` is the whole hit, the ward's part included). A hostile ability that deals no damage sends an
  evaded hit the same way when it is evaded (its type, and no parts), and nothing but its aura when it lands
  (#1164).
- **Heals** that restored more than 0 health broadcast `SMSG_UNIT_HEALED` (`SUnitHealedPacket { Healer,
  Target, Amount, CurrentHealth, AbilityId?, Result }`) to the healer, the target and every client within
  the interest radius of the target (#506, #532): `Amount` is the health restored, overheal left out, and
  `Result` is `Crit` for a critical heal. The new health also reaches clients through state replication.
- **The character sheet** is `SMSG_CHARACTER_STATS` (`SCharacterStatsPacket`), sent to its owner only
  (#506): the attributes, armour, crit and block chances clamped to the formula's caps, the weapon's range
  (the bare hand's with none), and since #1178 the evasion rating, the chance it gives against an attacker of the
  character's own level, the maximum ward, the four effective resistances (capped as a hit caps them), the crit
  multiplier, the three effective speeds, the weapon's attack time and the offence block's increased points, and since
  #1180 each Resonance theme's count of worn items (see [inventory and saves](inventory-and-saves.md)).

---

## 7. PvP

A player's PvP flag lives on the character row (`Characters.PvpEnabled`, `PvpOffAt`) and is changed
only by `PvpToggle`, reached from `CMSG_PVP_TOGGLE` and from the `/pvp` command. Turning it on is
immediate. Asking to turn it off starts a five-minute timer (`Game:PvpOffDelay`), during which the
player is still hostile; asking again cancels the timer. Every hit that deals damage from one player to
another living player restarts both players' running timers. The answer, and every change the timer
makes, is `SMSG_PVP_STATE { Enabled, OffInMs }`. Other players see the flag as `ObjectState.PvpEnabled`.
Towns never allow player hostility, whatever the flags.

---

## 8. The starter kit

Seeded by the World migration `SeedStarterSkillKit`; `CharacterCreateInfos.StartingSpells` grants each
class its three, and the Character migration `GrantStarterSkillKit` gave every existing character its
class's three in place of whatever it held. The retired skills 1, 2 and 100-103 are gone and their ids
are never reused. Every kit skill has `AllowedClasses` its own class, `ThreatMultiplier` 1, and
`HealThreatPerHp` 0 except Mending Circle (0.5). The numbers are placeholders to tune. Under the offence model
(#1178) the bases re-express the old numbers: an Attack's effectiveness carries its old level-1 average over a
starter weapon's roll, and a Spell's or a Heal's base and growth carry its old level-1 and level-10 averages, each
within a few percent. An Attack whose cooldown is 0, every basic one among them, waits its weapon's attack time
(`weapon`).

| id | class | name | shape | aim | anchor | reach (m) | radius (m) | arc (°) | speed (m/s) | pierce | affects | cast | cooldown | cost | tags | base | type |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 200 | Warrior | Cleave | Cone | Movement | – | 2.5 | – | 100 | – | – | Hostile | 0 | weapon | 0 | Attack, Melee, Area | 315 % | Physical |
| 201 | Warrior | Ground Slam | Circle | Movement | Caster | 0 | 3 | – | – | – | Hostile | 0 | 5 s | 20 | Attack, Melee, Area | 614 % | Physical |
| 202 | Warrior | Hurled Axe | Projectile | Cursor | – | 15 | – | – | 9 | no | Hostile | 0 | 3 s | 10 | Attack, Projectile | 492 % | Physical |
| 210 | Wizard | Spark Bolt | Projectile | Cursor | – | 20 | – | – | 11 | no | Hostile | 0 | 0.8 s | 0 | Spell, Projectile | 14-20, +0.70 %/level | Lightning |
| 211 | Wizard | Flame Burst | Circle | Cursor | AimPoint | 18 | 3 | – | – | – | Hostile | 0.6 s | 5 s | 25 | Spell, Area | 21-32, +0.75 %/level | Fire |
| 212 | Wizard | Frost Fan | Cone | Cursor | – | 6 | – | 60 | – | – | Hostile | 0 | 4 s | 15 | Spell, Area | 26-40, +0.80 %/level | Cold |
| 220 | Hunter | Quick Shot | Projectile | Cursor | – | 25 | – | – | 14 | no | Hostile | 0 | weapon | 0 | Attack, Projectile | 363 % | Physical |
| 221 | Hunter | Piercing Arrow | Projectile | Cursor | – | 30 | – | – | 12 | yes | Hostile | 0 | 4 s | 15 | Attack, Projectile | 690 % | Physical |
| 222 | Hunter | Scatter Shot | Cone | Cursor | – | 8 | – | 45 | – | – | Hostile | 0 | 4 s | 20 | Attack, Area | 516 % | Physical |
| 230 | Cleric | Smite | Projectile | Cursor | – | 18 | – | – | 10 | no | Hostile | 0 | 0.8 s | 0 | Spell, Projectile | 12-17, +0.45 %/level | Lightning |
| 231 | Cleric | Radiant Pulse | Circle | Movement | Caster | 0 | 4 | – | – | – | Hostile | 0 | 5 s | 20 | Spell, Area | 20-31, +0.35 %/level | Lightning |
| 232 | Cleric | Mending Circle | Circle | Cursor | AimPoint | 15 | 4 | – | – | – | Ally | 0 | 8 s | 25 | Heal, Area | 30-44 | – |
| 319 | Warrior | Heavy Strike | Cone | Movement | – | 2.5 | – | 60 | – | – | Hostile | 0 | weapon | 0 | Attack, Melee | 380 % | Physical |
| 320 | Wizard | Frostbolt | Projectile | Cursor | – | 22 | – | – | 13 | no | Hostile | 0.5 s | 2 s | 12 | Spell, Projectile | 28-42, +0.80 %/level | Cold |

**Heavy Strike and Frostbolt (#1181)** come with the talent tree ([talents.md](talents.md)), seeded by the World
migration `AddTalentTree`. Heavy Strike is the Warrior's starter, linked from the Warrior's start node: a `Basic`
Attack with `MaxTargets` 1, so it hits only the nearest hostile in its cone, paced by the weapon. Frostbolt is the
ability node between the Hunter's and the Wizard's regions. No class's `StartingSpells` grants either yet, and
their `AllowedClasses` are the Warrior and the Wizard.

Every cost is mana, for every class (#1181). `SeedIntegrityShould` pins the twenty-one kit abilities (this table's
twelve and the nine aura and crowd-control abilities, [auras](auras.md)), what the design fixes of Heavy Strike and
Frostbolt, the class links, the starting skills, one script per shape, Mending Circle as the only ally heal, and that
every cost is payable under the power rule from a level 1 character's maximum mana.

## 8b. The creature abilities

Seeded by the World migration `SeedCreatureAbilities` (#163). Every one is creature-only
(`AllowedClasses` empty), costs nothing, has `ThreatMultiplier` 1, and is an Attack (#1178, tagged
Projectile or Area by its shape and Melee as a cone of 2.5 m or less) that deals a roll of the creature's natural
`DamageMin..DamageMax` times its `DamageEffectiveness`.
Each basic has effectiveness 100 %, so it deals what the old raw swing did, and lists the seeded 2250 ms
swing interval as its cooldown, which it never reads: it waits the creature's `SwingInterval`. Cones and
circles aim along the facing (a circle sits on the creature); projectiles aim at the cursor, which a
creature sets to its target's position. The arcs and projectile speeds are the values chosen for #163.

| id | creature | name | shape | reach (m) | radius (m) | arc (°) | speed (m/s) | pierce | cast | cooldown | effectiveness |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 300 | Thornback Boar | Gore (basic) | Cone | 1.8 | – | 90 | – | – | 0 | swing | 100 % |
| 301 | Thornback Boar | Trample | Circle | 0 | 2.5 | – | – | – | 0 | 10 s | 160 % |
| 302 | Grey Fen Wolf | Bite (basic) | Cone | 1.8 | – | 90 | – | – | 0 | swing | 100 % |
| 303 | Grey Fen Wolf | Ravenous Claw | Cone | 2.5 | – | 90 | – | – | 0 | 8 s | 180 % |
| 304 | Blightfly Swarmling | Sting (basic) | Cone | 1.5 | – | 90 | – | – | 0 | swing | 100 % |
| 305 | Blightfly Swarmling | Blight Spit | Projectile | 10 | – | – | 14 | no | 0 | 6 s | 140 % |
| 306 | Husk of the Wold | Slam (basic) | Cone | 1.8 | – | 90 | – | – | 0 | swing | 100 % |
| 307 | Husk of the Wold | Rotting Burst | Circle | 0 | 3 | – | – | – | 0 | 12 s | 150 % |
| 308 | Bramblemaw Alpha | Maul (basic) | Cone | 2 | – | 90 | – | – | 0 | swing | 100 % |
| 309 | Bramblemaw Alpha | Rending Frenzy | Cone | 2.5 | – | 100 | – | – | 0 | 9 s | 180 % |
| 310 | Bramblemaw Alpha | Howling Roar | Circle | 0 | 5 | – | – | – | 1 s | 15 s | 200 % |
| 311 | Old Tuskroot | Tusk Gore (basic) | Cone | 2 | – | 90 | – | – | 0 | swing | 100 % |
| 312 | Old Tuskroot | Earthsplitter | Cone | 5 | – | 60 | – | – | 1.2 s | 14 s | 240 % |
| 313 | Old Tuskroot | Thorn Volley | Projectile | 12 | – | – | 16 | yes | 0 | 10 s | 160 % |
| 314 | Mother Bramble | Bramble Lash (basic) | Cone | 2.5 | – | 90 | – | – | 0 | swing | 100 % |
| 315 | Mother Bramble | Bramble Nova | Circle | 0 | 6 | – | – | – | 1.2 s | 16 s | 250 % |
| 316 | Mother Bramble | Thornspray | Cone | 5 | – | 120 | – | – | 0 | 8 s | 180 % |

Damage types (#1178): Rotting Burst (307), Earthsplitter (312) and Thorn Volley (313) name Blight; every other
leaves `DamageType` null and deals its creature's base type, Blight for the Blightfly Swarmling and Mother Bramble
and Physical for the rest.

Which of them a creature uses, and when, is its script's rotation (`creature-system.md`).
`SeedIntegrityShould` pins every row and each forest template's script. The client reads these rows,
with the kit's, from `schema/abilities/ability-catalog-v1.json` (`tools/Avalon.Exporter -- ability-catalog`).
`AbilityCatalogExportShould` fails when that file differs from the seed rendered (#1146), so a seed change needs a
re-export.

---

## 9. Out of scope

- Creature abilities that heal or buff other creatures (#163 leaves a creature's only ally itself).
- Removing the global cooldown (attack speed, cast speed and cooldown recovery exist since #1178, §3).
- A line or beam shape.
- Cones clipped by walls.
- Groups: allies are every non-hostile player, not a party.
- A cap on how many units one shape may hit.
