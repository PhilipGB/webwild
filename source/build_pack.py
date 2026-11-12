#!/usr/bin/env python3
"""Builds Godot scene wrappers, integration docs, and a resource manifest for Webwild's Reedwater models.
Run AFTER generate_assets.py. No runtime geometry generation is involved.
"""
import pathlib, json, sys, struct

root=pathlib.Path(sys.argv[1]).resolve()
models=root/'art/models/reedwater'
scenes=root/'scenes/world/reedwater/props'
scenes.mkdir(parents=True,exist_ok=True)
(root/'art/materials').mkdir(parents=True,exist_ok=True)
(root/'scenes/world/reedwater').mkdir(parents=True,exist_ok=True)
(root/'art/materials/reedwater_vertex_color_material.tres').write_text(
'''[gd_resource type="StandardMaterial3D" format=3]

[resource]
vertex_color_use_as_albedo = true
metallic = 0.02
roughness = 0.78
cull_mode = 2
''')

def title(name):return ''.join(w.title() for w in name.split('_'))

COLLIDERS={
 'moss_island_small':('CylinderShape3D',{'radius':1.07,'height':0.88},(0,-0.30,0)),
 'moss_island_medium':('CylinderShape3D',{'radius':1.65,'height':0.88},(0,-0.30,0)),
 'moss_island_large':('CylinderShape3D',{'radius':2.40,'height':0.88},(0,-0.30,0)),
 'stepping_stone':('CylinderShape3D',{'radius':0.53,'height':0.43},(0,-.14,0)),
 'pond_anchor_post':('CylinderShape3D',{'radius':0.17,'height':1.20},(0,.62,0)),
 'exposed_root_anchor':('BoxShape3D',{'size':(1.0,1.04,.85)},(0,.50,0))
}

def vec(x):return f'Vector3({x[0]:.5g}, {x[1]:.5g}, {x[2]:.5g})'

def glb_json(path):
 b=path.read_bytes()
 if b[:4]!=b'glTF':raise Exception('bad glTF header')
 n=struct.unpack_from('<I',b,12)[0]
 return json.loads(b[20:20+n])

manifest=json.loads((root/'docs/model_geometry.json').read_text());entries=[]
for entry in manifest:
 name=entry['name'];glb=models/(name+'.glb');data=glb_json(glb)
 scene_name=title(name)
 collision=COLLIDERS.get(name)
 header=f'[gd_scene load_steps={4 if collision else 3} format=3]\n\n'
 header+=f'[ext_resource type="PackedScene" path="res://art/models/reedwater/{name}.glb" id="1_model"]\n'
 header+='[ext_resource type="Material" path="res://art/materials/reedwater_vertex_color_material.tres" id="2_material"]\n\n'
 if collision:
  kind,params,pos=collision
  header+=f'[sub_resource type="{kind}" id="1_collision"]\n'
  for key,value in params.items():
   header+=f'{key} = {vec(value) if isinstance(value,tuple) else value}\n'
  header+='\n'
 body=f'[node name="{scene_name}" type="Node3D"]\n\n'
 body+='[node name="Visual" parent="." instance=ExtResource("1_model")]\n\n'
 # Trimesh Scene's glTF root node is 'world'; this explicit material override
 # survives editor import and matches the original Webwild asset conventions.
 nodes=data['nodes'];root_nodes=data['scenes'][0]['nodes']
 assert len(root_nodes)==1 and nodes[root_nodes[0]]['name']=='world'
 body+='[node name="world" parent="Visual" index="0"]\n\n'
 children=nodes[root_nodes[0]]['children']
 for idx,childid in enumerate(children):
  child=nodes[childid];assert 'mesh' in child and 'name' in child
  node=child['name'].replace('"','')
  body+=f'[node name="{node}" parent="Visual/world" index="{idx}"]\nmaterial_override = ExtResource("2_material")\n\n'
 if collision:
  kind, params,position=collision
  body+='[node name="Collision" type="StaticBody3D" parent="."]\n'
  body+='collision_layer = 1\ncollision_mask = 0\n\n'
  body+='[node name="CollisionShape3D" type="CollisionShape3D" parent="Collision"]\n'
  body+=f'position = {vec(position)}\nshape = SubResource("1_collision")\n\n'
 f=scenes/(scene_name+'.tscn');f.write_text(header+body)
 entries.append({'name':name,'scene':str(f.relative_to(root)).replace('\\','/'),
                 'model':str(glb.relative_to(root)).replace('\\','/'),
                 'collision':{'type':collision[0],'params':collision[1],'center_m':collision[2]} if collision else None,
                 **{k:entry[k] for k in ('bounds_m','size_m','meshes','triangles','size_bytes')}})

