# Map Generation

End-to-end guide: from chunk files → server bake → playable client.

## Concepts

The world is built from **chunks** — modular 30×30m geometry blocks (cell size configurable per chunk; current standard is 30). Chunks are stitched into **layouts**. Layouts feed two pipelines:

- **Town maps** (MapType.Town) — chunk placements are PREDEFINED in the database (`MapChunkPlacement` table). Multi-player shared instances. Same layout every time.
- **Procedural maps** (MapType.Normal) — chunk placements are GENERATED at runtime by `ProceduralChunkLayoutSource` using a chunk pool + RNG seed + per-map config (`ProceduralMapConfig`). Per-player private instances.

Both pipelines emit a `ChunkLayout` record (chunks, entry spawn, portals, cell size, optional config + seed). One factory (`ChunkLayoutInstanceFactory`) builds a `MapInstance` from the layout, bakes a navmesh from the stitched chunk geometry via DotRecast, and ships the layout to the client over `SChunkLayoutPacket`. The client bakes its own navmesh from the chunk `.obj` files plus the layout's obstacle boxes (its solid doodads', `SChunkLayoutPacket.Obstacles`) for prediction parity; today's client bakes the chunks alone (see [doodads](doodads.md#what-todays-client-does)).

```
                  MapTemplate (MapType: Town | Normal)
                         │
                         ▼
              ChunkLayoutSourceResolver
            ┌────────────┴───────────────┐
            ▼                            ▼
   PredefinedChunkLayoutSource   ProceduralChunkLayoutSource
   (reads MapChunkPlacement)     (RNG + ProceduralMapConfig + ChunkPool)
            │                            │
            └──────────────┬─────────────┘
                           ▼
                       ChunkLayout
                  (Chunks[], EntrySpawn, Portals[], CellSize)
                           │
                           ▼
              ChunkLayoutNavmeshBuilder.BakeAsync
                  (stitches chunk objs → in-mem combined obj → DotRecast)
                           │
                           ▼
                  MapInstance + MapNavigator
                           │
                           ▼
                Client: SChunkLayoutPacket
                (instanceId, seed, cellSize, chunks[], entrySpawn)
                           │
                           ▼
       Client: MapCompositionSystem (draws the vendored chunk meshes + markers)
       Client: NavmeshSystem → navmesh::NavMesh::Bake (mirror bake from the vendored .obj)
       Client: PlayerMovementSystem (predict + reconcile)
```

## Repos involved

- **Server:** `C:\dev\Avalon.Server` — chunk catalog, ChunkGen, bake, DB, instance factory, wire packet
- **Client:** `C:\dev\vulkan-multithreaded` — the C++ game client. It vendors the chunk geometry (`scripts/vendor-chunks.ps1`) and the navmesh and rotation vectors (`scripts/regen-proto.ps1`) and bakes its own navmesh from the vendored geometry, so a chunk change here is followed by a re-vendor there
- **Chunk catalog:** `src/Server/Avalon.Server.World/Maps/` — the forest pieces and Glimmerdell's four squares are written by `tools/Avalon.ChunkGen`; a few other chunk files are committed as they are; the town layouts, pools, groups, spawn tables and procedural configs are edited by hand; the World server seeds the database from it on every start. A path in this page that starts with `Maps/` is under this directory.

## Where things live

### Server (`C:\dev\Avalon.Server`)

| Path | Purpose |
|---|---|
| `src/Server/Avalon.World/ChunkLayouts/` | `ChunkLayout`, `IChunkLayoutSource`, factories, navmesh builder |
| `src/Server/Avalon.World.Generation/ChunkRotation.cs` | `ChunkRotation.LocalToWorld`, the one rotation case table (bake, slot placement, client mirror) |
| `src/Server/Avalon.World/Maps/Navigation/MapNavigator.cs` | DotRecast queries (`RaycastWalkable`, `SampleGroundHeight`) |
| `src/Server/Avalon.World/Maps/Navigation/NavmeshBuildSettings.cs` | Single source of truth for DotRecast bake constants |
| `src/Server/Avalon.World/Respawn/MapArrival.cs` | Sends `SChunkLayoutPacket` behind every arrival: a character select (`CharacterSelectHandler`), a portal entry (`EnterMapHandler`), a return to town and an item's teleport. Its chunk and portal entries are built with the instance, off the tick (`MapLayoutEntries`) |
| `src/Shared/Avalon.Network.Packets/World/SChunkLayoutPacket.cs` | Wire format |
| `src/Shared/Avalon.Domain/World/ChunkTemplate.cs` | DB entity for chunk metadata |
| `src/Shared/Avalon.Domain/World/MapChunkPlacement.cs` | DB entity joining MapTemplate → predefined chunk placements |
| `src/Shared/Avalon.Domain/World/ProceduralMapConfig.cs` | DB entity for procedural map RNG config |
| `src/Server/Avalon.Server.World/Maps/Chunks/<chunkName>.obj` + `.json` | Chunk geometry (navmesh bake) and metadata (see "The chunk format"). The forest pieces and Glimmerdell's four squares are written by `tools/Avalon.ChunkGen`; the rest (`forest_boss_01`, `town_corner_01`, `town_house_01`, `town_path_01`, `town_square_01`) are committed as they are and no tool writes them |
| `src/Server/Avalon.Server.World/Maps/Decor/<chunkName>.json` | What the client draws on a generated town square from the art kit (see "Decor and building recipes"). Written by `tools/Avalon.ChunkGen` beside the chunks, outside `Maps/Chunks/` because the seeder reads every `*.json` there as chunk metadata; the server never reads it |
| `src/Server/Avalon.Server.World/Maps/TownLayouts/<MapTemplateId>.json` | Town layouts: which chunk sits on which grid cell, edited directly (see "Creating a town"). ChunkGen writes the squares' geometry, not this file |
| `src/Server/Avalon.Server.World/Maps/chunk-pools.json` | Procedural pool membership (`{ "<pool>": ["<chunk>", …] }`), edited by hand |
| `src/Server/Avalon.Server.World/Maps/chunk-groups.json` | Set pieces per pool (see "Chunk groups (set pieces)"), edited by hand |
| `src/Server/Avalon.Server.World/Maps/spawn-tables.json` + `ProceduralMaps/<mapId>.json` | Spawn tables and procedural map configs with their depth bands (see "Spawn tables and procedural map configs"), edited by hand |
| `tools/Avalon.ChunkGen/` | Generates the forest pieces' and the town squares' `.obj` + `.json`, and the squares' decor files (see "Generated forest chunks" and "Generated town squares") |
| `tools/Avalon.ChunkGen/Recipes/<id>.json`, `tools/Avalon.ChunkGen/Kit/kit-bounds.json` | Building recipes (kit pieces plus collision proxies) and the art kit's bounds table (see "Decor and building recipes") |
| `tools/Avalon.Exporter/` | Exports the known-answer vectors the client vendors, among them `schema/vectors/navmesh-v1.txt` (`-- navmesh`) and `schema/vectors/rotation-v1.txt` (`-- rotation`) |
| `src/Server/Avalon.Database.World/Seeding/ChunkCatalogSeeder.cs` | Seeds chunk templates, town layouts, pools, chunk groups, spawn tables and procedural map configs from `Maps/`; run by the World server on start |

### Client (`C:\dev\vulkan-multithreaded`)

