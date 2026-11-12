#!/usr/bin/env python3
"""Build original low-poly bridge component GLBs for Webwild; metres, Y-up.
Requires numpy, trimesh, Pillow (Pillow only for previews). All geometry original.
"""
from __future__ import annotations
import json, math, pathlib, random, shutil, hashlib, zipfile
from collections import defaultdict
import numpy as np
import trimesh
from PIL import Image, ImageDraw, ImageFont

ROOT=pathlib.Path(__file__).resolve().parents[1]
MODELS=ROOT/'art/models/bridge'
SCENES=ROOT/'scenes/building/bridge/parts'
PREVIEW=ROOT/'previews'
EXAMPLES=ROOT/'examples'
DOCS=ROOT/'docs'
for d in [MODELS,SCENES,PREVIEW,EXAMPLES,DOCS]:d.mkdir(parents=True,exist_ok=True)

C={
    'oak':(172,118,76,255), 'oak_light':(201,149,99,255), 'oak_gold':(218,170,117,255),
    'oak_mid':(157,108,70,255), 'oak_dark':(116,78,53,255), 'bark':(104,75,55,255),
    'bark_light':(152,105,75,255), 'endgrain':(229,183,119,255),
    'silk':(241,239,212,255), 'silk_white':(254,252,229,255),
    'silk_shadow':(180,215,201,255), 'silk_mid':(218,233,211,255),
    'moss':(128,170,112,255), 'moss_dark':(95,140,90,255),
    'dew':(129,206,208,255),
}

# These per-face colors intentionally produce hard faceted, texture-free art.
def triangle_mesh(tris, colors):
    assert len(tris)==len(colors)
    v=np.asarray(tris,dtype=float).reshape(-1,3)
    f=np.arange(len(v)).reshape(-1,3)
    m=trimesh.Trimesh(vertices=v,faces=f,process=False)
    m.visual.vertex_colors=np.asarray([c for c in colors for _ in range(3)],dtype=np.uint8)
    return m


def tube(points, radius, c='silk', sides=6, closed=False):
    points=np.asarray(points,float)
    if len(points)<2:return None
    # Continuous tube with a stable local basis; segment-aligned rings.
    triangles=[]; colors=[]; pal=c if isinstance(c,tuple) else C[c]
    up=np.array([0,1.,0]); xaxis=np.array([1.,0,0])
    rings=[]
    for i,p in enumerate(points):
        tangent=points[min(i+1,len(points)-1)]-points[max(i-1,0)]
        tangent/=max(np.linalg.norm(tangent),1e-9)
        u=np.cross(tangent,up)
        if np.linalg.norm(u)<.15:u=np.cross(tangent,xaxis)
        u/=max(np.linalg.norm(u),1e-9)
        v=np.cross(tangent,u); v/=max(np.linalg.norm(v),1e-9)
        rings.append([p+radius*(u*np.cos(2*np.pi*k/sides)+v*np.sin(2*np.pi*k/sides)) for k in range(sides)])
    for i in range(len(points)-1):
        for k in range(sides):
            j=(k+1)%sides
            tris=[[rings[i][k],rings[i][j],rings[i+1][j]],[rings[i][k],rings[i+1][j],rings[i+1][k]]]
            triangles+=tris
            mul=[0.88,1.05,0.99,0.83,1.1,0.92,1.02,0.94][k%8]
            col=tuple(min(255,int(v*mul)) if idx<3 else v for idx,v in enumerate(pal))
            colors.extend([col,col])
    # cap ends
    for k in range(sides):
        j=(k+1)%sides
        triangles.append([points[0],rings[0][j],rings[0][k]])
        triangles.append([points[-1],rings[-1][k],rings[-1][j]])
        colors.extend([pal,pal])
    return triangle_mesh(triangles,colors)


