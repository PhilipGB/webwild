#!/usr/bin/env python3
"""Validate baked Webwild Reedwater content without launching Godot.
Checks GLB validity, mesh attributes, resource paths, scene wrappers, contacts and manifest.
Does not replace a Godot editor import or exported runtime test.
"""
import json,math,pathlib,re,struct,sys
import numpy as np
import trimesh

root=pathlib.Path(sys.argv[1]).resolve()
manifest=json.loads((root/'asset_manifest.json').read_text())
assert manifest['model_count']==15
assert len(manifest['models'])==15
errors=[]; stats=[]
for e in manifest['models']:
    model=root/e['model'];scene=root/e['scene']
    if not model.exists():errors.append(f'missing model {model}');continue
    if not scene.exists():errors.append(f'missing scene {scene}');continue
    glb=model.read_bytes()
    if len(glb)<20 or glb[:4]!=b'glTF' or struct.unpack_from('<I',glb,4)[0]!=2 or struct.unpack_from('<I',glb,8)[0]!=len(glb):
        errors.append(f'invalid GLB header {model}');continue
    nchunk=struct.unpack_from('<I',glb,12)[0]
    data=json.loads(glb[20:20+nchunk])
    if data.get('asset',{}).get('version')!='2.0':errors.append(f'bad version {model}')
    if data.get('textures') or data.get('images'):errors.append(f'unexpected external textures in {model}')
    for m in data['meshes']:
        for p in m['primitives']:
            if 'COLOR_0' not in p.get('attributes',{}):errors.append(f'missing COLOR_0: {model} {m.get("name")}')
            if 'POSITION' not in p.get('attributes',{}):errors.append(f'missing positions: {model}')
    loaded=trimesh.load(model,force='scene',process=False)
    if len(loaded.geometry)<2:errors.append(f'missing component meshes: {model}')
    if not np.isfinite(loaded.bounds).all():errors.append(f'nonfinite bounds: {model}')
    actual=[float(x) for x in loaded.bounds[1]-loaded.bounds[0]]
    if not np.allclose(actual,e['size_m'],atol=.004):errors.append(f'manifest bounds disagree {model}')
    text=scene.read_text()
    gltfnode=data['nodes'][data['scenes'][0]['nodes'][0]]
    if gltfnode['name']!='world':errors.append(f'root not world {model}')
    for idx in gltfnode['children']:
        node=data['nodes'][idx]['name']
        if f'[node name="{node}" parent="Visual/world"' not in text:
            errors.append(f'{scene}: missing material override node {node}')
    if text.count('material_override = ExtResource("2_material")') != len(gltfnode['children']):
        errors.append(f'{scene}: missing material override')
    if not ('res://'+e['model']) in text:errors.append(f'{scene}: wrong resource ref')
    if e['collision'] is not None and 'type="CollisionShape3D"' not in text:errors.append(f'{scene}: missing collider')
    if e['collision'] is None and 'type="CollisionShape3D"' in text:errors.append(f'{scene}: unexpected collider')
    stats.append((e['name'],len(loaded.geometry),sum(len(g.faces) for g in loaded.geometry.values())))

for scene in (root/'scenes').rglob('*.tscn'):
    text=scene.read_text()
    for res in re.findall(r'path="res://([^\"]+)"',text):
        if not (root/res).exists():errors.append(f'{scene}: missing resource res://{res}')

assert (root/'previews/reedwater_contact_sheet.png').exists()
assert (root/'art/materials/reedwater_vertex_color_material.tres').exists()
print(f'Verified {len(stats)} GLBs, {sum(x[1] for x in stats)} independent meshes, {sum(x[2] for x in stats)} triangles.')
print('15 authored prop scenes, gallery scene, vertex-colour material and resource references inspected.')
if errors:
    for err in errors:print('ERROR: '+err)
    sys.exit(1)
print('PASS (static package validation; Godot editor/runtime import still required)')