| Path | Purpose |
|---|---|
| `scripts/vendor-chunks.ps1` | Copies every `Maps/Chunks/*.obj` into `devproject/chunks/`, converts each to a `.gltf` beside it (one primitive per `usemtl` group, coloured from the script's palette: `stone`, `wood`, `roof`, `cloth`, `cloth_2`, `plaster`, `metal`, `water`), and writes `CHUNK-MANIFEST` (the server revision it copied from, plus a SHA-256 per file). The chunk `.json` files are not vendored. An unknown material name or an OBJ directive other than `o`, `v`, `f`, `usemtl` fails the script. The `collision` material (draw nothing) and the decor files (`Maps/Decor/*.json`) arrive with the client issue for the kit-built town |
| `scripts/regen-proto.ps1` | Vendors the server's schema set; among it `schema/vectors/navmesh-v1.txt` → `gamenet/schema/navmesh/navmesh-v1.txt` and `rotation-v1.txt` → `gamenet/schema/chunks/rotation-v1.txt` |
| `game/src/MapCompositionSystem.cpp` | Draws the server's layout from `SChunkLayoutPacket`: one entity per primitive of each chunk's `.gltf`, placed with the mirrored rotation, plus markers for the entry and the portals |
| `common/include/common/scene/ChunkRotation.h` | `chunkLocalToWorld`, the client's mirror of `ChunkRotation.LocalToWorld` |
| `game/src/ChunkGeometry.cpp` | Stitches the vendored `.obj` files (not the `.gltf`) into one triangle soup for the bake |
| `game/src/NavmeshSystem.cpp` + `NavmeshWorker.cpp` | Bakes the map on a worker thread whenever a new instance id arrives; `LoginFlowSystem` holds back the load report while it bakes |
| `navmesh/` | Recast/Detour bake + `RaycastWalkable` / `SampleGroundHeight`; `navmesh/src/BuildSettings.h` mirrors `NavmeshBuildSettings.cs` |
| `game/src/PlayerMovementSystem.cpp` | Movement prediction + reconciliation against server acks |
| `game/src/PortalSystem.cpp` | Offers the portal the player stands in and takes it on E |

## The chunk format

A chunk is two files in `Maps/Chunks/` with the same name: `<name>.obj` (geometry, chunk-local) and `<name>.json`
(metadata). For a generated chunk ChunkGen writes both; do not edit them by hand, edit the piece data and rerun it (see
"Generated forest chunks" and "Generated town squares"). An example, `forest_entry_01.json`:

```json
{
  "name": "forest_entry_01",
  "assetKey": "chunks/forest_entry_01",
  "cellFootprintX": 1,
  "cellFootprintZ": 1,
  "cellSize": 30,
  "exits": {
    "N": ["center"],
    "E": [],
    "S": [],
    "W": []
  },
  "spawnSlots": [
    { "tag": "entry", "localX": 15, "localY": 1, "localZ": 15 }
  ],
  "portalSlots": [
    { "role": "Back", "localX": 15, "localY": 1, "localZ": 5 }
  ],
  "tags": ["entry", "forest"]
}
```

- **name**: stable name. Lowercase, no spaces, unique across the catalog. It is the FILENAME on disk (`<name>.obj`,
  `<name>.json`; the seeder refuses a mismatch, and a `.json` with no `.obj`) AND the key `ChunkTemplate` rows are
  upserted by. Conventions: `town_*`, `forest_*`, `dungeon_*`, etc.
- **assetKey**: `chunks/<name>`.
- **cellFootprintX/Z**: how many cells the chunk spans. Every chunk today is 1×1, and `ChunkRotation` pivots on a 1×1
  chunk's centre.
- **cellSize**: meters per cell. Must match every chunk in any pool/layout the chunk will live in. Standard is **30**.
- **exits**: side (`N`, `E`, `S`, `W`) → the slots (`left`, `center`, `right`) on that side that permit traversal into
  the neighbouring chunk. The procedural generator matches exits between chunks ONLY when both chunks have an exit at
  the SAME slot on opposite sides. A side lists a slot at most once (the seeder folds exits into a bitmask, so no
  `(side, slot)` pair can be declared twice). The geometry must leave a gap in its wall where an exit is declared; the
  server reads only `(side, slot)`.
- **spawnSlots**: `tag` + chunk-local `localX/Y/Z`, the spawn point. The tag is consumed by spawn tables (procedural
  maps) or identifies the player entry chunk (towns). Conventions: `entry`, `pack`, `rare`, `boss`, `empty`
  (set pieces also carry `leader`). `entry` and `empty` need no spawn-table entry; every other slot tag in a pool does.
- **portalSlots**: `role` + chunk-local `localX/Y/Z`, a portal anchor. `Back` returns to a previous map (typically in
  entry chunks), `Forward` advances to the next (typically in boss chunks). The destination is not in the chunk: towns
  take it from the layout placement, procedural maps from `ProceduralMapConfig.BackPortalTargetMapId` /
  `ForwardPortalTargetMapId`.
- **doodadSlots** (optional, after `portalSlots`; left out when a chunk has none, as in `forest_entry_01` above):
  `tag` + chunk-local `localX/Y/Z` + `yaw`, a spot a procedural map may place a doodad at (see `docs/doodads.md`).
  `yaw` is the degrees the doodad faces (`Orientation.y`, 0 along +Z, 90 along +X), a quarter turn: 0, 90, 180 or 270.
  Tags: `doodad` (a walk-through doodad) and `doodad_large` (may hold a solid one). Doodad slot tags are not spawn-table
  tags and need no entry there. The seeder refuses a doodad slot with a blank tag, a yaw that is not a quarter turn,
  a position outside the chunk's own cells on X/Z (their edges included), or a `localY` that is not finite (the file
  may write `"NaN"` or `"Infinity"`, refused by name). A doodad slot stands on the floor (`localY` 0 in
  the generated forest, whose floor top is y = 0), where a spawn slot stands 1 m up: a walk-through doodad is snapped to
  the navmesh as a spawn is, so for it `localY` only centres the ground search, but a solid doodad keeps its slot's
  height, so its box and model stand where the slot does. Stored in `ChunkDoodadSlot` (owned by `ChunkTemplate`,
  migration `AddChunkDoodadSlots`).
- **tags**: free-form, e.g. `["town", "entry"]`, `["forest", "boss"]`.

The `.obj` is in chunk-local space: a 1×1 chunk spans `(0..30, *, 0..30)`, origin at its SW corner, `+X` east, `+Z`
north. The server's bake reads only `v` and `f` lines (`ChunkObjParserShould`); the client's converter also accepts `o`
and `usemtl` and refuses anything else.

### How a chunk enters the catalog

1. ChunkGen writes its `.obj` + `.json` into `Maps/Chunks/` (see "Generated forest chunks").
2. For a procedural chunk, add its name to its pool in `Maps/chunk-pools.json` (or, for a set piece's members, to
   `chunk-groups.json`). Town-only chunks need nothing here.
3. Restart the World server. There is no import step: it seeds the database from `Maps/` on every start
   (`ChunkCatalogSeeder`). `ChunkTemplate` rows are upserted by `Name` (ids kept), each town layout replaces its map's
   placements, and each pool in `chunk-pools.json` gets exactly the members listed.
4. Re-export the navmesh vectors if the geometry changed (`dotnet run --project tools/Avalon.Exporter -- navmesh`).
5. Commit `src/Server/Avalon.Server.World/Maps/` (and `schema/vectors/` if re-exported). These files are the catalog
   every environment is seeded from.
6. In the client, rerun `scripts/vendor-chunks.ps1` (and `scripts/regen-proto.ps1` for the vectors) and commit
   `devproject/chunks/`.

## Creating a town

A town is a `MapTemplate` with `MapType=Town` plus a set of `MapChunkPlacement` rows pinning specific chunks to specific grid cells. Glimmerdell (map 1) is the town today: its four squares are ChunkGen's (`tools/Avalon.ChunkGen/TownPieces.cs`, see "Generated town squares"), its NPCs are the seeded `MapCreatureSpawns`, and its layout is `Maps/TownLayouts/1.json`.

### 1. Make the town's chunks

Town chunks use the `town_*` naming convention. Each chunk's exits + walls determine adjacency. To make a 2×2 fully-connected town: use 4 corner-pattern chunks where each chunk has walls on its 2 perimeter sides and exits on its 2 inner sides (Glimmerdell's `town_sw_01` has exits `N` and `E` at `center`).

Designate ONE chunk as the entry chunk:
- A spawn slot with `tag: "entry"` (the player spawn point inside the chunk).
- Optional: a portal slot with `role: "Back"` (for portals back out of town to an overworld / dungeon entry).

### 2. Confirm the MapTemplate exists

The `MapTemplate` row identifies the map. Towns are seeded in EF migrations or set up by hand. Check:

```bash
psql -U postgres -d world -c "SELECT \"Id\", \"Name\", \"MapType\" FROM \"MapTemplates\";"
```

Expected: at least one row with `MapType = 0` (Town). The current dev town is `Id = 1`. If you need a NEW map id, add it via an EF migration in `Avalon.Database.World` (or seed via an SQL one-off if dev-only).

### 3. Write the town layout

`Maps/TownLayouts/<MapTemplateId>.json`, edited directly. Glimmerdell's:

