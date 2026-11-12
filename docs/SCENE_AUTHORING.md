# Scene authoring — Webwild v0.2.0 P1

Both playable regions are authored in Godot `.tscn` scenes. C# adapts the
engine-free simulation to that authored world; it does not build scenery.

## Coordinate contract

There are two explicit authorities:

| Concern | Authority |
|---|---|
| Gameplay coordinates, anchor ids, bridge bank points, graph nodes | `src/Core/WorldLayout.cs`, `src/Core/Regions.cs` |
| Persistent visual geometry, collision, lighting, landmark placement | `scenes/world/**/*.tscn` |

They must agree. Three mechanisms enforce this:

1. **Startup validation** — `SceneContentValidator` runs in
   `GardenClearing._Ready()` and aborts with a readable error if anything is
   missing or has drifted.
2. **Scene contract command** — `godot --headless --path . -- --scene-validate`
   runs the same checks, writes `artifacts/scene_validation.json` and exits
   non-zero on failure.
3. **Pure tests** — `SceneAuthoringTests` parse the committed `.tscn` text and
   compare anchor and landmark marker coordinates with `WorldLayout` and
   `ReedwaterLayout`.

Do not add a procedural fallback. A missing scene, anchor or collision shape is
a real error; the game must not silently reconstruct it in C#.

## Scene map

```text
scenes/
├── boot/
│   ├── Main.tscn                    # title/menu host
│   └── TitleBackdrop.tscn           # GardenWorld + orbit camera
├── actors/spider/SpiderPlayer.tscn
├── building/webs/AnchorSocket.tscn
└── world/
    ├── GardenClearing.tscn          # gameplay composition
    ├── GardenWorld.tscn             # persistent garden root
    ├── GardenEnvironment.tscn       # sky, sun, fill light
    ├── GardenTerrain.tscn           # meadows, ravine, ground collision
    ├── GardenPaths.tscn             # path stones and soil
    ├── GardenFlora.tscn             # scattered plants, rocks, mushrooms
    ├── GardenBoundaries.tscn        # hedge walls and edge foliage
    ├── GardenLandmarks.tscn         # eight landmark instances
    ├── landmarks/
    │   ├── HomeLeaf.tscn
    │   ├── TwinTwigs.tscn
    │   ├── PebbleGap.tscn
    │   ├── FlowerLane.tscn
    │   ├── LeafNursery.tscn
    │   ├── ClimbableStem.tscn
    │   ├── FallenLog.tscn
    │   └── WindEdge.tscn
    └── regions/
        ├── GardenClearing/GardenPassage.tscn
        └── ReedwaterBank/
            ├── ReedwaterBank.tscn      # region root: World, Anchors, travel markers
            ├── ReedwaterWorld.tscn
            ├── ReedwaterEnvironment.tscn
            ├── ReedwaterTerrain.tscn   # banks, pond water areas, pond bed
            ├── ReedwaterPaths.tscn
            ├── ReedwaterLandmarks.tscn # five landmark instances
            ├── ReedwaterFlora.tscn
            ├── ReedwaterBoundaries.tscn
            └── landmarks/
                ├── StartBank.tscn
                ├── IslandA.tscn
                ├── IslandB.tscn
                ├── FarBank.tscn
                └── Outpost.tscn
```

Reedwater's supplied model wrappers remain in `scenes/world/reedwater/props/`
so the asset-pack paths do not move.

`GardenWorld.tscn` is instanced by both `GardenClearing.tscn` and
`TitleBackdrop.tscn`, so the menu backdrop and the playable garden can never
diverge.

`GardenClearing.tscn` is the session scene. It adds the gameplay composition and
also holds cached external region roots under `Regions`:

```text
GardenClearing
├── World            (GardenWorld.tscn instance)
├── Anchors          (12 AnchorSocket.tscn instances with AnchorId)
├── Player           (SpiderPlayer.tscn instance)
├── Webs
├── Insects
├── Workers
├── WebPreview
├── TravelPoints     (ToReedwaterBank, FromReedwaterBank)
├── Regions          (external region scene cache)
└── UI               (GameUI is added here at runtime)
```

`ReedwaterBank.tscn` is authored as a sibling region root. It owns its `World` and
`Anchors`; the session reuses the single `Player`, camera, UI, audio and runtime
roots. Inactive regions have their visuals, collision and lights disabled, not
deleted, so regional progression is always retained in the core simulation.

## Editing scenery

### Move or replace a prop

1. Open `scenes/world/landmarks/<Landmark>.tscn` (or `GardenFlora`,
   `GardenPaths`, `GardenTerrain`, `GardenBoundaries`).
2. Select the prop node and move, rotate or scale it in the 3D viewport.
3. Save the scene and run:

   ```bash
   godot --headless --path . -- --scene-validate
   ```

4. Run the runtime probe if the change affects visuals or collision:

   ```bash
   godot --headless --path . -- --runtime-probe
   ```

Decorative props are ordinary `Node3D`/`MeshInstance3D` nodes or instances of
the original `.glb` assets. No C# change is required.

### Reuse or swap a model

Model instances reference the original `res://art/models/**.glb` files. Select
an instance and use **Change Type** / replace its `PackedScene` reference, or
instance the same `.glb` elsewhere. Keep the mesh-level
`material_override = vertex_color_material.tres` overrides so the vertex-colour
palette still renders. The validator reports any model instance whose meshes
lost their material.