def tapered(points, radii, color, sides=7):
    points=np.asarray(points,float); radii=np.asarray(radii,float)
    triangles=[];colors=[];rings=[]
    for i,p in enumerate(points):
        tangent=points[min(i+1,len(points)-1)]-points[max(i-1,0)]
        tangent/=max(np.linalg.norm(tangent),1e-8)
        ref=np.array([0.,0.,1.])
        u=np.cross(tangent,ref)
        if np.linalg.norm(u)<.1:u=np.cross(tangent,[1,0,0])
        u/=np.linalg.norm(u);v=np.cross(tangent,u)
        rings.append([p+radii[i]*(u*math.cos(2*math.pi*k/sides)+v*math.sin(2*math.pi*k/sides)) for k in range(sides)])
    for i in range(len(points)-1):
        for j in range(sides):
            h=(j+1)%sides
            triangles.extend([[rings[i][j],rings[i][h],rings[i+1][h]],[rings[i][j],rings[i+1][h],rings[i+1][j]]])
            scale=0.74+0.31*((j+2)%4)/3
            co=tuple(int(n*scale) if k<3 else n for k,n in enumerate(C[color]))
            colors.extend([co,co])
    return triangle_mesh(triangles,colors)


def add(scene, name, mesh, transform=None):
    if mesh is not None:
        scene.add_geometry(mesh,geom_name=name,node_name=name,transform=transform)


def make_plank(idx):
    rng=random.Random(261+idx)
    sc=trimesh.Scene()
    length=[.66,.73,.70][idx]; depth=[.22,.19,.24][idx]; thickness=[.07,.06,.055][idx]
    W=depth/2;L=length/2
    # Eight-point irregular polygon in XZ. Top, underface, bark edging, end grain are separate pieces.
    corners=[(-W+.015,-L),(W-.01,-L+.006),(W,-.18*L),(W-.004,.8*L),
            (W-.029,L),(-W+.02,L-.008),(-W, .34*L),(-W+.003,-.64*L)]
    corners=[(x+rng.uniform(-.006,.006),z+rng.uniform(-.014,.014)) for x,z in corners]
    upper=[(x,thickness/2+(rng.random()-.5)*.012,z) for x,z in corners]
    lower=[(x,-thickness/2,z) for x,z in corners]
    centre_top=(0,thickness/2+.002,0)
    tris=[];colors=[]
    topcolors=['oak_light','oak','oak_gold','oak_mid','oak_light','oak','oak_light','oak_gold']
    for j in range(len(upper)):
        k=(j+1)%len(upper)
        tris.append([centre_top,upper[j],upper[k]]);colors.append(C[topcolors[(j+idx)%len(topcolors)]])
        tris += [[upper[j],lower[j],lower[k]],[upper[j],lower[k],upper[k]]]
        sidecolor='endgrain' if (j in (0,4)) else 'bark_light' if j%2 else 'oak_dark'
        colors.extend([C[sidecolor], C[sidecolor]])
        tris.append([(0,-thickness/2,0),lower[k],lower[j]]);colors.append(C['bark'])
    add(sc,'FacetedPlank',triangle_mesh(tris,colors))
    # Carved grain: uneven lengthwise strokes across the top, visually consistent in palette.
    for j, x in enumerate((-.045,-.012,.028,.06)):
        if abs(x)>W-.01:continue
        zmax=.24 if idx!=1 else .28
        pts=[(x+rng.uniform(-.009,.009),thickness/2+.007, zz) for zz in np.linspace(-zmax,zmax,5)]
        add(sc, f'WoodGrain{j+1}',tube(pts,.0037,'oak_dark' if j%2 else 'oak_mid',4))
    if idx==1:
        # Small chipped edge; irregular natural profile, not rectangular tiling.
        add(sc,'ChippedBark',tapered([[-W*.65,-.009,L-.012],[-W*.15,.003,L+.02],[W*.15,.013,L-.012]],[.018,.023,.008],'bark',5))
    if idx==2:
        # A tiny moss accent distinguishes this variant.
        add(sc,'MossTouch',tube([(-.066,thickness/2+.011,.16),(-.056,thickness/2+.011,.20),(-.071,thickness/2+.011,.245)],.010,'moss',5))
    # Threaded bindings close to both tips; they read as silk, not nails.
    for k,z in enumerate((-.26,.26)):
        if abs(z)>=L-.01:continue
        pts=[(-W-.011,0,z),(-W-.012,thickness/2+.019,z),(.0,thickness/2+.022,z),
             (W+.014,thickness/2+.015,z),(W+.014,-thickness/2-.01,z),(-W-.006,-thickness/2-.007,z)]
        add(sc,f'SilkEdgeBinding{k+1}',tube(pts,.009,'silk_white' if k==0 else 'silk_mid',5))
    return sc