```json
{
  "mapTemplateId": 1,
  "mapName": "main_town",
  "cellSize": 30,
  "chunks": [
    { "chunkName": "town_sw_01", "gridX": 0, "gridZ": 0, "rotation": 0, "isEntry": true, "entrySpawn": { "localX": 15, "localY": 0, "localZ": 15 } },
    { "chunkName": "town_se_01", "gridX": 1, "gridZ": 0, "rotation": 0, "isEntry": false },
    { "chunkName": "town_nw_01", "gridX": 0, "gridZ": 1, "rotation": 0, "isEntry": false, "forwardPortalTargetMapId": 2 },
    { "chunkName": "town_ne_01", "gridX": 1, "gridZ": 1, "rotation": 0, "isEntry": false }
  ]
}
```

- **mapTemplateId**: must match an existing MapTemplate row's `Id` AND that row's `MapType` must be `Town`. The seeder rejects a mismatch.
- **mapName**: free-form, for human readability.
- **cellSize**: meters per cell. Must match the `cellSize` of every referenced chunk. The seeder enforces consistency.
- Per placement in **chunks**:
  - **chunkName**: must match a chunk in `Maps/Chunks/`.
  - **gridX / gridZ**: integer cell coordinates. World position will be `(gridX*cellSize, 0, gridZ*cellSize)`. Cell `(0, 0)` is the SW corner; `+X` is east, `+Z` is north.
  - **rotation**: 0–3 (90° steps around Y). All chunks at rotation 0 sit in their own orientation; non-zero rotates the chunk **around its centre** (`cellSize/2, cellSize/2` of the SW-anchored chunk-local frame) so the footprint stays inside the declared `(gridX, gridZ)` cell. The single source of truth for that pivot is `ChunkRotation.LocalToWorld` (mirrored on the client as `chunkLocalToWorld`); the bake, slot placement and the client's drawing and bake all call it. The procedural generator uses all 4 rotations to fit chunk exits to neighbouring cells.
  - **isEntry**: exactly ONE placement per layout must have `isEntry: true`. The player spawns at this chunk.
  - **entrySpawn**: chunk-local spawn coordinates (`localX/Y/Z`, only honored when `isEntry` is true; 0 when left out). Glimmerdell uses `(15, 0, 15)`. Server's `PredefinedChunkLayoutSource` transforms this to world space and uses it for `ChunkLayout.EntrySpawnWorldPos`.
  - **backPortalTargetMapId / forwardPortalTargetMapId**: optional; the destination map of the chunk's `Back` / `Forward` portal slot (see "Map teleportation").

Adjacency check: walk through pairs of neighbouring placements (`(X, Z) ↔ (X+1, Z)` and `(X, Z) ↔ (X, Z+1)`). For each pair, the chunks' shared boundary needs aligned exit slots on both sides; otherwise the player can't traverse. Walls block.

### 4. Restart the World server

No import step: on start the World server seeds the layout from `Maps/TownLayouts/<MapTemplateId>.json`. It validates that the MapTemplate exists and is `Town`, the chunk list is not empty, exactly one isEntry, no duplicate `(gridX, gridZ)`, every `chunkName` is in `Maps/Chunks/`, and every chunk's `cellSize` matches the layout's; then it replaces the map's `MapChunkPlacement` rows in one transaction. Commit the layout file.

The server caches `ChunkTemplate` data in `IChunkLibrary` at startup, so the restart is also what picks up new chunks/placements:

```bash
dotnet run --project src/Server/Avalon.Server.World
```

Town instances are built lazily on first character-select — no startup pre-build is needed.

### 5. Verify in DB

```bash
psql -U postgres -d world -c "SELECT \"MapTemplateId\", \"ChunkTemplateId\", \"GridX\", \"GridZ\", \"IsEntry\" FROM \"MapChunkPlacements\" WHERE \"MapTemplateId\" = 1;"
```

Expect one row per placement.

### 6. Test in the client

Re-vendor the chunks in the client if any changed (see "How a chunk enters the catalog"), then log in and select a character in the town. The character-select handler:
- Resolves the town instance via `InstanceRegistry.GetOrCreateTownInstanceAsync(MapTemplateId)`.
- Builds the instance via `ChunkLayoutInstanceFactory.BuildAsync` → `PredefinedChunkLayoutSource.BuildAsync` → reads `MapChunkPlacement` rows → produces `ChunkLayout`.
- Bakes navmesh from stitched chunk objs (server-side `Maps/Chunks/<name>.obj`).
- Sends `SChunkLayoutPacket` to the client.

Player should spawn at the entry chunk, walk through unblocked exits between chunks, hit walls where chunks aren't connected, and stay within authoritative bounds (server clamps via DotRecast raycast, client clamps the same way for prediction).

### 7. Commit

Avalon.Server: `Maps/` (and `schema/vectors/` if re-exported). Client: `devproject/chunks/` (and `gamenet/schema/` if re-vendored).

---

## Creating a procedural map

Procedural maps generate their layout at runtime from a chunk pool + RNG seed.

### 1. Make the chunks

Use distinct names from town chunks (e.g. `forest_*`, `dungeon_*`). Important slots:
- **Entry chunk** — must declare a spawn slot tagged `"entry"` AND a portal slot with `role: "Back"`. The generator picks an entry chunk from candidates that have both.
- **Boss chunk** (optional) — declares a spawn slot tagged `"boss"` AND a portal slot with `role: "Forward"`. Procedural generator places the boss at the end of the main path if `ProceduralMapConfig.HasBoss = true`. A set piece can carry the boss instead: on the forest the boss arena `forest_arena` ends the main path (see "Chunk groups (set pieces)").
- **Path chunks** — common middles. Declare any tags you want spawn tables to filter on.

Bring them into the catalog as under "How a chunk enters the catalog".

### 2. Define a ChunkPool

A `ChunkPool` is a named bag of `ChunkPoolMember`s, each with a weight and an optional per-layout cap. The procedural
generator picks chunks from a pool weighted-random.

Pools are `Maps/chunk-pools.json`, seeded by `ChunkCatalogSeeder` on every World start (see "How a chunk enters the catalog"). Convention: one pool per biome.

A pool entry is a chunk's name (weight 1, no cap) or an object `{ "chunk": "<name>", "weight": 2.5, "max": 1 }`, with
`weight` (left out: 1) a finite number above 0 and `max` (left out: no limit) the most times one layout may place the
chunk, 1 or more. The seeder writes both onto the pool's `ChunkPoolMembership` rows (`Weight`, `MaxPerLayout`) and
refuses, naming the file and the chunk, a weight of 0 or below, NaN or infinite (the file may write `"NaN"`), a `max`
below 1, or a chunk named twice in one pool.

`ProceduralLayoutGenerator` weighs every pick: the entry among the entry-capable members, and each later step among its
candidates, one per member and turn that fits (a set piece weighs 1), so a member's chance is its weight times the turns
that fit. A member at its `max` is left out of the candidates for the rest of that layout. Candidates that all weigh the
same are picked with the one draw a uniform pick always made, so a pool of equal weights and no caps generates the
layouts it always did. A cap is part of `LayoutConfigVersion` only when set, so a pool without caps
keeps its fingerprint; a changed weight or cap changes what an old seed generates.

### 3. Define a ProceduralMapConfig

`ProceduralMapConfig` lives in the World DB. Fields:
- `MapTemplateId` (FK to MapTemplate)
- `ChunkPoolId` (FK to ChunkPool)
- `SpawnTableId` (the spawn table, named in the file)
- `MainPathMin`, `MainPathMax` — main-path length range (chunk count)
- `BranchChance`, `BranchMaxDepth` — side-branch settings
- `HasBoss` — gate for boss chunk placement
- `BackPortalTargetMapId`, `ForwardPortalTargetMapId` — destination map ids for portal slots
- `DepthBands` — optional depth bands (rows in `ProceduralDepthBands`)
- `MinSetPieceStep` — the first main-path step (the entry is step 0) a set piece other than the boss's may be placed at;
  0, the default, places them from step 1 (see "Chunk groups (set pieces)")

Configs are `Maps/ProceduralMaps/<mapId>.json` and spawn tables `Maps/spawn-tables.json`, seeded by `ChunkCatalogSeeder` on every World start; see "Spawn tables and procedural map configs".

### 4. Restart server, test

Procedural map instances are built per-player on `EnterMapHandler` traversal (or character-select if the character is currently in a procedural map). Same flow as town from there: bake → wire → client draws and bakes.