(root/'asset_manifest.json').write_text(json.dumps({'pack':'Webwild Reedwater Bank P1 3D asset pack',
    'unit':'metres','coordinates':'Y-up (Godot compatible)','source':'Original locally authored meshes',
    'vertex_colors':True,'model_count':len(entries),'models':entries},indent=2)+'\n')

# Gallery: intentionally an editor-visible scene, no runtime scripting.
rows=['[gd_scene load_steps=16 format=3]', '']
for i,e in enumerate(entries):
 rows.append(f'[ext_resource type="PackedScene" path="res://{e["scene"]}" id="{i+1}_asset"]')
rows+=['','[node name="ReedwaterAssetGallery" type="Node3D"]','']
for i,e in enumerate(entries):
 # Five columns, three rows; deliberate spacing for large island.
 col=i%5;row=i//5
 pos=((col-2)*8, 0, (row-1)*8)
 rows.append(f'[node name="{title(e["name"])}" parent="." instance=ExtResource("{i+1}_asset")]')
 rows.append(f'position = {vec(pos)}');rows.append('')
 rows.append(f'[node name="Label_{i:02}" type="Label3D" parent="."]')
 rows.append(f'position = {vec((pos[0],3.2,pos[2]))}')
 rows.append(f'text = "{e["name"]}"')
 rows.append('font_size = 36');rows.append('pixel_size = 0.009');rows.append('billboard = 1');rows.append('no_depth_test = true');rows.append('')
rows.extend(['[node name="Sun" type="DirectionalLight3D" parent="."]','rotation_degrees = Vector3(-50, -35, 0)','light_energy = 1.35','shadow_enabled = true',''])
(root/'scenes/world/reedwater/ReedwaterAssetGallery.tscn').write_text('\n'.join(rows))

(root/'README.md').write_text('''# Webwild — Reedwater Bank 3D Asset Pack

**15 original, baked low-poly GLB models** for the Reedwater Bank region planned for Webwild v0.2.0 P1.
Includes **15 editor-visible Godot scene wrappers**, six with editable collision, an asset gallery, a vertex-colour material, a dimensions manifest, a preview sheet and source-generation scripts.

## Install into the existing Webwild repository

1. Extract this archive. Copy the `art/` and `scenes/` directories **into the existing repository root**, preserving their directory layout. The pack adds new paths and does not overwrite existing assets.
2. Open the project in Godot 4.7.2 .NET and let assets import.
3. Open `res://scenes/world/reedwater/ReedwaterAssetGallery.tscn` to examine the 15 models.
4. Instance individual props from `res://scenes/world/reedwater/props/` in the second region's authored scenes.
5. Integrate regional terrain/collision, interactions and insect routes using the normal Webwild code and `WorldLayout`/scene contract. **No gameplay logic or playable region is included in this asset pack.**

Every GLB stores its colour palette as vertex colours. The scene wrappers explicitly apply `res://art/materials/reedwater_vertex_color_material.tres`. The material matches the existing Webwild palette system, with double-sided rendering for thin foliage and insect wings. For builds where Godot discards imported child overrides, preserve the existing targeted `ArtMaterials.ApplyToModelInstances` runtime material pass. The GLBs are Y-up, model-space metres, pivot near ground/water level.

See `docs/ASSET_CATALOGUE.md`, `docs/OPENCODE_INTEGRATION.md` and `asset_manifest.json`.

## Notes and scope

- Static assets only. The dragonfly has no skeletal animation or gameplay route; it can be used as a model in a new insect definition.
- Lily pads are decorative by default. To make them walkable, add authored `StaticBody3D`/`CollisionShape3D` in the level scene at their placed transforms.
- Mossy islands and stepping stone have simple solid walkable scene colliders. Fine-tune collision in the editor for level layout.
- Water shading/simulation, region transitions, missions, sound and additional gameplay are outside the asset-only deliverable.
- The visuals are original and not derived from external copyrighted model assets. No external asset licences apply to the models in this pack.
- **Compatibility validation:** GLB format, vertex colours, geometry bounds and scene resource references are checked by the included verifier. Godot editor import/runtime has not been independently executed in this environment; perform the import as part of P1 CI.

## Source/regeneration

The final models are baked, not created by Godot runtime code. To alter/rebuild the original geometry, install `numpy` and `trimesh`, then run:

```bash
python source/generate_assets.py .
python source/build_pack.py .
python source/validate_pack.py .
```

These scripts are optional source-authoring tools, **not** game runtime dependencies.
''')