def make_knot(kind):
    sc=trimesh.Scene()
    if kind==0:
        # Coiled cinch knot, tip extends along x.
        pts=[(-.13,.005,-.038),(-.085,.04,.005),(-.05,.064,.068),(0,.035,.082),(.06,-.006,.04),(.055,-.038,-.032),
             (0,-.032,-.073),(-.055,0,-.055),(-.065,.041,.0),(-.028,.049,.049),(.033,.027,.046),(.11,-.02,.016)]
        add(sc,'CinchLoop',tube(pts,.022,'silk_mid',7))
        add(sc,'SilkEndLeft',tube([(-.07,-.01,-.014),(-.14,-.044,-.06),(-.17,-.07,-.07)],.012,'silk_white',5))
        add(sc,'SilkEndRight',tube([(.045,.02,.015),(.12,.073,.02),(.18,.09,-.015)],.012,'silk',5))
    else:
        for k,offset in enumerate((-.035,.035)):
            pts=[]
            for t in np.linspace(-1,1,19):
                theta=t*math.pi*1.05+(k*math.pi)
                pts.append((.08*t,.029*math.sin(theta)+offset,.053*math.cos(theta)))
            add(sc,'WovenHitch'+str(k+1),tube(pts,.018,'silk_white' if k==0 else 'silk_shadow',6))
        add(sc,'LockingTurn',tube([(-.043,.06,-.05),(0,.088,0),(.045,.06,.05),(.075,.008,.07)],.013,'silk',6))
    return sc


def make_eyelet():
    sc=trimesh.Scene()
    add(sc,'WoodPeg',tapered([[0,0,0],[.008,.09,0],[.011,.20,-.018],[0,.255,-.012]],[.053,.046,.042,.021],'oak',7))
    add(sc,'PegEndGrain',tapered([[0,.239,-.013],[0,.255,-.013],[0,.259,-.013]],[.030,.024,.010],'endgrain',8))
    points=[(.049,.18,0),(.063,.24,.03),(.025,.292,.034),(-.026,.29,.03),(-.061,.245,0),(-.045,.187,-.011)]
    add(sc,'SilkEyelet',tube(points,.015,'silk_white',7))
    for i,h in enumerate((.08,.115)):
        add(sc,'PegBinding'+str(i),tube([(math.cos(t)*.055,h+0.006*t/(2*math.pi),math.sin(t)*.055) for t in np.linspace(0,2*math.pi,16)],.008,'silk_mid',5))
    return sc


def make_fork():
    sc=trimesh.Scene()
    add(sc,'ForkStem',tapered([[0,0,0],[.004,.13,0],[0,.23,0]],[.063,.048,.039],'oak_mid',7))
    add(sc,'LeftFork',tapered([[0,.22,0],[-.04,.27,-.007],[-.10,.355,.008]],[.042,.030,.012],'oak_light',6))
    add(sc,'RightFork',tapered([[0,.22,0],[.045,.27,.0],[.098,.34,.006]],[.042,.028,.013],'oak',6))
    add(sc,'ForkBinding',tube([(math.cos(t)*.04,.20+.012*t/(2*math.pi),math.sin(t)*.04) for t in np.linspace(0,4*math.pi,29)],.012,'silk_white',6))
    add(sc,'OpenSilkLoop',tube([[-.087,.32,.017],[-.02,.376,.04],[.06,.367,.038],[.1,.32,.015]],.010,'silk_mid',5))
    return sc


