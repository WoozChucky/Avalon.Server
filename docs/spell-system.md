# Skill System

How a player skill (an *ability*) is defined, cast, resolved and shown to clients (#164, #521).

A skill **aims**; it never **targets**. A cast carries a direction (the character's facing) or a point
on the ground (the cursor), never a selected unit. The skill's **shape** (a circle, a cone or a
projectile) then decides which units it affects. There is one generic script per shape, and every
number that makes one skill differ from another is a column on its row.

Terms used below:

- **Caster**: the unit casting the skill. Only players cast today; creature casting is #163.
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
| `Cost` | uint | power points | Paid from the caster's power pool (see the power rule below). |
| `SpellScript` | string | | The script class that runs the skill: `CircleAbilityScript`, `ConeAbilityScript` or `ProjectileAbilityScript`. |
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
| `WeaponCoefficient` | float | | Multiplies a roll of the caster's main-hand weapon (`DamageMin1..DamageMax1`, inclusive; none with no weapon) into the base; finite and 0 or more (#506). |

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

A client casts with `CMSG_CAST_ABILITY` (`CCastAbilityPacket { AbilityId, GroundPos? }`).
`CastAbilityHandler` checks, in this order, and answers the first refusal with exactly one
`SAbilityNotReadyPacket` naming a `CastRejectReason` (#512). A connection with no character is the one
silent case.

1. **Dead**: the caster is dead.
2. **AlreadyCasting**: a cast-time cast is still in progress (#521 item 4).
3. **Gcd**: the global cooldown (`CombatConfig.GcdMs`, default 200 ms) since the last cast start has
   not run out; the answer carries the time left, rounded up.
4. **NotOwned**: the caster does not hold the skill.
5. **Cooldown**: the skill's own cooldown is running; the answer carries the time left, rounded up.
6. **RequiresOutOfCombat** / **RequiresInCombat**: the skill's combat flags.
7. **NoAimPoint**: a cursor skill whose `GroundPos` is missing or has a non-finite component.
8. **The power rule** (`AbilityCost`, #521 item 2): a cost above 0 needs a pool the cast spends
   (Mana, Energy or Fury) holding at least the cost. Too little is **NotEnoughPower**; a caster with no
   pool (`PowerType.None`) is **InternalError**, since the player cannot fix it. Fury is spent like the
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
  item 1). Every client in the instance gets `SUnitStartCastPacket`, which carries the `AbilityId` (#521 item 9). Each
  tick the cast timer runs down. Moving interrupts it (`SCharacterInterruptedCastPacket`; the cost is
  not refunded). When the timer runs out the cast leaves the queue and fires; a caster who died during
  the cast fires nothing and is free to cast again. The queue is never changed while it is walked
  (#521 item 3).

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
  - a creature caster finds nothing hostile (creature casting is #163).
- **Allies** are the caster itself and every player not hostile to it. A creature is never an ally.
- **Damage**: a `Hostile` skill calls `CombatService.ApplyDamage(caster, unit, EffectValue, ability)`
  on each hostile unit, with the usual threat, encounter, combat tag, death and invulnerable rules.
  Each unit's hit resolves on its own (#506): `EffectValue + ScalingCoefficient x stat +
  WeaponCoefficient x weapon roll`, then the unit's dodge, the caster's crit, the unit's block and
  its armour, floored with a minimum of 1 (0 on a dodge). See CLAUDE.md's combat-formula bullet.
- **Heal**: an `Ally` skill calls `CombatService.ApplyHeal` on each ally, which restores
  `min(Health, CurrentHealth + heal)`, the heal being the same base as damage and then a crit roll,
  never dodged, blocked or reduced by armour (#506). It never lowers health (a unit at or above its maximum
  keeps what it has, #548), never heals a dead unit, and adds heal threat from
  `HealThreatPerHp` when the healed unit is in an encounter. Heal threat counts the health actually
  restored, so overheal adds none (#531).

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
  Origin, Direction?, Centre? }`) to the instance: a circle carries its centre, a cone its direction.
- **Damage** arrives as the usual damage packets; `SCharacterDamagePacket.AbilityId` is filled (#521
  item 8), and both carry `Result`, a `HitResult` saying whether the hit crit, was blocked, or was
  dodged (sent with 0 damage) (#506).
- **Heals** have no packet of their own: the healed unit's new current health reaches clients through
  ordinary state replication.

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
| 211 | Wizard | Flame Burst | Circle | Cursor | AimPoint | 18 | 3 | – | – | – | Hostile | 0.6 s | 5 s | 25 | 35 |
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

---

## 9. Out of scope

- Creature casting on this pipeline (#163): creatures still only melee.
- Tempo: attack and cast speed, and removing the global cooldown (a later spec, with the #506 stats).
- A line or beam shape.
- Cones clipped by walls.
- Groups: allies are every non-hostile player, not a party.
- A cap on how many units one shape may hit.
