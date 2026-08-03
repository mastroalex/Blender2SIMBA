from __future__ import annotations
import argparse, json, re
from pathlib import Path
import h5py
import numpy as np

VERTEX_NAMES=("Nodes","Vertices","Coordinates","Positions")
TOPOLOGY_NAMES=("Connectivity","Triangles","Elements","Faces","Edges","Lines")

def all_datasets(h5):
    out={}
    h5.visititems(lambda n,o: out.__setitem__(n,o) if isinstance(o,h5py.Dataset) else None)
    return out

def tail(name): return name.split('/')[-1]
def find_by_tail(ds,names):
    for wanted in names:
        for name,obj in ds.items():
            if tail(name).lower()==wanted.lower(): return name,obj
    return None,None

def time_number(key):
    m=re.findall(r"\d+",key)
    return int(m[0]) if m else 0

def scalar_candidate(shape, frames, values):
    s=tuple(int(x) for x in shape)
    while len(s)>0 and s[-1]==1: s=s[:-1]
    return s in ((values,), (frames,values), (values,frames))

def inspect(path):
    with h5py.File(path,'r') as h5:
        time_keys=sorted([k for k,v in h5.items() if isinstance(v,h5py.Group) and k.startswith('Time_')],key=time_number)
        if time_keys and 'Connectivity' in h5:
            first=h5[time_keys[0]]
            nodes=np.asarray(first['Nodes'])
            edges=np.asarray(h5['Connectivity'])
            value_count=int(nodes.shape[0]); frame_count=len(time_keys); element_count=int(edges.shape[0])
            common=None; paths={}
            for key in time_keys:
                names=set()
                for n,o in h5[key].items():
                    if isinstance(o,h5py.Dataset) and n.lower()!='nodes' and scalar_candidate(o.shape,1,value_count):
                        names.add(n); paths.setdefault(n,f"{key}/{n}")
                common=names if common is None else common & names
            fields=sorted(common or [])
            if 'Radius' not in fields: fields.append('Radius')
            return dict(suggestedGeometry='LineMesh',frameCount=frame_count,valueCount=value_count,elementCount=element_count,
                        verticesDataset=f"{time_keys[0]}/Nodes",connectivityDataset='Connectivity',fields=fields,
                        fieldPaths=[paths.get(f,'synthetic:radius') for f in fields])
        ds=all_datasets(h5)
        vname,vobj=find_by_tail(ds,VERTEX_NAMES); cname,cobj=find_by_tail(ds,TOPOLOGY_NAMES)
        if vobj is None or cobj is None: raise RuntimeError('Could not identify vertices and connectivity datasets.')
        shape=vobj.shape
        if len(shape)==2: frame_count=1; value_count=int(shape[0] if shape[-1]==3 else shape[1])
        elif len(shape)==3:
            if shape[-1]==3: frame_count,value_count=int(shape[0]),int(shape[1])
            elif shape[1]==3: frame_count,value_count=int(shape[2]),int(shape[0])
            elif shape[0]==3: frame_count,value_count=int(shape[2]),int(shape[1])
            else: raise RuntimeError(f'Unsupported vertex shape {shape}')
        else: raise RuntimeError(f'Unsupported vertex shape {shape}')
        fields=[]; paths=[]
        excluded={vname,cname}
        for name,obj in ds.items():
            if name in excluded: continue
            if scalar_candidate(obj.shape,frame_count,value_count): fields.append(tail(name)); paths.append(name)
        unique={}
        for n,p in zip(fields,paths): unique.setdefault(n,(n,p))
        fields=[x[0] for x in unique.values()]; paths=[x[1] for x in unique.values()]
        if 'Radius' not in fields: fields.append('Radius'); paths.append('synthetic:radius')
        return dict(suggestedGeometry='ShellMesh',frameCount=frame_count,valueCount=value_count,elementCount=int(cobj.shape[0]),
                    verticesDataset=vname,connectivityDataset=cname,fields=fields,fieldPaths=paths)

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--input',required=True); args=ap.parse_args()
    print(json.dumps(inspect(Path(args.input))))
if __name__=='__main__': main()
