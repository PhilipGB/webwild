# Scene authoring — Webwild v0.2.0 P0

The persistent garden is authored in Godot `.tscn` scenes. C# adapts the
engine-free simulation to that authored world; it does not build scenery.

## Coordinate contract

There are two explicit authorities:

| Concern | Authority |
|---|---|
| Gameplay coordinates, anchor ids, bridge bank points, graph nodes | `src/Core/WorldLayout.cs` |
| Persistent visual geometry, collision, lighting, landmark placement | `scenes/world/**/*.tscn` |

They must agree. Three mechanisms enforce this:

1. **Startup validation** — `SceneContentValidator` runs in
   `GardenClearing._Ready()` and aborts with a readable error if anything is
   missing or has drifted.
2. **Scene contract command** — `godot --headless --path . -- --scene-validate`
   runs the same checks, writes `artifacts/scene_validation.json` and exits
   non-zero on failure.
3. **Pure tests** — `SceneAuthoringTests` parse the committed `.tscn` text and
   compare anchor and landmark marker coordinates with `WorldLayout`.

Do not add a procedural fallback. A missing scene, anchor or collision shape is
a real error; the game must not silently reconstruct it in C#.

## Scene map

```text
scenes/
├── boot/
│   ├── Main.tscn                  # title/menu host
│   └── TitleBackdrop.tscn         # GardenWorld + orbit camera
├── actors/spider/SpiderPlayer.tscn
├── building/webs/AnchorSocket.tscn
└── world/
    ├── GardenClearing.tscn        # gameplay composition
    ├── GardenWorld.tscn           # persistent garden root
    ├── GardenEnvironment.tscn     # sky, sun, fill light
    ├── GardenTerrain.tscn         # meadows, ravine, ground collision
    ├── GardenPaths.tscn           # path stones and soil
    ├── GardenFlora.tscn           # scattered plants, rocks, mushrooms
    ├── GardenBoundaries.tscn      # hedge walls and edge foliage
    ├── GardenLandmarks.tscn       # eight landmark instances
    └── landmarks/
        ├── HomeLeaf.tscn
        ├── TwinTwigs.tscn
        ├── PebbleGap.tscn
        ├── FlowerLane.tscn
        ├── LeafNursery.tscn
        ├── ClimbableStem.tscn
        ├── FallenLog.tscn
        └── WindEdge.tscn
```

`GardenWorld.tscn` is instanced by both `GardenClearing.tscn` and
`TitleBackdrop.tscn`, so the menu backdrop and the playable garden can never
diverge.

`GardenClearing.tscn` adds the gameplay composition:

```text
GardenClearing
├── World            (GardenWorld.tscn instance)
├── Anchors          (12 AnchorSocket.tscn instances with AnchorId)
├── Player           (SpiderPlayer.tscn instance)
├── Webs
├── Insects
├── Workers
├── WebPreview
└── UI               (GameUI is added here at runtime)
```

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

`--scene-validate` checks 24 contracts: scene composition, all eight landmark
instances, 12 unique anchor ids, anchor transforms and landmark markers,
nursery/climb/bridge markers, ravine width, collision layers, boundary bodies,
player spawn, environment, 123 model instances and 934 meshes, baked vertex
colour materials, removal of the old procedural builder, and scene loading.

The runtime probe includes the same 24 checks plus the full gameplay acceptance
flow.

## Known limitations

- Gameplay coordinates are still authored in C# (`WorldLayout`) and mirrored in
  scenes. There is no editor plugin that writes `WorldLayout` automatically;
  validation is the safety net, not an auto-sync.
- The garden is one clearing. The second region remains future work.
- Windows runtime and supervised child playtesting remain unverified.