def make_support():
    sc=trimesh.Scene()
    pts=[(0,0,0),(.012,.18,.007),(.005,.37,.018),(-.018,.54,.005),(-.045,.67,-.01)]
    add(sc,'FacetedTwigSupport',tapered(pts,[.065,.061,.054,.046,.024],'oak_light',8))
    add(sc,'BarkStripe',tube([(0,.05,.063),(.015,.21,.06),(.015,.45,.049),(-.01,.58,.035)],.007,'bark',4))
    # Twined rope near top and at foot, decorative, loops around wooden stub.
    for index,start in enumerate((.08,.48)):
        coords=[(np.cos(t)*(.07 if index==0 else .052),start+.03*t/(math.pi*4),np.sin(t)*(.07 if index==0 else .052)) for t in np.linspace(0,4*math.pi,37)]
        add(sc,('Foot' if index==0 else 'Top')+'SilkWrap',tube(coords,.012,'silk_white' if index==0 else 'silk_mid',6))
    add(sc,'SilkHook',tube([(-.04,.61,0),(-.012,.69,.02),(.07,.73,.025),(.105,.70,.005)],.014,'silk_shadow',7))
    return sc


def make_lashing():
    sc=trimesh.Scene()
    for n,sgn in enumerate((-1,1)):
        xs=np.linspace(-.13,.13,9)
        pts=[(float(x),.035+(.012 if n else 0),float(sgn*x*.8)) for x in xs]
        add(sc,'CrossLash'+str(n),tube(pts,.013,'silk_white' if n==0 else 'silk_shadow',6))
    for k in range(3):
        yy=.03+k*.018
        add(sc,'Twist'+str(k),tube([(-.032,yy,-.04),(-.02,yy+.018,.025),(.02,yy+.012,.045),(.055,yy,-.02)],.011,'silk_mid',6))
    add(sc,'Tail',tube([(.01,.043,0),(.085,-.01,.065),(.11,-.045,.08)],.008,'silk',5))
    return sc


def make_braid():
    sc=trimesh.Scene();tvals=np.linspace(-.5,.5,36)
    for n in range(3):
        phase=n*2*math.pi/3
        points=[(float(x),.014*math.sin(2*math.pi*4*(x+.5)+phase),.014*math.cos(2*math.pi*4*(x+.5)+phase)) for x in tvals]
        add(sc,'BraidedThread'+str(n+1),tube(points,.012,['silk_white','silk_mid','silk_shadow'][n],6))
    return sc


def make_woven_panel():
    sc=trimesh.Scene()
    # Sparser woven under-layer, not a solid ribbon.
    for k,z in enumerate(np.linspace(-.295,.295,9)):
        pts=[(x,.004*(1 if i%2 else -1),z) for i,x in enumerate(np.linspace(-.115,.115,9))]
        add(sc,f'Warp{k:02d}',tube(pts,.0053,'silk_white' if k%2==0 else 'silk_mid',5))
    for k,x in enumerate(np.linspace(-.10,.10,6)):
        pts=[(x,.009*(1 if i%2 else -1),z) for i,z in enumerate(np.linspace(-.295,.295,11))]
        add(sc,f'Weft{k:02d}',tube(pts,.0048,'silk_shadow' if k%2 else 'silk',5))
    return sc

