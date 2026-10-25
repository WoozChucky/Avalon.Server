# Map Generation

End-to-end guide: from chunk files → server bake → playable client.

## Concepts

The world is built from **chunks** — modular 30×30m geometry blocks (cell size configurable per chunk; current standard is 30). Chunks are stitched into **layouts**. Layouts feed two pipelines:

- **Town maps** (MapType.Town) — chunk placements are PREDEFINED in the database (`MapChunkPlacement` table). Multi-player shared instances. Same layout every time.
- **Procedural maps** (MapType.Normal) — chunk placements are GENERATED at runtime by `ProceduralChunkLayoutSource` using a chunk pool + RNG seed + per-map config (`ProceduralMapConfig`). Per-player private instances.

Both pipelines emit a `ChunkLayout` record (chunks, entry spawn, portals, cell size, optional config + seed, and the scenery ring drawn round it). One factory (`ChunkLayoutInstanceFactory`) builds a `MapInstance` from the layout, bakes a navmesh from the stitched chunk geometry via DotRecast, and ships the layout to the client over `SChunkLayoutPacket`: its chunks, entry spawn, portals, the solid doodads' obstacle boxes and the scenery ring's placements. The client bakes its own navmesh from the chunk `.obj` files plus the layout's obstacle boxes (`SChunkLayoutPacket.Obstacles`, which it decodes and appends after its chunks) for prediction parity (see [doodads](doodads.md#what-todays-client-does)); the scenery ring is drawn, never baked, on either side (see "Scenery ring").

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
        (Chunks[], EntrySpawn, Portals[], CellSize, Scenery[])
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
 (seed, instanceId, cellSize, chunks[], entrySpawn, portals[], mapId, obstacles[], scenery[], sceneryNames[])
                           │
                           ▼
       Client: MapCompositionSystem (draws the vendored chunk meshes and decor + markers)
       Client: NavmeshSystem → navmesh::NavMesh::Bake (mirror bake from the vendored .obj + obstacle boxes)
       Client: PlayerMovementSystem (predict + reconcile)
