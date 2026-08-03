from __future__ import annotations
import argparse,re,struct
from pathlib import Path
import h5py,numpy as np
from field_export_common import FieldBlock, field_stats, radius_field, sanitize_field, write_string
MAGIC=b'LINEM003'; VERSION=3; GEOMETRY_TYPE=1

def time_number(key):
    m=re.findall(r'\d+',key); return int(m[0]) if m else 0

def normalize_edges(edges,node_count):
    e=np.asarray(edges,dtype=np.int64).copy()
    if e.ndim!=2 or e.shape[1]!=2: raise ValueError(f'Connectivity shape {e.shape}, expected (n,2)')
    if e.min()==1:e-=1
    e=e[e[:,0]!=e[:,1]]
    if e.min()<0 or e.max()>=node_count: raise ValueError('Connectivity out of range')
    return np.ascontiguousarray(e,dtype=np.int32)

def find_dataset(group,name):
    target=name.lower()
    for key,obj in group.items():
        if isinstance(obj,h5py.Dataset) and key.lower()==target:return obj
    return None

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--input',required=True); ap.add_argument('--output',required=True); ap.add_argument('--fields',nargs='*',default=[])
    ap.add_argument('--fps',type=float,default=30.0); ap.add_argument('--frame-step',type=int,default=1); ap.add_argument('--scale',type=float,default=1.0)
    ap.add_argument('--no-swap-yz',action='store_true'); ap.add_argument('--negate-z',action='store_true'); ap.add_argument('--exclude-last-frame',action='store_true'); ap.add_argument('--add-radius',action='store_true')
    args=ap.parse_args(); inp=Path(args.input); out=Path(args.output)
    with h5py.File(inp,'r') as h5:
        if 'Connectivity' not in h5: raise KeyError('Connectivity missing')
        keys=sorted([k for k in h5.keys() if k.startswith('Time_')],key=time_number)
        if not keys: raise RuntimeError('No Time_* groups found')
        nodes_all=np.stack([np.asarray(h5[f'{k}/Nodes'],dtype=np.float32) for k in keys]); edges=normalize_edges(np.asarray(h5['Connectivity']),nodes_all.shape[1])
        raw=[]
        for name in args.fields:
            if name.lower()=='radius':continue
            frames=[]
            for key in keys:
                ds=find_dataset(h5[key],name)
                if ds is None: frames=[];break
                frames.append(np.asarray(ds))
            if frames: raw.append(FieldBlock(name,'',sanitize_field(np.stack(frames),len(keys),nodes_all.shape[1],name)))
            else: print(f'WARNING: field {name} not found',flush=True)
    step=max(1,args.frame_step); indices=np.arange(0,len(keys),step,dtype=np.int32)
    if not args.exclude_last_frame and indices[-1]!=len(keys)-1:indices=np.append(indices,np.int32(len(keys)-1))
    original=nodes_all[indices]; converted=original[..., [0,2,1]] if not args.no_swap_yz else original.copy()
    if args.negate_z:converted[...,2]*=-1
    converted=np.ascontiguousarray(converted*np.float32(args.scale),dtype=np.float32)
    fields=[FieldBlock(f.name,f.units,f.values[indices]) for f in raw]
    if args.add_radius or any(n.lower()=='radius' for n in args.fields) or not fields: fields.append(FieldBlock('Radius','m',radius_field(original)*np.float32(args.scale)))
    unique={};[unique.setdefault(f.name.lower(),f) for f in fields];fields=list(unique.values())
    out.parent.mkdir(parents=True,exist_ok=True)
    with out.open('wb') as f:
        f.write(MAGIC);f.write(struct.pack('<iiiiifii',VERSION,GEOMETRY_TYPE,len(indices),converted.shape[1],edges.shape[0],args.fps,step,len(fields)))
        stats=[]
        for field in fields:
            gmin,gmax,fmin,fmax=field_stats(field);stats.append((fmin,fmax));write_string(f,field.name);write_string(f,field.units);f.write(struct.pack('<ff',gmin,gmax))
        np.asarray(indices,dtype='<i4').tofile(f);np.asarray(edges,dtype='<i4').tofile(f)
        for fmin,fmax in stats:np.asarray(fmin,dtype='<f4').tofile(f);np.asarray(fmax,dtype='<f4').tofile(f)
        for frame in range(len(indices)):
            np.asarray(converted[frame],dtype='<f4').tofile(f)
            for field in fields:np.asarray(field.values[frame],dtype='<f4').tofile(f)
    print(f'Created {out}',flush=True);print('Fields: '+', '.join(f.name for f in fields),flush=True)
if __name__=='__main__':main()