ASSETS={
 'bark_plank_a':(make_plank(0),'transverse bark plank A; 0.22 m pitch, local Z spans bridge width'),
 'bark_plank_b':(make_plank(1),'transverse bark plank B with chipped end'),
 'bark_plank_c':(make_plank(2),'transverse bark plank C with subtle moss'),
 'silk_cinch_knot':(make_knot(0),'small coiled silk knot for ends and rail ties'),
 'silk_double_hitch':(make_knot(1),'decorative crossed silk hitch'),
 'wooden_eyelet':(make_eyelet(),'short wooden attachment peg with silk loop'),
 'forked_rail_fixture':(make_fork(),'forked twig fixture for rail lines'),
 'silk_wrapped_support':(make_support(),'side rail support, origin at base, +Y up'),
 'silk_cross_lashing':(make_lashing(),'woven crossing tie on plank or rope'),
 'braided_silk_segment':(make_braid(),'1 m reference silk braid; use procedural curves for arbitrary spans'),
 'woven_silk_panel':(make_woven_panel(),'airy 0.23 x 0.59 m woven silk segment to layer under planks'),
}

manifest=[]
for name,(scene,descr) in ASSETS.items():
    target=MODELS/(name+'.glb'); target.write_bytes(scene.export(file_type='glb'))
    loaded=trimesh.load(target,force='scene')
    assert len(loaded.geometry)>0
    assert all(len(m.vertices)>0 and len(m.faces)>0 and hasattr(m.visual,'vertex_colors') for m in loaded.geometry.values()), name
    b=loaded.bounds
    info={"name":name,"description":descr,"model":f"art/models/bridge/{name}.glb","scene":f"scenes/building/bridge/parts/{''.join(s.capitalize() for s in name.split('_'))}.tscn","bounds_m":np.round(b,4).tolist(),"mesh_parts":len(loaded.geometry),"triangles":sum(len(g.faces) for g in loaded.geometry.values()),"size_bytes":target.stat().st_size,"sha256":hashlib.sha256(target.read_bytes()).hexdigest()}
    manifest.append(info)
    out=ROOT/info['scene']
    text=['[gd_scene load_steps=3 format=3]','','[ext_resource type="PackedScene" path="res://'+info['model']+'" id="1_model"]',
          '[ext_resource type="Material" path="res://art/materials/vertex_color_material.tres" id="2_vertex"]','',
          f'[node name="{"".join(s.capitalize() for s in name.split("_"))}" type="Node3D"]','',
          '[node name="Visual" parent="." instance=ExtResource("1_model")]','','[node name="world" parent="Visual" index="0"]','']
    for i,geometry_name in enumerate(scene.geometry.keys()):
        text += [f'[node name="{geometry_name}" parent="Visual/world" index="{i}"]','material_override = ExtResource("2_vertex")','']
    out.write_text('\n'.join(text)+'\n')

