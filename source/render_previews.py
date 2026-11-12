#!/usr/bin/env python3
"""Generate an offline orthographic mesh preview (not a Godot screenshot)."""
import io, math, pathlib, sys, json
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import trimesh

root=pathlib.Path(sys.argv[1]).resolve()
manifest=json.loads((root/'asset_manifest.json').read_text())['models']
out=root/'previews';out.mkdir(parents=True,exist_ok=True)
W,H=380,315

camera=np.array([3.0,2.3,3.7]);camera/=np.linalg.norm(camera)
right=np.cross(np.array([0,1.,0]),camera);right/=np.linalg.norm(right)
up=np.cross(camera,right);up/=np.linalg.norm(up)
light=np.array([1.2,2.0,1.4]);light/=np.linalg.norm(light)

try:
 fontpath='/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
 titlefont=ImageFont.truetype(fontpath,20)
 smaller=ImageFont.truetype(fontpath,14)
 heading=ImageFont.truetype(fontpath,38)
except:
 titlefont=smaller=heading=ImageFont.load_default()


def image_for(name):
    p=root/'art/models/reedwater'/(name+'.glb')
    scene=trimesh.load(p,force='scene',process=False)
    # Need vertices in world coordinates, geometry nodes have world transformations but are identity here.
    triangles=[];allv=[]
    for nodename in scene.graph.nodes_geometry:
        T,gname=scene.graph[nodename]
        g=scene.geometry[gname]
        verts=trimesh.transform_points(g.vertices,T)
        colors=np.asarray(g.visual.vertex_colors)
        if colors.shape[1]==3:
            colors=np.c_[colors,np.ones(len(colors))*255]
        for f in g.faces:
            tri=verts[f];col=np.mean(colors[f,:3],axis=0)
            normal=np.cross(tri[1]-tri[0],tri[2]-tri[0]);norm=np.linalg.norm(normal)
            if norm>0:normal=normal/norm
            lightness=.72+.26*abs(float(np.dot(normal,light)))
            col=np.clip(col*lightness,0,255).astype(int).tolist()
            triangles.append((tri,col,float(tri.mean(axis=0)@camera)))
            allv.extend(list(tri))
    mn,mx=np.asarray(allv).min(axis=0),np.asarray(allv).max(axis=0)
    center=(mn+mx)*.5;center[1]=(mn[1]+mx[1])*.5
    # Fixed 3/4 orthographic from above with fit margin.
    projected=np.asarray([[np.dot(v-center,right),np.dot(v-center,up)] for v in np.asarray(allv)])
    prmin,prmax=projected.min(axis=0),projected.max(axis=0)
    span=np.maximum(prmax-prmin,.01)
    scale=min(305/span[0],215/span[1])*.92
    baseX=W/2-(prmax[0]+prmin[0])*scale/2
    baseY=159+(prmax[1]+prmin[1])*scale/2
    im=Image.new('RGB',(W,H),(234,241,228))
    d=ImageDraw.Draw(im)
    d.rounded_rectangle((6,5,W-6,H-6),radius=22,fill=(245,248,239),outline=(213,224,207),width=2)
    # ground shadow
    r=120 if name.startswith('moss_island') else 68
    sh=Image.new('RGBA',im.size,(0,0,0,0));ds=ImageDraw.Draw(sh)
    ds.ellipse((W/2-r,238-r*.12,W/2+r,238+r*.23),fill=(62,87,73,60))
    sh=sh.filter(ImageFilter.GaussianBlur(11))
    im=Image.alpha_composite(im.convert('RGBA'),sh)
    d=ImageDraw.Draw(im)
    for tri,col,depth in sorted(triangles,key=lambda x:x[2]):
        xy=[(baseX+np.dot(v-center,right)*scale,baseY-np.dot(v-center,up)*scale) for v in tri]
        d.polygon(xy,fill=tuple(col))
    # bottom card inset
    d.rounded_rectangle((15,271,W-15,302),radius=11,fill=(224,235,220))
    d.text((25,276),name.replace('_',' ').title(),font=titlefont,fill=(34,74,61))
    im=im.convert('RGB')
    im.save(out/(name+'.png'))
    return im

thumbs=[]
for e in manifest:
    name=e['name'];thumbs.append(image_for(name));print(name)
cols,rows=5,3; margin=30;gutter=18
sheet=Image.new('RGB',(cols*W+(cols-1)*gutter+2*margin,rows*H+(rows-1)*gutter+180),(236,243,233));draw=ImageDraw.Draw(sheet)
draw.text((margin,27),'REEDWATER BANK',font=heading,fill=(35,73,60))
draw.text((margin,82),'Webwild • Original low-poly 3D asset collection • 15 baked GLB models',font=titlefont,fill=(72,105,88))
draw.text((margin,117),'Orthographic mesh previews • Y-up • vertex-colour palette • no textures',font=smaller,fill=(90,117,97))
for i,img in enumerate(thumbs):
 x=margin+(i%cols)*(W+gutter);y=164+(i//cols)*(H+gutter)
 sheet.paste(img,(x,y))
sheet.save(out/'reedwater_contact_sheet.png',optimize=True)
print('Contact sheet',sheet.size)
