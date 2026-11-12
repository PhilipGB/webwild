#!/usr/bin/env python3
"""Static bridge handoff checks; Python stdlib only. Does not substitute for Godot import."""
from pathlib import Path
import json,struct,re,hashlib,sys
ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'docs/bridge_asset_manifest.json').read_text())
errors=[]

def check(cond,msg):
    if not cond: errors.append(msg)

def glb_info(path):
    raw=path.read_bytes(); check(len(raw)>=20 and raw[:4]==b'glTF',f'{path}: missing glTF header')
    magic,version,total=struct.unpack_from('<4sII',raw,0)
    check(version==2 and total==len(raw),f'{path}: bad v2 length')
    length,code=struct.unpack_from('<II',raw,12)
    check(code==0x4e4f534a,f'{path}: JSON chunk absent')
    gltf=json.loads(raw[20:20+length])
    check('meshes' in gltf and len(gltf['meshes'])>0,f'{path}: no meshes')
    for mesh in gltf.get('meshes',[]):
        for primitive in mesh['primitives']:
            check('POSITION' in primitive.get('attributes',{}),f'{path}: no positions')
            check('COLOR_0' in primitive.get('attributes',{}),f'{path}: no vertex colours')
    return gltf

check(len(manifest['models'])==11,'Expected 11 assets')
for model in manifest['models']:
    p=ROOT/model['model']; q=ROOT/model['scene']
    check(p.is_file(),f'Missing GLB {p}')
    check(q.is_file(),f'Missing wrapper {q}')
    if not p.is_file() or not q.is_file():continue
    check(hashlib.sha256(p.read_bytes()).hexdigest()==model['sha256'],f'GLB SHA mismatch {p.name}')
    data=glb_info(p)
    wrapper=q.read_text()
    check(model['model'] in wrapper,f'wrapper has no model ref: {q}')
    check('res://art/materials/vertex_color_material.tres' in wrapper,f'missing vertex color material ref: {q}')
    check('CollisionShape3D' not in wrapper and 'StaticBody3D' not in wrapper,f'wrapper has unintended collider: {q}')
    # trimesh GLB export names the mesh child nodes after the geometry names.
    nodes={x.get('name','') for x in data.get('nodes',[])}
    override_names=set(re.findall(r'^\[node name="([^"]+)" parent="Visual/world"',wrapper,re.M))
    mesh_names={x.get('name','') for x in data.get('meshes',[])}
    check(override_names==mesh_names,f'material override name mismatch {p.name}: {override_names^mesh_names}')
check((ROOT/'scenes/building/bridge/BridgeAssetGallery.tscn').is_file(),'Missing gallery')
check((ROOT/'examples/bridge_4m_reference_only.glb').is_file(),'Missing demo')
if (ROOT/'examples/bridge_4m_reference_only.glb').is_file():glb_info(ROOT/'examples/bridge_4m_reference_only.glb')
for p in [ROOT/'00_START_HERE.md',ROOT/'01_OPENCODE_P2_MASTER_PROMPT.md',ROOT/'docs/BRIDGE_ASSET_INTEGRATION.md',ROOT/'docs/BRIDGE_P2_ACCEPTANCE.md']:
    check(p.is_file(),f'Missing handoff doc: {p}')
print('Webwild P2 pack: '+('PASS' if not errors else 'FAIL')+f' — {len(manifest["models"])} coloured GLBs; {len(errors)} errors')
for e in errors:print('FAIL:',e)
if errors:sys.exit(1)
