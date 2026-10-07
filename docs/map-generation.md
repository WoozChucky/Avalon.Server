# Map Generation

End-to-end guide: from chunk files → server bake → playable client.

## Concepts

The world is built from **chunks** — modular 30×30m geometry blocks (cell size configurable per chunk; current standard is 30). Chunks are stitched into **layouts**. Layouts feed two pipelines:

- **Town maps** (MapType.Town) — chunk placements are PREDEFINED in the database (`MapChunkPlacement` table). Multi-player shared instances. Same layout every time.
- **Procedural maps** (MapType.Normal) — chunk placements are GENERATED at runtime by `ProceduralChunkLayoutSource` using a chunk pool + RNG seed + per-map config (`ProceduralMapConfig`). Per-player private instances.

Both pipelines emit a `ChunkLayout` record (chunks, entry spawn, portals, cell size, optional config + seed). One factory (`ChunkLayoutInstanceFactory`) builds a `MapInstance` from the layout, bakes a navmesh from the stitched chunk geometry via DotRecast, and ships the layout to the client over `SChunkLayoutPacket`. The client bakes its own navmesh from the same chunk `.obj` files for prediction parity.

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
| `src/Server/Avalon.World/Handlers/CharacterSelectHandler.cs` | Sends `SChunkLayoutPacket` on character enter |
| `src/Server/Avalon.World/Handlers/EnterMapHandler.cs` | Sends `SChunkLayoutPacket` on portal traversal |
| `src/Shared/Avalon.Network.Packets/World/SChunkLayoutPacket.cs` | Wire format |
| `src/Shared/Avalon.Domain/World/ChunkTemplate.cs` | DB entity for chunk metadata |
| `src/Shared/Avalon.Domain/World/MapChunkPlacement.cs` | DB entity joining MapTemplate → predefined chunk placements |
| `src/Shared/Avalon.Domain/World/ProceduralMapConfig.cs` | DB entity for procedural map RNG config |
| `src/Server/Avalon.Server.World/Maps/Chunks/<chunkName>.obj` + `.json` | Chunk geometry (navmesh bake) and metadata (see "The chunk format"). The forest pieces and Glimmerdell's four squares are written by `tools/Avalon.ChunkGen`; the rest (`forest_boss_01`, `town_corner_01`, `town_house_01`, `town_path_01`, `town_square_01`) are committed as they are and no tool writes them |
| `src/Server/Avalon.Server.World/Maps/TownLayouts/<MapTemplateId>.json` | Town layouts: which chunk sits on which grid cell, edited directly (see "Creating a town"). ChunkGen writes the squares' geometry, not this file |
| `src/Server/Avalon.Server.World/Maps/chunk-pools.json` | Procedural pool membership (`{ "<pool>": ["<chunk>", …] }`), edited by hand |
| `src/Server/Avalon.Server.World/Maps/chunk-groups.json` | Set pieces per pool (see "Chunk groups (set pieces)"), edited by hand |
| `src/Server/Avalon.Server.World/Maps/spawn-tables.json` + `ProceduralMaps/<mapId>.json` | Spawn tables and procedural map configs with their depth bands (see "Spawn tables and procedural map configs"), edited by hand |
| `tools/Avalon.ChunkGen/` | Generates the forest pieces' and the town squares' `.obj` + `.json` (see "Generated forest chunks" and "Generated town squares") |
| `tools/Avalon.Exporter/` | Exports the known-answer vectors the client vendors, among them `schema/vectors/navmesh-v1.txt` (`-- navmesh`) and `schema/vectors/rotation-v1.txt` (`-- rotation`) |
| `src/Server/Avalon.Database.World/Seeding/ChunkCatalogSeeder.cs` | Seeds chunk templates, town layouts, pools, chunk groups, spawn tables and procedural map configs from `Maps/`; run by the World server on start |

### Client (`C:\dev\vulkan-multithreaded`)