# Preview-only bridge sample, assembled at a fixed 4 m span; runtime needs to place these dynamically.
def make_sample_bridge():
    demo=trimesh.Scene()
    # Extend from x=-2.0 to +2.0, with a pronounced but traversable curve.
    def p(t):
        x=-2+4*t
        y=.42-.25*math.sin(math.pi*t)
        return np.array([x,y,0.])
    for i in range(18):
        t=(i+.5)/18;w=(i%3)
        wood=ASSETS['bark_plank_'+('abc'[w])][0]
        mat=np.eye(4);mat[:3,3]=p(t)
        # imported model lives in XY/Z axes already. Tiny alternating offsets create handmade rhythm.
        mat[2,3]=[.006,-.005,.012][w]
        # Rotation around local Z to match sag slope, about world lateral z.
        slope=-.25*math.pi*math.cos(math.pi*t)/4
        theta=math.atan(slope)
        c,s=math.cos(theta),math.sin(theta)
        mat[:3,:3]=[[c,-s,0],[s,c,0],[0,0,1]]
        for gname,geom in wood.geometry.items():
            xform=mat@wood.graph.get(gname)[0] if False else mat
            add(demo,f'Plank{i:02d}_{gname}',geom.copy(),xform)
        # Woven panel per plank on the underside, slightly lower to avoid occlusion.
        if i%2==0:
            st=mat.copy();st[1,3]-=.038
            for gname,geom in ASSETS['woven_silk_panel'][0].geometry.items():
                add(demo,f'Woven{i:02d}_{gname}',geom.copy(),st)
    for side in [-1,1]:
        z=side*.385
        # a web-like railing: ivory rope, separate upper and lower loops
        for rail_index,h in enumerate((.12,.43)):
            pts=[]
            for t in np.linspace(0,1,40):
                cpt=p(t);pts.append(cpt+np.array([0,h-.045*math.sin(math.pi*t),z]))
            add(demo,f'Rail_{side}_{rail_index}',tube(pts,.011 if h<.4 else .018,'silk_white' if rail_index else 'silk_mid',6))
        for i in range(9):
            t=i/8
            base=p(t)+np.array([0,0,side*.31])
            top=base+np.array([0,.43-.045*math.sin(math.pi*t),side*.075])
            if i in (0,4,8):
                support=ASSETS['silk_wrapped_support'][0]
                # all support-post geometry anchored at base
                mat=np.eye(4);mat[:3,3]=base
                for gname,g in support.geometry.items():
                    add(demo,f'Support_{side}_{i}_{gname}',g.copy(),mat)
            else:
                add(demo,f'RailDrop_{side}_{i}',tube([base,top],.009,'silk_shadow',6))
    # endpoints attach to existing authored anchor posts in the actual game.
    for endpoint,t in [('Start',0),('End',1)]:
        for side in [-1,1]:
            fixture=ASSETS['forked_rail_fixture'][0]
            mat=np.eye(4);mat[:3,3]=p(t)+np.array([0,0,side*.38])
            for gname,g in fixture.geometry.items():add(demo,f'{endpoint}_{side}_{gname}',g.copy(),mat)
    return demo

sample=make_sample_bridge()
(EXAMPLES/'bridge_4m_reference_only.glb').write_bytes(sample.export(file_type='glb'))

# Render previews with simple painter's algorithm. The images are accurate GLB mesh projections.
fontbase='/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
fontbold='/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'
font=lambda n,bold=False:ImageFont.truetype(fontbold if bold else fontbase,n)
CAM=np.array([4.0,3.3,5.0]); CAM/=np.linalg.norm(CAM)
RIGHT=np.cross(np.array([0.,1.,0.]),CAM);RIGHT/=np.linalg.norm(RIGHT)
UP=np.cross(CAM,RIGHT); UP/=np.linalg.norm(UP)
LIGHT=np.array([-0.2,.8,.65]); LIGHT/=np.linalg.norm(LIGHT)

def scene_triangles(scene):
    result=[]
    for node_name in scene.graph.nodes_geometry:
        tf,gname=scene.graph[node_name]
        m=scene.geometry[gname];xyz=trimesh.transform_points(m.vertices,tf)
        verts=xyz[m.faces];vc=np.asarray(m.visual.vertex_colors)
        cols=vc[m.faces].mean(axis=1)
        ab=verts[:,1]-verts[:,0];ac=verts[:,2]-verts[:,0]
        normals=np.cross(ab,ac);nlen=np.linalg.norm(normals,axis=1)
        normals=normals/(nlen[:,None]+1e-12)
        for tri,col,n in zip(verts,cols,normals):
            result.append((tri,col,n))
    return result

def render_scene(scene, dims=(500,350), bg='#edf3e5', limits=None):
    w,h=dims; im=Image.new('RGB',(w,h),bg);d=ImageDraw.Draw(im)
    tris=scene_triangles(scene)
    if not tris:return im
    allv=np.concatenate([x[0] for x in tris])
    centre=(allv.min(axis=0)+allv.max(axis=0))/2
    pr=np.column_stack(((allv-centre)@RIGHT,(allv-centre)@UP))
    spans=pr.max(axis=0)-pr.min(axis=0)
    scale=min((w*.82)/(spans[0]+.001),(h*.72)/(spans[1]+.001))
    scale=min(scale,600) if max(spans)<1 else scale
    ordered=sorted(tris,key=lambda tr:float((tr[0].mean(axis=0)-centre)@CAM))
    ox=w*.5;oy=h*.51
    for v,col,n in ordered:
        p=np.column_stack(((v-centre)@RIGHT,(v-centre)@UP))
        polygon=[(int(ox+x*scale),int(oy-y*scale)) for x,y in p]
        shade=max(.58,min(1.11,.78+.25*np.dot(n,LIGHT)))
        fill=tuple(max(0,min(255,int(i*shade))) for i in col[:3])
        d.polygon(polygon,fill=fill)
    return im