Each player gets their own seed (server's `ProceduralChunkLayoutSource.NextSeed()`), so layouts differ per session.

---

## Generated forest chunks

The forest pieces are written by `tools/Avalon.ChunkGen`. Each is a flat 30 x 30 m floor (the forest slab, y -0.2 to 0)
with blockers on it, boxes for rocks and ruined walls and twelve-sided cylinders for tree clusters and pillars, 3 m
tall. Blockers cut the navmesh (the agent climbs 0.9 m), so creatures path around them and walkable rays (movement,
ground-target casts, projectiles) stop at them. The geometry is a placeholder until real art exists.

    dotnet run --project tools/Avalon.ChunkGen -- forest
    dotnet run --project tools/Avalon.ChunkGen -- forest --maps <Maps directory>

writes every piece's `.obj` and `.json` into `Maps/Chunks/` and nothing else (`Maps/` is
`src/Server/Avalon.Server.World/Maps/` unless `--maps` names another). Before writing, it reads every piece back
through the World server's own catalog reader (`ChunkCatalogSeeder.ReadCatalogAsync`) and bakes each piece, and each
set piece's four members together, with `ChunkLayoutNavmeshBuilder` in a temporary copy of `Maps/`; if anything fails
it exits 1 and nothing is written. After writing, it lists every `forest_*` file in `Maps/Chunks/` it did not generate
(`forest_boss_01`, or one left over from a renamed piece) and deletes none of them. Edit `tools/Avalon.ChunkGen/ForestPieces.cs`
and rerun it; `ForestPiecesShould` fails when the committed files differ from a fresh run, when a slot is within 2.5 m
of a blocker or 2 m of the chunk's edge, when a blocker stands in an exit's throat, or when a slot or exit cannot be
reached on the baked navmesh. The tool writes LF line ends, and `.gitattributes` pins every `.obj` and `.json` under
`Maps/` to LF, so a regeneration on any machine is an empty diff. The tool does not touch `chunk-pools.json` or
`chunk-groups.json`: add new single pieces to the pool, and new set pieces to the groups file, by hand.

Every generated forest piece also carries doodad slots (`DoodadSlots` on the piece, `Doodad(x, z, yaw)` and
`LargeDoodad(x, z, yaw)` in `ForestPieces.cs`): each single piece two `doodad` slots and one `doodad_large`, each set-piece
member one `doodad` slot, and each of the arena's members a `doodad_large` too; a set piece lists them in its 60 x 60 m
frame and they are split into its members as spawn slots are. Doodad slots are written on the floor, at `localY` 0
(`ChunkPiece.DoodadSlotY`, the floor's top), and spawn slots 1 m up, at `localY` 1. The hand-made chunks (`forest_entry_01`, `forest_boss_01`,
the paths, junctions, `forest_clearing_01` and `forest_deadend_01`) have none, and the tool writes `"doodadSlots"` only
for a piece that has some, so the town squares' files are unchanged by it. `ForestPiecesShould` keeps every doodad slot
off the paths and clear of the rest, with a `doodad_large` slot further away because it may hold a solid doodad:

| From | `doodad` | `doodad_large` |
|---|---|---|
| any blocker | 2.5 m | 4 m |
| a path: in a single piece, the segment from an exit's throat centre to the piece centre (15, 15); in a set piece, checked in its 60 x 60 m frame, the segments from each outer exit's throat centre to the frame centre (30, 30) and between every two outer exits | 4 m | 5 m |
| any spawn slot | 3 m | 4 m |
| the chunk's edge | 2 m | 2 m |

