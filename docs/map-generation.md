# Map Generation

End-to-end guide: from Unity sketching → server bake → playable client.

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
       Client: ClientMapNavigator.LoadFromLayoutAsync
       Client: ChunkLayoutVisualizer (renders meshes)
       Client: ChunkMarkerVisualizer (debug markers)
       Client: PlayerMovementPredictor (predict + reconcile)
```

## Repos involved

- **Server:** `C:\dev\Avalon.Server` — bake, DB, instance factory, wire packet
- **Client:** `C:\dev\3D` — Unity authoring scenes, runtime visualizers, predictor
- **Server CLI tool:** `tools/Avalon.ChunkImporter` — imports chunk + layout JSON files into DB

## Where things live

### Server (`C:\dev\Avalon.Server`)

| Path | Purpose |
|---|---|
| `src/Server/Avalon.World/ChunkLayouts/` | `ChunkLayout`, `IChunkLayoutSource`, factories, navmesh builder |
| `src/Server/Avalon.World/Maps/Navigation/MapNavigator.cs` | DotRecast queries (`RaycastWalkable`, `SampleGroundHeight`) |
| `src/Server/Avalon.World/Maps/Navigation/NavmeshBuildSettings.cs` | Single source of truth for DotRecast bake constants |
| `src/Server/Avalon.World/Handlers/CharacterSelectHandler.cs` | Sends `SChunkLayoutPacket` on character enter |
| `src/Server/Avalon.World/Handlers/EnterMapHandler.cs` | Sends `SChunkLayoutPacket` on portal traversal |
| `src/Shared/Avalon.Network.Packets/World/SChunkLayoutPacket.cs` | Wire format |
| `src/Shared/Avalon.Domain/World/ChunkTemplate.cs` | DB entity for chunk metadata |
| `src/Shared/Avalon.Domain/World/MapChunkPlacement.cs` | DB entity joining MapTemplate → predefined chunk placements |
| `src/Shared/Avalon.Domain/World/ProceduralMapConfig.cs` | DB entity for procedural map RNG config |
| `src/Server/Avalon.Server.World/Maps/Chunks/<chunkName>.obj` | Server-side chunk geometry (consumed by navmesh bake) |
| `tools/Avalon.ChunkImporter/Program.cs` | CLI tool that reads `<exportDir>` + writes DB rows |

### Client (`C:\dev\3D`)

| Path | Purpose |
|---|---|
| `Assets/Scenes/ChunkAuthoring.unity` | Where you author NEW chunks |
| `Assets/Scenes/TownLayoutAuthoring.unity` | Where you author predefined town layouts |
| `Assets/Scripts/Map/Procedural/Authoring/` | Chunk authoring components (`ChunkAuthoringRoot`, `ChunkExitMarker`, `ChunkSpawnSlotMarker`, `ChunkPortalSlotMarker`) |
| `Assets/Scripts/Map/Procedural/Authoring/Editor/ChunkExporter.cs` | Editor menu: `Avalon → Procedural → Chunk Exporter` |
| `Assets/Scripts/Map/Town/Authoring/` | Town layout authoring components (`TownLayoutAuthoringRoot`, `TownChunkPlacement`) |
| `Assets/Scripts/Map/Town/Authoring/Editor/TownLayoutExporter.cs` | Editor menu: `Avalon → Town → Town Layout Exporter` |
| `Assets/Scripts/Map/Chunks/Editor/ChunkCatalogSync.cs` | Editor menu: `Avalon → Chunks → Sync Chunk Catalog` |
| `Assets/Scripts/Map/Navigation/ClientMapNavigator.cs` | Client-side DotRecast bake + raycast (mirrors server) |
| `Assets/Scripts/Map/Visual/ChunkLayoutVisualizer.cs` | Runtime renders chunk mesh from `.obj` |
| `Assets/Scripts/Map/Visual/ChunkMarkerVisualizer.cs` | Runtime debug markers (entry, exits, portals, spawns) |
| `Assets/Scripts/Map/Visual/ObjMeshLoader.cs` | Runtime OBJ → Unity Mesh parser |
| `Assets/Scripts/Map/Visual/ChunkMetaCatalog.cs` | Runtime chunk.json loader |
| `Assets/StreamingAssets/Chunks/<name>.obj` | Per-chunk geometry shipped to client (used by both navmesh bake AND ChunkLayoutVisualizer) |
| `Assets/StreamingAssets/Chunks/<name>.json` | Per-chunk metadata (used by ChunkMarkerVisualizer) |
| `Assets/Scripts/Gameplay/PlayerMovementPredictor.cs` | Movement prediction + reconciliation against server |

### Editor export output (gitignored, project-root)

| Path | Purpose |
|---|---|
| `<projectRoot>/ChunksExport/<chunkName>/chunk.obj` | Per-chunk geometry emitted by `ChunkExporter` |
| `<projectRoot>/ChunksExport/<chunkName>/chunk.json` | Per-chunk metadata emitted by `ChunkExporter` |
| `<projectRoot>/ChunksExport/town_layouts/<MapTemplateId>.json` | Town layout emitted by `TownLayoutExporter` |

`ChunksExport/` is the bridge directory. Both client editor + server CLI read from it. Default location is `<UnityProjectRoot>/ChunksExport`. Persisted via `EditorPrefs` key `Avalon.ChunkExporter.OutputDir` and shared with sibling editor windows.

---

## Authoring a new chunk

### 1. Open the chunk authoring scene

Unity → File → Open Scene → `Assets/Scenes/ChunkAuthoring.unity`.

### 2. Create the chunk root GameObject

- Hierarchy → right-click → Create Empty. Name it `<chunkName>_root` (e.g. `dungeon_room_03_root`).
- Position at world origin. Other chunks in the scene live alongside; they don't conflict because each is exported in its own LOCAL space.
- Add component **`ChunkAuthoringRoot`** with these fields:
  - **ChunkName**: stable name. Lowercase, no spaces, must be unique across the database. This is the FILENAME on disk (`<ChunkName>.obj`) AND the lookup key for `MapChunkPlacement.ChunkTemplateId`. Conventions: `town_*`, `forest_*`, `dungeon_*`, etc.
  - **AssetKey**: optional Addressables/Resources key. Defaults to `chunks/{ChunkName}` if blank.
  - **CellFootprintX/Z**: how many cells the chunk spans (most are 1×1). Multi-cell chunks (2×1, 2×2) are supported by the procedural generator's exit-stitching logic but rare in practice.
  - **CellSize**: meters per cell. Must match every chunk in any pool/layout the chunk will live in. Standard is **30**.
  - **Tags**: free-form `string[]` consumed by `ChunkPool` validation (e.g. `["town", "entry"]`, `["forest", "boss"]`).

### 3. Build the geometry as children

Place primitives or imported meshes as **children** of the root. Geometry is exported relative to the root's transform — vertices end up in chunk-local space (origin at the root, footprint extends to `(CellFootprintX*CellSize, *, CellFootprintZ*CellSize)`).

Convention: a 1×1 chunk's local space is `(0..30, *, 0..30)`. Floor at local `(15, 0, 15)`, walls along the 4 perimeters.

Mesh requirements:
- All meshes you want exported must have **Read/Write Enabled** in their import settings (the OBJ exporter reads vertices via the `MeshFilter.sharedMesh`).
- Primitives (Cube, Plane, etc.) are Read/Write by default.
- For imported FBX/OBJ assets, select in Project, Inspector → Model → Read/Write → Enabled.

### 4. Add ChunkExitMarker for each exit

Per side that should permit traversal into a neighbouring chunk, add an empty child GameObject with the **`ChunkExitMarker`** component.

- **Side**: `N`, `E`, `S`, `W` — direction the exit faces.
- **Slot**: `Left`, `Center`, `Right` — which third of the side carries the exit. The procedural generator matches exits between chunks ONLY when both chunks have an exit at the SAME slot on opposite sides.

Visual: place the marker at the gap in your wall geometry (the marker's world position is informational only — the server consumes only `(Side, Slot)`). Yellow gizmo sphere + line indicates direction.

Constraint: no two markers may share `(Side, Slot)` on the same chunk. Editor exporter validates.

### 5. Add ChunkSpawnSlotMarker for spawn slots (optional)

Per spawn point (creature, loot, player entry) add a child with **`ChunkSpawnSlotMarker`**.

- **Tag**: free-form keyword consumed by spawn tables (procedural maps) or used to identify the player entry chunk (towns). Conventions: `entry`, `pack`, `rare`, `boss`, `empty`. Towns use `entry` to mark the player spawn chunk.

The marker's world position is the spawn point. Stored chunk-local in chunk.json.

### 6. Add ChunkPortalSlotMarker for portals (optional)

Per portal anchor add a child with **`ChunkPortalSlotMarker`**.

- **Role**: `Back` (returns to a previous map — typically placed in entry chunks) or `Forward` (advances to the next map — typically placed in boss chunks).

Procedural maps use these slots to instantiate portal triggers; `ProceduralMapConfig.BackPortalTargetMapId` + `ForwardPortalTargetMapId` hold the destination map ids.

### 7. Save the scene

`Ctrl+S` or File → Save.

### 8. Export the chunks

- Menu: `Avalon → Procedural → Chunk Exporter`.
- "Output Dir" — defaults to `ChunksExport`. Change if you want; the `EditorPrefs` value is shared with `ChunkCatalogSync` and `TownLayoutExporter`, so all three pick up the same dir.
- Click **Export Selected** (operates on selected `ChunkAuthoringRoot`s in the hierarchy) or **Export All In Scene**.
- Per-chunk output: `<ExportDir>/<ChunkName>/chunk.obj` + `<ExportDir>/<ChunkName>/chunk.json`.
- Validation: duplicate `(Side, Slot)` exits, empty `ChunkName`, missing root → printed in the result panel.

### 9. Sync to the client runtime catalog

- Menu: `Avalon → Chunks → Sync Chunk Catalog`.
- Click **Sync**. Copies every `<ExportDir>/<chunk>/chunk.obj` AND `chunk.json` → `Assets/StreamingAssets/Chunks/<chunk>.obj` + `<chunk>.json`.
- StreamingAssets is the runtime-readable bridge: client's `ClientMapNavigator` reads `.obj` for navmesh bake, `ChunkLayoutVisualizer` reads `.obj` for visual rendering, `ChunkMarkerVisualizer` reads `.json` for debug markers.

### 10. Import into server

Switch to the server repo:

```bash
cd C:\dev\Avalon.Server
dotnet run --project tools/Avalon.ChunkImporter -- /c/dev/3D/ChunksExport
```

Path is the export dir from step 8. The CLI:
- Reads each `<chunkName>/chunk.json`, upserts a `ChunkTemplate` row in the World DB (matched by `Name`; existing rows are updated, new rows inserted).
- Copies `chunk.obj` to `src/Server/Avalon.Server.World/Maps/Chunks/<chunkName>.obj` for the runtime navmesh bake.

DB connection comes from `appsettings.json` or `Database__World__ConnectionString` env var.

After this step, the chunk template is in the DB and its geometry is on disk for both server (Maps/Chunks/) and client (StreamingAssets/Chunks/).

### 11. Commit

Two repos:
- Client (`C:\dev\3D`): `Assets/Scenes/ChunkAuthoring.unity`, `Assets/StreamingAssets/Chunks/<name>.obj`, `<name>.json` + meta files.
- Server (`C:\dev\Avalon.Server`): `src/Server/Avalon.Server.World/Maps/Chunks/<name>.obj`.

Stage explicitly.

---

## Creating a town

A town is a `MapTemplate` with `MapType=Town` plus a set of `MapChunkPlacement` rows pinning specific chunks to specific grid cells.

### 1. Author the town's chunks

Follow the chunk authoring flow above. Town chunks typically use the `town_*` naming convention. Each chunk's exits + walls determine adjacency. To make a 2×2 fully-connected town: use 4 corner-pattern chunks where each chunk has walls on its 2 perimeter sides and exits on its 2 inner sides.

Designate ONE chunk as the entry chunk:
- Add a `ChunkSpawnSlotMarker` with `Tag = "entry"` (used as the player spawn point inside the chunk).
- Optional: add a `ChunkPortalSlotMarker` with `Role = Back` (for portals back out of town to an overworld / dungeon entry).

Re-export, sync, import (steps 8–10 above).

### 2. Confirm the MapTemplate exists

The `MapTemplate` row identifies the map. Towns are seeded in EF migrations or set up by hand. Check:

```bash
psql -U postgres -d world -c "SELECT \"Id\", \"Name\", \"MapType\" FROM \"MapTemplates\";"
```

Expected: at least one row with `MapType = 0` (Town). The current dev town is `Id = 1`, name `MainTown` (or similar). If you need a NEW map id, add it via an EF migration in `Avalon.Database.World` (or seed via an SQL one-off if dev-only).

### 3. Open the town layout authoring scene

Unity → File → Open Scene → `Assets/Scenes/TownLayoutAuthoring.unity`.

### 4. Add TownLayoutAuthoringRoot

If a layout for your map id already exists, edit it. Otherwise:
- Create empty GameObject named e.g. `MainTown_Root`.
- Add component **`TownLayoutAuthoringRoot`**:
  - **MapTemplateId**: must match an existing MapTemplate row's `Id` AND that row's `MapType` must be `Town`. Importer rejects mismatch.
  - **MapName**: free-form string for human readability in the JSON output.
  - **CellSize**: meters per cell. Must match the `CellSize` field on every referenced ChunkTemplate. Importer enforces consistency.

### 5. Add a TownChunkPlacement child per chunk

For each chunk you want to place, add an empty child GameObject and attach **`TownChunkPlacement`**:

- **ChunkName**: must match a `ChunkTemplate.Name` already in the DB (i.e., already imported via `Avalon.ChunkImporter`).
- **GridX / GridZ**: integer cell coordinates. World position will be `(GridX*CellSize, 0, GridZ*CellSize)`. Cell `(0, 0)` is the SW corner; `+X` is east, `+Z` is north.
- **Rotation**: 0–3 (90° steps clockwise around Y). All chunks at rotation 0 sit in their authored orientation; non-zero rotates the chunk **around its centre** (`cellSize/2, cellSize/2` of the SW-anchored authoring frame) so the footprint stays inside the declared `(GridX, GridZ)` cell. The single source of truth for that pivot is `ChunkRotation.LocalToWorld` (mirrored on server + client); both bake and visualizer call it. The procedural generator uses all 4 rotations to fit chunk exits to neighbouring cells.
- **IsEntry**: exactly ONE placement per layout must have `IsEntry = true`. The player spawns at this chunk.
- **EntrySpawnLocal**: chunk-local spawn coordinates (only honored when `IsEntry = true`). Defaults to `(15, 0, 15)` for a 30×30 cell. Server's `PredefinedChunkLayoutSource` transforms this to world space and uses it for `ChunkLayout.EntrySpawnWorldPos`.

Adjacency check: walk through pairs of neighbouring placements (`(X, Z) ↔ (X+1, Z)` and `(X, Z) ↔ (X, Z+1)`). For each pair, the chunks' shared boundary needs aligned exit slots on both sides; otherwise the player can't traverse. Walls block. The visualizer's gizmos show wall positions in chunk-local; the layout authoring scene's gizmos show the GridX/GridZ wireframes for spatial reference.

### 6. Save the scene

### 7. Export the town layout

- Menu: `Avalon → Town → Town Layout Exporter`.
- Click **Export All In Scene** (or **Export Selected** for a specific root).
- Validation: at least one placement, exactly one `IsEntry`, no duplicate `(GridX, GridZ)`, every `ChunkName` non-empty, `MapTemplateId > 0`.
- Output: `<ExportDir>/town_layouts/<MapTemplateId>.json`.

### 8. Import the layout

Same CLI as for chunks:

```bash
dotnet run --project tools/Avalon.ChunkImporter -- /c/dev/3D/ChunksExport
```

The CLI walks `<exportDir>/town_layouts/*.json` after the chunk loop. For each file:
- Validates: MapTemplateId exists + is `Town`, exactly one IsEntry, no duplicate `(gridX, gridZ)`, all `ChunkName`s resolve, every chunk's `CellSize` matches the layout's.
- Transactional upsert: deletes all existing `MapChunkPlacement` rows for the map id, inserts the new set.

Console output: `... town_layouts/N.json: replaced X, inserted Y placements for MapId N`.

### 9. Verify in DB

```bash
psql -U postgres -d world -c "SELECT \"MapTemplateId\", \"ChunkTemplateId\", \"GridX\", \"GridZ\", \"IsEntry\" FROM \"MapChunkPlacements\" WHERE \"MapTemplateId\" = 1;"
```

Expect one row per placement.

### 10. Restart the server

The server caches `ChunkTemplate` data in `IChunkLibrary` at startup. Restart to pick up new chunks/placements:

```bash
dotnet run --project src/Server/Avalon.Server.World
```

Town instances are built lazily on first character-select — no startup pre-build is needed.

### 11. Test in Unity

Press Play. Log in. The character-select handler:
- Resolves the town instance via `InstanceRegistry.GetOrCreateTownInstanceAsync(MapTemplateId)`.
- Builds the instance via `ChunkLayoutInstanceFactory.BuildAsync` → `PredefinedChunkLayoutSource.BuildAsync` → reads `MapChunkPlacement` rows → produces `ChunkLayout`.
- Bakes navmesh from stitched chunk objs (server-side `Maps/Chunks/<name>.obj`).
- Sends `SChunkLayoutPacket` to the client.

Client-side, the `AuthFlowOrchestrator` pre-subscribes to `SChunkLayoutPacket` BEFORE sending `CCharacterSelected` (the dispatcher is fire-and-forget so a late subscriber would miss it). Captured packet stashes on `GameSession.InitialChunkLayout`. After scene transition, three components consume it:
- `PlayerMovementPredictor.OnChunkLayout` — warps `transform.position` to the authoritative `EntrySpawn`, kicks off `ClientMapNavigator.LoadFromLayoutAsync` (background bake of the same navmesh server uses).
- `ChunkLayoutVisualizer.OnChunkLayout` — instantiates one `MeshFilter`+`MeshRenderer` per placement using the runtime-parsed `.obj` from `StreamingAssets/Chunks/`.
- `ChunkMarkerVisualizer.OnChunkLayout` — debug markers (entry / spawn / portals / exits).

Player should spawn at the entry chunk, walk through unblocked exits between chunks, hit walls where chunks aren't connected, and stay within authoritative bounds (server clamps via DotRecast raycast, client clamps the same way for prediction).

### 12. Commit

Same as chunk authoring — two repos, two commits, personal email.

---

## Creating a procedural map

Procedural maps generate their layout at runtime from a chunk pool + RNG seed.

### 1. Author the chunks

Use distinct names from town chunks (e.g. `forest_*`, `dungeon_*`). Important markers:
- **Entry chunk** — must declare a `ChunkSpawnSlotMarker` tagged `"entry"` AND a `ChunkPortalSlotMarker` with `Role = Back`. The generator picks an entry chunk from candidates that have both.
- **Boss chunk** (optional) — declares a spawn slot tagged `"boss"` AND a `ChunkPortalSlotMarker` with `Role = Forward`. Procedural generator places the boss at the end of the main path if `ProceduralMapConfig.HasBoss = true`.
- **Path chunks** — common middles. Declare any tags you want spawn tables to filter on.

Export, sync, import as before.

### 2. Define a ChunkPool

A `ChunkPool` is a named bag of `ChunkPoolMember`s with weights. The procedural generator picks chunks from a pool weighted-random.

Currently chunk pools are seeded in EF migrations or via SQL. Convention: one pool per biome.

### 3. Define a ProceduralMapConfig

`ProceduralMapConfig` lives in the World DB. Fields:
- `MapTemplateId` (FK to MapTemplate)
- `ChunkPoolId` (FK to ChunkPool)
- `MainPathMin`, `MainPathMax` — main-path length range (chunk count)
- `BranchChance`, `BranchMaxDepth` — side-branch settings
- `HasBoss` — gate for boss chunk placement
- `BackPortalTargetMapId`, `ForwardPortalTargetMapId` — destination map ids for portal slots

Configs are seeded in migrations.

### 4. Restart server, test

Procedural map instances are built per-player on `EnterMapHandler` traversal (or character-select if the character is currently in a procedural map). Same flow as town from there: bake → wire → client visualizes.

Each player gets their own seed (server's `ProceduralChunkLayoutSource.NextSeed()`), so layouts differ per session.

---

## Updating existing chunks/towns

### Updating a chunk's geometry or markers

1. Open `Assets/Scenes/ChunkAuthoring.unity`.
2. Edit the chunk root GameObject in place. Same `ChunkName` MUST be retained — the importer matches by name and does an UPSERT. Renaming creates a new template + leaves the old orphaned.
3. Re-export (`Avalon → Procedural → Chunk Exporter`).
4. Sync (`Avalon → Chunks → Sync Chunk Catalog`).
5. Import (`dotnet run --project tools/Avalon.ChunkImporter ...`).
6. Restart server.
7. Test in Unity.

The `ChunkTemplate` row is updated (CellSize, Exits, SpawnSlots, PortalSlots, Tags). All maps using that chunk pick up the changes on next instance-build.

### Updating a town layout

1. Open `Assets/Scenes/TownLayoutAuthoring.unity`.
2. Edit the `TownLayoutAuthoringRoot` and its child placements. `MapTemplateId` MUST match the existing town's id.
3. Re-export (`Avalon → Town → Town Layout Exporter`).
4. Import (CLI).
5. The importer wipes existing `MapChunkPlacement` rows for the map id and inserts the new set inside a transaction. Idempotent.
6. Restart server.
7. Test.

### Renaming a chunk

Don't. The importer matches by `Name` and would treat the rename as a NEW chunk + leave the old `ChunkTemplate` row orphaned. If you really need to rename:
1. Author the new chunk with the new name.
2. Update every layout/pool that referenced the old name to use the new one.
3. Manually delete the old `ChunkTemplate` row from the DB (only safe if no remaining references).

### Adding a new chunk to an existing town

Edit the town's TownLayoutAuthoring scene → add a new `TownChunkPlacement` child → re-export → import. Importer's `ReplaceForMapAsync` handles the upsert.

---

## Map teleportation

Portals route the player between maps. Each portal is one of two roles:
`Back` (returns to a previous map) or `Forward` (advances to the next).

### Authoring a portal

1. In `ChunkAuthoring.unity`: place a `ChunkPortalSlotMarker` on the chunk
   at the desired chunk-local position. Pick `Role` = `Back` or `Forward`.
2. Re-export chunks + sync.
3. In `TownLayoutAuthoring.unity` (for towns): on the `TownChunkPlacement`
   that holds this chunk, set `BackPortalTargetMapId` or
   `ForwardPortalTargetMapId` to the destination map's `MapTemplate.Id`.
   Leave 0 if the slot is decorative / not routed.
4. Re-export layout + run `Avalon.ChunkImporter`.

For procedural maps the targets come from
`ProceduralMapConfig.BackPortalTargetMapId` / `ForwardPortalTargetMapId` —
no per-placement override.

### Runtime flow

Server-side `PredefinedChunkLayoutSource.BuildPortals` reads the per-
placement targets and emits `PortalPlacement(role, world, targetMapId)`
into `ChunkLayout.Portals`. `PortalPlacementService.Place` materialises
them as `PortalInstance`s on the `MapInstance`. The wire packet
`SChunkLayoutPacket` ships the placements as `PortalPlacementDto[]`.

Client `PortalRuntimeSpawner` instantiates a clickable cylinder per
portal (`PortalRuntime` MonoBehaviour). On click, `PortalRuntime` checks
proximity vs the player and sends `CEnterMapPacket(TargetMapId)`. The
server validates proximity again (anti-cheat), resolves / creates the
target instance via `ChunkLayoutInstanceFactory`, calls
`World.TransferPlayer`, and emits `SMapTransitionPacket(Success, ...)`
followed by `SChunkLayoutPacket` for the new instance.

Client `MapTransitionService` receives the success packet, warps the
player + calls `PlayerMovementPredictor.ResetForTransition` (clears ring
buffer, `_nextSeq=1`, sets `_transitionInFlight=true`).
`MapTransitionLoadingOverlay` fades a fullscreen black layer in. The
new `SChunkLayoutPacket` triggers `ClientMapNavigator.LoadFromLayoutAsync`
(invalidates prior navmesh + background bake). When `IsReady` flips, the
overlay fades out and the predictor's input gate releases.

Server resets `connection.LastInputSeq = 0` in `MapInstance.AddCharacter`
so the client's reset seq=1 is accepted.

---

## End-to-end trace (what happens when a player enters a town)

1. **Client:** `AuthFlowOrchestrator` pre-subscribes to `SChunkLayoutPacket`, sends `CCharacterSelectedPacket`.
2. **Server `CharacterSelectHandler`:** loads the `Character` from DB, looks up the `MapTemplate`, calls `InstanceRegistry.GetOrCreateTownInstanceAsync(templateId)`.
3. **Server `InstanceRegistry`:** if a town instance exists with capacity, returns it; otherwise calls `CreateAndInitializeInstanceAsync` → `ChunkLayoutInstanceFactory.BuildAsync(template, ownerAccountId=null, ct)`.
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
6. **Client `AuthFlowOrchestrator`:** captures the layout packet via the pre-subscription, stashes it on `GameSession.InitialChunkLayout`, then transitions to `GameplayScene`.
7. **Client `PlayerMovementPredictor.Start`:** subscribes to `SPlayerStateAckPacket` + `SChunkLayoutPacket`. Reads `GameSession.InitialChunkLayout` (immediate, since the wire packet was already dispatched) → `OnChunkLayout`:
   - Warps `transform.position` to authoritative `EntrySpawn`.
   - Kicks off `ClientMapNavigator.LoadFromLayoutAsync(layout)` on a worker thread → stitches the same chunk objs from `StreamingAssets/Chunks/<name>.obj` via the same `AppendTransformed` rotation case table the server uses → bakes via UniRecast (deterministic mirror of server bake).
8. **Client `ChunkLayoutVisualizer.Start`:** same pattern; on layout receipt, parses each `<name>.obj` via `ObjMeshLoader` into a Unity `Mesh`, instantiates a child GameObject per placement at `(GridX*CellSize, 0, GridZ*CellSize)` with `Quaternion.Euler(0, -Rotation*90, 0)` (negation matches AppendTransformed's left-handed rotation table).
9. **Client `ChunkMarkerVisualizer.Start`:** loads each chunk's `.json` via `ChunkMetaCatalog`, instantiates coloured primitive markers at world-transformed local coords for spawn slots, portal slots, and exit gap centers. Plus a green cylinder at `EntrySpawn`.
10. **Client `PlayerMovementPredictor.FixedUpdate`:** samples WASD, sends `CPlayerInputPacket`, predicts via `IntegrateStep` (clamp via `ClientMapNavigator.RaycastWalkable`, ground via `SampleGroundHeight`).
11. **Server `PlayerInputHandler`:** validates seq, integrates the same way (its own `MapNavigator.RaycastWalkable`), updates `entity.Position`, sends `SPlayerStateAckPacket`.
12. **Client `OnAck`:** if drift > 1mm, rebases predicted ring + replays unacked inputs. Snap or smooth-correct depending on drift magnitude.

Identical bake on both sides (same `.obj` source, same DotRecast settings) keeps drift sub-mm in practice — so reconciliation rarely fires.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Server crashes at startup with "Unable to activate type 'PredefinedChunkLayoutSource'. Constructors are ambiguous" | Two public ctors on the source | Single ctor + static `ForTesting(...)` factory |
| Server: `Chunk obj not found: Maps\Chunks\N.obj` (where N is a number) | Bake using `TemplateId.Value` instead of `Name` for filename | `ChunkLayoutNavmeshBuilder` resolves name via `IChunkLibrary.GetById(id).Name` |
| Server: `No MapChunkPlacement rows for town map N` | DB hasn't been imported with placements | Run `ChunkImporter` after exporting `town_layouts/N.json` |
| Server: `Town map N has no IsEntry placement` | Layout exporter accepted layout with 0 or >1 IsEntry | Edit `TownLayoutAuthoring` scene; ensure exactly one TownChunkPlacement has `IsEntry=true` |
| Server: `chunk 'X' has CellSize=Y != layout Z` | Mixed cell sizes in one layout | All chunks in a layout MUST have identical `CellSize` |
| Client: player spawns at wrong position | Persisted `Character.X/Y/Z` is stale | For towns, `CharacterSelectHandler` already overrides with `Layout.EntrySpawnWorldPos`. For procedural, persist position correctly when player exits map. |
| Client: player can't move (`navReady=true` but pos stuck) | Navmesh raycast lookup miss | Verify chunk-axis convention: `RaycastWalkable` MUST query DotRecast with the SAME coords used during bake (no X-flip) |
| Client: chunks not visible in scene | Visual rendering not wired | Add `ChunkLayoutVisualizer` to `GameplayScene` |
| Client: only first SChunkLayoutPacket missed | Race: predictor subscribes after scene load (post-packet) | `AuthFlowOrchestrator` pre-subscribes BEFORE sending `CCharacterSelected`, stashes on `GameSession.InitialChunkLayout`; predictor reads it on Start |
| Client: chunks visible but "rotation wrong" | Either chunk-author error OR rotation-axis mismatch between bake and visualizer | Bake's `AppendTransformed` is the source of truth; client visualizer's rotation handling negates the angle to match left-handed Unity |
| Client: chunk visualizer can't find `.obj` | `StreamingAssets/Chunks/` not synced | Run `Avalon → Chunks → Sync Chunk Catalog` |
| Client: marker visualizer can't find `.json` | Same | Same |
| Layout valid but adjacency broken (chunks isolated) | Authored chunks have mismatched exits at neighbouring boundaries | Re-author chunks so each pair of neighbouring chunks has an aligned `(Side, Slot)` exit on both faces of the shared boundary |
| Server importer: `unknown chunk names: ...` | Layout references chunk that wasn't imported in the same run (or is misspelled) | Author + export the missing chunk; or fix the typo in `TownChunkPlacement.ChunkName` |

## Conventions and gotchas

- **Chunk filename = `ChunkTemplate.Name`** (NOT `ChunkTemplate.Id`). Importer copies `chunk.obj` → `Maps/Chunks/<Name>.obj` and `<Name>.obj` → `StreamingAssets/Chunks/<Name>.obj`. Bake on both sides looks up by name.
- **DotRecast bake is deterministic** given identical input + identical `NavmeshBuildSettings`. Server and client baking the same chunk objs produces the same navmesh (modulo last-bit floating-point that's well within snap thresholds). Drift = build settings drift between repos. The constants live in `NavmeshBuildSettings.cs` server-side and `ClientMapNavigator.BuildSettings()` client-side; **keep them byte-identical**.
- **`AppendTransformed`** is the source of truth for chunk-stitching geometry. Server's lives in `ChunkLayoutNavmeshBuilder`; client's lives in `ClientMapNavigator` + `ChunkLayoutVisualizer`. **All three must agree byte-for-byte** on the rotation case table:
  - `0: (x, z)` — identity
  - `1: (z, -x)` — 90° rotation (left-handed)
  - `2: (-x, -z)` — 180°
  - `3: (-z, x)` — 270°
- **Unity left-handed coords**: `Quaternion.Euler(0, +y, 0)` rotates `+X → +Z`, opposite of the bake's case table. The visualizer applies `Quaternion.Euler(0, -Rotation*90, 0)` to match.
- **Coord-flip discipline**: bake takes Unity coords as-is (no axis flip); queries (`RaycastWalkable`, `SampleGroundHeight`) must also use Unity coords as-is. The legacy `(x,z).obj` pipeline used a -X convention — its remnants in `MapNavigator.FindPath` / `HasVisibility` aren't load-bearing on chunk maps but should be cleaned up when those code paths are revisited.
- **Town spawn override**: returning players in towns have stale persisted `Character.X/Y/Z` (e.g. baked under the old world.bin pipeline). `CharacterSelectHandler.OnInstanceObtained` overrides with `Layout.EntrySpawnWorldPos` for `MapType=Town`; procedural maps still respect persisted coords (set by `EnterMapHandler` on traversal in).
- **Pre-subscribe race**: the dispatcher is fire-and-forget. Server emits `SChunkLayoutPacket` immediately after `SCharacterSelectedPacket`. By the time `GameplayScene` loads, the layout packet has been dispatched to zero subscribers. `AuthFlowOrchestrator.LoginAsync` (and its reconnect twin) pre-subscribes during character-select with a `TaskCompletionSource`, awaits the first packet, and stashes it on `GameSession.InitialChunkLayout`. Components in `GameplayScene` read from there instead of relying on dispatcher live-fire.
- **Email policy**: every commit on Avalon.Server + 3D MUST be authored by `Nuno Silva <nuno.levezinho@live.com.pt>`. Subagents that commit MUST `unset GIT_AUTHOR_*` before `git commit`. Verify after with `git log -1 --pretty=format:'%ae %an'`.
- **Test policy on the Unity client**: NO tests in `C:\dev\3D`. Compile-check via Unity domain reload + console error filter only.
- **Plans + specs gitignored** in `docs/superpowers/`. They stay local. Regular `docs/` (this file) is committed.
