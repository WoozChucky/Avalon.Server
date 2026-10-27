# Multi-world REST API

A deployment runs any number of world servers, one per row of the auth `Worlds` table, and every world has its own
world database (content: templates, maps, chunks, quests) and characters database; only the auth database and Redis
are shared (#523). The API serves all of them: world content and characters are the worlds service's
(`Avalon.Api.Worlds`), and identity reads each world's Characters database for game admission and account
consolidation ([API services](api-services.md)). World content and characters live under `/world/{worldId}/...`:
`WorldRouteMiddleware` decides, before authorization, whether this API can serve that world to this caller and selects
it for the request, and the ordinary repositories then open that world's databases. Views that span worlds (an
account's characters, the world list, live presence) read each world by name. Adding a world is configuration only.
The world servers are unchanged: each serves one world from its own `Database:World` and `Database:Characters`, and
each migrates its own databases; the API never migrates them, and each API process checks the world databases it
reads once, at startup.

## Where the code lives

The world database plumbing is shared hosting, under `src/Server/Avalon.Api.Hosting/Worlds/` (namespace
`Avalon.Api.Hosting.Worlds`), so every service that reads a world uses the same code; the world controllers, previews
and services are the worlds service's, under `src/Server/Avalon.Api.Worlds/`. Paths below are under `src/Server/`.

| Type | File | Role |
|---|---|---|
| `WorldDatabaseSettings` | `Avalon.Api.Hosting/Worlds/WorldDatabaseSettings.cs` | Parses `Database:Worlds`, for the parts a process reads. `TryParseWorldId` is the one parse of a world id, for the configuration keys and the route alike |
| `WorldDatabaseParts` | `Avalon.Api.Hosting/Worlds/WorldDatabaseParts.cs` | Which of a world's databases a process reads: both for worlds, only the Characters database for identity, none for commerce and distribution (a process reads the union for its services) |
| `ConfiguredWorld`, `WorldDatabaseStatus` | `Avalon.Api.Hosting/Worlds/ConfiguredWorld.cs`, `WorldDatabaseStatus.cs` | One configured world: its id, the connection strings the process reads and its status (`Available`, `Unavailable`). A class, not a record, and its `ToString` prints only the id and status, so no generated member can print a connection string |
| `IWorldDatabases`, `WorldDatabases` | `Avalon.Api.Hosting/Worlds/IWorldDatabases.cs`, `WorldDatabases.cs` | The configured worlds in id order. `IsAvailable(world)` is the one test of "this API process can serve that world" |
| `ApiDatabaseMigrator` | `Avalon.Api.Hosting/Worlds/ApiDatabaseMigrator.cs` | Startup: migrates the auth database for the process that owns its schema (identity), and checks the world databases the process reads |
| `WorldRouteMiddleware` | `Avalon.Api.Hosting/Worlds/WorldRouteMiddleware.cs` | Selects the request's world on `[WorldScoped]` and `[PublicWorldScoped]` endpoints; in the pipeline only where a running service declares world routes (worlds) |
| `WorldScopedAttribute`, `PublicWorldScopedAttribute` | `Avalon.Api.Hosting/Worlds/WorldScopedAttribute.cs`, `PublicWorldScopedAttribute.cs` | The controller markers the middleware acts on (route value `worldId`) |
| `ICurrentWorld`, `CurrentWorld` | `Avalon.Api.Hosting/Worlds/CurrentWorld.cs` | Scoped: the request's world id and its auth row's name. Selected at most once; a second `Select` throws |
| `CurrentWorldDbContextFactory<TContext>` | `Avalon.Api.Hosting/Worlds/CurrentWorldDbContextFactory.cs` | The API's `IDbContextFactory<WorldDbContext>` and `IDbContextFactory<CharacterDbContext>`: opens the request's world, throws when none is selected |
| `IWorldDbContextFactory`, `ConfiguredWorldDbContextFactory` | `Avalon.Api.Hosting/Worlds/WorldDbContextFactory.cs` | Opens a named world's contexts (the caller disposes them). `ForWorld`/`ForCharacters` fix a factory to one world |
| `IWorldRepositories`, `WorldRepositories` | `Avalon.Api.Hosting/Worlds/WorldRepositories.cs` | Repositories over one named world, for cross-world work; each call builds a fresh repository |
| `WorldDatabaseRegistration` | `Avalon.Api.Hosting/Worlds/WorldDatabaseRegistration.cs` | `AddWorldDatabases`, called by `AddApiHosting` (`Avalon.Api.Hosting/ApiHostingRegistration.cs`) with the parts the running services read |
| `PublicCaller`, `PublicWorlds` | `Avalon.Api.Hosting/Worlds/PublicCaller.cs`, `PublicWorlds.cs` | Who a public request comes from; which worlds it may read, and the default one |
| `PublicController`, `PublicWorldController`, `PublicPreviewController` | `Avalon.Api.Worlds/Controllers/` | Item and ability tooltips, the public world list, link previews |
| `LinkPreviewPage`, `LinkPreviewText`, `PreviewConfiguration`, `PublicSiteSettings` | `Avalon.Api.Worlds/Previews/` | Link preview HTML, its one-line description and its settings |
| `AccountCharactersService`, `WorldService`, `ObservabilityService` | `Avalon.Api.Worlds/Services/` | The cross-world views |
| `ApiStartup` | `Avalon.Api.Hosting/ApiStartup.cs` | The startup order (validation, world parse, startup checks, auth schema, world checks) |

The repositories themselves are the shared ones (`AddWorldRepositories`, `AddCharacterRepositories`), registered as
singletons as in every host. Only their context factories differ in the API, which is what makes them per world; see
[DbContext Lifetime](dbcontext-lifetime.md) for why a repository opens a context per call.

## Rules and invariants

- **A world id has one canonical form**: ASCII digits, 1 to 65535, no sign, no leading zero, no whitespace
  (`WorldDatabaseSettings.TryParseWorldId`). `01` and `1` would be two configuration keys and two routes naming one
  world, so `01` is refused everywhere, never read as world 1.
- **A world's id agrees in three places**: its auth `Worlds` row, the API's `Database:Worlds:<id>` key (whose strings
  must point at the databases that world server uses), and that world server's `Game:WorldId` (Helm
  `server.game.worldId`). The Redis keys that join the API to a world are named by it: `world:{id}:presence`,
  `presence:world:{id}:character:{characterId}`, `world:{id}:ready`, `world:{id}:scripts`, `world:{id}:reload` and
  `world:{id}:reload:result`.