cards=[]
for info in manifest:
    scene=trimesh.load(MODELS/(info['name']+'.glb'),force='scene')
    image=render_scene(scene,(580,360),bg='#eff4eb')
    im_path=PREVIEW/(info['name']+'.png');image.save(im_path)
    cards.append((info['name'],image))
ref_img=render_scene(sample,(1520,570),bg='#dcefe5')
ref_img.save(PREVIEW/'bridge_4m_reference_only.png')

cw,ch=520,330
ncols=3
rows=math.ceil(len(cards)/ncols)
W=1640;H=710+rows*ch+40
sheet=Image.new('RGB',(W,H),'#e6f0df');draw=ImageDraw.Draw(sheet)
draw.rounded_rectangle((20,15,W-20,690),radius=34,fill='#fbf9ed')
draw.text((44,32),'WEBWILD  /  REEDWATER BRIDGE KIT',font=font(29,True),fill='#25433e')
draw.text((46,79),'Planks + authored detail pieces  •  procedural silk + curve',font=font(21),fill='#50645b')
sheet.paste(ref_img,(60,125))
draw.text((950,635),'Reference assembly • not a fixed-size runtime bridge',font=font(15),fill='#6c8179')
for i,(name,img) in enumerate(cards):
    r,c=divmod(i,ncols);x=32+c*540;y=715+r*ch
    draw.rounded_rectangle((x,y,x+520,y+ch-12),radius=23,fill='#fffdf5',outline='#ccdcc8',width=2)
    small=img.resize((484,268));sheet.paste(small,(x+18,y+14))
    draw.text((x+18,y+286),name.replace('_',' ').title(),font=font(20,True),fill='#2f504b')
sheet.save(PREVIEW/'bridge_asset_contact_sheet.png')

# Godot editor gallery composed of real scene instances.
gallery='[gd_scene load_steps='+str(1+len(manifest))+' format=3]\n\n'
for i,info in enumerate(manifest,1):
    gallery+=f'[ext_resource type="PackedScene" path="res://{info["scene"]}" id="{i}_item"]\n'
gallery+='\n[node name="BridgeAssetGallery" type="Node3D"]\n'
for i,info in enumerate(manifest,1):
    x=((i-1)%4)*1.5; z=((i-1)//4)*1.5
    name=''.join(v.capitalize() for v in info['name'].split('_'))
    gallery+=f'\n[node name="{name}" parent="." instance=ExtResource("{i}_item")]\nposition = Vector3({x-2.25}, 0, {z-1.1})\n'
(ROOT/'scenes/building/bridge/BridgeAssetGallery.tscn').write_text(gallery)

(DOCS/'bridge_asset_manifest.json').write_text(json.dumps({"pack":"Webwild v0.2.0 P2 modular silk and wood bridge kit","model_count":len(manifest),"units":"metres","coordinates":"Godot Y-up; plank local +X along travel, +Z across span; Y up","material":"Existing res://art/materials/vertex_color_material.tres (vertex_color_use_as_albedo=true)","models":manifest},indent=2)+'\n')

print('Created',len(manifest),'GLBs; examples',len(sample.geometry),'demo meshes. Root',ROOT)
print('Manifest triangles',sum(m['triangles'] for m in manifest),'total bytes',sum(m['size_bytes'] for m in manifest))
