from __future__ import annotations
import argparse, struct
from pathlib import Path
import h5py, numpy as np
from field_export_common import FieldBlock, field_stats, radius_field, sanitize_field, write_string
MAGIC=b'SHMSH003'; VERSION=3; GEOMETRY_TYPE=0
VERTEX_CANDIDATES=("Nodes","Vertices","Coordinates","Positions")
TOPOLOGY_CANDIDATES=("Connectivity","Triangles","Elements","Faces")

def datasets(h5):
    out={}; h5.visititems(lambda n,o: out.__setitem__(n,o) if isinstance(o,h5py.Dataset) else None); return out

def find(ds,names):
    for candidate in names:
        for name,obj in ds.items():
            if name.split('/')[-1].lower()==candidate.lower(): return obj
    raise KeyError(f'Dataset not found: {names}')

def find_optional(ds,target):
    for name,obj in ds.items():
        if name.split('/')[-1].lower()==target.lower(): return obj
    return None

def vertices_shape(a):
    a=np.asarray(a)
    if a.ndim==2:
        if a.shape[-1]!=3 and a.shape[0]==3: a=a.T
        a=a[None]
    elif a.ndim==3 and a.shape[-1]!=3:
        if a.shape[1]==3: a=np.transpose(a,(2,0,1))
        elif a.shape[0]==3: a=np.transpose(a,(2,1,0))
    if a.ndim!=3 or a.shape[-1]!=3: raise ValueError(f'Unsupported vertices shape {a.shape}')
    return np.asarray(a,dtype=np.float32)

def triangles_shape(a):
    a=np.asarray(a)
    if a.ndim!=2: raise ValueError(f'Connectivity shape {a.shape}')
    if a.shape[1]<3: a=a.T
    a=np.asarray(a[:,:3],dtype=np.int64)
    if a.min()==1: a-=1
    return np.asarray(a,dtype=np.int32)

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--input',required=True); ap.add_argument('--output',required=True)
    ap.add_argument('--fields',nargs='*',default=[]); ap.add_argument('--fps',type=float,default=30.0); ap.add_argument('--frame-step',type=int,default=1)
    ap.add_argument('--scale',type=float,default=1.0); ap.add_argument('--no-swap-yz',action='store_true'); ap.add_argument('--add-radius',action='store_true')
    args=ap.parse_args(); input_path=Path(args.input); output=Path(args.output)
    with h5py.File(input_path,'r') as h5:
        ds=datasets(h5); original=vertices_shape(find(ds,VERTEX_CANDIDATES)[...]); triangles=triangles_shape(find(ds,TOPOLOGY_CANDIDATES)[...])
        requested=[]
        for name in args.fields:
            if name.lower()=='radius': continue
            obj=find_optional(ds,name)
            if obj is not None: requested.append(FieldBlock(name,'',sanitize_field(obj[...],original.shape[0],original.shape[1],name)))
            else: print(f'WARNING: field {name} not found',flush=True)
    step=max(1,args.frame_step); idx=np.arange(0,original.shape[0],step,dtype=np.int32)
    if len(idx)==0: raise RuntimeError('No frames selected')
    original=original[idx]; converted=original[..., [0,2,1]] if not args.no_swap_yz else original.copy(); 
    if not args.no_swap_yz:
        triangles = triangles[:, [0, 2, 1]]
    converted=np.ascontiguousarray(converted*np.float32(args.scale),dtype=np.float32)
    fields=[FieldBlock(f.name,f.units,f.values[idx]) for f in requested]
    if args.add_radius or any(n.lower()=='radius' for n in args.fields) or not fields:
        fields.append(FieldBlock('Radius','m',radius_field(original)*np.float32(args.scale)))
    unique={}; [unique.setdefault(f.name.lower(),f) for f in fields]; fields=list(unique.values())
    output.parent.mkdir(parents=True,exist_ok=True)
    with output.open('wb') as f:
        f.write(MAGIC); f.write(struct.pack('<iiiiifi',VERSION,GEOMETRY_TYPE,len(idx),converted.shape[1],triangles.shape[0],args.fps/step,len(fields)))
        stats=[]
        for field in fields:
            gmin,gmax,fmin,fmax=field_stats(field); stats.append((fmin,fmax)); write_string(f,field.name); write_string(f,field.units); f.write(struct.pack('<ff',gmin,gmax))
        np.asarray(triangles,dtype='<i4').tofile(f)
        for fmin,fmax in stats: np.asarray(fmin,dtype='<f4').tofile(f); np.asarray(fmax,dtype='<f4').tofile(f)
        for frame in range(len(idx)):
            np.asarray(converted[frame],dtype='<f4').tofile(f)
            for field in fields: np.asarray(field.values[frame],dtype='<f4').tofile(f)
    print(f'Created {output}',flush=True); print('Fields: '+', '.join(f.name for f in fields),flush=True)
if __name__=='__main__': main()