### Edit collision

Ground, boundary and landmark collision is ordinary `StaticBody3D` +
`CollisionShape3D` content:

- `GardenTerrain.tscn` — `WestMeadow` and `EastMeadow` ground boxes.
- `GardenBoundaries.tscn` — four hedge walls.
- `ReedwaterTerrain.tscn` — `StartBank`/`FarBank` ground boxes and four
  `Area3D` water volumes on layer 8, mask 2. The tall channel volumes catch jumps
  over open water; the low pond volume catches falls. C# returns the player to
  the last safe bank position when they touch one.
- `ReedwaterLandmarks.tscn` — island, post and root colliders.
- `AnchorSocket.tscn` — the 0.75 m interaction sphere (`Area3D`, layer 4).

Select the `CollisionShape3D` in the editor to move or resize it. Keep ground
and hedge bodies on layer 1 with mask 0, and anchors on layer 4 with mask 0.
The validator checks these contracts.

### Add a landmark

1. Create `scenes/world/landmarks/MyLandmark.tscn` with a `Node3D` root.
2. Add model instances and any collision.
3. Add the landmark scene as an instance under `GardenLandmarks.tscn`.
4. Add its expected path to `SceneContentValidator.Landmarks` and to
   `SceneAuthoringTests`.
5. Add an `Anchor_<id>` `Marker3D` for every gameplay anchor the landmark owns.
6. Add the anchor to `WorldLayout.CreateAnchors()` and an `AnchorSocket.tscn`
   instance under `GardenClearing/Anchors` with the matching `AnchorId`.
7. Run scene validation and the runtime probe.

For Reedwater, follow the same steps in
`scenes/world/regions/ReedwaterBank/landmarks/` and
`ReedwaterLayout.CreateAnchors()`.

### Move or add an anchor

Anchors are duplicated deliberately because the simulation is engine-free:

1. Update the coordinate in `WorldLayout.CreateAnchors()`.
2. Move the `AnchorSocket.tscn` instance under `GardenClearing/Anchors` and keep
   its `AnchorId` unchanged.
3. Move the matching `Anchor_<id>` marker inside the owning landmark scene.
4. Run:

   ```bash
   godot --headless --path . -- --scene-validate
   ```

A moved socket without the other two changes fails with
`anchor-transforms-match-simulation` and `landmark-anchor-markers`, naming the
exact anchor and both coordinates.

The nursery, climb base/top and bridge bank points follow the same rule:
`NurseryPoint`, `ClimbBase`, `ClimbTop`, `BridgeGroundWest` and
`BridgeGroundEast` markers must match `WorldLayout`.

## What remains runtime-generated

The following are intentionally not authored as persistent scenery:

- capture webs and silk bridges (including bridge collision)
- insects and worker spiders
- wind particles and temporary effects
- labels, glows, previews and all UI
- simulation state, missions, economy and save/load
- player movement and camera behaviour

## Validation reference

```bash
dotnet build -warnaserror
dotnet test
godot --headless --path . --editor --quit
godot --headless --path . -- --scene-validate
godot --headless --path . -- --runtime-probe
```

`--scene-validate` checks **47 contracts** across both regions: the garden
composition, eight landmarks, 12 anchors and markers, ravine/collision contracts,
123 model instances and 934 meshes, plus Reedwater's authored world groups, five
landmarks, 10 anchors and markers, bridge bank points, bank/water collision,
66 model instances and 397 meshes, its three insect routes, required
`reedwater_vertex_color_material.tres`, and transition-registry checks.

The runtime probe includes the same 47 checks plus the full gameplay acceptance
flow, including water blocking, bridge construction/traversal, Reedwater
objectives, worker crossing, return travel and in-region save/load.

## Adding a future region

The state model, save format, UI and scene loader are already region-aware.

1. Add a stable id to `RegionIds` and an unlock/arrival definition in
   `src/Core/Regions.cs` (or a new `*Layout` class). Add its anchors, nodes,
   ground edges and insect routes to `RegionCatalog`.
2. Author the region root under `scenes/world/regions/<Name>/` with the same
   `World` + `Anchors` contract. Every anchor needs a landmark `Anchor_<id>`
   marker, and each bridge anchor needs a `BridgeGround_<id>` marker.
3. Register the root scene in `RegionSceneCatalog` (`scripts/world/`) with its
   stable core id. The session will instantiate and cache it without duplicating
   the player, UI, audio or runtime roots.
4. Add its validation contract beside `SceneContentValidator.ValidateReedwater`
   and include it in `Main.RunSceneValidation()` and the pure tests.
5. If its objective set differs, add a regional objective config; the shared
   food/silk ledger and total-spider cap already apply across regions.

## Known limitations

- Gameplay coordinates are still authored in C# (`WorldLayout`) and mirrored in
  scenes. There is no editor plugin that writes `WorldLayout` automatically;
  validation is the safety net, not an auto-sync.
- Windows runtime and supervised child playtesting remain unverified.
- Region transitions cache both scenes after first load rather than freeing
  their GPU resources; this is intentional so switching remains instant and
  cannot reset authored content.
- Reedwater's dragonfly is a static GLB used as an insect model. It has no
  skeletal animation or authored flight animation.