catalogue='''# Reedwater Bank — asset catalogue

All dimensions are metre-scale and taken from actual exported GLB geometry. **Y is up.** Where an asset sits on water, place the model pivot at the chosen pond surface; decorative tops may be just above zero. Island tops are approximately at Y=+0.12–0.15 while the soil and rock descend beneath the waterline.

| Model | Approximate width × height × depth (m) | Use | Collision |\n|---|---:|---|---|\n'''
purposes={
'lily_pad_small':'Small floating decoration; capture-web visual context',
'lily_pad_large':'Broad floating pad or pond waypoint',
'lily_pad_cluster':'Grouped pond foliage, irregular arrangement',
'water_lily_open':'Visible pink-and-cream bloom for pond landmarks',
'water_lily_bud':'Closed water lily for variety and progression cues',
'cattail_single':'Sparse marsh edge vertical landmark',
'cattail_cluster':'Dense pond-bank silhouette and visual framing',
'marsh_grass_tuft':'Ground-edge coverage, marsh islands, shallow banks',
'moss_island_small':'Single small bridge destination / landing island',
'moss_island_medium':'Mid-route island for multi-bridge route planning',
'moss_island_large':'Destination island / colony waypoint',
'pond_anchor_post':'Placed web/bridge anchor post on solid bank',
'exposed_root_anchor':'Organic anchor landmark and climbing silhouette',
'dragonfly':'Optional new insect species / harder flight-path challenge',
'stepping_stone':'Small landing point / optional navigation obstacle',
}
for e in entries:
 dims=' × '.join(f'{x:.2f}' for x in e['size_m'])
 catalogue+=f'| `{e["name"]}.glb` | {dims} | {purposes[e["name"]]} | {"Scene collider" if e["collision"] else "None"} |\n'
catalogue+='''
## Art direction

- Low-poly, flat-shaded faceted surfaces with muted garden greens, pastel flowers, amber wood and teal insects.
- Geometry contains actual authored silhouettes: notched lily pads, multi-layered petals, seed-bearing cattails, irregular earthen islands, carved bridge posts, branching roots, four-wing dragonfly.
- Colour is baked into `COLOR_0` attributes; no texture files or online materials are required.
- Keep terrain and water as separate authored scenes. These models do not create a pond or a traversal system by themselves.

## Collision and anchor usage

`moss_island_*` and `stepping_stone` scene wrappers include basic walkable `StaticBody3D` colliders, and `pond_anchor_post`/`exposed_root_anchor` also include solid props. These do not automatically register gameplay anchors: add matching `AnchorSocket.tscn` instances and `WorldLayout` definitions in the game and run `--scene-validate`.

For bridge landing points, position anchor posts entirely on solid ground/islands. Add explicit authored `BridgeGroundPoint` markers and keep both direction and elevation consistent with collision.

## Performance

These are modest-size, independent GLBs. Prefer instancing the same model repeatedly, rather than duplicating imported meshes. Group repeated decorations under the region's `Flora` scene. Dragonfly wing shading uses opaque pastel facets rather than costly transparent surfaces.
'''
(root/'docs/ASSET_CATALOGUE.md').write_text(catalogue)
(root/'docs/OPENCODE_INTEGRATION.md').write_text('''# OpenCode P1 handoff — integrate Reedwater assets

The supplied pack contains **finished new assets**. Do not regenerate them, unless you deliberately need to revise a specific model. Add files into the existing Webwild repository preserving paths.

1. Import `art/models/reedwater/*.glb` and `art/materials/reedwater_vertex_color_material.tres`.
2. Import `scenes/world/reedwater/props/*.tscn` and `scenes/world/reedwater/ReedwaterAssetGallery.tscn`.
3. Confirm all 15 files import in the Godot editor without missing resources or GLB warnings.
4. Use authored prop scene instances to assemble the Reedwater Bank pond/islands/reed beds. Make pond terrain/water in authored `.tscn` scenes, not runtime C# geometry.
5. Keep the existing `WorldLayout` + `SceneContentValidator` approach. Add new region IDs, safe arrival points and authored bridge-bank markers. Use new `pond_anchor_post`/`exposed_root_anchor` model instances at the authored anchor sites.
6. Add `dragonfly.glb` to a new insect route only if you choose to implement corresponding insect behaviour and tests. Do not imply the static model is already animated.
7. The models use `COLOR_0`; prop scenes include material overrides. For exported builds run a rendered probe to verify colour and retain the existing `ArtMaterials.ApplyToModelInstances` compatibility pass when needed.
8. Add region acceptance assertions for water collision, both bridge direction orders, player and worker traversal, save/load and return travel. Validate all previously existing acceptance checks.

**Treat the asset pack as content, not executable game logic.** Do not remove or alter original Webwild art. Test all new assets in editor and Linux export before claiming the region is playable.
''')
print('scene wrappers',len(entries),'gallery',root/'scenes/world/reedwater/ReedwaterAssetGallery.tscn')
