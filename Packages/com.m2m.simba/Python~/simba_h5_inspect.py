from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

import h5py
import numpy as np

VERTEX_NAMES = ('Nodes', 'Vertices', 'Coordinates', 'Positions')
TOPOLOGY_NAMES = ('Connectivity', 'Triangles', 'Elements', 'Faces', 'Edges', 'Lines')


def all_datasets(h5):
    out = {}
    h5.visititems(lambda name, obj: out.__setitem__(name, obj) if isinstance(obj, h5py.Dataset) else None)
    return out


def tail(name):
    return name.split('/')[-1]


def find_by_tail(ds, names):
    for wanted in names:
        for name, obj in ds.items():
            if tail(name).lower() == wanted.lower():
                return name, obj
    return None, None


def time_number(key):
    match = re.findall(r'\d+', key)
    return int(match[0]) if match else 0


def scalar_candidate(shape, frames, values):
    s = tuple(int(x) for x in shape)
    while s and s[-1] == 1:
        s = s[:-1]
    return s in ((values,), (frames, values), (values, frames))


def group_dataset(group, names):
    for wanted in names:
        for name, obj in group.items():
            if isinstance(obj, h5py.Dataset) and name.lower() == wanted.lower():
                return name, obj
    return None, None


def inspect_time_groups(h5, time_keys):
    vertex_counts = []
    element_counts = []
    connectivity_arrays = []
    common_fields = None
    paths = {}
    geometry = None
    first_vertex_path = None
    first_connectivity_path = None

    global_conn_name, global_conn = group_dataset(h5, TOPOLOGY_NAMES)
    for key in time_keys:
        group = h5[key]
        vname, vobj = group_dataset(group, VERTEX_NAMES)
        if vobj is None:
            raise RuntimeError(f'{key}: no node/vertex dataset')
        nodes = np.asarray(vobj)
        value_count = int(nodes.shape[0] if nodes.shape[-1] == 3 else nodes.shape[1])
        vertex_counts.append(value_count)
        first_vertex_path = first_vertex_path or f'{key}/{vname}'

        cname, cobj = group_dataset(group, TOPOLOGY_NAMES)
        if cobj is None:
            cname, cobj = global_conn_name, global_conn
        if cobj is None:
            raise RuntimeError(f'{key}: no connectivity dataset')
        conn = np.asarray(cobj)
        if conn.ndim != 2:
            raise RuntimeError(f'{key}: unsupported connectivity shape {conn.shape}')
        width = int(conn.shape[1] if conn.shape[1] in (2, 3) else conn.shape[0])
        geometry = geometry or ('LineMesh' if width == 2 else 'ShellMesh')
        element_counts.append(int(conn.shape[0] if conn.shape[1] in (2, 3) else conn.shape[1]))
        connectivity_arrays.append(conn)
        first_connectivity_path = first_connectivity_path or (f'{key}/{cname}' if cobj is not global_conn else cname)

        names = set()
        for name, obj in group.items():
            if not isinstance(obj, h5py.Dataset) or name.lower() in {vname.lower(), (cname or '').lower()}:
                continue
            if scalar_candidate(obj.shape, 1, value_count):
                names.add(name)
                paths.setdefault(name, f'{key}/{name}')
        common_fields = names if common_fields is None else common_fields & names

    dynamic = len(set(vertex_counts)) > 1 or len(set(element_counts)) > 1
    if not dynamic:
        first = connectivity_arrays[0]
        dynamic = any(not np.array_equal(first, conn) for conn in connectivity_arrays[1:])

    fields = sorted(common_fields or [])
    if 'Radius' not in fields:
        fields.append('Radius')
    return dict(
        suggestedGeometry=geometry,
        topologyMode='Dynamic' if dynamic else 'Static',
        frameCount=len(time_keys),
        valueCount=max(vertex_counts),
        elementCount=max(element_counts),
        frameValueCounts=vertex_counts,
        frameElementCounts=element_counts,
        verticesDataset=first_vertex_path,
        connectivityDataset=first_connectivity_path,
        fields=fields,
        fieldPaths=[paths.get(field, 'synthetic:radius') for field in fields],
    )


def inspect(path):
    with h5py.File(path, 'r') as h5:
        time_keys = sorted(
            [name for name, obj in h5.items() if isinstance(obj, h5py.Group) and name.startswith('Time_')],
            key=time_number,
        )
        if time_keys:
            return inspect_time_groups(h5, time_keys)

        ds = all_datasets(h5)
        vname, vobj = find_by_tail(ds, VERTEX_NAMES)
        cname, cobj = find_by_tail(ds, TOPOLOGY_NAMES)
        if vobj is None or cobj is None:
            raise RuntimeError('Could not identify vertices and connectivity datasets.')

        shape = vobj.shape
        if len(shape) == 2:
            frame_count = 1
            value_count = int(shape[0] if shape[-1] == 3 else shape[1])
        elif len(shape) == 3:
            if shape[-1] == 3:
                frame_count, value_count = int(shape[0]), int(shape[1])
            elif shape[1] == 3:
                frame_count, value_count = int(shape[2]), int(shape[0])
            elif shape[0] == 3:
                frame_count, value_count = int(shape[2]), int(shape[1])
            else:
                raise RuntimeError(f'Unsupported vertex shape {shape}')
        else:
            raise RuntimeError(f'Unsupported vertex shape {shape}')

        conn = np.asarray(cobj)
        dynamic = conn.ndim == 3
        if conn.ndim == 2:
            element_count = int(conn.shape[0] if conn.shape[1] in (2, 3) else conn.shape[1])
            width = int(conn.shape[1] if conn.shape[1] in (2, 3) else conn.shape[0])
        elif conn.ndim == 3:
            element_count = int(conn.shape[1])
            width = int(conn.shape[2])
            dynamic = any(not np.array_equal(conn[0], conn[i]) for i in range(1, conn.shape[0]))
        else:
            raise RuntimeError(f'Unsupported connectivity shape {conn.shape}')

        fields = []
        paths = []
        for name, obj in ds.items():
            if name in {vname, cname}:
                continue
            if scalar_candidate(obj.shape, frame_count, value_count):
                fields.append(tail(name))
                paths.append(name)
        unique = {}
        for name, field_path in zip(fields, paths):
            unique.setdefault(name, (name, field_path))
        fields = [value[0] for value in unique.values()]
        paths = [value[1] for value in unique.values()]
        if 'Radius' not in fields:
            fields.append('Radius')
            paths.append('synthetic:radius')

        return dict(
            suggestedGeometry='LineMesh' if width == 2 else 'ShellMesh',
            topologyMode='Dynamic' if dynamic else 'Static',
            frameCount=frame_count,
            valueCount=value_count,
            elementCount=element_count,
            frameValueCounts=[value_count] * frame_count,
            frameElementCounts=[element_count] * frame_count,
            verticesDataset=vname,
            connectivityDataset=cname,
            fields=fields,
            fieldPaths=paths,
        )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', required=True)
    args = parser.parse_args()
    print(json.dumps(inspect(Path(args.input))))


if __name__ == '__main__':
    main()
