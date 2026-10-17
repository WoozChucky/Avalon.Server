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
| `Cooldown` | uint | ms | Time before the skill can be cast again. |
| `Cost` | uint | power points | Paid from the pool `CostPowerType` names (see the power rule below). |
| `CostPowerType` | `PowerType` | | The pool `Cost` is spent from: Mana, Fury or Energy (#652). None only with a cost of 0; the catalog and a check constraint refuse a cost with none. |
| `ScriptName` | string | | The script class that runs the skill: `CircleAbilityScript`, `ConeAbilityScript` or `ProjectileAbilityScript`. |
| `Effects` | `SpellEffect` | | `Damage` for a hostile skill, `Heal` for an ally skill. |
| `EffectValue` | uint | health points | The base of the damage dealt or health restored per unit affected; the scaling terms below are added to it (#506). |
| `AllowedClasses` | list of `CharacterClass` | | The classes that may hold it. |
| `ThreatMultiplier`, `HealThreatPerHp`, `TauntDurationMs` | float, float, uint | | Threat rules, unchanged by #164. The two floats must be finite and 0 or more (#529). |
| `Flags` | `AbilityFlags` | | `RequiresInCombat`, `RequiresOutOfCombat`. |
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
| `ScalingStat` | `ScalingStat` | | `Attack` (0) scales with the caster's AttackDamage, `Ability` (1) with its AbilityDamage (#506). |
| `ScalingCoefficient` | float | | Multiplies the scaling stat into the base; finite and 0 or more (#506, also a database check). |
| `BaseDamageCoefficient` | float | | Multiplies a roll of the caster's base damage into the base: a character's main-hand weapon (`DamageMin1..DamageMax1`, inclusive; none with no weapon), a creature's natural `DamageMin..DamageMax` (#163); finite and 0 or more (#506). Was `WeaponCoefficient` until #163. |

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
2. **AlreadyCasting**: a cast-time cast is still in progress (#521 item 4).
3. **Gcd**: the global cooldown (`CombatConfig.GcdMs`, default 200 ms) since the last cast start has
   not run out, measured on the container's `TimeProvider` (#793); the answer carries the time left, rounded up.
4. **NotOwned**: the caster does not hold the skill.
5. **Cooldown**: the skill's own cooldown is running; the answer carries the time left, rounded up.
6. **RequiresOutOfCombat** / **RequiresInCombat**: the skill's combat flags.
7. **NoAimPoint**: a cursor skill whose `GroundPos` is missing or has a non-finite component.
8. **The power rule** (`AbilityCost`, #521 item 2, #652): a cost above 0 is spent from the pool the
   ability names (`CostPowerType`), and only a caster whose own pool is that one pays it. A caster
   whose pool is another one, or none, is **WrongPowerType**, however much it holds: a Mana ability
   given to a Warrior never spends Fury. Too little in the right pool is **NotEnoughPower**. A cost
   whose row names no pool is **InternalError**, since the player cannot fix it. A cost of 0 needs no
   pool. Fury is spent like the
   others but nothing generates it yet (#526): a Warrior enters full and does not regenerate.
9. **The instance**: the caster's instance must exist, and the cast system must accept the cast (its
   script found and built); otherwise **InternalError**.

`TargetGuid`, a range check and a facing check no longer exist. `TargetNotFound`, `NotFacing` and
`OutOfRange` stay in the enum (values are append-only) but are never sent.

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
Its haste is its own `min(HastePct, HasteCap)`. Its basic's cooldown is its `SwingInterval`, which already
carries that haste; any other cooldown is the row's divided by it. A creature that dies or turns for
home during a cast has the cast dropped on the next tick, with the interrupt, however much was left, and
any projectile it loosed that is still in flight dropped with it; so does one removed from its instance.
Moving never interrupts a creature's cast: its script stands still for a wind-up, so only a push (a
crowd's separation) could move it. See [creature-system.md](creature-system.md).

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
- **Damage**: a `Hostile` skill calls `CombatService.ApplyDamage(caster, unit, EffectValue, ability)`
  on each hostile unit, with the usual threat, encounter, combat tag, death and invulnerable rules.
  Each unit's hit resolves on its own (#506): `EffectValue + ScalingCoefficient x stat +
  BaseDamageCoefficient x base damage roll`, then the unit's dodge, the caster's crit, the unit's block and
  its armour, floored with a minimum of 1 (0 on a dodge). See "Combat reads the derived stats" in [World simulation](world-simulation.md).
- **Heal**: an `Ally` skill calls `CombatService.ApplyHeal` on each ally, which restores
  `min(Health, CurrentHealth + heal)`, the heal being the same base as damage and then a crit roll,
  never dodged, blocked or reduced by armour (#506). It never lowers health (a unit at or above its maximum
  keeps what it has, #548), never heals a dead unit, and adds heal threat from
  `HealThreatPerHp` when the healed unit is in an encounter. Heal threat counts the health actually
  restored, so overheal adds none (#531).
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
  first qualifying unit, stopping at the point of its step nearest that unit's centre. With `Pierce`
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
  dodged (sent with 0 damage) (#506).
- **Heals** that restored more than 0 health broadcast `SMSG_UNIT_HEALED` (`SUnitHealedPacket { Healer,
  Target, Amount, CurrentHealth, AbilityId?, Result }`) to the healer, the target and every client within
  the interest radius of the target (#506, #532): `Amount` is the health restored, overheal left out, and
  `Result` is `Crit` for a critical heal. The new health also reaches clients through state replication.
- **The character sheet** is `SMSG_CHARACTER_STATS` (`SCharacterStatsPacket`), sent to its owner only
  (#506): the attributes, armour, damage stats, chances clamped to the formula's caps, and weapon range.

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
`HealThreatPerHp` 0 except Mending Circle (0.5). The numbers are placeholders to tune.

| id | class | name | shape | aim | anchor | reach (m) | radius (m) | arc (°) | speed (m/s) | pierce | affects | cast | cooldown | cost | value |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 200 | Warrior | Cleave | Cone | Movement | – | 2.5 | – | 100 | – | – | Hostile | 0 | 0.8 s | 0 | 12 |
| 201 | Warrior | Ground Slam | Circle | Movement | Caster | 0 | 3 | – | – | – | Hostile | 0 | 5 s | 20 | 25 |
| 202 | Warrior | Hurled Axe | Projectile | Cursor | – | 15 | – | – | 18 | no | Hostile | 0 | 3 s | 10 | 20 |
| 210 | Wizard | Arcane Bolt | Projectile | Cursor | – | 20 | – | – | 22 | no | Hostile | 0 | 0.8 s | 0 | 12 |
| 211 | Wizard | Flame Burst | Circle | Cursor | AimPoint | 18 | 3 | – | – | – | Hostile | 0.6 s | 5 s | 25 | 18 |
| 212 | Wizard | Frost Fan | Cone | Cursor | – | 6 | – | 60 | – | – | Hostile | 0 | 4 s | 15 | 22 |
| 220 | Hunter | Quick Shot | Projectile | Cursor | – | 25 | – | – | 28 | no | Hostile | 0 | 0.8 s | 0 | 12 |
| 221 | Hunter | Piercing Arrow | Projectile | Cursor | – | 30 | – | – | 24 | yes | Hostile | 0 | 4 s | 15 | 25 |
| 222 | Hunter | Scatter Shot | Cone | Cursor | – | 8 | – | 45 | – | – | Hostile | 0 | 4 s | 20 | 22 |
| 230 | Healer | Smite | Projectile | Cursor | – | 18 | – | – | 20 | no | Hostile | 0 | 0.8 s | 0 | 12 |
| 231 | Healer | Radiant Pulse | Circle | Movement | Caster | 0 | 4 | – | – | – | Hostile | 0 | 5 s | 20 | 22 |
| 232 | Healer | Mending Circle | Circle | Cursor | AimPoint | 15 | 4 | – | – | – | Ally | 0 | 8 s | 25 | 40 |

Costs are paid from the class's pool: Warriors Fury, Wizards and Healers Mana, Hunters Energy
(`ClassPowerType`). `SeedIntegrityShould` pins the twelve rows, the class links, the starting skills,
one script per shape, Mending Circle as the only ally heal, and that every cost is payable by its
class's pool under the power rule.

## 8b. The creature abilities

Seeded by the World migration `SeedCreatureAbilities` (#163). Every one is creature-only
(`AllowedClasses` empty), costs nothing, has `EffectValue` 0, `ThreatMultiplier` 1 and no scaling
stat, and deals `BaseDamageCoefficient` times a roll of the creature's natural `DamageMin..DamageMax`.
Each basic has coefficient 1.0, so it deals what the old raw swing did, and lists the seeded 2250 ms
swing interval as its cooldown, which it never reads: it waits the creature's `SwingInterval`. Cones and
circles aim along the facing (a circle sits on the creature); projectiles aim at the cursor, which a
creature sets to its target's position. The arcs and projectile speeds are the values chosen for #163.

| id | creature | name | shape | reach (m) | radius (m) | arc (°) | speed (m/s) | pierce | cast | cooldown | base damage |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 300 | Thornback Boar | Gore (basic) | Cone | 1.8 | – | 90 | – | – | 0 | swing | 1.0 |
| 301 | Thornback Boar | Trample | Circle | 0 | 2.5 | – | – | – | 0 | 10 s | 1.6 |
| 302 | Grey Fen Wolf | Bite (basic) | Cone | 1.8 | – | 90 | – | – | 0 | swing | 1.0 |
| 303 | Grey Fen Wolf | Ravenous Claw | Cone | 2.5 | – | 90 | – | – | 0 | 8 s | 1.8 |
| 304 | Blightfly Swarmling | Sting (basic) | Cone | 1.5 | – | 90 | – | – | 0 | swing | 1.0 |
| 305 | Blightfly Swarmling | Blight Spit | Projectile | 10 | – | – | 14 | no | 0 | 6 s | 1.4 |
| 306 | Husk of the Wold | Slam (basic) | Cone | 1.8 | – | 90 | – | – | 0 | swing | 1.0 |
| 307 | Husk of the Wold | Rotting Burst | Circle | 0 | 3 | – | – | – | 0 | 12 s | 1.5 |
| 308 | Bramblemaw Alpha | Maul (basic) | Cone | 2 | – | 90 | – | – | 0 | swing | 1.0 |
| 309 | Bramblemaw Alpha | Rending Frenzy | Cone | 2.5 | – | 100 | – | – | 0 | 9 s | 1.8 |
| 310 | Bramblemaw Alpha | Howling Roar | Circle | 0 | 5 | – | – | – | 1 s | 15 s | 2.0 |
| 311 | Old Tuskroot | Tusk Gore (basic) | Cone | 2 | – | 90 | – | – | 0 | swing | 1.0 |
| 312 | Old Tuskroot | Earthsplitter | Cone | 5 | – | 60 | – | – | 1.2 s | 14 s | 2.4 |
| 313 | Old Tuskroot | Thorn Volley | Projectile | 12 | – | – | 16 | yes | 0 | 10 s | 1.6 |
| 314 | Mother Bramble | Bramble Lash (basic) | Cone | 2.5 | – | 90 | – | – | 0 | swing | 1.0 |
| 315 | Mother Bramble | Bramble Nova | Circle | 0 | 6 | – | – | – | 1.2 s | 16 s | 2.5 |
| 316 | Mother Bramble | Thornspray | Cone | 5 | – | 120 | – | – | 0 | 8 s | 1.8 |

Which of them a creature uses, and when, is its script's rotation (`creature-system.md`).
`SeedIntegrityShould` pins every row and each forest template's script. The client reads these rows,
with the kit's, from `schema/abilities/ability-catalog-v1.json` (`tools/Avalon.Exporter -- ability-catalog`).

---

## 9. Out of scope

- Creature abilities that heal or buff other creatures (#163 leaves a creature's only ally itself).
- Tempo: attack and cast speed, and removing the global cooldown (a later spec, with the #506 stats).
- A line or beam shape.
- Cones clipped by walls.
- Groups: allies are every non-hostile player, not a party.
- A cap on how many units one shape may hit.