and never inside an exit's throat (10 m wide, 6 m deep, centred on its side); every doodad slot stands on the baked
navmesh floor, and is a reachability target too: a walk over the piece's (or the set piece's) navmesh reaches it from
the first exit, as it reaches every exit and spawn slot. Each yaw is the quarter turn that best faces the piece's centre
(a set piece's, for its members).

### Solid doodads in the bake

A solid doodad (a template with `Collision` other than `None`: the fountain and the shrine) is a wall in the instance's
navmesh, and the client is sent its box to bake the same wall (see [doodads](doodads.md#placement)).

- **The box.** The doodad plan (`DoodadPlacementService.Plan`, before the bake) gives each solid doodad it puts at a slot
  one `DoodadObstacle`: the template's `CollisionHalfX` on X and `CollisionHalfZ` on Z around the slot's world position,
  swapped on an odd quarter turn (yaw 90 or 270), from `BaseY`, the slot's world height (every doodad slot is written on
  the floor, at local y = 0, and a solid doodad keeps its slot's height), up to `TopY = BaseY + CollisionHeight`. The
  boxes are in plan order. `ChunkLayoutInstanceFactory` sets them on the layout (`ChunkLayout.Obstacles`) before the bake, so the instance
  keeps them with its layout.
- **Quarter turns only.** A slot's yaw is 0, 90, 180 or 270 (the seeder refuses any other), so a box is always
  axis-aligned and needs no rotation.
- **Never quest-gated, never off a slot.** A wall in the navmesh is there for everyone, so a solid doodad is never hidden:
  the catalog refuses a gate on a solid template, a spawn's gate on one is dropped, and the seeder refuses a required
  rule on one. The fallback, which places a required doodad on open navmesh after the bake, never places a solid one,
  so every solid doodad standing in an instance has its box in that instance's bake.
- **A solid doodad that throws as it is placed keeps its box.** The box was planned and baked before placement, so a
  solid doodad whose `Place` step threw (it is logged and skipped) leaves its box in the navmesh and in the layout
  packet: a wall with nothing drawn in it. No realistic path throws there: building its script and running `OnSpawn`
  are contained, and what is left only files the doodad.
- **The bake.** `ChunkLayoutNavmeshBuilder` appends every box after the last chunk, through
  `DoodadObstacleGeometry.Append`: 8 vertices, (MinX, BaseY, MinZ), (MaxX, BaseY, MinZ), (MaxX, BaseY, MaxZ),
  (MinX, BaseY, MaxZ), then the same four at TopY, and 10 triangles with no bottom face, 1-based on those 8 and offset
  by the vertices already written: the top (5 6 7) (5 7 8), then the sides (1 2 6) (1 6 5), (2 3 7) (2 7 6),
  (3 4 8) (3 8 7), (4 1 5) (4 5 8), numbers written with the invariant culture. Every solid template stands at least
  1.05 m tall, above the agent's 0.9 m climb, so a walk stops an agent radius short of the box's sides.
- **The top winds downward on purpose.** Its normal points down, so the bake marks it unwalkable and no island of
  navmesh forms on a box's flat top. Copy the faces exactly as written and never re-wind them: a top wound upward
  bakes walkable, and the `forest-doodad` vector's probe above the box catches it.
- **A tall box bakes an island inside it.** A box 2.0 m tall or more (the agent's height; the shrine's is 2.2 m)
  leaves room under its top for the floor inside it to bake, as an enclosed island no walk reaches. It is harmless:
  nothing stands there, and the fallback never draws from it, since it draws only polygons connected to the entry's.
- **The wire.** `SChunkLayoutPacket.Obstacles` (field 8, `DoodadObstacleDto` with fields 1-6: `MinX`, `MaxX`, `MinZ`,
  `MaxZ`, `BaseY`, `TopY`) carries the boxes in the same order; `MapLayoutEntries` builds the list once, with the
  instance. A client that does not know the field skips it, and bakes no box.
- **The client mirror.** A client bakes the boxes after its chunks, with exactly these vertices and faces, or its mesh
  and the server's differ around the doodad. `schema/vectors/navmesh-v1.txt` pins it: its `obstacle <minX> <maxX> <minZ>
  <maxZ> <baseY> <topY>` lines (after a layout's `chunk` lines) are baked in order, and the `forest-doodad` layout puts
  one 4 x 4 m box, 1.8 m tall, on `forest_path_01` with a ray that stops at it, a ground probe beside it and one above
  its centre that finds no ground (a top wound upward would answer with its height). `SolidDoodadNavmeshShould`
  builds an instance through the factory and proves the walk stops at the box, at yaw 0 and 90, and that the layout
  packet carries the planned box.

## Generated town squares

Glimmerdell's four squares (`town_sw_01`, `town_se_01`, `town_nw_01`, `town_ne_01`, map 1) are generated too, since the
town beautification (2026-10-01), and drawn from the medieval village kit since 2026-10-17 (see "Decor and building
recipes"): `tools/Avalon.ChunkGen/TownPieces.cs` holds the approved layout as data, chunk-local. Each building is a
recipe placed with `TownBuilding.Place`; each prop is a kit or `avalon_*` model with a collision box around its bounds,
or, where parts of it stand overhead, with shapes of its real parts (the well, the torches and the stalls, below);
wooden fences close the yards of the two houses and the smithy's wood pile, and an iron railing stands beside the bank;
stone curbs line the red-brick roads; the walls and the paving are decor over the wall and floor boxes. The collision
is a small shape model (`TownSquare.cs`: boxes, twelve-sided cylinders and rings, gabled roof blocks, and the wall
boxes by name; the fountain's basin is a ring with the water disc inside it). Each building part and prop is its own
obj object named `<Building>_<part>` with a `usemtl` line. In every generated square that line is `collision`, for
the walls and the floor too, because decor draws all of it; the format's other names (`stone`, `wood`, `roof`,
`cloth`, `cloth_2`, `plaster`, `metal`, `water`) are for a shape the client colours itself, which no generated square
has today. The server's bake reads only `v` and `f` lines (`ChunkObjParserShould`). The floor is y -0.05 to 0.05, and
the walls are 0.5 m thick and 2 m high, the inner ones opened at 12-18.

    dotnet run --project tools/Avalon.ChunkGen -- town [--maps <Maps directory>]

stages, validates (`ChunkCatalogSeeder.ReadCatalogAsync`), bakes the four squares together and writes them, as the
forest command does; a square breaking a layout rule is refused before anything is written (`TownRules`: nothing within
2 m of a wall, the doorway lanes and the arrival-to-portal corridor clear, 2.5 m of headroom under every flat roof,
walkable risers of at most 0.3 m). The headroom rule judges a slab whose base stands 2 m up or more (an awning, a
balcony, the gate's lintel, the well's and a stall's roof slab); a sloped kit roof is a gable block
resting on its building and is not judged. Its eaves come down to 2.34 m on a one-storey building, so a player can
stand under the outer ~0.2 m of it: accepted (owner decision, 2026-10-17), while every flat roof keeps 2.5 m.
`TownPiecesShould` fails when the committed files differ from a fresh run, when a solid piece lets a walk in on the
baked navmesh or has a top a path from the arrival point can end on (Recast climbs any step of 0.8 m or less and rounds
a top up to a 0.2 m voxel, so a top at or under 1.0 m above the ground beside it would be walked over), or when a solid
piece's top is under 1.05 m (every bench, crate, barrel, counter, fence section, the well's rim, a torch's foot, a brazier and
the cart reach 1.05 m, owner decision; the collision box stands that high even where the model, a bench or a 0.81 m
wooden fence say, is lower). Roof tops and building interiors are navmesh islands nothing reaches, as the forest's
blocker tops are. It also fails when a free-standing piece a player would bump into (below body height, not part of
a building) reaches into a red-brick road (`TownSquare.OverlapsRoad`, owner decision 2026-10-18): the roads are the
routes through the town, and the props stand in the open ground beside them. Buildings are where they are, the bank's
and the inn's bodies reaching into the north-east square's roads. The portal square's road also runs north to a gate
in its wall (`TownSquare.Gate`, a road side that is not an exit: no doorway lane, no exit in the chunk's meta): the
north wall is two stubs with the opening at X 12-18 between them, the gate arch stands just inside it (its north face
on the wall line, exempt from the wall clearance its pillars meet), and the forward portal is in the arch, at world
(15, 58.75) (owner decision, 2026-10-18). Every portal's reach is 1.5 m, measured on the ground (`EnterMapHandler`),
and the forest's portal slots stand on the ground. `TownNpcPlacementShould`
checks the seven NPC spots against the same data. The market's middle stays
open for the walking benchmarks, whose 8 m loop about world (45, 10) (`TownNavmesh.LoopCentre` in
`tests/Avalon.World.Testing`) needs nothing solid 7-9 m from it, and the ground round the arrival point stays open for
the idle benchmark's grid of players about the entry spawn.

A prop with parts overhead gets shapes of those parts, so no box reaches past what is drawn at head height: the well's
rim (1.05 m up), its two posts and its roof as a slab at 2.5-2.8 m (1.9 m wide where the art's is 1.6, so the rim's
corners lie within the tolerance of it); a torch's stone foot (raised to 1.05 m) and its pole to the top of its fire cup
at 2.2 m (the cup has no shape of its own: an overhead 2 m up would have no headroom under it), and a brazier's bowl
on its legs as one cylinder raised to 1.05 m. The town is lit by fire, not lamps: `avalon_torch` and `avalon_brazier`
carry a warm light tagged `extras.flame`, where the client burns a flame and flickers the light; a stall's counter (1.1 m up, its front), its four posts and its cloth roof as a slab at 2.9-3.4 m, the
booth behind the counter open beneath it. A post thinner than the navmesh's 0.3 m voxel still stops a walk: Recast
marks every cell a face crosses, and the agent radius does the rest. Each 2 m fence section is a prop with its own
box: a wooden section (`Prop_WoodenFence_Single`, then `_Extension1` and `_Extension2` in turn) stands 0.81 m, its box
1.05 m; an iron railing section (`Prop_MetalFence_Simple` and `_Ornament` in turn) its own 2.87 m. The curbs
(`Prop_ExteriorBorder_Straight1` and `_Straight2`, `_Corner` where a road turns outward, 0.134 m high) are dressing,
laid by `SquareBuilder.Curbs` on the open side of every road tile's edge and left out where one would meet a wall, a
solid piece at body height or another curb, so they run only past open ground. Nothing of a curb is baked: a player
walks over it on the floor's navmesh.

The town is laid out for the client's camera, which is fixed (owner decisions, 2026-10-02 and 2026-10-17) at yaw 45° and 50°
below horizontal, south-west of what it looks at: a player sees the −X and −Z sides of everything, and the south-west of each
square is the bottom of the screen. So tall buildings stand on a square's far (high X/Z) edges with their open face
toward −X or −Z, and an NPC stands on that side, clear of its roof. A kit building puts its gable end on that side,
so its door wall is not under a roof slope, and the ground south-west of every NPC is kept low (the sight check below
treats a kit roof as a box up to its ridge). Two checks hold it: each building's open face (its door, a counter)
carries a `Front`, which `TownPiecesShould` requires to be −X or −Z; and `TownNpcPlacementShould` casts a line from each
NPC's head toward the camera and fails on any piece, wall or solid decor item's box in the way (a roof over the NPC
counts: under a 2.9 m stall roof a head shows only within about 0.6 m of the roof's camera-side edge). The two angles
are mirrored from the client's `IsometricCameraComponent`. The four other `town_*` chunks (`town_corner_01`,
`town_house_01`, `town_path_01`, `town_square_01`) are listed by the tool and never touched. A geometry change here
needs a restart (instances bake once), a re-export of the navmesh vectors (`tools/Avalon.Exporter -- navmesh`), and a
re-vendor in the client (`scripts/vendor-chunks.ps1`, and `scripts/regen-proto.ps1` for the vectors).

### Decor and building recipes

A town square is two layers. The chunk `.obj` is the collision proxy: the simple shapes above, which the server and the
client bake. The decor file, `Maps/Decor/<chunkName>.json`, lists what the client draws instead: art-kit models
(Medieval Village MegaKit, CC0) and `avalon_*` custom props, each at a chunk-local position (metres from the square's
south-west corner, `+X` east, `+Z` north, `y` up) and a yaw of 0, 90, 180 or 270 degrees, clockwise seen from above as
`ChunkRotation` turns a chunk, so the client places items through the same `chunkLocalToWorld` it places the chunk with:

```json
{
  "chunk": "town_ne_01",
  "kit": "medieval-village-megakit-standard",
  "items": [
    { "model": "Wall_Plaster_Door_Round", "x": 22, "y": 0, "z": 19, "yaw": 270 }
  ]
}
```

ChunkGen writes one for every generated square (`"items": []` when it has none), staged and checked with the chunks
and written only when they are, items sorted by model, then x, z, y and yaw, numbers with at most 3 decimals, LF line
ends; `TownPiecesShould` compares them with a fresh run as it does the chunks, and a decor file with the command's
prefix that the run did not write is listed, never deleted, as the chunks are. The seeder never reads `Maps/Decor/`.
A proxy part that decor draws is written `usemtl collision`, which the client does not draw: every proxy of a placed
building, the square's walls when `TownSquare.WallsAsDecor` is set, and the floor when `TownSquare.Paved` is.

A building is a recipe, `tools/Avalon.ChunkGen/Recipes/<id>.json` (copied next to the tool's assembly and read by
`BuildingRecipe.Load`), its coordinates relative to the building's anchor:

```json
{
  "id": "inn",
  "pieces": [ { "model": "Wall_Plaster_Straight", "x": 1, "y": 0, "z": 0, "yaw": 0 } ],
  "proxies": [
    { "kind": "box", "part": "body", "minX": 0, "maxX": 6, "minZ": 0, "maxZ": 4, "y0": 0, "top": 3.12, "front": "NegX" },
    { "kind": "gable", "part": "roof", "minX": -0.2, "maxX": 6.2, "minZ": -0.2, "maxZ": 4.2, "eaves": 3.12, "ridge": 5 }
  ]
}
```

A proxy's `kind` is `box` (`minX`, `maxX`, `minZ`, `maxZ`, `y0`, `top`, optional `walkable`), `gable` (the box's
extents, `eaves`, `ridge`), `cylinder` (`x`, `z`, `radius`, `y0`, `top`) or `ring` (the cylinder's fields and
`innerRadius`), each with a `part`, an optional `material` (only `collision`, which every proxy is written with
anyway) and an optional `front` (`NegX`, `PosX`, `NegZ`, `PosZ`); an unknown or missing field is refused.
`TownBuilding.Place(recipe, building, anchorX, anchorZ, rotation)` turns the recipe about its anchor by `rotation`
quarter turns in `ChunkRotation`'s sense (a box's or gable's extents swap on an odd turn, the front turns NegX, PosZ,
PosX, NegZ), adds 90 to every yaw per turn, names the proxies after `building` and makes them `collision`. A gable's
ridge runs along its longer extent, so it turns with the extents; a square gable's ridge runs along X whichever way it
is turned, so `Place` refuses a square gable: make one axis longer. It also refuses a model the kit's bounds table does
not have, a yaw that is not a quarter turn, and a recipe without exactly one front. `TownSquare.Validate` checks decor
set on a square the same way, and that each item stands inside the 0-30 m cell.

What a player sees and what they collide with must agree, so `TownSquare.Validate` then runs `DecorRules`. A decor
item is solid unless it is flat ground or thin hanging dressing (`DecorRules.DressingPrefixes`: `Prop_Vine`,
`WindowShutters_`, `Window_`, `Floor_`, `Prop_ExteriorBorder_`, `HoleCover_`). A solid item's footprint, its model's
box turned by its yaw and moved to its position (`DecorRules.WorldBounds`), must lie within 0.15 m
(`DecorRules.Tolerance`) of the union of its building's non-walkable proxies, or, for an item placed under the
building `Walls` (the square's own wall sections), of the square's wall boxes: no drawn wall to walk through. A solid
item whose building has no non-walkable proxy is refused. The reverse holds proxy by proxy: every `collision` proxy,
which the client does not draw, must have solid decor on it, and one standing at body height (its base below 1.9 m,
`DecorRules.BodyHeight`) must have its −X and −Z faces, the camera's side, drawn by the solid decor overlapping it at
body height, whatever building that decor was placed under: a line 0.15 m inside each face, less 0.15 m at either
end, must lie within that decor's footprints, so no proxy reaches more than 0.15 m past the art drawn over it: no
invisible wall. A recipe's body proxy therefore stands 0.227-0.242 m beyond its wall line, the kit's corner pieces
standing 0.377 m out and its walls' outer faces 0.092 m: the recipes use 0.235 (`BODY_MARGIN` in the recipe
generator, which writes the gate's pillars with it too). A square drawn as decor is drawn whole: with `WallsAsDecor`
every wall box is covered by its `Walls` sections, and with `Paved` its `Paving` tiles cover the floor to its edges.
Each refusal names the square, the building and the model or part.

A prop the client draws as a doodad rather than from the decor file is placed with `SquareBuilder.DoodadProp` instead
of `Prop`: the same collision box in the `.obj` (so the bake, the navmesh and `TownPiecesShould`'s piece count are
unchanged), its item in `TownSquare.DoodadDrawn` instead of `TownSquare.Decor`. `DecorJsonWriter` never writes
`DoodadDrawn`, but `Validate` checks its items as it checks decor, and `DecorRules` counts them as solid decor both
ways: each must stand inside its building's proxy, and it draws that proxy, so the proxy is no invisible wall
(`DecorRulesShould.Count_a_doodad_drawn_prop_as_drawing_its_proxy`). Each one is drawn by a fixed doodad spawn of the
town (`MapDoodadSpawns`, [doodads](doodads.md#placement)) at the same spot and yaw, with a template whose `ModelKey` is
the item's model; `TownDoodadSpawnsShould` pins one seed row per item and none without one. The box stays whatever
the doodad shows, so a doodad-drawn prop is never hidden: no visibility gate on its row or template, no despawn once
spent, no script, which could despawn it or hide it through `CanSee` (the same test). Glimmerdell's notice board
(`avalon_notice_board` at local (9, 24), yaw 90, in `town_sw_01`) is the one today: it left
`Maps/Decor/town_sw_01.json` and is doodad 6, the board of quest 9's objective 902.

The bounds table, `tools/Avalon.ChunkGen/Kit/kit-bounds.json`, holds each model's box in its own space, so pieces can
be checked against the art without the kit, which stays out of the repository:

    dotnet run --project tools/Avalon.ChunkGen -- kit-bounds --kit <glTF directory> [--props <glTF directory>] [--props-kit <glTF directory>] [--nature-kit <glTF directory>] [--forest-props <glTF directory>]

measures every `*.gltf` directly in the kit directory (and the custom props' directory; the props kit's: Fantasy
Props MegaKit, CC0, whose `Chest_Wood`, `Barrel` and `Crate_Metal` the forest's doodads wear; the nature kit's:
Stylized Nature MegaKit, Standard, CC0, the forest's trees, plants, rocks and stepping stones; and the custom forest
models', the `avalon_*` rocks, logs, ruins and dirt paths) from its accessors' bounds through the node transforms, and
rewrites the table; a model in two of the directories, an empty directory or a file it cannot measure leaves the
table as it was. The committed table is measured from the village kit's `glTF`, `avalon-art\town-kit\gltf`, the props
kit's `Exports\glTF`, `avalon-art\forest-kit\kit` (the nature kit's `glTF`, its colour textures darkened for the
forest) and `avalon-art\forest-kit\gltf`.

## Chunk groups (set pieces)

A chunk group is several 1x1 chunks placed together: `Maps/chunk-groups.json` (`{ "<pool>": [ { "name", "members":
[ { "chunk", "cellX", "cellZ" } ] } ] }`), seeded into `ChunkGroups`/`ChunkGroupMembers` on every start. Its members
are in no pool on their own, fill a whole rectangle of cells, and declare exits only on outer edges (the seeder refuses
anything else). `ProceduralLayoutGenerator` places a group as one main-path step on free cells, turned as a whole: cell
`(x, z)` of an `sx` by `sz` group goes to `(z, sx-1-x)` for rotation 1 (`ChunkGroupRotation`), and every member gets
that rotation, which is the same as turning the whole group about its centre, so `ChunkRotation` and the client are
unchanged and the client sees ordinary 1x1 chunks. A group joins the layout only through an exit on one of its outer
edges. Groups are placed on the main path only, never on a branch: a group with a `boss` slot only as the main path's
last step, every other group at most once per layout and no earlier than the config's `MinSetPieceStep` (the boss's
group ignores it). The forest's is 8, the step its 5-8 band begins (owner decision), so a party leaving the entry never
meets a set piece's level 5-8 packs and Alphas in the next cells; the generator draws no number for a group it does not
offer, so a pool without groups is unaffected. The forest's main path is 12-16 steps (raised from 10, owner decision),
so every run has at least three steps a set piece may take: 690 non-boss set pieces in 1000 seeds (545 with a minimum
of 10). The forest has three: `forest_clearing_big`,
`forest_grove_ruin` and the boss arena `forest_arena`. `forest_boss_01` is no longer in the forest pool (its files stay
in the catalog), so the arena ends every run.

A layout attempt that fails (no free cell for the next step, say) is retried with the next seed, up to 10 attempts
(`ProceduralLayoutGenerator.MaxRetries`); `CommittedForestGenerationShould` generates the committed forest for 1000
seeds and fails if any of them runs out of attempts.

**`chunk-groups.json` is required for the forest.** Its `boss` and `leader` slots exist only in set pieces, so without
the file the spawn table's `boss`, `leader` and `leader_pack` entries match no slot in the pool and the seeder refuses
the whole catalog (the tag check under "Spawn tables and procedural map configs"). The same holds for any pool whose
spawn table names a tag only its set pieces carry.

## Depth and depth bands

Every placed chunk records its depth: grid steps from the entry (0) over stitched connections, a group's inner edges
included. A procedural map's `ProceduralDepthBands` (rows owned by its `ProceduralMapConfig`) set the levels its
creatures roll at a piece of that depth, each creature rolled on its own; every set piece rolls from the highest band
and the boss stands at that band's top. A depth no band covers (the entry at 0, or a gap between bands), and every
creature on a map with no bands, rolls from its template's own range. Bands must not overlap, only the highest may be
open-ended, and levels start at 1 and do not run backwards; the World server refuses the map's config otherwise when it
loads its chunk library. The forest (map 2): depth 1-3 levels 1-3, 4-7 levels 3-6, 8 and deeper (and every set piece)
levels 5-8, the boss at 8.

A `leader` slot spawns its spawn-table `leader` entry (a Bramblemaw Alpha) and then a roll of its `leader_pack` entries
(Grey Fen Wolves, 2-3) spread around it, all at the slot's band. No slot carries `leader_pack`: a spawn table may name
it only when its pool has `leader` slots.

## Spawn tables and procedural map configs

Like chunks, town layouts, pools and chunk groups, a procedural map's data is files under `Maps/`, seeded by
`ChunkCatalogSeeder` on every World server start, not migrations:

- `Maps/spawn-tables.json`: `{ "<table name>": [ { "tag", "creatureId", "weight", "min", "max" } ] }`. A table is
  matched by name (its id is kept; a new one gets the highest id + 1) and its entries are replaced.
- `Maps/ProceduralMaps/<mapId>.json`: every `ProceduralMapConfig` field, the pool (`chunkPool`) and spawn table
  (`spawnTable`) by name, `depthBands`, the optional `minSetPieceStep` (0 when left out) and the optional `doodadRules`
  (none when left out). Matched by `mapTemplateId` (the file's name); its bands and doodad rules are replaced, each rule
  stored as a `ProceduralDoodadRules` row keyed by its place in the file from 1 (`Sequence`).
- A doodad rule is `{ "doodadTemplateId", "slotTag", "min", "max", "required", "minSpacing", "minDepth"?, "maxDepth"?,
  "visibleGate"?, "usableGate"? }`, a gate `{ "questId", "condition", "objectiveId"? }` with the condition a
  `DoodadGateCondition` by name (`notStarted`, `onObjective`, `completed`, `notCompleted`). See
  [doodads](doodads.md#placement) for how the rules are planned and placed.

Before writing anything the seeder refuses, naming the file, JSON it cannot read, a map file that leaves out a required
field (every number and flag but `forwardPortalTargetMapId` and `minSetPieceStep`), a map whose `MapTemplate` is missing
or not `Normal`, a back or forward portal target that is no `MapTemplate`, a depth band whose `maxLevel` is above the
highest `CreatureBaseStats` level (10 today), a pool or spawn table the files do not name, a group with a blank name,
a `creatureId` with no `CreatureTemplates` row, a doodad rule the world could not honour (below), and a spawn table
whose tags do not match the slot tags its pool's chunks (set pieces included) use, checked both ways: every entry tag is a slot tag (or
`leader_pack` beside `leader` slots), and every slot tag but `entry` and `empty` has an entry. Rows with no file are
left alone. A doodad rule is refused for a template with no `DoodadTemplates` row, a slot tag no doodad slot of the
pool (set pieces included) carries, `min` above `max` or `max` above 50, a `minSpacing` that is not a finite 0 or more
(the file may write `"NaN"`, refused by name), `minDepth` above `maxDepth` or either below 0, a gate condition that is
no condition name (or an `objectiveId` with any condition but `onObjective`, or none with it), a solid template at any
slot tag but `doodad_large` (the only slot its box fits), a gate on a solid template, and, for a required rule, a
solid template or a `max` above the pool's slots of its tag that its depth range reaches (the slots of
every chunk and set piece, the entry-capable chunks alone for a range ending at depth 0).
`CommittedForestDoodadsShould` proves the committed forest places every required doodad at a slot for seeds 0 to 999.
A change to these files takes a World server restart. Trade-offs: the balance simulator reads `HasData`
only, so it never sees these rows; the REST API's layout preview and observability read the database, so they show the
files as of the last World server start (a fresh database shows the old migration values until one has started); and
`SeedIntegrityShould` cannot see them, so `ChunkCatalogSeederShould` and `CommittedForestGenerationShould` (which seed
the committed `Maps/` into SQLite) check them instead.

## Updating existing chunks/towns

### Updating a chunk's geometry or slots

1. Edit the piece in `tools/Avalon.ChunkGen/ForestPieces.cs` or `TownPieces.cs` and rerun ChunkGen (`forest` or `town`). Same name MUST be retained — the seeder matches by name and does an UPSERT. Renaming creates a new template + leaves the old orphaned.
2. Restart the World server (it seeds from `Maps/`).
3. Re-export the navmesh vectors if the geometry changed.
4. Commit `Maps/Chunks/` (and `schema/vectors/`) in the server repo.
5. Re-vendor in the client and test there.

The `ChunkTemplate` row is updated (CellSize, Exits, SpawnSlots, PortalSlots, Tags). All maps using that chunk pick up the changes on next instance-build.

The chunks no tool writes (`forest_boss_01` and the four other `town_*` chunks) are only ever changed by editing their files directly.

### Updating a town layout

A building or prop in Glimmerdell moves in `TownPieces.cs` (see "Generated town squares"). Which chunk sits on which cell, its rotation, the entry and the portal targets live in `Maps/TownLayouts/<MapTemplateId>.json`:

1. Edit the layout file. `mapTemplateId` MUST match the existing town's id.
2. Restart the World server. The seeder validates the file (see "Creating a town", step 4), then wipes existing `MapChunkPlacement` rows for the map id and inserts the new set inside a transaction. Idempotent.
3. Test.
4. Commit the layout file.

### Renaming a chunk

Don't. The seeder matches by `Name` and would treat the rename as a NEW chunk + leave the old `ChunkTemplate` row orphaned. If you really need to rename:
1. Make the chunk under the new name.
2. Update every layout/pool/group that referenced the old name to use the new one.
3. Manually delete the old `ChunkTemplate` row from the DB (only safe if no remaining references).
4. Delete the old files from `Maps/Chunks/` (ChunkGen lists them but never deletes them).

### Adding a new chunk to an existing town

Add a placement to the town's `Maps/TownLayouts/<MapTemplateId>.json` and restart the World server; the seeder replaces the map's placements with the file's.

---

## Map teleportation

Portals route the player between maps. Each portal is one of two roles:
`Back` (returns to a previous map) or `Forward` (advances to the next).

### Adding a portal

1. Give the chunk a portal slot (`portalSlots` in its `.json`, written by ChunkGen from the piece data) at the desired
   chunk-local position, with `role` = `Back` or `Forward`.
2. For towns: on the placement in `Maps/TownLayouts/<MapTemplateId>.json` that holds this chunk, set
   `backPortalTargetMapId` or `forwardPortalTargetMapId` to the destination map's `MapTemplate.Id`
   (Glimmerdell's `town_nw_01` carries `"forwardPortalTargetMapId": 2`). Leave it out if the slot is decorative / not
   routed.
3. Restart the World server.

For procedural maps the targets come from
`ProceduralMapConfig.BackPortalTargetMapId` / `ForwardPortalTargetMapId` —
no per-placement override.

### Runtime flow

Server-side `PredefinedChunkLayoutSource.BuildPortals` reads the per-
placement targets and emits `PortalPlacement(role, world, targetMapId)`
into `ChunkLayout.Portals`. `PortalPlacementService.Place` materialises
them as `PortalInstance`s on the `MapInstance`. The wire packet
`SChunkLayoutPacket` ships the placements as `PortalPlacementDto[]`.

The client draws a marker per portal (`MapCompositionSystem`). Its
`PortalSystem` makes its own range test from the player's logical
position and offers the portal; E sends `CEnterMapPacket(TargetMapId)`.
The server validates proximity again (anti-cheat), resolves / creates the
target instance via `ChunkLayoutInstanceFactory`, calls
`World.TransferPlayer`, and emits `SMapTransitionPacket(Success, ...)`
followed by `SChunkLayoutPacket` for the new instance. The new instance
id is what makes the client's `NavmeshSystem` bake the new map.

Server resets `connection.LastInputSeq = 0` in `MapInstance.AddCharacter`
so the client's reset input sequence is accepted.

---

## End-to-end trace (what happens when a player enters a town)

1. **Client:** sends `CCharacterSelectedPacket`.
2. **Server `CharacterSelectHandler`:** loads the `Character` from DB, looks up the `MapTemplate` (walking a non-town map back to its town), calls `InstanceRegistry.GetOrCreateTownInstanceAsync(templateId, maxPlayers)`.
3. **Server `InstanceRegistry`:** if a town instance of that map has room, returns the least populated; if a build of that map is already under way, returns its task; otherwise starts one off the tick (`StartBuild` → `ChunkLayoutInstanceFactory.BuildAsync(template, ownerCharacterId: null, ct)`), which `World.PublishBuiltInstances` publishes on a later tick.
4. **Server `ChunkLayoutInstanceFactory`:**
   - `_resolver.Resolve(template)` → `PredefinedChunkLayoutSource` (for Town).
   - `source.BuildAsync(template, ct)` → reads `MapChunkPlacement` rows via repository, validates, builds `ChunkLayout` with `Seed=0`, `Chunks[]`, `EntryChunk`, `EntrySpawnWorldPos` (transformed from `EntryLocalX/Y/Z`), `Portals[]` (from chunk `PortalSlots`), `CellSize`, `Config=null`.
   - `_navBuilder.BuildAsync(layout, ct)` → `ChunkLayoutNavmeshBuilder` stitches chunk objs from `Maps/Chunks/<name>.obj` (filename via `IChunkLibrary.GetById(id).Name`), bakes via DotRecast `TileNavMeshBuilder.Build` with `NavmeshBuildSettings.Create()`.
   - Constructs `MapInstance` with the layout + a `MapNavigator` initialized from the bake.
   - `_portalPlace.Place(instance, layout, config: null)` registers Back portals (no Forward for null BossChunk).
5. **Server `CharacterSelectHandler` (after instance returned):**
   - For Town: overrides `CharacterInfo.X/Y/Z` with `instance.Layout.EntrySpawnWorldPos` (persisted DB coords are stale for hubs).
   - Sends `SCharacterSelectedPacket` with the `CharacterInfo`.
   - Sends `SChunkLayoutPacket` with `(seed, instanceId, cellSize, chunks[], entrySpawn)`.
6. **Client `MapCompositionSystem`:** spawns one entity per primitive of each chunk's vendored `chunks/<name>.gltf`, positioned with `chunkLocalToWorld` and turned `90° × rotation` about Y, plus markers for the entry and the portals.
7. **Client `NavmeshSystem`:** the new instance id starts a bake on a worker thread: `ChunkGeometry` stitches the vendored `chunks/<name>.obj` files with the same rotation, and `navmesh::NavMesh::Bake` bakes them with the mirrored build settings. `LoginFlowSystem` holds back the load report while it bakes.
8. **Client `PlayerMovementSystem`:** samples input, sends the input packet, predicts each step with `WalkStep` (clamp via `RaycastWalkable`, ground via `SampleGroundHeight`).
9. **Server `PlayerInputHandler`:** validates seq, integrates the same way (its own `MapNavigator.RaycastWalkable`), updates `entity.Position`, sends `SPlayerStateAckPacket`.
10. **Client on the ack:** if drift > 1mm, takes the server's position and replays the unacknowledged inputs; above 0.15 m it snaps, below that the correction is smoothed.

Identical bake on both sides (the same chunk `.obj` files plus the layout's obstacle boxes, the same Recast settings) keeps drift sub-mm in practice — so reconciliation rarely fires.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Server crashes at startup with "Unable to activate type 'PredefinedChunkLayoutSource'. Constructors are ambiguous" | Two public ctors on the source | Single ctor + static `ForTesting(...)` factory |
| Server: `Chunk obj not found: Maps\Chunks\N.obj` (where N is a number) | Bake using `TemplateId.Value` instead of `Name` for filename | `ChunkLayoutNavmeshBuilder` resolves name via `IChunkLibrary.GetById(id).Name` |
| Server: `No MapChunkPlacement rows for town map N` | `Maps/TownLayouts/N.json` missing | Add the layout file, restart the World server, commit `Maps/` |
| Server: `must have exactly one IsEntry placement` | 0 or >1 placements with `isEntry: true` | Edit the layout file so exactly one placement has `isEntry: true` |
| Server: `chunk 'X' has CellSize=Y but layout declares Z` | Mixed cell sizes in one layout | All chunks in a layout MUST have identical `cellSize` |
| Server: `unknown chunk names: ...` | Layout references a chunk that is not in `Maps/Chunks/` (or is misspelled); a pool says `pool 'P' names unknown chunks: ...` | Generate the missing chunk; or fix the name in the layout / pool file |
| Client: player spawns at wrong position | Persisted `Character.X/Y/Z` is stale | For towns, `CharacterSelectHandler` already overrides with `Layout.EntrySpawnWorldPos`. For procedural, persist position correctly when player exits map. |
| Client: player can't move (navmesh ready but pos stuck) | Navmesh raycast lookup miss | Verify chunk-axis convention: `RaycastWalkable` MUST query with the SAME coords used during bake (no X-flip) |
| Client: a chunk is missing, or has the old shape, or no prediction on a map | The client has not re-vendored since the server's chunks changed (a chunk the project does not carry fails the client's bake) | Rerun `scripts/vendor-chunks.ps1` in the client and commit `devproject/chunks/` |
| Client: chunks visible but "rotation wrong" | Either a chunk error OR the client's rotation mirror disagrees with the server | `ChunkRotation.LocalToWorld` is the source of truth; the client's `chunkLocalToWorld` mirrors it, and the vendored `rotation-v1.txt` carries the server's known answers for it |
| Layout valid but adjacency broken (chunks isolated) | Chunks have mismatched exits at neighbouring boundaries | Fix the chunks so each pair of neighbouring chunks has an aligned `(side, slot)` exit on both faces of the shared boundary |

## Conventions and gotchas

- **Chunk filename = `ChunkTemplate.Name`** (NOT `ChunkTemplate.Id`). Files are `Maps/Chunks/<Name>.obj` on the server and `devproject/chunks/<Name>.obj` (+ `.gltf`) in the client. Bake on both sides looks up by name.
- **The bake is deterministic** given identical input + identical `NavmeshBuildSettings`. Server (DotRecast) and client (Recast) baking the same chunk objs produce the same navmesh (modulo last-bit floating-point that's well within snap thresholds). Drift = build settings drift between repos. The constants live in `NavmeshBuildSettings.cs` server-side and `navmesh/src/BuildSettings.h` client-side; **keep them identical**.
- **`ChunkRotation.LocalToWorld`** is the source of truth for chunk-stitching geometry. Server's `ChunkLayoutNavmeshBuilder.AppendTransformed` calls it, and the client mirrors it in `common/include/common/scene/ChunkRotation.h`. **Both must agree** on the rotation case table (about the chunk centre):
  - `0: (x, z)` — identity
  - `1: (z, -x)` — 90°
  - `2: (-x, -z)` — 180°
  - `3: (-z, x)` — 270°
- **Coord discipline**: the bake takes chunk coords as-is (no axis flip); queries (`RaycastWalkable`, `SampleGroundHeight`, `FindPath`, `HasVisibility`) must also use them as-is — do NOT negate X.
- **Town spawn override**: returning players in towns have stale persisted `Character.X/Y/Z` (e.g. baked under the old world.bin pipeline). `CharacterSelectHandler.OnInstanceObtained` overrides with `Layout.EntrySpawnWorldPos` for `MapType=Town`; procedural maps still respect persisted coords (set by `EnterMapHandler` on traversal in).
- **A chunk change is a two-repo change**: the server's `Maps/` (and vectors), then a re-vendor in the client. The client's `CHUNK-MANIFEST` records the server revision it copied from.
- **Email policy**: every commit on Avalon.Server MUST be authored by `Nuno Silva <nuno.levezinho@live.com.pt>`. Subagents that commit MUST `unset GIT_AUTHOR_*` before `git commit`. Verify after with `git log -1 --pretty=format:'%ae %an'`.
- **Plans + specs gitignored** in `docs/superpowers/`. They stay local. Regular `docs/` (this file) is committed.