- **There is no fallback world.** A repository used where no world is selected throws `InvalidOperationException`
  (`CurrentWorldDbContextFactory.NoWorldSelected`: "No world selected for this request: the world and characters
  databases are reached only under /world/{worldId}/, or through IWorldDbContextFactory for a named world."), which
  the exception middleware answers as a 500. Nothing reads a default world. (#523 proposed keeping the single
  `Database:World` pair as a fallback; the implementation dropped it, and the API reads no singular `Database:World`
  or `Database:Characters`.)
- **A route names a world exactly where it reads one.** Every route containing `{worldId` is on a `[WorldScoped]`
  or `[PublicWorldScoped]` controller, and no other route is (`WorldScopedEndpointsShould`). A route naming a world
  without the marker would read no world through no check; a marker on a route without one would select nothing.
- **Non-disclosure.** An unknown world, a world this API is not configured for and a world the caller may not enter
  all get the same empty 404, and the 404 always comes before any 503, so a restricted world is never revealed, not
  even while it is unavailable. The auth row is read before the configuration is asked, so an unconfigured world
  costs the same lookup as a restricted one.
- **The world rule is a mask test**, `AccessLevels.ForWorld(world.AccessLevelRequired).Allows(callerLevel)`, the same
  rule identity game admission applies to its world list and join tickets. Never compare levels with `>=`:
  `AccountAccessLevel` is `[Flags]`, and PTR (32) and Tournament (16) are numerically above Admin (4).
- **Character ids are unique only within one world** (each world has its own characters database). Anything that
  keys a character, in the API, Redis or a client, keys it by `(worldId, id)`, never by `id` alone (#556).
- **Connection strings never reach a log or a message.** A configuration refusal names the setting, never its value;
  a failure reading a world is logged with the world id and the exception type only, because a driver's message can
  carry hosts and ports.
- **The API never migrates a world's databases.** Each world server alone migrates its own World and Characters
  databases at its start (`WorldStartup.PrepareAsync`; homelab spec 2026-09-28-release-channels-design, D2). Worlds
  on different release channels can therefore run different schemas, while the API reads every world with the one EF
  model it was built with. Columns a world has and the model does not map are ignored (pinned for the creature
  columns #745 dropped, `TemplateEditShould.Read_and_save_a_creature_in_a_world_that_still_has_the_dropped_columns`);
  a column the model maps that a world's database lacks fails every query that reads it. Keep world schema changes
  readable by the API across the releases the worlds run.
  **Exception, #1142.** `StoreClassesAsNumbers` changes `ItemTemplates.AllowedClasses` from text (comma-joined class
  names) to `smallint[]`, and `CharacterClassNames.Class` from text to `smallint`. An API built on either side of
  that change cannot read the other side's `ItemTemplates`. For a world whose schema does not match the API's model,
  every read of an item template fails: the worlds service's item-template list, get and `PUT`, the public item
  tooltip and link preview, a character's inventory as soon as the character holds any item, and its abilities once
  it has an item equipped (the main hand's damage range is read from the template). The order is:
  merge it when no ramp is running; the dev worlds migrate themselves at their next start; cut an API release with
  this change straight away; and start any stopped world once, so it migrates too. To roll a world back to a build
  without the change, first run `dotnet ef database update SeedCrowdControl` against that world's World database.
  No world or API does that by itself.
  **Release order, #710.** `TrackRepeatedQuestCompletions` adds `LastCompletedAt` and `CompletionCount` to a world's
  `CharacterCompletedQuests`, and the API maps both (a character's quest log carries them). The columns are additive,
  so the worlds go first: each migrates its Character database at its next start (start any stopped world once, so
  it migrates too), and only then is an API release with the change cut. An older API reading a migrated world
  ignores the new columns; an API with the change reading a world that has not migrated fails that world's character
  quest log, the only API read of the table. The migration drops the columns' defaults after filling the stored rows,
  so a world build without the change, which writes neither column, fails every save that turns a quest in (the
  insert breaks the NOT NULL constraint and the save's transaction rolls back whole). To roll a world back to a build
  without the change, first run `dotnet ef database update GrantCrowdControlAbilities` against that world's Character
  database; that drops both columns, and with them each completion's count and last turn-in (its first turn-in,
  `CompletedAt`, stays). No world or API does that by itself.
  **Release order, #711.** `AddQuestChoiceRewards` adds the `QuestItemChoices` table to a world's World database
  (each quest's choose-one item set, seeded on quests 3 and 11, the latter new), and the API maps it: a quest
  template carries its `itemChoices`, and an item `PUT` that changes the flags checks the quests offering that item
  as a choice. The table is additive, so the worlds go first: each migrates its World database at its next start
  (start any stopped world once, so it migrates too), and only then is an API release with the change cut. An older
  API reading a migrated world ignores the new table; an API with the change reading a world that has not migrated
  fails that world's quest-template list and get, and its item `PUT` flag checks, the API's reads of the table. To
  roll a world back to a build without the change, run `dotnet ef database update RaiseHushProjectileSpeed` against
  that world's World database; that drops the table, and quest 11 with it. No world or API does that by itself.
  **Exception, #1173.** Gold is the one currency: `RenameMoneyToGold` renames a world's `Characters.Money` (Character
  database) to `Gold`, and `RenameRewardMoneyToRewardGold` renames `QuestTemplates.RewardMoney` (World database) to
  `RewardGold`, both in place with their values kept. The API maps both columns by name, so an API built on either
  side of the rename cannot read the other side's tables: an older API reading a migrated world fails with Postgres
  42703 (column `Money` or `RewardMoney` does not exist), and an API with the change reading a world that has not
  migrated fails the same way on `Gold` or `RewardGold`. For a world whose schema does not match the API's model, every
  read of a character row fails: identity game admission naming a character (`GameServerAllocator`, its owner check),
  the worlds service's `/world/{worldId}/character/...` routes (`CharacterService`), and the account's character list,
  which is contained and lists that world under `UnavailableWorlds`; and every quest-template read fails, the worlds
  service's quest-template list and get. Writes by column (`ExecuteUpdate`, as character consolidation does) and the
  item `PUT` flag checks (`TemplateItemUsers`) name neither column and are not affected. The order is: merge it when
  no ramp is running and after the last dev image has rolled; the dev worlds roll and migrate themselves at their next
  start (start any stopped world once, so it migrates too); get the commit onto PTR (trigger the nightly by hand); cut
  the release, API and world, straight away; the release-channel worlds migrate when they roll (start any stopped one
  once); only then the Dashboard and client follow-ups. Between a world migrating and an API with the change serving
  it, that world's reads above fail, so keep the gap short. To roll a world back to a build without the change, first
  run `dotnet ef database update TrackRepeatedQuestCompletions` against that world's Character database and
  `dotnet ef database update ItemsAllowEveryClassWhenEmpty` against its World database; both rename the columns back
  with their values. No world or API does that by itself.
  **Release order, #1178 (part A).** Damage types and the defence layers change only additively what the API reads. A
  world's World database migrates through `AddDefenceLayers` and then `ReexpressForestDefences`: `AbilityTemplates`
  gains `DamageType`, `CreatureTemplates` gains the four resistances, `EvasionModifier` and `WardModifier` (its base
  damage type stays in the `DmgSchool` column the API already maps), and `ItemTemplates` gains `RequiredStrength`,
  `RequiredAgility` and `RequiredIntellect`; the columns the migrations drop or rename (`CombatFormula`,
  `ClassStatFactors`, `CreatureRarityModifiers`) are on tables only the world reads. Its Character database migrates
  through `AddDefenceStats`: `CharacterStats` gains `Evasion`, `MaxWard` and the four resistances, and `CharacterAuras`
  gains `DamageType`. `CharacterStats.DodgePct` is retired but kept, still mapped: the world writes it 0 and the API no
  longer returns it (`CharacterStatsDto` drops `dodgePct`), so an older API reading a migrated world shows a dodge of 0, nothing
  worse. The stored item damage types move to the new numbering (Poison 1 became Blight 4, Fire 2 became 1); no
  seeded item deals anything but Physical, so no row the API reads changes. The worlds go first: each migrates at its
  next start (start any stopped world once, so it migrates too), and only then is an API release with the change cut.
  An older API reading a migrated world ignores the new columns, an ability's `DamageType` among them, and serves the
  migrated seed as it is: ability 210 renamed Spark Bolt (it was Arcane Bolt), and creatures 6 and 10 with `dmgSchool` 4
  (Blight), a value it reads but does nothing with. An API with the change reading a world that has not
  migrated fails that world's reads of the three template tables, a character's stats and its auras. To roll a world
  back to a build without the change, first run `dotnet ef database update RenameRewardMoneyToRewardGold` against its
  World database and `dotnet ef database update AddTradeRecords` against its Character database; the World rollback
  restores the dropped columns with their old seed values, and its `CK_AuraStatModifiers_Stat` again allows only stats
  1 to 10, so an aura modifier on a stat from 11 to 15 (none is seeded) must be removed first. No world or API does
  that by itself.
  **Exception, #1178 (part B).** The offence model is not additive: the owner chose no backward compatibility while
  the overhaul runs (2026-10-23), so its migrations drop and rename columns the released API maps, and nothing keeps
  them for it. A world's World database migrates through `AddOffenceModel` (`AbilityTemplates` gains `Tags`,
  `DamageEffectiveness`, `SpellBaseMin`, `SpellBaseMax`, `SpellBasePerLevelPct`, `ConvertToType`, `ConvertPct`,
  `AddedType`, `AddedMin` and `AddedMax`; `AuraTemplates` gains `PeriodicEffectiveness`; `ItemTemplates` gains
  `AttackTimeMs`, which #1179 part B2 drops again; `CombatFormula` gains the attribute conversions, `LifePerStamina`, `BaseCritPct`, the three speed caps
  and the unarmed range and attack time; aura modifiers gain stats 16-32 and the kind More), then
  `SplitAttackAndCastSpeed` (drops `CombatFormula.HasteCap`), then `ClassNeutralAttributes` (drops
  `ClassStatFactors.HpPerStamina` and `BaseCrit` and rebalances `ClassLevelStats`), then `RetireOldOffence` (drops
  `AbilityTemplates.EffectValue`, `ScalingStat`, `ScalingCoefficient` and `BaseDamageCoefficient`, the last three on
  `AuraTemplates` too, and `ClassStatFactors.AttackPerStrength`, `AttackPerAgility` and `AbilityPerIntellect`; renames
  `CreatureTemplates.DmgSchool` to `DamageType`; re-expresses the forest weapons' ranges and retires the old aura
  stats 2, 3 and 7). Its Character database migrates through `AddAuraCritBonus` (`CharacterAuras` gains
  `CritMultiplierBonus`), then `DropRetiredCharacterStats` (drops `CharacterStats.DodgePct`, `AttackDamage` and
  `AbilityDamage`). Each world applies them in that order at its start. `CombatFormula` is read by the API from this
  release on: the worlds service builds a character's advertised ability amounts from the world's formula row. The
  order is: the worlds go first, each migrating at its next start (start any stopped world once, so it migrates too),
  and then the API release with the change is cut. The released API is broken in between, which the owner accepted:
  an older API reading a migrated world fails with Postgres 42703 on the dropped or renamed columns, so every ability,
  aura and creature template read fails (the worlds service's template list, get and `PUT`, the public ability
  tooltips and link previews, a character's abilities) and so does a character's stats. An API with the change reading
  a world that has not migrated fails the same way on the new columns: every ability, aura, creature and item template
  read (a character's inventory as soon as it holds an item), a character's abilities (the formula's new columns) and
  its auras. To roll a world back to a build without the change, first run
  `dotnet ef database update ReexpressForestDefences` against its World database and
  `dotnet ef database update AddDefenceStats` against its Character database; the World rollback restores the dropped
  columns with their old seed values, and its `CK_AuraStatModifiers_Stat` again allows only stats 1 to 15 and its
  `_Kind` only Flat and Percent, so an aura modifier on a stat from 16 to 32 or of kind More added by hand (the seeded
  ones are handled) must be removed first; the Character rollback restores the three stats columns at 0 until each
  character's next save. No world or API does that by itself.
  **Migration order, #1179 (part A).** Bases and instance modifiers are not additive either (no backward
  compatibility, owner, 2026-10-23). A world's World database migrates through `ReplaceItemsWithBases`:
  `ItemTemplates.Rarity` becomes a `smallint` in the new numbering (`Common` 0, `Unique` 3, the only two a template may
  hold, `CK_ItemTemplates_Rarity`), `BaseKind` (`CK_ItemTemplates_BaseKind`) and `RequiresAnyAttribute` are added,
  `ItemPower` is dropped, and the equipment is reseeded as 74 bases and 5 uniques (items 4, 27-31 and 51-55 deleted,
  38 new rows 66-103), with the loot entries (table 14 new), vendor rows and quest rewards that name it. Its Character
  database migrates through `WipeOwnedEquipment`, which deletes every instance of the old equipment templates wherever
  it is (bags, banks and worn; slot rows by cascade; consumables and quest items stay), and then
  `AddItemInstanceModifiers` (`ItemInstances` gains `Rarity` and `ItemLevel`, existing rows 0 and 1; the tables
  `ItemInstanceModifiers` and `ItemInstanceModifierValues`; `TradeRecordItems` gains `Rarity` and `ItemLevel`; the
  tables `TradeRecordItemModifiers` and `TradeRecordItemModifierValues`). Each world applies them in that order at its
  start. The order is: the worlds go first, each migrating at its next start (start any stopped world once, so it
  migrates too), and then the API release with the change is cut. The released API is broken in between, which the
  owner accepted: an older API reading a migrated world fails with Postgres 42703 on the dropped `ItemPower`, so every
  item template read fails (the worlds service's item template list, get and `PUT`, the public item tooltips and link
  previews, a character's inventory as soon as it holds an item, and its abilities once it wears one). An API with the
  change reading a world that has not migrated fails the same way on the new columns: every item template read, and a
  character's inventory and abilities on the instance columns and the modifier tables. To roll a world back to a build
  without the change, first run `dotnet ef database update RetireOldOffence` against its World database and
  `dotnet ef database update DropRetiredCharacterStats` against its Character database. The World rollback restores the
  old items with `ItemPower` and the old rarity numbers, and the loot, vendor and quest rows that named them, and
  deletes items 66-103; the Character rollback drops the modifier tables and the new columns, and every modifier with
  them. The wiped equipment never comes back (`WipeOwnedEquipment` restores nothing), and an item of a template the
  World rollback deleted stays as a row whose template is gone. No world or API does that by itself.
  **Migration order, #1179 (part B).** The modifier catalog, implicits and rolling at drop are not additive either (no
  backward compatibility, owner, 2026-10-23). A world's World database migrates through `AddItemModifierCatalog` (the
  five catalog tables `ItemModifierGroups`, `ItemModifiers`, `ItemModifierStats`, `ItemModifierWeights` and
  `ItemTemplateModifiers`, seeded: 109 groups, 283 modifiers and every base's and unique's links, [item
  modifiers](item-modifiers.md)), then `MoveBaseStatsToImplicits` (each defence-bearing base's stat lines are cleared
  from its template: its defence is now its implicit modifier), then `SeedUniqueAndBossDrops` (the five uniques' loot
  entries on tables 6, 7 and 8, and table 15 "Forest boss gear", which table 8 rolls at 100 %). Its Character database
  migrates through `WidenModifierIds` (`ItemInstanceModifiers.ModifierId` and `TradeRecordItemModifiers.ModifierId`
  from `smallint` to `integer`), then `AddImplicitsToOwnedItems` (every owned copy of the 60 defence-bearing bases gets
  its implicit row at the drafted value, so none loses defence; a copy that already has modifier rows, made by a world
  running this part, is left as it is), then `AddRareName` (`ItemInstances.RareName` with `CK_ItemInstances_RareName`,
  and `TradeRecordItems.RareName` with `CK_TradeRecordItems_RareName`, the same rule). Each world applies them at its
  start, before it serves (`WorldStartup`: its Character database, then its World database); neither set reads the
  other database, so that order does not matter (the backfill carries the implicits' ids as plain values). The order
  is: the worlds go first, each migrating at its next start (start any stopped world once, so it migrates too), and
  then the API release with the change is cut. The released API is broken in between, which the owner accepted: an
  older API reading a migrated world serves the bases without their defence (the template no longer carries it, and
  that API cannot read the catalog), and its inventory read knows neither a copy's name nor its modifiers' kinds. An
  API with the change reading a world that has not migrated fails with Postgres 42P01 or 42703 on the missing catalog
  tables and column: the public item tooltips and link previews (`IItemModifierRepository`, which reads the one
  template's links and their rows) and a character's inventory (`RareName`, and the rows its items carry). To roll a
  world back to a build without the change, first run `dotnet ef database update ReplaceItemsWithBases` against its
  World database and `dotnet ef database update AddItemInstanceModifiers` against its Character database. The World
  rollback drops the catalog, puts each base's defence lines back on its template and removes the new loot entries and
  table 15; the Character rollback drops the rare names and their checks, deletes every `ItemInstanceModifiers` row
  naming an implicit (their values by cascade; a trade record's own copies, `TradeRecordItemModifiers`, keep theirs) and
  narrows the modifier ids back. A rolled copy keeps its rarity, item level and affix rows, and a unique copy its unique
  modifier row, which part A reads as stat lines like any other. No world or API does that by itself.
  **Migration order, #1179 (part B2).** Base stats, shared implicits, jewellery lines and the starting-zone values are
  not additive either (no backward compatibility, owner, 2026-10-23). A world's Character database migrates through
  `WipeOwnedEquipmentForBaseStats` first: it deletes every owned copy of the 79 equipment templates of the time (the
  74 bases and 5 uniques, 5-8, 12-26, 32-50, 58 and 64-103), in bags, banks and worn slots, with its slot, modifier and
  value rows by cascade (owner, 2026-10-24, as part A's `WipeOwnedEquipment`); consumables, scrolls and quest items
  stay, and the trade records are left as they are, still naming the wiped copies, their templates and their rolled
  rows as history (`TradeRecordItems` has no foreign key to an item). It is **irreversible**: its Down restores
  nothing. Then `AddItemInstanceBaseStats` (the tables `ItemInstanceBaseStats` and `TradeRecordItemBaseStats`, each
  keyed by item and stat, `CK_..._Stat` 0-6 and `CK_..._Value` at least 0). Its World database migrates through
  `AddItemTemplateBaseStats` (the table `ItemTemplateBaseStats`, `CK_ItemTemplateBaseStats_Stat` and `_Range`, seeded
  with every base's ranges), then `RetireDefenceImplicits` (part B's 60 per-base implicits, groups 50-109 and rows
  224-283, with their stat lines and links, deleted; their ids are never reused), then
  `RevalueAffixesForTheStartingZone` (`CK_ItemModifiers_Tier` widened to 1-9; the first three tiers renumbered T9, T8
  and T7 at item levels 1, 4 and 7 with the starting zone's values, the fourth and fifth tiers deleted, the families'
  weights narrowed to their slots, and the new family S14, group 110, rows 284-286), then
  `AddThemedImplicitsAndJewellery` (the 26 shared implicit groups 111-136, rows 287-364, one link per base; the ring
  and amulet lines and the two trinket bases, templates 104-151; `ItemTemplates.BaseTemplateId` with
  `CK_ItemTemplates_BaseTemplate` and a restricting foreign key to `ItemTemplates`, every unique pointed at its base
  and its own lines re-authored), then `DropTemplateWeaponColumns` (`DamageMin1`, `DamageMax1`, `DamageMin2`,
  `DamageMax2`, `DamageType2`, `AttackTimeMs` and `CK_ItemTemplates_AttackTimeMs` dropped, `DamageType1` renamed
  `DamageType`). Each world applies them at its start, before it serves (`WorldStartup`: its Character database, then
  its World database); neither set reads the other database. The order is: the worlds go first, each migrating at its
  next start (start any stopped world once, so it migrates too), and then the API release with the change is cut. The
  released API is broken in between, which the owner accepted: an older API reading a migrated world fails with
  Postgres 42703 on the dropped and renamed template columns, so every item template read fails (the worlds service's
  item template list, get and `PUT`, the public item tooltips and link previews, a character's inventory as soon as it
  holds an item, and its abilities once it wears one). An API with the change reading a world that has not migrated
  fails with Postgres 42P01 or 42703 on the missing tables and columns: every item template read (the base-stat ranges
  load with each template, `BaseTemplateId`, `DamageType`), and a character's inventory and abilities on
  `ItemInstanceBaseStats`. To roll a world back to a build without the change, first run
  `dotnet ef database update SeedUniqueAndBossDrops` against its World database and
  `dotnet ef database update AddRareName` against its Character database. The World rollback restores the template
  columns with the weapons' part B values, drops `BaseTemplateId`, the new implicits and templates 104-151, restores the
  old unique lines, the fourth and fifth tiers and the old values, and puts part B's 60 implicits and their links back;
  the Character rollback drops both base-stat tables, every copy's and every trade record's base stats with them. The
  wiped equipment never comes back. A copy made by a world running this part keeps its implicit row, whose modifier
  the rolled-back catalog no longer holds (null in the inventory read), and a copy of templates 104-151 stays as a row
  whose template is gone. No world or API does that by itself.
  **Migration order, #1179 (part C).** Identification is not additive either (no backward compatibility, owner,
  2026-10-23). A world's Character database migrates through `AddItemIdentified`: `ItemInstances.Identified` and
  `TradeRecordItems.Identified` (`boolean NOT NULL`), each added with a default of true, so every row that exists
  reads identified, then the default is dropped, so every insert must write the value; then the checks
  `CK_ItemInstances_Identified` and `CK_TradeRecordItems_Identified` (`"Rarity" <> 0 OR "Identified"`). Its World
  database migrates through `AddResonanceWorkbench`: `DoodadTemplates.Service` (`smallint`, nullable, null on every
  existing doodad) with `CK_DoodadTemplates_Service` (`"Service" IS NULL OR "Service" = 1`), doodad template 7, the
  Resonance Workbench, and Glimmerdell's fixed doodad spawn row 2, which places it. Each world applies them at its
  start, before it serves (`WorldStartup`: its Character database, then its World database); neither reads the other
  database. The order is: the worlds go first, each migrating at its next start (start any stopped world once, so it
  migrates too), and then the API release with the change is cut. The released API is not broken in between, but it
  shows too much: an older API reading a migrated world ignores the new column, so its inventory read names an
  unidentified copy and lists every modifier it rolled, to the owner and to staff, until the API release lands (the
  API reads no doodad table). An API with the change reading a world that has not migrated fails with Postgres 42703
  on the missing `Identified` column: a character's inventory as soon as it holds an item, and its abilities once it
  wears one. To roll a world back to a build without the change, first run
  `dotnet ef database update AddItemInstanceBaseStats` against its Character database and
  `dotnet ef database update DropTemplateWeaponColumns` against its World database. The Character rollback drops both
  checks and both columns, and with them whether any copy or trade record's copy was identified (the older build
  has no such state and shows every copy whole); the World rollback drops
  the check, deletes spawn row 2 and template 7, and drops `Service`. No world or API does that by itself.
  **Migration order, #1180 (part A1).** Resonance themes are additive. A world's World database migrates through
  `AddItemThemes`: the table `ItemThemes` (`Id`, `Name` `varchar(40)` with the unique index `IX_ItemThemes_Name`),
  seeded with the eight themes; then `ItemModifierGroups.ThemeId` (`integer`, nullable), set on the 45 affix groups;
  its index, the check `CK_ItemModifierGroups_Theme` (`("Kind" IN (1, 2)) = ("ThemeId" IS NOT NULL)`: a prefix or
  suffix group has a theme, any other none) and its foreign key to `ItemThemes` (`Restrict`). There is no Character
  migration: an item stores its modifier ids, and their themes are read from the catalog. Each world applies it at its
  start, before it serves (`WorldStartup`). The order is: the worlds go first, each migrating at its next start (start
  any stopped world once, so it migrates too), and then the API release with the change is cut. An older API reading
  a migrated world ignores the new column and shows no themes (no `resonance` on `/stats`, no `themeId` on the
  inventory read's modifiers). An API with the change reading a world that has not migrated fails every read of the
  modifier catalog with Postgres 42703 on the missing `ThemeId` column: the public item tooltips and link previews, the
  item template edit's checks, a character's inventory as soon as it holds an item with a modifier, and its stats once
  it wears one; which is why the worlds go first. To roll a world back to a build without the change, run
  `dotnet ef database update AddResonanceWorkbench` against its World database: it drops the foreign key, the check,
  the index, the column and `ItemThemes`. Nothing is lost that a copy holds. No world or API does that by itself.
  **Migration order, #1180 (part A2).** Resonance effects are additive. A world's World database migrates through
  `AddResonanceEffects`: the table `ItemThemeEffects` (its eight checks, `CK_ItemThemeEffects_Count`, `_Trigger`,
  `_Target`, `_ChancePct`, `_CooldownMs`, `_ChainTargets`, `_AtOrBelowLifePct` and `_DamageType`, its foreign keys to
  `ItemThemes` and `AuraTemplates`, both `Restrict`, and the unique index `IX_ItemThemeEffects_Theme_Count_Trigger`),
  seeded with the 29 effect rows; the 29 effect auras, `AuraTemplates` 16-44; and their 26 `AuraStatModifiers`. There
  is no Character migration: a character's procs and cooldowns live in memory only. Each world applies it at its
  start, before it serves (`WorldStartup`). The API reads no effect row, and lists the new auras in its aura template
  routes (`/world/{worldId}/aura-template`) once a world has migrated, whichever API release reads them, so the order
  between the worlds and the API does not matter. To roll a world back to a build without the change, run
  `dotnet ef database update AddItemThemes` against its World database: it drops `ItemThemeEffects` and deletes the 26
  modifiers and the 29 auras. A character saved holding one of those auras restores without it on the older build
  (an aura no longer loaded is dropped at select, with a warning). No world or API does that by itself.
  **Migration order, #1180 (part B).** Awakening is additive. A world's Character database migrates through
  `AddModifierSeals`: two nullable columns, `ItemInstanceModifiers.SealProgress` and
  `TradeRecordItemModifiers.SealProgress` (`integer`), null on every existing row; two checks,
  `CK_ItemInstanceModifiers_SealProgress` and `CK_TradeRecordItemModifiers_SealProgress`
  (`"SealProgress" IS NULL OR "SealProgress" >= 0`); and the partial unique index `IX_ItemInstanceModifiers_Sealed`
  (`ItemInstanceId`, where `"SealProgress" IS NOT NULL`: at most one seal per item). There is no backfill, so no rare
  that already exists is sealed. There is no World migration: the threshold and the reach are code
  (`ItemAwakening`), the weights, the level gap and the tier-up chance are world configuration, and binding is the
  existing `Attuned` bit of `ItemInstances.Flags`. Each world applies it at its start, before it serves
  (`WorldStartup`). The order is: the worlds go first, each migrating at its next start (start any stopped world once,
  so it migrates too), and then the API release with the change is cut. The API reads the new column (the inventory
  read's `sealProgress` and `sealThreshold`, and `/stats`, which leaves a sealed affix out of the stats and the
  Resonance counts), so it needs the migration applied before a release carrying this part serves: an API with the
  change reading a world that has not migrated fails with Postgres 42703 on the missing `SealProgress` column wherever
  it reads an item's modifiers, a character's inventory and its stats. An older API reading a migrated world ignores
  the column: until the API release lands, it shows a sealed affix as an ordinary line, counts it in `/stats` and its
  `resonance`, and has no `attuned`. To roll a world back to a build without the change, run
  `dotnet ef database update AddItemIdentified` against its Character database: it drops the index, both checks and
  both columns, and with them every seal, so a sealed affix becomes an ordinary awake affix with the values it rolled,
  and every partial bar is lost. A copy that woke keeps its `Attuned` bit, which the older build reads for no rule, so
  it trades again. No world or API does that by itself.
  **Migration order, #1180 (part C).** Extraction is additive. A world's World database migrates through
  `AddGlyphTemplate`, which inserts item template row 152, the Glyph (`ItemClass.Crafting`, `ItemSubClass.Glyph` 401,
  priced 0), and nothing else. Its Character database migrates through `AddImprintsAndInstability`: six `NOT NULL`
  columns, each added with a default that fills every existing row with 0 or false and is then dropped, as the model
  declares none, so no insert can leave a column out: `ItemInstances.Instability` and `TradeRecordItems.Instability`
  (`smallint`, 0), `ItemInstances.Tainted` and `TradeRecordItems.Tainted` (`boolean`, false), and
  `ItemInstanceModifiers.Imprinted` and `TradeRecordItemModifiers.Imprinted` (`boolean`, false); and four checks,
  `CK_ItemInstances_Instability` and `CK_TradeRecordItems_Instability` (`"Instability" >= 0`), and
  `CK_ItemInstances_Tainted` and `CK_TradeRecordItems_Tainted` (`"Tainted" = ("Instability" >= 100)`).
  There is no backfill: no existing copy is imprinted, unstable or Tainted.
  Each world applies both at its start, before it serves (`WorldStartup`). The order is: the worlds go first, each
  migrating at its next start (start any stopped world once, so it migrates too), and then the API release with the
  change is cut. The API reads the new columns (the inventory read loads each `ItemInstance` whole, `Instability`
  included, though it maps only `tainted` and each line's `imprinted`), so both migrations must be applied before a
  release carrying this part serves: an API with the change reading a world that has not migrated fails with Postgres
  42703 on the missing columns wherever it reads a character's items, its inventory and its stats. An older API
  reading a migrated world ignores the columns: until the API release lands, it shows no `tainted` or `imprinted`, and
  lists template 152 with a subclass its contract does not name. To roll a world back to a build without the change,
  run `dotnet ef database update AddModifierSeals` against its Character database, which drops the four checks and the
  six columns, and with them every item's instability, every Tainted mark and every imprinted mark (an imprinted affix
  becomes an ordinary line with its values, and a Tainted copy an ordinary one, whose `Attuned` bit stays cleared), and
  `dotnet ef database update AddResonanceEffects` against its World database, which deletes template 152. The two
  databases share no foreign key, so a glyph already made stays in its owner's items, naming a template the older
  build does not hold. No world or API does that by itself.
  **Migration order, #1181 (part A).** Mana for every class is not additive (no backward compatibility, owner,
  2026-10-23). A world's World database migrates through `ManaFormula`, then `CostsInMana`. `ManaFormula` adds
  `CombatFormula.ManaBase` and `ManaPerLevel` (`bigint`, seeded 100 and 8) and `ManaPerIntellect` (`real`, seeded 4)
  with their checks `CK_CombatFormula_ManaBase`, `_ManaPerLevel` and `_ManaPerIntellect`, which the API reads (the
  worlds service loads the formula row for a character's advertised ability amounts), and drops the table
  `ClassStatFactors` and the column `ClassLevelStats.BaseMana`, which only the world reads. `CostsInMana` drops
  `AbilityTemplates.CostPowerType` and `PowerGainPerHit`, with their checks `CK_AbilityTemplates_CostPowerType` and
  `CK_AbilityTemplates_PowerGainPerHit_NonNegative`, which the released API maps, and moves item 2, the Mana Potion, to
  the cooldown group `mana-potion`. There is no Character migration. Each world applies both at its start, before it
  serves (`WorldStartup`). The order is: the worlds go first, each migrating at its next start (start any stopped world
  once, so it migrates too), and then the API release with the change is cut. The released API is broken in between,
  as the owner accepted for #1178 part B: an older API reading a migrated world fails with Postgres 42703 on the
  dropped ability columns, so every ability template read fails (the worlds service's ability template list, get and
  `PUT`, the public ability tooltips and link previews, and a character's abilities). An API with the change reading a
  world that has not migrated fails the same way on the formula's new columns: a character's abilities. To roll a
  world back to a build without the change, run `dotnet ef database update AddGlyphTemplate` against its World
  database: it restores `ClassStatFactors`, `ClassLevelStats.BaseMana`, `CostPowerType` and `PowerGainPerHit` with
  their seed values and checks (a seeded row a live edit gave a cost while it was free goes on Mana, so the restored
  check holds), puts the Mana Potion back in the `potion` group, and drops the three formula columns. No world or API
  does that by itself.
  **Migration order, #1181 (part B1).** The talent tree as data is additive. A world's World database migrates through
  `AddTalentTree`: the six tree tables, `TalentForkGroups` (empty), `TalentNodes`, `TalentEdges`, `TalentNodeStats`,
  `TalentNodeAbilities` and `TalentClassStarts`, with their checks, keys and indexes, seeded with the first region
  (305 nodes, 345 edges, 286 stat lines, 23 ability links, four class starts); `QuestTemplates.RewardTalentPoints` and
  `AbilityTemplates.MaxTargets` (`integer`, default 0, with `CK_QuestTemplates_RewardTalentPoints` and
  `CK_AbilityTemplates_MaxTargets`, each at least 0), the first set to 1 on quests 3, 7 and 8; and two ability rows,
  319 Heavy Strike and 320 Frostbolt. There is no Character migration: no character holds a node yet, nor either new
  ability. Each world applies it at its start, before it serves (`WorldStartup`). The order is: the worlds go first,
  each migrating at its next start (start any stopped world once, so it migrates too), and then the API release with
  the change is cut. An older API reading a migrated world ignores the new tables and columns. An API with the change
  reading a world that has not migrated fails with Postgres 42703 on the missing columns, every ability template read
  and every quest template read, and with 42P01 on the public talent tree; which is why the worlds go first. To roll a
  world back to a build without the change, run `dotnet ef database update CostsInMana` against its World database:
  it drops the six tables, the two columns and their checks, and deletes abilities 319 and 320. No world or API does
  that by itself.

## How a request reaches its world

The pipeline every API process runs (`ApiPipeline`) has `UseRouting`, CORS, `UseAuthentication`, the services' hooks
after it (in a process running identity, the game workload authentication), the request rate limiter
(`UseApiRateLimiting`, #561), then `WorldRouteMiddleware`, then `UseAuthorization`. The world middleware is added only
in a process that runs a service with world routes, which only worlds has. The world check sits behind the rate
limiter, so a flood of refused world requests is limited too, and before authorization, so an unknown world is a 404
whatever the endpoint's role policy.

On a `[WorldScoped]` endpoint the middleware does, in order:

1. **An endpoint that allows anonymous callers** answers the same empty 404. Authorization would let an anonymous
   caller through to an action with no world selected, so this fails closed. No world endpoint allows anonymous
   callers (`WorldScopedEndpointsShould.Let_no_anonymous_caller_onto_a_world_endpoint`).
2. **It authenticates** through the default policy's schemes (the JWT bearer and the `Avalon` personal access token),
   so a personal access token is checked exactly like a JWT. The handlers cache their result, so the account is still
   loaded once per request. A caller that is not authenticated passes on, and authorization answers 401 as
   everywhere.
3. **A world id not in canonical form** is an empty 404, with nothing looked up. The routes use `{worldId:int}`, so a
   segment that is not an integer matches no endpoint at all and is the router's 404.
4. **The auth row is read.** No row, the caller fails the world rule, or the id is not under `Database:Worlds`: the
   same empty 404 each time.
5. **A world whose databases failed the startup check** answers 503 ProblemDetails (`Type` `ServiceUnavailable`,
   `Title` "World unavailable", `Detail` "World {id} is unavailable").
6. **Otherwise** it selects the world on the scoped `CurrentWorld` (the id and the auth row's name) and the endpoint's
   role policy decides the rest (403 for a role the endpoint does not admit).

A `[PublicWorldScoped]` endpoint goes through steps 3 to 6 with the caller's level from `PublicCaller` (see
[Public routes](#public-routes)). A request on any other endpoint passes straight through.

Behind the selection, `CurrentWorldDbContextFactory<T>` reads `ICurrentWorld` from the request's services through
`IHttpContextAccessor` and opens that world's context through `IWorldDbContextFactory`, so concurrent requests on
different worlds each read their own (`CurrentWorldContextsShould.Keep_concurrent_requests_in_their_own_worlds`).
The per-world contexts follow the rule every context follows for sensitive data logging (#558): on only when the host
environment is Development, never turned on by configuration, and off for every context of a process that runs
commerce with checkout enabled (the commerce registration post-configures `DatabaseConfiguration`).

## Startup and availability

`ApiStartup.ValidateAndMigrateAsync` runs, in order:

1. `IStartupValidator.Validate()`: the options validations, `Database:Auth:ConnectionString` among them
   (`ValidateDatabasesOnStart(DatabaseConnections.Auth)`), and, where worlds runs, `Application:Templates`.
2. In a process that reads a world database, it resolves `WorldDatabases`, which parses `Database:Worlds` for the
   parts the process reads and stops startup, naming the setting and never its value, for no world, an id not in
   canonical form, a world missing a string the process reads, or a blank string. A process running worlds needs both
   strings of every world and one running identity without worlds only the Characters strings; a string the process
   does not read is ignored, and a process running only commerce or distribution reads no `Database:Worlds` at all.
3. The services' startup checks (`IApiStartupCheck`; identity's are the store authentication and the Steam web link).
4. The auth schema (`AuthSchemaGate`): a process running identity migrates the auth database, and a failure stops it;
   any other waits until no migration its build knows of is pending ([API services](api-services.md#the-auth-schema)).
5. `ApiDatabaseMigrator.CheckWorldsAsync`: for each configured world in id order, its World database is checked with
   `Database.CanConnectAsync` where the process reads it, and its Characters database where the process reads it and
   the World database answered (or was not read). A world that cannot be reached, or whose check throws, is marked
   `Unavailable` and logged at Error with its id (and the exception's type, when one was thrown). Every world's status
   is then logged at Information. When a world is unavailable, `ApiStartup` hands the worlds to `WorldDatabaseRecheck`
   (below).

`Database:Worlds` is deliberately not an options validation: OpenAPI generation starts the host with no world
configured (`AVALON_OPENAPI_GENERATION_ONLY=true`) and skips `ApiStartup`, and `WorldDatabases` is built only when
first resolved, so generating the document needs no world.

What follows from it:

- **An unavailable world is checked again until it answers.** `WorldDatabaseRecheck`, a hosted service of every
  process that reads a world database, runs only when the startup check left a world unavailable. It waits 5 seconds,
  then checks each unavailable world's databases the same way (`ApiDatabaseMigrator.RecheckUnavailableAsync`), and
  marks available each that answers, logging it at Information ("World 1 is available"); a world still unreachable
  is logged at Debug only. Each wait is twice the last, at most a minute, until every world is available or the host
  stops. It runs off every request path: a request only reads the status, which the recheck writes.
- **Available is final for the life of the process.** A world whose databases fail later is not marked; its requests
  fail as database errors, which the exception middleware answers as 503 (`DbException`), and it stays `Available`.
- **Each unreachable world adds the driver's connect timeout to startup.**
- **A database that does not exist yet counts as unreachable.** On a fresh install the world server creates its
  databases at its first start; an API started before that (the Aspire AppHost starts the API first, and the auth and
  world servers wait for it) answers 503 for that world until the recheck finds them, within about a minute of their
  creation. The check is reachability only, as at startup: a world whose databases exist but are still being
  migrated counts as available.

## Routes

World-scoped routes (`[WorldScoped]`). They, and every other route on this page, are the worlds service's. Character
reads admit the owner or a game master (`CharacterReadHandler`), character writes the owner or an admin
(`CharacterWriteHandler`).

| Route | Verbs | Policy | Notes |
|---|---|---|---|
| `/world/{worldId}/character/paginate` | GET | GameMaster | |
| `/world/{worldId}/character/{id}` | GET, PATCH | Player | Owner, or game master (read) or admin (write) |
| `/world/{worldId}/character/{id}/inventory`, `/abilities`, `/stats`, `/quests`, `/auras` | GET | Player | Owner or game master. An inventory item carries its copy's `rarity`, `itemLevel`, `name` and `modifiers` (each `modifierId` with its `kind`, `Prefix`, `Suffix`, `Unique` or `Implicit` from the world's modifier catalog, null for a modifier the catalog no longer holds, its `themeId`, the Resonance theme of a `Prefix` or `Suffix` line, null on an `Implicit` or `Unique` line and when the catalog no longer holds the modifier (#1180), its `sealProgress` and `sealThreshold`, set exactly on a sealed affix of an identified copy (its bar's points and `ItemAwakening.Threshold` of the copy's item level) and null on an awake one (#1180, part B); `sealProgress` is the bar as the character's last save left it, up to one periodic save behind the bar in the world, and its `values`: `stat`, `value` and, on an "adds X to Y" line, `value2`), in the order the item shows them (#1179), and `baseStats`, the base stats the copy rolled (`stat`, an `ItemBaseStat` name, and `value`), by stat, empty for a copy that has none (#1179, part B2); `name` is an identified magic copy's prefix and suffix around the template's name or an identified rare copy's two words, null where the template's name is the item's; that is the only rarity the read shows, and its `template` carries none. `identified` (#1179, part C) says whether the copy is identified: an unidentified one (magic, rare or unique) has `name` null and only its `Implicit` modifiers (a modifier the catalog no longer holds is left out), with its `rarity`, `itemLevel` and `baseStats` as they are, for every caller, the owner and staff alike (`CharacterService`, which takes no caller input, by the rule the world's messages follow, `ItemVisibility`), so it names no theme of an affix it hides, and says nothing of a seal. `attuned` (#1180, part B) says whether the copy is attuned, from its `Flags`: one of its affixes woke, and it can no longer be traded. `tainted` (#1180, part C) says whether an imprint bricked the copy, which can then never be modified or attuned again, and each modifier line the read shows has `imprinted`, whether that affix came from a glyph (false on every line of an unidentified copy); the hidden instability that bricks an item is loaded with the item and never mapped, for any caller, staff included. `/stats` also returns `resonance` (#1180): each Resonance theme the worn items carry, as `themeId` and `count` (how many worn items carry at least one of its prefixes or suffixes, each counted once per theme), a theme at 0 not listed; it is counted on each read from the saved inventory, the items in a slot that fits them and the catalog rows they carry, through the step the world's stats refresh takes (`CharacterStatsCalculator.CountResonance`), and is never saved, so it follows the equipment as the character's last save left it |
| `/world/{worldId}/map-template`, `/{id}` | GET | Player | |
| `/world/{worldId}/map-template/{id}/preview-layout` | GET | GameMaster | Runs the procedural generator against that world's data; see [Map Generation](map-generation.md) |
| `/world/{worldId}/map-template/chunk-asset/{*filename}` | GET | GameMaster | Whitelisted against the world's `ChunkTemplate.GeometryFile` values; the bytes come from the API's own copy of `Maps/` (`Application:MapAssets:ChunkAssetRoot`), not from the world |
| `/world/{worldId}/item-template`, `/ability-template`, `/creature-template`, `/aura-template` (and `/{id}`) | GET | Player | Each row carries `version` and `editable`; see [Live Template Editing](live-template-editing.md) |
| `/world/{worldId}/item-template/{id}` (and ability, creature, aura) | PUT | Admin | [Live Template Editing](live-template-editing.md) |
| `/world/{worldId}/quest-template`, `/{id}` | GET | GameMaster | Read-only |
| `/world/{worldId}/observability/character/{id}` | GET | GameMaster | One character's live presence, from that world's keys only |
| `/world/{worldId}/scripts` | GET | GameMaster | The script names the world has published (`world:{id}:scripts`); see [Live Template Editing](live-template-editing.md) |

The map, item, ability, creature, aura and quest template lists are paged with `page` (below 1 reads as 1) and
`pageSize` (outside 1 to 50 reads as 50). In the OpenAPI document `worldId` is a required path parameter
(`WorldRoutesOpenApiShould.Mark_worldId_a_required_path_parameter` pins it route by route).

Routes that span worlds, or read only the auth database, take no `worldId`: `GET /character`, `GET /world`,
`GET /world/{id}` (the auth world lookup, which shares the prefix but is not world-scoped), the world create, update,
maintenance and player limit (`PUT /world/{id}/player-limit`, [World player limit](world-player-limit.md)) endpoints,
`GET /observability/online` and `GET /observability/instance/{instanceId}`.
`WorldRoutesOpenApiShould.Leave_no_world_content_route_outside_a_world` fails if a world content route reappears
outside `/world/{worldId}/`.

## Public routes

`[PublicWorldScoped]` is the anonymous-friendly variant, for the public site's item and ability tooltips, the public realm board and the talent tree (#1181):

| Route | Answer |
|---|---|
| `GET /public/world` | `{ defaultWorldId, worlds: [{ id, name, type, status, population, queueLength, maintenanceDeadlineUtc, lore }] }`: the configured, available worlds the caller may read, in id order, each with its live state. `Cache-Control: private, no-cache`, since the list depends on the caller |
| `GET /public/world/{worldId}/item/{id}` | `PublicItemDto` (`PublicItems.ToPublicAsync`); its `baseStats` are the base-stat ranges every copy rolls within (#1179, part B2: `stat`, `min`, `max`, by stat; a weapon's attack time in milliseconds before attack speed), the item's own or, for a unique, its base's; `damageType` is a weapon's damage type (null otherwise), and `baseItemId` the base a unique names (null for any other item); its `modifierSpans` are the lines every copy rolls within (#1179): a base's implicit, or a unique's base's implicit and then its own modifier, each `kind`, `stat`, `min`, `max` and, on an "adds X to Y" line, `min2` and `max2`, from the world's modifier catalog (`IItemModifierRepository`). The template's `damage` and `attackTimeMs` are gone |
| `GET /public/world/{worldId}/ability/{id}` | `PublicAbilityDto` |
| `GET /public/world/{worldId}/talents` | `TalentTreeDto` (#1181, `GetPublicTalentTree`): the world's talent tree as its World database holds it (`ITalentTreeRepository.GetTreeAsync`, `TalentMapping.ToDto`), whether or not the world's catalog accepted it: `nodes` by id, each its `id`, `kind` (the string enum `TalentNodeKind`), `name` (null on a small node or a junction), `angle` (degrees, in (-180, 180]), `radius` (layout units), `forkGroupId`, `abilityId` (the ability a start or an ability node grants, else null) and `stats` by stat then kind (`stat`, `kind` `Added` or `More`, `value`, `valueMax`, set only on an "adds X to Y" line); `edges` once each, the smaller id first (`nodeA`, `nodeB`); `classStarts` by class (`class`, `nodeId`). The same cache headers as the tooltips |

- **The caller** (`PublicCaller.AccessLevelAsync`) is authenticated through the default policy: a signed-in caller
  reads with its own access flags plus Player, and an anonymous caller, or one whose token is not valid, counts as a
  Player. So anyone reads a world every player may enter, and only a caller who may enter a restricted world reads
  it. A world the caller may not read is the same empty 404, before any 503, through the same selection as above.
- **Caching:** an answer from a world every player may enter is `Cache-Control: public, max-age=300`; one from a
  world only this caller may enter is `private, max-age=300` (the middleware stores which in
  `HttpContext.Items[PublicWorldScopedAttribute.OpenToEveryoneItem]`).
- **Live fields** (`GET /public/world`): `status` and `queueLength` come from the same `WorldLiveState` read as the
  admin list. `population` is a band (`Low`, `Medium`, `High`, `Full`), never a count: `Full` iff the status is
  Full, null when the world is Offline, in Maintenance or has no capacity, where capacity is the world's player
  limit, else `Application:PublicPopulationReference`. `maintenanceDeadlineUtc` is set only while maintenance is
  enabled (a scheduled cut-off shows with the status still Online); a stale deadline on a world whose maintenance
  is off is null. `lore` is the world's lore text. Null properties are omitted from the JSON. If Redis cannot be
  read, every world reads Offline with `queueLength` 0 and the answer is still 200.
- **What it never returns:** host, port, `MinVersion`, `Version`, the exact online count, the player limit or the
  maintenance revision. The endpoint is anonymous, so these stay in the Player-only `GET /world`. Live state is read
  only for worlds the caller may read, so a staff-only world (Development, PTR) leaks nothing to anonymous callers.
- **The default world** (`PublicWorlds.DefaultOf`) is `Application:PublicWorldId` when the caller may read it, else
  the first readable world, else none.
- **GET only:** every `[PublicWorldScoped]` endpoint is a single-verb GET under `public/world/`
  (`WorldScopedEndpointsShould.Keep_public_world_endpoints_apart_from_player_world_endpoints`).
- **Rate limiting:** the public endpoints carry no authorization requirement, so the request rate limiter does not
  look up a personal access token there: such a request counts against its source address's anonymous budget, and
  `PublicCaller` authenticates the token afterwards, outside the limiter's failed-lookup budget. A JWT, which
  `UseAuthentication` validates on every request, counts against its account as anywhere else.

### Link previews (#308)

`PublicPreviewController` serves `GET /public/preview/item/{id}` and `GET /public/preview/ability/{id}`: a small
HTML page with no scripts, for the link-preview bots of chat services, which run no JavaScript (the public site's
nginx sends those bots here). The world is `?world=N`, or else the default world `GET /public/world` would pick for
this caller. The route names no world, so the controller applies the selection order itself, through the shared
`PublicWorlds`: a world id not in canonical form, a world the caller may not read or that is not configured, or a
missing template is a 404 page; a 503 page only for a readable world whose databases failed. It then selects the world
on `CurrentWorld` itself, so the ordinary repositories read it.

- **The page** (`LinkPreviewPage.Render`, `text/html; charset=utf-8`): `<title>`, `description`, `og:site_name` (when
  `Application:Previews:SiteName` is set), `og:type` `website`, `og:title`, `og:description`, `og:url` (only when
  `Application:PublicSiteUrl` is set), `theme-color` (only for a valid `#RRGGBB` colour), `twitter:card` `summary`, and
  a body with one link. Every value from game data or configuration is HTML-encoded.
- **The link** is `<PublicSiteUrl>/item/{id}` (or `/ability/{id}`), with `?world=N` only when the request named a
  world. With no `PublicSiteUrl`, `og:url` is left out and the link is relative.
- **The description** (`LinkPreviewText.Describe`) reads like the site's tooltips: for an item its rarity, slot,
  subclass, the base-stat ranges every copy rolls within, in `ItemBaseStat` order (#1179, part B2, below: `6-10 Armor`
  on the Ironbound Chestguard, `2-4 to 6-8 Physical Damage` and then `0.67-0.74 sec attack time` on the Ironwood
  Sword, before attack speed), the lines every copy rolls within (#1179, `ModifierSpans`: `+1-2% increased Health`, the
  Ironbound Chestguard's implicit, `+4-5 Health` and `Adds 1 to 3 Physical Damage to Attacks` on a unique), armour,
  other stats, required level, required attributes (`Requires 12 Strength`, #1178; one part, `Requires 30
  Strength or 30 Intellect`, when any one is enough, #1179) and allowed classes (only when restricted to some; an
  empty list means every class, #1171); for an ability its cost, cast time, cooldown and what one hit deals or heals
  (#1178): an Attack the talent tree grants (#1181, `PublicAbilityDto.Granted`) as its weapon damage (`315 % of weapon
  damage`, then `converts 50 % to cold` or `adds 2-4 blight damage`; a creature's, which the tree does not grant, as
  its natural damage, and any attack's type when it is not
  physical: `240 % of its natural damage, blight damage`), a Spell or a Heal as its level-1 base and growth (`14-20
  lightning damage, +0.7 % per level`, `Heals 30-44`);
  an ability with no direct amount (one that only applies an aura) shows its damage type instead (`Fire damage`).
  Templates have no description or flavour text. Parts are joined by ` · `, collapsed to one line and capped at 200
  characters with an ellipsis.
- **Item wording shared with the tooltip** (#1189): `LinkPreviewText.FormatStat` writes armour as `8 Armor`; the
  `…Pct` stats, the four resistances, `CritMultiplier`, `AttackSpeed`, `CastSpeed`, `CooldownRecovery` and
  `MovementSpeed` (the world adds it to `MovementSpeedPct`) as percent points (`+2% Crit`, `+5% Fire Resistance`,
  `+5% Movement Speed`); the `Increased…` stats as `+10% increased Fire Damage`; the rest, Evasion and Ward among
  them, as a plain rating (`+12 Evasion`); a negative value keeps its own sign (`-3% Cold Resistance`, `-2 Strength`).
  Times (a base's attack time, `LinkPreviewText.FormatBaseStat`, and an ability's cast time and cooldown) are seconds to
  the hundredth, a half rounded up and computed in decimal so a tie is exact, with no trailing zeros (`2.6 sec attack
  time`, `2 sec`, `0.78 sec`, `1005` ms as `1.01 sec`; the tie cases are pinned here once Avalon.Dashboard#462 adds them
  to the shared file). In JavaScript the shared rule is `Math.round(ms / 10) / 100`, which the public site adopts
  through Avalon.Dashboard#460. An item without an attack time says none (a base's starts at 1 ms). The one wording
  table is the Dashboard's
  `packages/shared/src/items/stat-wording.json` (a row per `StatType`, negative values, attack times), which the
  site's formatter tests against; a verbatim copy sits in
  `tests/Avalon.Api.Worlds.UnitTests/Worlds/stat-wording.json`, refreshed from there and never edited here.
  `StatWordingShould` fails when a `StatType` member has no row, or a row names no stat, or the preview writes a row's
  text differently, so a new stat fails until the shared file and both formatters handle it.
- **Ranges a copy rolls within** (#1179): `LinkPreviewText.FormatSpan` words a modifier span as `FormatStat` words the
  stat, with the range for its number (`7-9 Armor`, `+24-36 Health`, `+3-5% Crit`; one number when both ends are
  equal), and an "adds X to Y" line with both its ranges (`Adds 2 to 3-5 Physical Damage to Attacks`). The wording
  file's `spans` rows (`type`, `min`, `max`, and `min2` and `max2` on a two-range line, with the `text`) hold it, and
  `StatWordingShould` checks each. These rows run ahead in the copy here until the Dashboard's file takes them; the
  copy is then checked byte-identical again.
- **Base-stat ranges** (#1179, part B2) are worded before the spans, in `ItemBaseStat` order:
  `LinkPreviewText.FormatBaseStat` writes `4-6 Armor`, `19-29 Evasion`, `6-8 Ward`, `4-6% Block` and an attack time's
  two ends in seconds as times are written (`0.95-1.05 sec attack time`), and `FormatDamage` a weapon's two damage
  ranges and its type as one part where its low end stands (`2-4 to 6-8 Physical Damage`); one number for a range whose
  ends are equal. The wording file's `baseStats` rows (`stat`, `min`, `max`, and on the damage row `min2`, `max2` and
  `type`, with the `text`) hold it, and `StatWordingShould` checks each; they run ahead of the Dashboard's file as the
  `spans` rows do, and so do the `IncreasedMaximumLife` row (`+5% increased Health`) and the two mana rows (#1181:
  `+5% increased Mana`, `+5% increased Mana Regeneration`).
- **Colours:** an item's `theme-color` is its rarity's colour, an ability's `AbilityColour`. A missing or malformed
  colour leaves `theme-color` out; it never fails the request.
- **Not-found and unavailable pages** are titled "Not found · {SiteName}" and "Unavailable · {SiteName}" (or just
  "Not found" and "Unavailable").
- **Caching:** `public, max-age=300` only when the world is one every player may enter and the page does not depend
  on the caller (a named world, or the default for a caller with Player rights only); otherwise
  `private, max-age=300`.

## Cross-world views

These read each world by name, through `IWorldRepositories` or Redis, and never let one world's failure fail the
whole answer.

- **`GET /character`** (`AccountCharactersController`, `AccountCharactersService`) returns
  `{ characters, unavailableWorlds }`. It walks the auth `Worlds` rows in id order and skips a world the caller may not
  enter (it appears nowhere) and one this API is not configured for. A world that is unavailable, or whose read
  throws, goes into `unavailableWorlds` (a failed read is logged with the world id and exception type); the call still
  succeeds with the other worlds' characters. Each `CharacterDto` carries `worldId` and `worldName`.
- **`GET /world`** lists only the worlds the caller may enter (paging and the total count cover only those, #452),
  each with `configured` (under `Database:Worlds`) and `available` (configured, and its databases answered at
  startup), beside the derived status. `GET /world/{id}` answers 404 for a world the caller may not enter, the same as
  for a missing one.
- **Observability** (`ObservabilityService`) is cross-world because presence lives in Redis: each world server writes
  `world:{id}:presence` and `presence:world:{id}:character:{characterId}`. `GET /observability/online` and
  `GET /observability/instance/{instanceId}` read every world the caller may enter (instance ids are GUIDs from
  `Guid.NewGuid()` in `MapInstance`, so they cannot collide across worlds). A snapshot with an unknown schema version,
  or naming another world than its key, is ignored. Template names and the layout-staleness check are read from each
  presence's own world, with the per-request name memo and the 30-second pool cache keyed by world (pool 3 of one
  world is not pool 3 of another); a world this API does not serve or that is unavailable, or a failed lookup, names
  the template `#id` and reports the layout not stale.
- **One character's presence** is `GET /world/{worldId}/observability/character/{id}` (`WorldObservabilityController`),
  behind the world check. It reads only that world's index key and snapshot and treats an index entry naming another
  world as absent. Its 404 means "not in this world now", not "no such character".
- **Game sessions** (identity's) also read worlds by name, through their Characters databases:
  `GameServerAllocator` offers no destination on a world this process marked unavailable (in a process running
  identity without worlds, one whose Characters database did not answer at startup); `GameSessionFenceService` writes
  gameplay fences into the named world's characters database (`IWorldRepositories.GameplayFences`); and
  `AccountConsolidationService` runs across every configured world,
  refusing with `WorldConfigIncomplete` while any auth `Worlds` row is missing from `Database:Worlds` and with
  `WorldUnavailable` while any configured world is unavailable. It includes configured worlds whose auth row is gone,
  so a retired world stays configured while it still holds characters.

## Configuration

### `Database:Worlds`

| Key | Description |
|---|---|
| `Database:Worlds:<id>:World:ConnectionString` | World `<id>`'s content database |
| `Database:Worlds:<id>:Characters:ConnectionString` | World `<id>`'s characters database |

`<id>` is the world's id in the auth `Worlds` table, in canonical form. The environment form is
`Database__Worlds__2__World__ConnectionString`. A process reads only the strings of the databases its services read
(`WorldDatabaseParts`): both for worlds, the Characters string for identity, none for commerce or distribution.

```json
"Database": {
  "Auth": { "ConnectionString": "…" },
  "Worlds": {
    "1": { "World": { "ConnectionString": "…" }, "Characters": { "ConnectionString": "…" } },
    "2": { "World": { "ConnectionString": "…" }, "Characters": { "ConnectionString": "…" } }
  }
}
```

The API's `appsettings.json` lists no world, so the published image ships none and every deployment states its own.
`appsettings.Development.json` holds world 1 (the local `world` and `characters` databases), and the Aspire AppHost
(`src/Server/Avalon/Program.cs`) sets the same pair through the environment.

### Helm (`avalon-api` chart)

A `worlds` map keyed by world id; every connection string reaches the pod through the Secret (`secretKeyRef`), never
as a plain value. A release renders the strings of the parts its services read, as above; one that runs only commerce
or distribution ignores `worlds`, and the checks below apply to the parts a release renders.

- **Chart-managed Secret** (no `existingSecret`): give `worlds.<id>.world.connectionString` and
  `worlds.<id>.characters.connectionString`, from files (`--set-file`; a trailing newline is trimmed). The Secret holds
  them under `database-world-<id>-connection-string` and `database-characters-<id>-connection-string`, and a changed
  string changes the checksum annotation, which restarts the pods.
- **`existingSecret`**: give no strings. The Secret holds each world's two keys under those default names, or under
  the names in `worlds.<id>.worldKey` / `worlds.<id>.charactersKey` (trimmed; blank means the default), which are
  accepted only with `existingSecret`. A world with default names is still listed, for example
  `--set worlds.2.worldKey=`.
- **The chart refuses to render** with no world; a world id that is not `^[1-9][0-9]{0,4}$` up to 65535; with a
  chart-managed Secret, a world missing either string or naming a key; with `existingSecret`, any inline string; a
  key that is not a valid Secret key name (letters, digits, `-`, `_`, `.`); a key two settings would read (two worlds,
  a world's own two keys, or one of the chart's keys `jwt-signing-private-key`, `game-auth-host-key`,
  `database-auth-connection-string`, `cache-password`, `notification-private-key` and `distribution-secret-key`); and
  the removed `database.world` / `database.characters` values. `src/Server/Avalon.Api/Helm/avalon-api/ci/test.sh` pins
  each refusal.

### Public routes and previews

| Key | Default | Description |
|---|---|---|
| `Application:PublicWorldId` | none (`null`) | The default world of `GET /public/world` and of the link previews. Helm `publicWorldId`, rendered only when set |
| `Application:PublicPopulationReference` | none (`null`) | The head count that reads as busy for a world with no player limit, which the public realm list bands its population against; unset shows no population for such a world. Must be positive, else startup stops, naming the setting. Helm `publicPopulationReference`, rendered only when set |
| `Application:PublicSiteUrl` | none (blank) | The public site's base URL, e.g. `https://avalon.example`: an absolute http or https URL with no query or fragment, trailing slash dropped (`PublicSiteSettings.Create`); anything else stops startup, naming the setting. Helm `publicSiteUrl`; the AppHost sets `http://localhost:5173` |
| `Application:Previews:SiteName` | `Avalon` | `og:site_name` and the not-found title; left out when empty |
| `Application:Previews:AbilityColour` | `#BC8A4E` | An ability's `theme-color` |
| `Application:Previews:RarityColours:<Rarity>` | Common `#E9E2D8`, Magic `#38BDF8`, Rare `#FBBF24`, Unique `#F97316` (#1179) | An item's `theme-color` by rarity name (any case) |

The preview colours mirror the Dashboard's `rarity.ts` and the public site's primary accent; change them together.

## Extension points

- **A new world-scoped controller**: route `world/{worldId:int}/...`, mark it `[WorldScoped]`, never allow anonymous
  callers, and read through the ordinary repositories (or `ICurrentWorld` for the id). The pairing test fails if the
  marker and the route disagree.
- **A new public world-scoped endpoint**: GET only, under `public/world/{worldId:int}/`, on a `[PublicWorldScoped]`
  controller, setting `Cache-Control` from `OpenToEveryoneItem` as `PublicController` does.
- **A route that names no world but must read one** (as the link previews do) applies the same order itself (parse,
  auth row, rule, configured, available; one 404 for the first four) and then selects the world on the scoped
  `CurrentWorld`, so the ordinary repositories follow it.
- **A view across worlds**: walk `IWorldDatabases.All` or the auth rows, skip a world the caller may not enter,
  check `IsAvailable`, read through `IWorldRepositories` (add a member there when a new repository is needed) or
  `IWorldDbContextFactory`, and contain each world's failure: log the world id and exception type, report the world
  as unavailable, never fail the whole view.
- **Adding a world**: an auth `Worlds` row, a `Database:Worlds:<id>` pair (Helm `worlds.<id>`) pointing at that world
  server's databases, and the same id as the world server's `Game:WorldId`; then restart the API processes that read
  worlds (those running worlds or identity), which read the configuration and check the databases only at startup.

## Tests

The shared plumbing's tests are in `tests/Avalon.Api.Hosting.UnitTests`, the worlds service's in
`tests/Avalon.Api.Worlds.UnitTests`, and the host-level ones in `tests/Avalon.Api.UnitTests`.

| Test | Project | Guards |
|---|---|---|
| `Worlds/WorldDatabaseSettingsShould` | Hosting | Parsing, canonical ids, refusals, connection strings kept out of `ToString`, statuses |
| `Worlds/WorldDatabasePartsShould` | Hosting | A process reads, requires and opens only the world databases its services need |
| `Hosting/ApiStartupValidationShould` | Hosting | `Database:Worlds` refused at startup before any database call, the options checks passing without a world, one world's failed check leaving the others available |
| `Worlds/ApiDatabaseMigratorShould` | Worlds | Auth migrated and worlds only checked, a failed world marked and logged by type, the 503 for it |
| `Hosting/ApiHostGraphShould` | Host | Each service's real host opens the request's world through the per-request context factories |
| `Worlds/CurrentWorldContextsShould` | Hosting | The selected world's database, refusal with no world or no request, concurrent requests, named-world reads |
| `Worlds/WorldContextSensitiveLoggingShould` | Hosting | Sensitive data logging follows the host environment for every world |
| `Worlds/WorldRouteShould` | Worlds | The middleware's order: 404 cases alike, auth row before configuration, 404 before 503, 401, personal access tokens, a mid-request database failure as 503 |
| `Worlds/WorldScopedEndpointsShould` | Worlds | Marker and route agree; no anonymous world endpoint; public endpoints GET only |
| `Worlds/WorldScopedDataShould` | Worlds | The same route reads each world's own rows |
| `Worlds/WorldRoutesOpenApiShould` | Worlds | `worldId` a required path parameter; no world content outside a world; cross-world routes unscoped |
| `Worlds/PublicRouteShould`, `Worlds/PublicWorldListShould`, `Worlds/PublicRoutesOpenApiShould` | Worlds | Public tooltips, caching, the default world |
| `Worlds/LinkPreviewShould` | Worlds | Preview tags, encoding, the 200-character cap, `og:url`, colours, world selection |
| `Worlds/WorldObservabilityRouteShould`, `Services/ObservabilityServiceShould` | Worlds | Presence per world (#556) and the cross-world views |
| `Services/AccountCharactersServiceShould` | Worlds | `GET /character` across worlds with `unavailableWorlds` |
| `Worlds/QuestTemplateRouteShould`, `Worlds/WorldScriptCatalogRouteShould` | Worlds | Two world-scoped routes behind the world check |