```

### Query storage after the bake (#1116)

`MapNavigator.LoadFromNavMesh` constructs a `ReusingNavMeshQuery` before the finished instance is published to the
tick. Its constructor prepares the path and surface node pools, open heap and surface queue for the whole mesh:
one state-zero node per polygon, plus each incoming tile-link state used by `FindPath`. It then clears the searches
while retaining their storage. A first long or unsuccessful route can therefore use the full mesh without growing
those collections on the tick. There is no search cap or change to polygon discovery order.

The mesh must be finished before constructing the query; adding or removing tiles afterwards is unsupported, as
it is for `StandingPolygonSearch`. Each query still belongs to one navigator and serves one thread at a time.
This reserves memory during construction and makes sparse searches clear larger dictionaries; the measured costs
and first-use allocation checks are in [benchmarks](benchmarks.md#navigation-search-storage--navigationstorageharnesscs-1116).

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
| `src/Server/Avalon.World/Respawn/MapArrival.cs` | Shares the `SChunkLayoutPacket` encoded during instance construction behind every arrival: a character select (`CharacterSelectHandler`), a portal entry (`EnterMapHandler`), a return to town and an item's teleport (`MapLayoutEntries`, #1130) |
| `src/Shared/Avalon.Network.Packets/World/SChunkLayoutPacket.cs` | Wire format (fields 9 and 10, `SceneryPlacementDto` and the piece names: the scenery ring) |
| `src/Server/Avalon.World.Generation/SceneryRing.cs`, `SceneryPool.cs`, `SceneryFile.cs`, `SceneryPlacement.cs` | The scenery ring: its cells and the forest's ring built per layout (`SceneryRing`), the validated pool, the ring file reader, a placement (see "Scenery ring") |
| `src/Server/Avalon.World/ChunkLayouts/SceneryCatalog.cs` | Reads every map's ring file once at start-up; the layout sources read it |
| `src/Shared/Avalon.Domain/World/ChunkTemplate.cs` | DB entity for chunk metadata |
| `src/Shared/Avalon.Domain/World/MapChunkPlacement.cs` | DB entity joining MapTemplate → predefined chunk placements |
| `src/Shared/Avalon.Domain/World/ProceduralMapConfig.cs` | DB entity for procedural map RNG config |
| `src/Server/Avalon.Server.World/Maps/Chunks/<chunkName>.obj` + `.json` | Chunk geometry (navmesh bake) and metadata (see "The chunk format"). The forest pieces and Glimmerdell's four squares are written by `tools/Avalon.ChunkGen`; the rest (`forest_boss_01`, `town_corner_01`, `town_house_01`, `town_path_01`, `town_square_01`) are committed as they are and no tool writes them |
| `src/Server/Avalon.Server.World/Maps/Decor/<chunkName>.json` | What the client draws on a generated town square, on every generated forest piece and on every scenery piece from the art kits (see "Decor and building recipes", "Dressing the forest" and "Scenery ring"). Written by `tools/Avalon.ChunkGen` beside the chunks, outside `Maps/Chunks/` because the seeder reads every `*.json` there as chunk metadata; the server never reads it |
| `src/Server/Avalon.Server.World/Maps/Scenery/<name>.obj` + `<mapTemplateId>.json` | Each scenery piece's drawn terrain, and each map's ring file (the town's placements, the forest's pool), written by `tools/Avalon.ChunkGen` (see "Scenery ring"). The World server reads the ring files at start-up; nothing bakes the terrain, and the seeder never reads the directory |
| `src/Server/Avalon.Server.World/Maps/TownLayouts/<MapTemplateId>.json` | Town layouts: which chunk sits on which grid cell, edited directly (see "Creating a town"). ChunkGen writes the squares' geometry, not this file |
| `src/Server/Avalon.Server.World/Maps/chunk-pools.json` | Procedural pool membership (`{ "<pool>": ["<chunk>", …] }`, a member also `{ "chunk", "weight", "max" }`: see "Define a ChunkPool"), edited by hand |
| `src/Server/Avalon.Server.World/Maps/chunk-groups.json` | Set pieces per pool (see "Chunk groups (set pieces)"), edited by hand |
| `src/Server/Avalon.Server.World/Maps/spawn-tables.json` + `ProceduralMaps/<mapId>.json` | Spawn tables and procedural map configs with their depth bands (see "Spawn tables and procedural map configs"), edited by hand |
| `tools/Avalon.ChunkGen/` | Generates the forest pieces' and the town squares' `.obj` + `.json`, the decor files of the squares and of every forest piece, and both maps' scenery rings (`Scenery/`, `ForestScenery*.cs`, `TownScenery*.cs`; see "Generated forest chunks", "Generated town squares" and "Scenery ring") |
| `tools/Avalon.ChunkGen/Recipes/<id>.json`, `tools/Avalon.ChunkGen/Kit/kit-bounds.json`, `tools/Avalon.ChunkGen/Kit/forest-footprints.json` | Building recipes (kit pieces plus collision proxies), the art kits' bounds table, and the custom forest models' solid footprints (see "Decor and building recipes" and "Dressing the forest") |
| `tools/Avalon.Exporter/` | Exports the known-answer vectors the client vendors, among them `schema/vectors/navmesh-v1.txt` (`-- navmesh`) and `schema/vectors/rotation-v1.txt` (`-- rotation`) |
| `src/Server/Avalon.Database.World/Seeding/ChunkCatalogSeeder.cs` | Seeds chunk templates, town layouts, pools, chunk groups, spawn tables and procedural map configs from `Maps/`; run by the World server on start |

### Client (`C:\dev\vulkan-multithreaded`)

| Path | Purpose |
|---|---|
| `scripts/vendor-chunks.ps1` | Copies every `Maps/Chunks/*.obj` into `devproject/data/world/chunks/`, converts each to a `.gltf` beside it (one primitive per `usemtl` group, coloured from the script's palette: `stone`, `wood`, `roof`, `cloth`, `cloth_2`, `plaster`, `metal`, `water`, `forest_floor`; a `collision` group is not drawn), copies the decor files (`Maps/Decor/*.json`) byte for byte into `devproject/data/world/decor/`, and writes `CHUNK-MANIFEST` (the server revision it copied from, plus a SHA-256 per file). The chunk `.json` files are not vendored. An unknown material name or an OBJ directive other than `o`, `v`, `f`, `usemtl` fails the script. `Maps/Scenery/` and the scenery materials are not vendored yet (see "Scenery ring") |
| `scripts/regen-proto.ps1` | Vendors the server's schema set; among it `schema/vectors/navmesh-v1.txt` → `gamenet/schema/navmesh/navmesh-v1.txt` and `rotation-v1.txt` → `gamenet/schema/chunks/rotation-v1.txt` |
| `game/src/MapCompositionSystem.cpp` | Draws the server's layout from `SChunkLayoutPacket`: one entity per primitive of each chunk's `.gltf`, and one static batch of its decor file's items, placed with the mirrored rotation, plus markers for the entry and the portals |
| `common/include/common/scene/ChunkRotation.h` | `chunkLocalToWorld`, the client's mirror of `ChunkRotation.LocalToWorld` |
| `game/src/ChunkGeometry.cpp` | Stitches the vendored `.obj` files (not the `.gltf`) into one triangle soup for the bake, then appends the layout's obstacle boxes as the server does |
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
    { "role": "Back", "localX": 15, "localY": 0, "localZ": 5 }
  ],
  "doodadSlots": [
    { "tag": "doodad_large", "localX": 27, "localY": 0, "localZ": 27, "yaw": 180 },
    { "tag": "doodad", "localX": 3, "localY": 0, "localZ": 3, "yaw": 0 },
    { "tag": "doodad", "localX": 27, "localY": 0, "localZ": 3, "yaw": 0 }
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
- **doodadSlots** (optional, after `portalSlots`; left out when a chunk has none, as in `forest_boss_01`):
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
   `devproject/data/world/`.

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

Avalon.Server: `Maps/` (and `schema/vectors/` if re-exported). Client: `devproject/data/world/` (and `gamenet/schema/` if re-vendored).

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
refuses, naming the file, the pool and the chunk, a weight of 0 or below or infinite, a weight of NaN (which the file
can write only as the string `"NaN"`, and which is refused like any other bad weight), a `max` below 1, any other key
(a misspelt `"max"` would leave the chunk uncapped), or a chunk named twice in one pool.

`ProceduralLayoutGenerator` weighs every pick: the entry among the entry-capable members, and each later step among its
candidates, one per member and turn that fits (a set piece weighs 1), so a member's chance is its weight times the turns
that fit. A member at its `max` is left out of the candidates for the rest of that layout. Candidates that all weigh the
same are picked with the one draw a uniform pick always made, so a pool of equal weights and no caps generates the
layouts it always did. The generator walks a pool in chunk id order whatever order it is handed (and
`ChunkPoolRepository` reads the rows in that order), so the database's row order cannot change a layout; that is also
the order `LayoutConfigVersion` hashes the members in. A cap is part of `LayoutConfigVersion` only when set, so a pool
without caps keeps its fingerprint; a changed weight or cap changes what an old seed generates.

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

The forest pieces are written by `tools/Avalon.ChunkGen`: thirty-two single pieces and three set pieces, every forest
chunk but `forest_boss_01` (hand-made, out of the pool). Nine of the singles (`forest_entry_01`, `forest_path_01` to
`_04`, `forest_junction_t`, `forest_junction_x`, `forest_clearing_01` and `forest_deadend_01`) were bare floors
written by hand until forest dressing (#1109) ported them, their exits, spawn slots, tags and the entry's portal as
they were; six of those floors had their top at y 0.05, and every forest floor now has it at 0. Forest dressing also
added fifteen, three of each exit shape (see "Overhangs and the fifteen pieces of #1109" below). Each is a flat 30 x
30 m floor (the forest slab, y -0.2 to 0) with blockers on it, boxes for rocks, ruined walls and tree walls and
twelve-sided cylinders for tree clusters, trunks and pillars, 3 m tall. Blockers cut the navmesh (the agent climbs 0.9
m), so creatures path around them and walkable rays (movement, ground-target casts, projectiles) stop at them. What
the client draws is the piece's decor file (see "Dressing the forest" below); the blockers are its collision.

    dotnet run --project tools/Avalon.ChunkGen -- forest
    dotnet run --project tools/Avalon.ChunkGen -- forest --maps <Maps directory>

writes every piece's `.obj` and `.json` into `Maps/Chunks/` and its decor file into `Maps/Decor/` (`Maps/` is
`src/Server/Avalon.Server.World/Maps/` unless `--maps` names another). Before writing, it reads every piece back
through the World server's own catalog reader (`ChunkCatalogSeeder.ReadCatalogAsync`) and bakes each piece, and each
set piece's four members together, with `ChunkLayoutNavmeshBuilder` in a temporary copy of `Maps/`, and stages the
forest's scenery there too (each piece's terrain in `Maps/Scenery/`, its decor in `Maps/Decor/`, the ring file
`Maps/Scenery/2.json`) once the scenery rules pass, its names checked against the staged chunks' and the ring file read
back through the World server's reader (`SceneryCatalog.Load`, see "Scenery ring"); if anything fails it exits 1 and
nothing is written. After writing, it lists every `forest_*` file in `Maps/Chunks/`, `Maps/Decor/` and `Maps/Scenery/`
it did not generate (`forest_boss_01`, or one left over from a renamed piece) and deletes none of
them. Edit `tools/Avalon.ChunkGen/ForestPieces.cs` and rerun it; `ForestPiecesShould` fails when the committed files
(decor included) differ from a fresh run, when a slot is within 2.5 m
of a blocker or 2 m of the chunk's edge, when a blocker stands in an exit's throat, or when a slot or exit cannot be
reached on the baked navmesh. The tool writes LF line ends, and `.gitattributes` pins every `.obj` and `.json` under
`Maps/` to LF, so a regeneration on any machine is an empty diff. The tool does not touch `chunk-pools.json` or
`chunk-groups.json`: add new single pieces to the pool, and new set pieces to the groups file, by hand.

Every generated forest piece also carries doodad slots (`DoodadSlots` on the piece, `Doodad(x, z)` and
`LargeDoodad(x, z)` in `ForestPieces.cs`, `SetDoodad` and `SetLargeDoodad` in a set piece's frame): each single piece two `doodad` slots and one `doodad_large`, each
set-piece member one `doodad` slot, and each of the arena's members a `doodad_large` too; a set piece lists them in
its 60 x 60 m frame and they are split into its members as spawn slots are. Doodad slots are written on the floor, at
`localY` 0 (`ChunkPiece.DoodadSlotY`, the floor's top), and spawn slots 1 m up, at `localY` 1. The hand-made
`forest_boss_01` has none, and the tool writes `"doodadSlots"` only for a piece that has some, so the town squares'
files are unchanged by it. The entry's slots stand in its corners (its tree walls stop short of them): the placement
drops a slot within 15 m of the entry spawn or 8 m of a portal (see `docs/doodads.md`), and the corners are the only
spots of the entry clear of both. A piece's portal slots (`PortalSlots`, `forest_entry_01`'s `Back` at (15, 5)) are
written at `localY` 0 (`ChunkPiece.PortalSlotY`), and `ForestPiecesShould` holds them to the spawn slots' rules: 2.5 m
from any blocker, on the navmesh floor, and reached. `ForestPiecesShould` keeps every doodad slot off the paths and
clear of the rest, with a `doodad_large` slot further away because it may hold a solid doodad:

| From | `doodad` | `doodad_large` |
|---|---|---|
| any blocker | 2.5 m | 4 m |
| a path: in a single piece, the segment from an exit's throat centre to the piece centre (15, 15); in a set piece, checked in its 60 x 60 m frame, the segments from each outer exit's throat centre to the frame centre (30, 30) and between every two outer exits | 4 m | 5 m |
| any spawn slot | 3 m | 4 m |
| the chunk's edge | 2 m | 2 m |

and never inside an exit's throat (10 m wide, 6 m deep, centred on its side); every doodad slot stands on the baked
navmesh floor, and is a reachability target too: a walk over the piece's (or the set piece's) navmesh reaches it from
the first exit, as it reaches every exit and spawn slot. Each yaw is the quarter turn that best faces the piece's centre
(a set piece's, for its members), worked out from the slot's position (`ForestPieces.Facing`; a tie goes to 0 or 180),
and `ForestPiecesShould` checks it.

### Dressing the forest

Every generated forest piece gets a decor file, `Maps/Decor/<chunk>.json`, in the town's format (see "Decor and building
recipes") with the kit `stylized-nature-megakit-standard`: the Stylized Nature MegaKit's trees, rocks, plants and
stepping stones, and the custom `avalon_*` forest models (rocks, logs, ruins, the dirt path), at any whole-degree yaw
and a scale. `ForestDresser` writes it, a set piece's in its 60 x 60 m frame, split into its members by the cell each
item stands in, as its slots are. It is deterministic: one `Random` seeded from the FNV-1a hash of the piece's name,
whole-degree yaws turned through a table of sines rounded to 9 decimals, positions written with at most 3 decimals, so a run on
any machine writes the same bytes. Its densities are named constants in `ForestDresser`.

**Blockers carry what draws them.** Every forest blocker has a `Dressing` role, set in `ForestPieces.cs`:

| Role | Shape | What the dresser draws on it |
|---|---|---|
| `TreeDrum` | cylinder | two to four trees (by the piece's tree mix) inside the drum near its rim, their trunks within its flat sides; a ring of bushes, ferns and tall plants just inside the rim; a kit rock or a bush at its heart |
| `Rock` | box or cylinder | the custom large or leaning rock that fills it best, if one fits (centred, at scale 1), then up to five kit rocks (`Rock_Medium_*`, scaled, any yaw), each on what is still barest, until 80 % is covered; on a box of 25 m² or more, half the time a tree among the rocks; bushes on the corners rocks leave bare |
| `RuinWall` | box | `avalon_ruin_wall_*` sections end to end along its long axis, a pillar where they leave room at an end, stumps along the far side of a box thick enough, rubble over it |
| `Pillar` | cylinder | `avalon_ruin_pillar_1` and a `_2` stump side by side, rubble at its foot |
| `Trunk` | cylinder | the free-standing tree of its `Tree` (species and scale; the dresser picks the variant and the yaw) |
| `TreeWall` | box | two rows of trees (three in a wall 3 m deep or more) along the frame edge the box stands against, each row's trees 1.5 to 2.1 m apart and staggered against the next row's, scale 1 to 1.6, every trunk inside the box; the piece's tree mix, a dead tree less often and at most 1.3, never a twisted one; ferns, tall plants, grass and bushes among the trunks, and a skirt of undergrowth (kept small) along its open face |
| `Log`, `LeaningRock`, `ArchLeg`, `Overhang` (and `Rock`, `RuinWall`, `Pillar` when it has one) | the model's footprint | the custom model of its `Prop`, once for all its parts |

A free-standing tree stands on its own trunk cylinder, `ForestDresser.Trunk(species, scale, x, z)`: its radius is the
species' trunk radius (`ForestDresser.TrunkRadius`, measured from the kit's bark below 1 m, the widest of the five
variants: `CommonTree` 0.65 m, `Pine` 0.45, `DeadTree` 0.75, `TwistedTree` 1.4) times its scale, over cos 15° so the
twelve-gon's flat sides reach the trunk, rounded up to 5 cm. A custom model's blockers are exactly its footprint parts
(`ForestDresser.Prop(model, x, z, yaw)`, read from `tools/Avalon.ChunkGen/Kit/forest-footprints.json`): a box part as a
box, so the model turns only by quarter turns, a circle part as a cylinder of its `blockerRadius`, so a model whose
parts are all circles turns only by multiples of 30 degrees, and every custom solid model stands at scale 1. Today's
pieces add a fallen log (three a set piece) and a leaning rock in `forest_rocks_01` and `forest_glade_01`; the drums,
rock boxes, walls and pillars are unchanged. The nine ported pieces have one or two tree drums or custom rocks each
(`avalon_rock_large_*` placed by `ForestDresser.Prop`, so their blockers are the rocks' footprints) and a log;
`forest_clearing_01` has four small drums in its corners instead.

**Tree walls.** A free-standing tree needs a blocker of its own, so a wood of them would cost a blocker a tree. The
density comes from tree walls instead (owner decision, #1109): one `TreeWall` box, about 3.5 m deep (from 0.1 m inside
the edge to 3.6 m), along every closed side of a single piece, and in a piece with at most one closed side (the T
junctions and the crosses) in the corners between its exits too, outside the throats; a set piece has one along each
closed outer edge of each of its cells, never touching a cell edge. A wall is made shallower (down to 2 m deep) or
shorter where it would come within 0.3 m of a drum, a rock or ruin box, a log, a custom rock or a trunk (none of them
moved), or too near a slot, and stops 0.1 m short of a throat; where a side's depth would change over runs shorter
than 6 m, those runs make one box at the shallowest of them (fewer boxes, a simpler navmesh), and a wall shorter than
2.5 m is left out. The free-standing edge trees the walls replace are gone; the few left stand on the exit sides or
inside, and `forest_ruin_01`, a crossroads whose corners hold its ruins, has trees inside its two ruined corners and
in a third. The dresser packs each wall with trees (the roles table above), its rows along the frame edge the box
stands against, which more than trebles the forest's trees (1,775 in the 44 decor files, from 492): 26 to 73 a single
piece, 97 to 134 a set piece, but 18 in each of the ruined crossroads `forest_ruin_01` and `forest_ruin_05`; a piece's
blocker count stays about where it was (7 to 18 a single piece, from 8 to 15; a set piece 20 to 27, from 34 to 37).
`ForestDecorRules` holds a wall to at least one tree for every 3 m of its length, each trunk counted for one wall
only, and `ForestPiecesShould` to a tree wall along every closed side. Doodad slots near a closed side moved inward,
each to the nearest spot within 7 m that keeps every slot rule above (and, in a set piece, stays in its cell), its yaw
worked out again; a slot with no such spot stays where it was and the walls stop short of it, leaving it a nook, as
the entry's three corners do. The trees keep clear the lines the navmesh vectors' `forest-floors` rays run along (the
Exporter's `navmesh` artifact): that layout turns `forest_clearing_01` and `forest_path_03` so its seam crossings run
exit to exit past the walls.

**Overhangs and the fifteen pieces of #1109.** Fifteen single pieces were added with the dressing, three of each exit
shape, eight to twelve blockers each before the tree walls (tree drums, rock boxes and cylinders, ruin walls and
pillars, logs and an edge tree line, which the walls replaced), every slot rule above kept:

| Shape | Pieces |
|---|---|
| dead end | `forest_hollow_02` (its meeting point at (15, 17), deeper in), `forest_ruin_03`, `forest_mire_01` |
| straight | `forest_rocks_03` (the arch), `forest_grove_01`, `forest_thicket_03` |
| corner | `forest_rocks_04` (the overhang), `forest_glade_02`, `forest_mire_02` |
| T junction | `forest_ruin_04`, `forest_grove_02`, `forest_thicket_04` |
| cross | `forest_glade_03`, `forest_ruin_05`, `forest_grove_03` |

Overhangs live in two single pieces only, both tagged `overhang` in the catalog, never in a set piece:
`forest_rocks_03` stands `avalon_rock_arch` over its meeting point, its legs either side of both paths (4.3 m apart,
the strip 2.8 m wide) and its span over them (its underside 2.95 m up, the agent 2 m tall); `forest_rocks_04` stands
`avalon_rock_overhang` in the corner away from its exits, its ledge (its underside at least 2.7 m up) reaching west
over open floor, side-on to the three-quarter camera. Only the standing parts are blockers (the arch's legs, the
overhang's base, as `ForestDresser.Prop` reads their footprints); the span and the ledge are drawn and walked under.
Owner: one, at most two, chunks of a run carry an overhang. In `chunk-pools.json` each of the two is `{ "weight": 1,
"max": 1 }` and every other forest single `{ "weight": 0.5 }`: the cap makes two the most a layout can hold, the
weight twice the others' makes one usual, and the singles' half weight keeps the set pieces (which weigh 1) about as
frequent as they were with seventeen singles. Over the committed 1000 seeds 156 layouts hold none, 449 one and 395 two
(160, 457 and 383 before #1113; `CommittedForestGenerationShould` requires at most two in every layout and at least
one in 80 %).

The fifteen make forests bigger: six of them are T junctions or crosses, so a run branches more. Over seeds 1 to 64 a
forest instance places 237 creatures on average (134 to 335), where the seventeen singles placed 208 (137 to 271);
seeds 1 to 4 went from 200, 269, 173 and 200 to 214, 288, 187 and 286 (measured at #1109; #1113 gave 12 of those 64
seeds other layouts, none of seeds 1 to 4).

**Collision tagging.** A dressed blocker is written `usemtl collision` in the chunk `.obj`, which the client does not
draw, and the floor `usemtl forest_floor`, which the client gives its ground look; the server ignores both.

**Paths.** `ForestPaths` lays a worn dirt strip from the midpoint of every exit's side to the piece's meeting point
(its centre, or its set piece's frame's centre, unless `DressingHints` sets one): `avalon_path_dirt_2m` segments, 2 m
long and 2.8 m wide (`ForestPaths.Width`, the same for every piece), end to end, the first running straight in from
the side, so a path always meets its neighbour's at the shared edge whatever either chunk's rotation. After the first
segment a path wanders up to 1 m off the straight line, bending at most 6 degrees a segment (`ForestPaths.MaxBend`),
and ends within 1.6 m of the meeting point, under an `avalon_path_dirt_hub` patch there. Each segment after the first
starts 0.2 m back over the one before (`ForestPaths.Overlap`, more than half the width times sin 6°, 0.146 m), which
covers the wedge a bend opens at the strip's outer edge; every other segment is raised 2 mm, so that overlap never
draws two strips at one height (the hub's top is above both). About every other segment carries a `RockPath_*`
stepping stone.

**Undergrowth and ground cover.** Along every edge, outside the exits' throats and the tree walls, a 3.5 m band of
ferns, bushes, tall plants and tall grass; on the open floor, off the strip and the blockers and clear of the doodad
slots (1.5 m, 3 m from a `doodad_large`) and 3 m from a portal slot, grass in clumps (most), ferns, clover, plants,
pebbles, a few small bushes and very few flowers, and mushrooms at the feet of trees, drums and logs. Off a blocker,
cover is scaled down to be drawn no taller than 1.2 m, so what a player walks through on open floor reads as small;
bushes and tall plants at full size stand only on a drum or a rock box. The kit's red-leaved `Bush_Common` is used
once a frame at most, and a `TwistedTree` only in an old wood (`TreeMix.Old`), once a frame at most. The piece's
`DressingHints.Trees` picks the tree mix: `Mixed`, `Broadleaf`, `Pine` or `Old`.

**The rules.** `ChunkFiles` runs `ForestDecorRules.Check` on every piece's (and set piece's) decor before anything is
written, and `ForestPiecesShould` runs it again over the committed files. It refuses, naming the piece and the model,
part or exit:

- an item that is not a forest model in the bounds table, or that stands outside the frame;
- a solid part that does not lie within 0.15 m of a blocker: a tree's trunk circle, a kit rock's scaled and turned box,
  or each footprint part of a custom model (off scale 1 or its yaw rule, it is refused outright): nothing drawn solid
  where a player walks;
- a blocker with no solid part within it: no invisible wall; a tree wall with fewer trees than one for every 3 m of its
  length (`ForestDecorRules.TreeWallSpacing`, its trunks within it), so a wall always reads as a wood;
- an exit with no 2.8 m strip ending on its side's midpoint and running straight in, a strip that breaks off before
  the hub (each segment must start 0.2 m back over the last one's end along its own direction, within 0.01 m, so a
  start ahead of the end or beside it is refused), a segment at another scale,
  outside the frame or on no exit's path, and anything but one hub;
- a segment that bends more than 6 degrees from the one before it, or lies at its height; two segments that overlap at
  one height outside the hub (two paths' strips drawn over each other would z-fight);
- a blocker reaching onto a strip; ground cover within 2 m of a strip's centre line or 3 m of the hub (stepping stones
  only stand on the strip, within 0.9 m of its centre line, or on the hub);
- inside an exit's throat, ground cover other than grass or cover drawn no taller than 0.6 m: nothing hides the way out,
  though grass beside the strip at the edge is fine;
- walk-through cover drawn taller than 1.2 m (its model's top times its scale) that does not stand within a blocker.

The bushes on the corners a rock box's rocks leave bare, and how much of the box the rocks cover, are the dresser's
habit, not a checked rule: the rules ask only that each blocker has something solid on it.

`ForestPiecesShould` also turns every chunk the tool writes through the four rotations (`ChunkRotation`) and checks
that a strip ends on each exit's rotated side, centred on its midpoint and 2.8 m wide.

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
  instance, and encodes it into the cached layout packet (#1130). The scenery ring follows it in fields 9 and 10
  (see "Scenery ring"). Today's client decodes the boxes
  (`gamenet/src/session/SessionMap.cpp`) and bakes them after its chunks (`game/src/ChunkGeometry.cpp`).
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

stages, validates (`ChunkCatalogSeeder.ReadCatalogAsync`), bakes the four squares together and writes them, with the
town's scenery ring (its 21 pieces and `Maps/Scenery/1.json`, see "Scenery ring"), as the forest command does; a
square breaking a layout rule is refused before anything is written (`TownRules`: nothing within 2 m of a wall, the
doorway lanes and the arrival-to-portal corridor clear, 2.5 m of headroom under every flat roof,
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

An item may also carry `"scale"`, a uniform scale about the model's origin, in [0.001, 4] (a scale its 3 decimals
would write as 0 is refused), written only when it is not 1, so a file whose items are all at their kit size has none.
The yaw rule follows the file's `kit`: a town square's (`medieval-village-megakit-standard`) keeps quarter turns, since
its pieces meet on a grid; a forest piece's (`stylized-nature-megakit-standard`) may carry any whole degree, 0-359,
so trees, rocks and ground cover do not all face four ways. `DecorItem.Check` takes the rule from its caller
(`DecorYaw.QuarterTurns` or `DecorYaw.AnyDegree`) and refuses anything else; a scenery piece's file allows any whole
degree whatever its kit (see "Scenery ring").

An item drawn from another kit than its file's names its own, `"kit"`, written last on its line and only when it
differs from the file's, so one file may mix kits: the town ring's files are the village kit's with the nature kit's
bright trees, grass and flowers in them, and the forest's town-ward fields carry the village kit's wheat, hedges,
fences, field walls, hay bales, haystacks and a scarecrow. A decor item's kit is one of three:
`medieval-village-megakit-standard` (the village kit and the custom town props), `stylized-nature-megakit-standard`
(the nature kit with its colours darkened for the forest, and the custom forest models) and
`stylized-nature-megakit-bright` (the nature kit's models in their own bright colours, none of the custom forest
models). `fantasy-props-megakit-standard`, the props kit, is no decor kit: its models are drawn as doodads.
`DecorItem.Check` refuses an item whose kit (its own, else the file's where the caller passes it: the scenery rules,
the forest's decor rules and `TownBuilding.Place` do; a town square's own check, which also sees its doodad-drawn
props-kit models, does not) is not a decor kit or does not hold its model, as the bounds table records each
model's kit.

ChunkGen writes one for every generated square (`"items": []` when it has none) and every generated forest piece,
staged and checked with the chunks and written only when they are, items sorted by model, then x, z, y, yaw and
scale, numbers with at most 3 decimals, LF line ends; `TownPiecesShould` and `ForestPiecesShould` compare them with a
fresh run as they do the chunks, and a decor file with the
command's prefix that the run did not write is listed, never deleted, as the chunks are. The seeder never reads
`Maps/Decor/`. A proxy part that decor draws is written `usemtl collision`, which the client does not draw: every
proxy of a placed building, the square's walls when `TownSquare.WallsAsDecor` is set, and the floor when
`TownSquare.Paved` is.

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
box scaled by its scale, turned by its yaw and moved to its position (`DecorRules.WorldBounds`), must lie within 0.15 m
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
spent, no script, which could despawn it or hide it through `CanSee` (the same test). There are two today.
Glimmerdell's notice board (`avalon_notice_board` at local (9, 24), yaw 90, in `town_sw_01`) left
`Maps/Decor/town_sw_01.json` and is doodad 6, the board of quest 9's objective 902. The Resonance Workbench (#1179,
the props kit's `Workbench`, 2.02 x 1.02 m, at local (6, 4), yaw 0, in the market `town_se_01`, so world (36, 0, 4))
was added as a doodad-drawn prop from the start, so it added a box to `town_se_01.obj` and a piece to the town's count;
it is doodad 7, where a player identifies items ([doodads](doodads.md#the-resonance-workbench)). It stands in the
market's open south-west, its nearest corner about 9.7 m from the walking benchmarks' loop centre, outside the 7-9 m
band kept clear.

The bounds table, `tools/Avalon.ChunkGen/Kit/kit-bounds.json`, holds each model's box in its own space, so pieces can
be checked against the art without the kit, which stays out of the repository:

    dotnet run --project tools/Avalon.ChunkGen -- kit-bounds --kit <glTF directory> [--props <glTF directory>] [--props-kit <glTF directory>] [--nature-kit <glTF directory>] [--forest-props <glTF directory>]

measures every `*.gltf` directly in the kit directory (and the custom props' directory; the props kit's: Fantasy
Props MegaKit, CC0, whose `Chest_Wood`, `Barrel` and `Crate_Metal` the forest's doodads wear; the nature kit's:
Stylized Nature MegaKit, Standard, CC0, the forest's trees, plants, rocks and stepping stones; and the custom forest
models', the `avalon_*` rocks, logs, ruins and dirt paths) from its vertices through the node transforms, and
rewrites the table, each model under its directory's kit (`"kit"`: the village kit for `--kit` and `--props`, the
props kit for `--props-kit`, the nature kit for `--nature-kit` and `--forest-props`). The custom directories also hold
the scenery ring's models: the town ring's fields, yards and landmarks in `--props`, the forest ring's ancient trees,
cliff and ravine sections, boulders, brambles and fallen giants in `--forest-props`. The vertices are read from each
POSITION accessor's buffer, not from the accessor's min and max, which some kit exports leave stale: the nature kit's
`Fern_1` and `Plant_1_Big` state boxes up to three times their meshes. With `--forest-props` it also copies that
directory's `footprints.json` (the solid parts each custom forest model is measured with in Blender) into
`tools/Avalon.ChunkGen/Kit/forest-footprints.json`, which the forest dresser and its rules read. A model in two of the
directories, an empty directory, a file it cannot measure, or a footprints file that does not list exactly the models
beside it leaves both tables as they were. The committed table is measured from the village kit's `glTF`,
`avalon-art\town-kit\gltf`, the props kit's `Exports\glTF`, `avalon-art\forest-kit\kit` (the nature kit's `glTF`, its
colour textures darkened for the forest) and `avalon-art\forest-kit\gltf`.

## Scenery ring

Every map is drawn with a ring of scenery round its playable layout (#1110): drawn ground, rock faces, water, mist and
decor on the cells past its edge, so the view at any zoom shows a world that goes on. The ring is never walkable and
never a chunk: no catalog row, no exits, no slots, no navmesh. Glimmerdell's ring is one fixed composition; the
forest's is generated for each layout from its seed.

### Why, and how deep

Where nothing is drawn, a view ray meets the client's hidden mist floor (`mist.floor` in its environment files: -10 m
in the forest, -12 m in Glimmerdell), and before the ring that was all a player saw past the last chunk. Closed chunk
sides are not walled whole either: the forest's tree walls stop short of drums, boxes, logs and slots, and
`forest_entry_01`'s south wall covers only x 5.6-24.4, so a player at the walkable edge of a closed side looks straight
out of the map.

The ring's depth comes from the client's camera (`IsometricCameraComponent`): a vertical field of view of 45°, a pitch
of 50° and a yaw of 45°, looking toward +X and +Z, 5 to 25 m from the player. 25 m is the maximum and is never raised
(owner). At 25 m on a 16:9 screen the top of the frame meets the ground about 17 m ahead of the player along the
diagonal, the frame's corners reach about 39 m along X or Z, and toward -X and -Z the view reaches 7 to 17 m; the
camera never sees sky. Raised ground shows less, not more: a 12 m tall object stays wholly in frame only within about
8-12 m of the walkable edge. So the ring is **2 cells (60 m) deep toward +X and +Z and 1 cell (30 m) toward -X and
-Z**, from every layout cell (owner decision); only its first 25-35 m shows in play, the rest is for the overview and
margin. These figures are kept here so the depth can be checked again if the camera changes. The client cuts away what
stands between the eye and the character per fragment (a 1.5 m cylinder with a 0.5 m dithered rim, on every static
mesh), so a ring model near the camera hides no player.

### The cells

For a layout's cells L, the ring is every cell `(x + dx, z + dz)` with `dx, dz` in -1, 0, 1, 2 over every `(x, z)`
in L, minus L itself (`SceneryRing.Cells`, sorted by GridX then GridZ), and every ring cell gets exactly one piece.
Round Glimmerdell's 2 x 2 squares that is 21 cells, x and z -1 to 3 without the town.

### The forest's ring, per layout

`SceneryRing.Build(layout, exitsOf, pool)` (`Avalon.World.Generation`) is pure and deterministic.
`ProceduralChunkLayoutSource` calls it after generating a layout, with the map's pool, so it runs off the tick as the
instance is built; a map without a ring file is built without a ring. It draws from a random stream of its own, the
layout seed through the SplitMix64 finalizer over a fixed salt (`"RINGSCEN"` in ASCII), so the layout, and the
creatures and doodads placed from its seed, are the same with the ring as without it. It visits the ring cells by
GridX, then GridZ, and each takes the first of:

1. **A cap**, when open exits face the cell. An exit is open when its side of the placed (turned) chunk has any exit
   slot and the cell beyond holds no chunk (`SceneryRing.CappedSides`; nothing else records open exits). The cell's
   capped sides are the world sides those exits come through, a 4-bit mask (`ScenerySides`: N 1, E 2, S 4, W 8). It
   takes a cap whose own capped sides turn exactly onto that mask, drawn by weight among the caps of that shape that
   no cell within two of it (counting diagonals) already holds, so one set piece does not close two exits in one view
   (a shape with one cap repeats; where every cap of the shape stands that near, it avoids those within one cell, then
   those west or south of it, and repeats a cap only when every cap of the shape stands west or south of it), at one of
   the turns that fit, drawn too. The pool holds a cap of every shape, so one always fits.
2. **A town-ward piece**, on one of the 8 spots round the entry chunk (-1 to 1 on each axis from it, the entry
   itself left out), when the pool has them: that spot's piece, never turned. The entry is always `forest_entry_01` at
   (0, 0), rotation 0, so the spots never move. A spot the layout fills is a layout cell and gets nothing (spot (0, 1)
   always is: the entry's only exit is N), and a spot an open exit faces takes a cap.
3. **A border**, drawn by weight, never the non-filler piece already west or south of it (an old-woods filler may
   stand beside itself), turned so its front faces the layout: toward the first of N, E, S, W that is a layout cell;
   else the first of NE, SE, SW, NW that is one, faced through N, E, S, W respectively; else, for a cell with no layout
   cell round it, toward the nearest layout cell (fewest steps counting diagonals, ties by GridX then GridZ), along Z
   when it is at least as far along Z as along X, else along X.

A turn is `ChunkRotation`'s (a quarter turn takes N to E, as `ExitMask.Rotate` turns exits), and every forest border's
front is S. Weighted draws always run the weight loop (one number below the total weight, less each weight in order).
`CommittedForestGenerationShould` rings the committed forest's layouts from the committed pool and checks one piece on
every ring cell and none on a layout cell, a cap of the right shape on every cell an open exit faces and none
elsewhere, the town-ward pieces on their spots, no non-filler border beside itself, and the same ring from the same
layout every time.

### The pieces

ChunkGen composes every piece (`tools/Avalon.ChunkGen/ForestScenery.cs`, `ForestScenery.Caps.cs`,
`ForestScenery.TownWard.cs` and `TownScenery.cs`, `TownScenery.Ground.cs`, `TownScenery.Scene.cs`). The terrain (the
ground's relief, the stream beds and their water, the ravines cut down to their floors, the cliff plateaus and their
rough rock faces, the mist) is made in C#, so the stale-file tests rebuild it from the repository alone; everything a
heightfield cannot make, or that needs sculpted detail (the cliffs' strata and ledges, the ravines' overhanging lips,
roots, creepers, the fallen giants, the landmarks), is a Blender model in an art kit, placed as decor, and the terrain
is shaped to seat it. Every forest piece is dressed from a random stream of its own (its name's seed) and the town ring
from one stream seeded by its prefix, so a rerun writes the same files on any machine.

The forest's pieces are drawn from the forest kit (the darkened nature kit and the custom `avalon_*` forest models).
Each meets every edge at y 0 on forest floor, carries a barrier along all four edges and keeps its feature inward of
the edge band, so any piece sits beside any other, or beside any chunk, at any rotation:

| Pieces | Kind, family, weight | What they show |
|---|---|---|
| `forest_ring_woods_01` to `_04` | border, `woods`, 3, filler | two or three ancient trees (`_04`: one, off the centre beside a glade, kit trees round it), a darker understorey, mossy stones, fallen wood, gentle relief |
| `forest_ring_cliffs_01`, `_02` | border, `cliffs`, 1 | a plateau 6-8 m up inward of the edge band, its rough rock face dressed with layered face sections, ledges, roots and creepers over the lip, scree at the foot, trees on top (`_02`: a crag whose top falls back to the ground as an earth bank) |
| `forest_ring_ravine_01`, `_02` | border, `ravine`, 1 | an irregular cut down to -15 m, below the forest's mist floor, rock walls, an overhanging lip all round its brink, dead trees rising out of three mist layers (`_01` a broad pit, `_02` a ragged slot running corner to corner) |
| `forest_ring_giant_01` | border, `giant`, 0.75 | a giant fallen tree across the cell, its root plate on edge at its root end over the torn earth it left |
| `forest_ring_stream_01`, `_02` | border, `stream`, 1 | dark water rising among mossy stones and running down its bed into a still pool (`_01`) or away among stones (`_02`), stones and pebbles along its banks |
| `forest_cap_rockfall_01`, `forest_cap_trunk_01`, `forest_cap_thicket_01`, `forest_cap_brink_01` | cap (S), `rockfall`, `trunk`, `thicket`, `brink`, 1 | one capped side: a rockfall, a giant trunk across the path, a thicket, or the path breaking off at a ravine's brink with broken fence posts |
| `forest_cap_corner_01` | cap (S, W), `cap`, 1 | a rockfall on S and a thicket on W |
| `forest_cap_opposite_01` | cap (N, S), `cap`, 1 | a trunk across S and a rockfall across N |
| `forest_cap_three_01` | cap (N, S, W), `cap`, 1 | a rockfall on S, a trunk on N, a thicket on W |
| `forest_cap_four_01` | cap (N, E, S, W), `cap`, 1 | a rockfall, a trunk, a ravine's brink and a thicket, one on each side, ancient trees only in the centre |
| `forest_townward_<dx>_<dz>` (8) | town-ward, `townward` | the scene below |

On every capped side the exit's path carries on from the edge's midpoint as a dirt stub (its segments laid as a chunk
lays them, on ground kept at y 0) and ends under the side's obstacle, which covers the whole edge band, so an exit in
any slot (left, centre, right) is closed; a brink's path breaks off at the lip, 2 m in. The town-ward pieces are one
scene in world metres over the 8 spots, cut into a piece per spot, the woods thinning toward the south: spot (0, -1),
behind the portal, carries the road from the town (x 13-17) to a low field wall with brambles along the cell's north
band, the visible barrier behind the portal; spots (-1, -1) and (1, -1) the fields' edges, fenced round, standing
wheat on `wheat_field` ground and, in the west field, stubble on `tilled_soil` (the wheat, hedges, fences, field
walls, hay bales, haystacks and the scarecrow are town-kit items); the other spots the woods' fringe, with deadfall
and thorn toward the entry's corners. The road stays inside (0, -1), so every town-ward piece still meets its edges at
y 0 on forest floor and any of them may be left out.

Glimmerdell's outskirts are one scene in world metres over x and z -30 to 120, cut by `SceneryComposer` into the 21
pieces `town_ring_<gx>_<gz>` (a negative coordinate written `m<abs>`: `town_ring_m1_3`), all at rotation 0. The ground
is one function sampled per cell, so every seam meets itself; it lies at y 0 along the town's walls and rises gently
outward, with a hill under the windmill, rock-faced terraces, a valley under the stream, and the south and west edges
falling away into the mist. Every building and landmark stands whole inside one cell: an item whose turned box crosses
a cell edge is refused, and a rock face may not cross one either (only the ground and the surfaces are cut). The
decor file's kit is the town kit; the trees, grass, bushes and flowers are the nature kit's bright models, the dark
forest's tree line the forest kit's.

| Side | What it shows |
|---|---|
| North (z 60-120) | the road from the gate (x 12-18) winding north to the map's edge, cottages, a well and carts outside the gate, a lane east along the wall to the bridge; hedged and fenced fields beyond (wheat on `wheat_field` ground, vegetables, a ploughed field, hay meadows) with scarecrows, haystacks and bales; scattered trees, then the dark forest from about z 97 (92 in the east), the forest portal's destination showing ahead |
| East (x 60-120) | the stream down the wall to a still pool short of the map's south edge, the watermill (its house at about (71, 42), its wheel in the water), reeds and stones on the banks, the lane south past the mill, the orchard on its slope, the rocky terraces, the windmill on its hill (about (104, 76)) with a barn; the windmill shows only in the overview, and is kept for it |
| North-east corner | the stream coming down from the north-east hills, the lane crossing it on the stone bridge, the wayside shrine at the bridge's east foot (about (70.4, 62.4)) and a ring of eight standing stones about 13 m out from the town's corner (centred at (72, 55)), where the game camera sees them (10-20 m, owner decision) |
| South and west (x or z -30 to 0) | the backs of houses, gardens, lean-to sheds, wood piles, washing lines, compost and low fences, kept low near the wall, between the camera and the player |

The ground's materials, which the client's palette must hold, are `SceneryTerrain.Palette`: `forest_floor`, `meadow`,
`dirt_road`, `tilled_soil`, `wheat_field`, `cliff_rock`, `water`, `mist`.

### The files

ChunkGen writes them all (`forest` the forest's, `town` the town's, staged and checked with the chunks; see below):

- **`Maps/Scenery/<name>.obj`**, a piece's terrain, piece-local (x and z 0-30, y up), in the subset the client's
  vendor script reads: `o Ground` (every grid vertex once, every `SceneryTerrain.Step`, 1 m, apart: 31 x 31 vertices),
  one `usemtl` group per material that has faces, in palette order, each quad split along its (x0, z0)-(x1, z1)
  diagonal and each triangle wearing the material at its centroid; then each rough rock face as `o Wall_<n>` and each
  water, mist, dirt or forest-floor surface as `o Surface_<n>`, each under its material. Ground and surface triangles
  are wound to face up. No material group is ever empty (the client's script refuses one). Numbers are invariant with
  at most 3 decimals and never `-0`, line ends LF, so a rerun is byte-identical. Nothing bakes it.
- **`Maps/Decor/<name>.json`**, a piece's decor, piece-local, in the decor format ("Decor and building recipes"), its
  items at any whole-degree yaw whatever the file's kit, and an item that is not drawn from the file's kit naming its
  own (`"kit"`). A scenery piece's name may never be a chunk's, since both write `Maps/Decor/<name>.json`.
- **`Maps/Scenery/<mapTemplateId>.json`**, a map's ring file, in one of two shapes, every field required and no other
  allowed (`SceneryFile.Read`):

  ```json
  { "mapTemplateId": 1, "placements": [
    { "name": "town_ring_m1_m1", "gridX": -1, "gridZ": -1, "rotation": 0 } ] }

  { "mapTemplateId": 2, "pool": [
    { "name": "forest_cap_corner_01", "kind": "cap", "weight": 1, "capped": ["S", "W"] },
    { "name": "forest_ring_woods_01", "kind": "border", "weight": 3, "front": "S", "filler": true },
    { "name": "forest_townward_0_m1", "kind": "townward", "spot": [0, -1] } ] }
  ```

  The town's (`1.json`) is its fixed placements; the forest's (`2.json`) is its pool, each entry its piece, its kind,
  and a border's weight, front and filler flag, a cap's weight and capped sides (in the piece's own frame), or a
  town-ward piece's spot (its offset from the entry chunk).

The World server reads every ring file once at start-up (`SceneryCatalog.Load`, an `ISceneryCatalog` singleton), every
`Maps/Scenery/<n>.json` whose name is a map template id; one that does not read, or whose `mapTemplateId` is not its
name, stops the start, naming the file. It refuses placements with a rotation outside 0-3, two on one cell or a name
that is not `[a-z0-9_]+`, and a pool (`SceneryPool`) with a name not `[a-z0-9_]+` or listed twice, a border or cap
weight that is not a finite number above 0, a border front none of N, E, S, W, a cap that caps no side, no cap for one
of the five shapes (one side, two adjacent, two opposite, three, four, whatever the turn), no filler border, or
town-ward pieces neither absent nor exactly one per spot. `PredefinedChunkLayoutSource` puts a town's placements on
its layout as they are; `ProceduralChunkLayoutSource` builds a procedural map's ring from its pool. The seeder never
reads `Maps/Scenery/`, and neither does the API. `SceneryCatalogShould.Hold_a_ring_for_every_map_with_a_layout` holds
every shipped map to its ring, of the kind its layout source reads: a map in `Maps/TownLayouts/` its placements, one in
`Maps/ProceduralMaps/` its pool. A new map ships its ring with it.

### The rules

`ChunkFiles.ForestScenery` and `ChunkFiles.TownScenery` run `SceneryRules` on every piece before anything is written,
and `ForestPiecesShould` and `TownPiecesShould` compare the committed files with a fresh run. Each refusal is an
`InvalidOperationException` naming the piece, the pool or the ring:

- **Inside its cell.** Every item is a model of its kit (`DecorItem.Check`, any whole degree; the item's own kit when
  it names one) and its kit box, scaled and turned by its yaw, lies within its cell (to 0.01 m).
- **Small near walkable floor** (forest pieces on all four edges, town pieces on the edges they share with the town).
  Within 6 m of the edge (`SceneryRules.EdgeBand`, measured from the item's position), an item drawn taller than
  2.2 m is a kit tree at no more than the tree walls' scale (`ForestDresser.WallTreeMaxScale`, 1.6; a dead tree 1.3),
  never a twisted or an ancient tree, or another model drawn no taller than 4 m: a bigger crown there hangs over the
  paths at the camera's full zoom.
- **The forest's seam.** Every ground vertex on a forest piece's edge is at y 0 (to 0.5 mm) and every ground triangle
  touching it is `forest_floor`.
- **The forest's barrier.** Each edge of a forest piece, in ten 3 m runs (`BarrierSpacing`), has for every run a
  barrier item within 6 m of it whose turned box spans part of the run: a kit tree, undergrowth (`Bush_`, `Fern_`,
  `Plant_1_Big`), a rock, a cliff or ravine rim, brambles, a field wall, a fence, a fallen giant or log, a hedge or
  wheat. Closed chunk sides have gaps in their tree walls, so every view from a walkable edge into the ring meets
  something that reads as impassable, whatever the piece's turn.
- **A cap's clearing.** No tree, kit or ancient, within 10 m of a capped edge's midpoint, so the obstacle reads from
  the camera.
- **Low beside the town's south and west walls.** In a ring cell south or west of the town, nothing whose turned box
  comes within 4 m of the town is drawn taller than 2 m (the wall), and nothing within 10 m taller than 6 m: those
  cells lie between the camera and the player.
- **Dirt on the ground, not under it.** Every surface but water and mist (the dirt and forest floor laid over the
  ground, whose curves the ground's triangles would give saw-teeth) lies at or above the drawn ground everywhere on it,
  within 2 mm.
- **A full forest pool.** At least 3 old-woods borders, every one a filler; a border of each of `cliffs`, `ravine`,
  `giant` and `stream`; a cap with exactly one capped side of each of `rockfall`, `trunk`, `thicket` and `brink`; and
  the ring file read back through the server's own reader, which refuses a missing cap shape or town-ward spot.
- **A whole town ring.** A piece on every one of the 21 cells and none elsewhere; the ground at y 0 along every edge a
  piece shares with the town; and the same height on both sides of every edge two ring pieces share (the ring is one
  scene, so a seam between its own cells need not lie at y 0).
- **Names.** A scenery name that is also a chunk's, or that two pieces share.

The terrain refuses its own mistakes as it is built: a stream's water must drop at least 0.5 % of each step (flowing
water runs downhill) and its edges never fold back over themselves, at a bend too tight for its half-width or at any
corner of 90° or more; and the obj writer refuses a material outside the palette and a height that is not a number.

### The wire and the instance

`ChunkLayout.Scenery` holds the placements (`SceneryPlacement(Name, GridX, GridZ, Rotation)`), kept apart from
`ChunkLayout.Chunks`, which feeds the navmesh bake, the spatial grid's bounds, creature and doodad placement, the
packet's chunk list and the API's map preview: scenery reaches none of them, and nothing about a layout, its seed's
creatures and doodads, or its navmesh changes with its ring. `CommittedForestGenerationShould` composes the bake's
input (`ChunkLayoutNavmeshBuilder.ComposeCombinedObj`) for a committed layout with its ring and without it and finds
the two identical.

`SChunkLayoutPacket.Scenery` (field 9) carries the ring, a repeated `SceneryPlacementDto` with fields 1-4:
`NameIndex` (`uint32`, the piece's index into field 10), `GridX` and `GridZ` (`short`, `sint32` on the wire) and
`Rotation` (`byte`, quarter turns 0-3). `SChunkLayoutPacket.SceneryNames` (field 10, repeated string) names each piece
the ring uses once, in the order the placements first use it, so a forest's border pieces, repeated round the ring, cost
a byte or two each rather than their name. A ring cell lies outside the map's own cells and is often at -1, so the cell
is zigzag-encoded: a coordinate within 63 of the origin costs its tag and one byte either side of it, where a plain
`int32` writes a negative one in eleven; the chunks' `PlacedChunkDto.GridX` and `GridZ` (fields 2 and 3) are `sint32`
for the same reason, since a forest's chunk cells may be negative too. A forest's ring adds 642-1,213 bytes to its
arrival (929 on average over seeds 1-500), the town's 21 pieces 479. `MapLayoutEntries` builds both lists once, with
the instance, beside the chunk, portal and obstacle entries, and encodes the whole layout once (#1130). Arrivals
share that immutable payload without serializing the ring again. `MapArrivalShould` sends the town's and two forests'
real rings and pins each coordinate's two bytes. The scenario allocation gate runs forest layouts that carry their ring
(`ForestLayouts` in `tests/Avalon.World.Testing` builds them as the source does), and `ArrivalBenchmarks` the town's
(`ScenarioReferenceData.TownLayout`, built with the scenery catalog).

### The client's half

A client draws each placement as it draws a chunk: the piece's `Scenery/<name>.obj` drawn by material and its
`Decor/<name>.json` drawn from the kits, both placed in the cell `(GridX * 30, 0, GridZ * 30)` and turned about the
cell's centre with `chunkLocalToWorld`. It never bakes them: its navmesh, like the server's, is the chunks and the
obstacle boxes alone. Today's client (`C:\dev\vulkan-multithreaded`, `origin/main` at `631ef5e5`) does not know fields
9 and 10 yet, skips them as unknown fields and draws no ring; it also still reads a chunk's `GridX` and `GridZ` as
`int32`, so it places a chunk at a cell other than 0 wrongly until it re-vendors the schema. The client issue covers
re-vendoring, decoding the ring (each placement's piece is `SceneryNames[NameIndex]`), vendoring
`Maps/Scenery/*.obj` (its vendor script already copies every `Maps/Decor/*.json`, the scenery pieces' decor among
them), the new materials (`meadow`, `dirt_road`, `tilled_soil`, `wheat_field`, `cliff_rock`, `mist`), an item's own
kit and the bright nature kit, the new town-kit and forest-kit models, flowing water and drifting mist.

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
so every run has at least three steps a set piece may take: 668 non-boss set pieces in 1000 seeds (661 before #1113
gave the seeds that retry other layouts; 690 with the seventeen singles before #1109, whose fifteen more halved the
singles' weight to keep that share; 545 with a minimum of 10, then). The forest has three: `forest_clearing_big`,
`forest_grove_ruin` and the boss arena `forest_arena`. `forest_boss_01` is no longer in the forest pool (its files stay
in the catalog), so the arena ends every run.

A layout attempt that fails (no free cell for the next step, say) is retried, up to 10 attempts
(`ProceduralLayoutGenerator.MaxRetries`); `CommittedForestGenerationShould` generates the committed forest for 1000
seeds and fails if any of them runs out of attempts. The first attempt generates from the seed itself. A retry
generates from a seed mixed from the seed and the attempt number (`ProceduralLayoutGenerator.AttemptSeed`, a fixed
integer mix, the same in every process), never from the next seed: until #1113 the retry of seed N was seed N + 1's
first attempt, so the two seeds gave the same forest, creatures and doodads included (they are placed from the
layout's seed), and about a quarter of the forest's consecutive seeds were such pairs (48 in seeds 0 to 199). The
layout records the seed of the attempt that built it (`ChunkLayout.Seed`), which the creature and doodad placement
read. About 28% of the forest's seeds retry (283 of seeds 0 to 999); the others kept their layouts through #1113.
`CommittedForestGenerationShould` checks that no seed of 0 to 199 gives the layout of the seed before it.

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
   - `source.BuildAsync(template, ct)` → reads `MapChunkPlacement` rows via repository, validates, builds `ChunkLayout` with `Seed=0`, `Chunks[]`, `EntryChunk`, `EntrySpawnWorldPos` (transformed from `EntryLocalX/Y/Z`), `Portals[]` (from chunk `PortalSlots`), `CellSize`, `Config=null`, and `Scenery` (the town's fixed ring placements, from `Maps/Scenery/<id>.json` through `ISceneryCatalog`; never baked).
   - `_navBuilder.BuildAsync(layout, ct)` → `ChunkLayoutNavmeshBuilder` stitches chunk objs from `Maps/Chunks/<name>.obj` (filename via `IChunkLibrary.GetById(id).Name`), bakes via DotRecast `TileNavMeshBuilder.Build` with `NavmeshBuildSettings.Create()`.
   - Constructs `MapInstance` with the layout + a `MapNavigator` initialized from the bake.
   - `_portalPlace.Place(instance, layout, config: null)` registers Back portals (no Forward for null BossChunk).
5. **Server `CharacterSelectHandler` (after instance returned):**
   - For Town: overrides `CharacterInfo.X/Y/Z` with `instance.Layout.EntrySpawnWorldPos` (persisted DB coords are stale for hubs).
   - Sends `SCharacterSelectedPacket` with the `CharacterInfo`.
   - Sends `SChunkLayoutPacket` with `(seed, instanceId, cellSize, chunks[], entrySpawn, portals[], mapId, obstacles[], scenery[], sceneryNames[])`.
6. **Client `MapCompositionSystem`:** spawns one entity per primitive of each chunk's vendored `chunks/<name>.gltf` (and one batch of its decor), positioned with `chunkLocalToWorld` and turned `90° × rotation` about Y, plus markers for the entry and the portals.
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
| Client: a chunk is missing, or has the old shape, or no prediction on a map | The client has not re-vendored since the server's chunks changed (a chunk the project does not carry fails the client's bake) | Rerun `scripts/vendor-chunks.ps1` in the client and commit `devproject/data/world/` |
| Client: chunks visible but "rotation wrong" | Either a chunk error OR the client's rotation mirror disagrees with the server | `ChunkRotation.LocalToWorld` is the source of truth; the client's `chunkLocalToWorld` mirrors it, and the vendored `rotation-v1.txt` carries the server's known answers for it |
| Layout valid but adjacency broken (chunks isolated) | Chunks have mismatched exits at neighbouring boundaries | Fix the chunks so each pair of neighbouring chunks has an aligned `(side, slot)` exit on both faces of the shared boundary |

## Conventions and gotchas

- **Chunk filename = `ChunkTemplate.Name`** (NOT `ChunkTemplate.Id`). Files are `Maps/Chunks/<Name>.obj` on the server and `devproject/data/world/chunks/<Name>.obj` (+ `.gltf`) in the client. Bake on both sides looks up by name.
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