| Path | Purpose |
|---|---|
| `scripts/vendor-chunks.ps1` | Copies every `Maps/Chunks/*.obj` into `devproject/chunks/`, converts each to a `.gltf` beside it (one primitive per `usemtl` group, coloured from the script's palette: `stone`, `wood`, `roof`, `cloth`, `cloth_2`, `plaster`, `metal`, `water`), and writes `CHUNK-MANIFEST` (the server revision it copied from, plus a SHA-256 per file). The chunk `.json` files are not vendored. An unknown material name or an OBJ directive other than `o`, `v`, `f`, `usemtl` fails the script |
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

A `ChunkPool` is a named bag of `ChunkPoolMember`s with weights. The procedural generator picks chunks from a pool weighted-random.

Pools are `Maps/chunk-pools.json`, seeded by `ChunkCatalogSeeder` on every World start (see "How a chunk enters the catalog"). Convention: one pool per biome.

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

## Generated town squares

Glimmerdell's four squares (`town_sw_01`, `town_se_01`, `town_nw_01`, `town_ne_01`, map 1) are generated too, since the
town beautification (2026-10-01): `tools/Avalon.ChunkGen/TownPieces.cs` holds the approved layout as data, chunk-local,
over a small shape model (`TownSquare.cs`: boxes, twelve-sided cylinders and rings, gabled roof blocks, and the
wall boxes by name; the fountain's basin is a ring with the water disc inside it). Each building part and prop is its own obj object named `<Building>_<part>` with a `usemtl` line
(`stone`, `wood`, `roof`, `cloth`, `cloth_2`, `plaster`, `metal`, `water`; the walls `stone`, the floor untagged); the
server's bake reads only `v` and `f` lines (`ChunkObjParserShould`), the client colours the materials. The floor
is y -0.05 to 0.05, and the walls are 0.5 m thick and 2 m high, the inner ones opened at 12-18.

    dotnet run --project tools/Avalon.ChunkGen -- town [--maps <Maps directory>]

stages, validates (`ChunkCatalogSeeder.ReadCatalogAsync`), bakes the four squares together and writes them, as the
forest command does; a square breaking a layout rule is refused before anything is written (`TownRules`: nothing within
2 m of a wall, the doorway lanes and the arrival-to-portal corridor clear, 2.5 m of headroom under every roof, walkable
risers of at most 0.3 m). `TownPiecesShould` fails when the committed files differ from a fresh run, when a solid piece
lets a walk in on the baked navmesh or has a top a path from the arrival point can end on (Recast climbs any step of
0.8 m or less and rounds a top up to a 0.2 m voxel, so a top at or under 1.0 m above the ground beside it would be
walked over), or when a solid piece's top is under 1.05 m (every bench, crate, barrel, counter, the well ring and the
cart's shaft reach 1.05 m, owner decision). Roof tops and building interiors are navmesh islands nothing reaches, as the
forest's blocker tops are. `TownNpcPlacementShould` checks the seven NPC spots against
the same data.

The town is laid out for the client's camera, which is fixed (owner decision, 2026-10-02) at yaw 45° and 60° below
horizontal, south-west of what it looks at: a player sees the −X and −Z sides of everything, and the south-west of each
square is the bottom of the screen. So tall buildings stand on a square's far (high X/Z) edges with their open face
toward −X or −Z, and an NPC stands on that side, clear of its roof. Two checks hold it: each building's open face
(porch, counter, lean-to, steps) carries a `Front`, which `TownPiecesShould` requires to be −X or −Z; and
`TownNpcPlacementShould` casts a line from each NPC's head toward the camera and fails on any piece or wall in the way
(a roof over the NPC counts: under a 2.9 m stall roof a head shows only within about 0.6 m of the roof's camera-side
edge). The two angles are mirrored from the client's `IsometricCameraComponent`. The four other `town_*` chunks
(`town_corner_01`, `town_house_01`, `town_path_01`, `town_square_01`) are listed by the tool and never touched. A geometry change
here needs a restart (instances bake once), a re-export of the navmesh vectors (`tools/Avalon.Exporter -- navmesh`),
and a re-vendor in the client (`scripts/vendor-chunks.ps1`, and `scripts/regen-proto.ps1` for the vectors).

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
  (`spawnTable`) by name, `depthBands`, and the optional `minSetPieceStep` (0 when left out). Matched by
  `mapTemplateId` (the file's name); its bands are replaced.

Before writing anything the seeder refuses, naming the file, JSON it cannot read, a map file that leaves out a required
field (every number and flag but `forwardPortalTargetMapId` and `minSetPieceStep`), a map whose `MapTemplate` is missing
or not `Normal`, a back or forward portal target that is no `MapTemplate`, a depth band whose `maxLevel` is above the
highest `CreatureBaseStats` level (10 today), a pool or spawn table the files do not name, a group with a blank name,
a `creatureId` with no `CreatureTemplates` row, and a spawn table whose tags do not match the
slot tags its pool's chunks (set pieces included) use, checked both ways: every entry tag is a slot tag (or
`leader_pack` beside `leader` slots), and every slot tag but `entry` and `empty` has an entry. Rows with no file are
left alone. A change to these files takes a World server restart. Trade-offs: the balance simulator reads `HasData`
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

Identical bake on both sides (same `.obj` source, same Recast settings) keeps drift sub-mm in practice — so reconciliation rarely fires.

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
