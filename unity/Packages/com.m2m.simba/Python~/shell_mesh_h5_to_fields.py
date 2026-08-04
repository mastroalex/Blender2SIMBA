from __future__ import annotations

import argparse
import re
import struct
from pathlib import Path

import h5py
import numpy as np

from field_export_common import (
    FieldBlock,
    TopologyMode,
    choose_topology_mode,
    radius_field,
    sanitize_field,
    sanitize_scalar_frame,
    write_field_headers,
    write_field_ranges,
)

MAGIC = b'SHMSH004'
VERSION = 4
GEOMETRY_TYPE = 0
VERTEX_CANDIDATES = ('Nodes', 'Vertices', 'Coordinates', 'Positions')
TOPOLOGY_CANDIDATES = ('Connectivity', 'Triangles', 'Elements', 'Faces')


def time_number(key: str) -> int:
    match = re.findall(r'\d+', key)
    return int(match[0]) if match else 0


def datasets(h5):
    out = {}
    h5.visititems(lambda name, obj: out.__setitem__(name, obj) if isinstance(obj, h5py.Dataset) else None)
    return out


def find(ds, names):
    for candidate in names:
        for name, obj in ds.items():
            if name.split('/')[-1].lower() == candidate.lower():
                return name, obj
    raise KeyError(f'Dataset not found: {names}')


def find_optional_in_group(group, target):
    for name, obj in group.items():
        if isinstance(obj, h5py.Dataset) and name.lower() == target.lower():
            return obj
    return None


def vertices_shape(a):
    a = np.asarray(a)
    if a.ndim == 2:
        if a.shape[-1] != 3 and a.shape[0] == 3:
            a = a.T
        a = a[None]
    elif a.ndim == 3 and a.shape[-1] != 3:
        if a.shape[1] == 3:
            a = np.transpose(a, (2, 0, 1))
        elif a.shape[0] == 3:
            a = np.transpose(a, (2, 1, 0))
    if a.ndim != 3 or a.shape[-1] != 3:
        raise ValueError(f'Unsupported vertices shape {a.shape}')
    return np.asarray(a, dtype=np.float32)


def triangles_shape(a, node_count):
    a = np.asarray(a)
    if a.ndim != 2:
        raise ValueError(f'Connectivity shape {a.shape}')
    if a.shape[1] < 3 and a.shape[0] >= 3:
        a = a.T
    if a.shape[1] < 3:
        raise ValueError(f'Connectivity shape {a.shape}, expected (n,3)')
    a = np.asarray(a[:, :3], dtype=np.int64)
    if a.size and a.min() == 1:
        a -= 1
    if a.size == 0 or a.min() < 0 or a.max() >= node_count:
        raise ValueError('Triangle connectivity out of range')
    return np.ascontiguousarray(a, dtype=np.int32)


def load_group_layout(h5, requested_fields):
    keys = sorted(
        [name for name, obj in h5.items() if isinstance(obj, h5py.Group) and name.startswith('Time_')],
        key=time_number,
    )
    if not keys:
        return None

    vertices = []
    triangles = []
    raw_fields = {name: [] for name in requested_fields if name.lower() != 'radius'}

    for key in keys:
        group = h5[key]
        node_ds = find_optional_in_group(group, 'Nodes') or find_optional_in_group(group, 'Vertices')
        if node_ds is None:
            raise KeyError(f'{key}: Nodes/Vertices missing')
        frame_vertices = vertices_shape(node_ds[...])[0]

        conn_ds = None
        for name in TOPOLOGY_CANDIDATES:
            conn_ds = find_optional_in_group(group, name)
            if conn_ds is not None:
                break
        if conn_ds is None:
            for name in TOPOLOGY_CANDIDATES:
                if name in h5 and isinstance(h5[name], h5py.Dataset):
                    conn_ds = h5[name]
                    break
        if conn_ds is None:
            raise KeyError(f'{key}: connectivity missing')

        vertices.append(frame_vertices)
        triangles.append(triangles_shape(conn_ds[...], len(frame_vertices)))
        for name in list(raw_fields):
            ds = find_optional_in_group(group, name)
            if ds is None:
                raw_fields.pop(name, None)
                print(f'WARNING: field {name} not found in every frame', flush=True)
            else:
                raw_fields[name].append(sanitize_scalar_frame(ds[...], len(frame_vertices), name))

    fields = [FieldBlock(name, '', values) for name, values in raw_fields.items()]
    return vertices, triangles, fields, keys


def load_array_layout(h5, requested_fields):
    ds = datasets(h5)
    _, vertex_ds = find(ds, VERTEX_CANDIDATES)
    _, topology_ds = find(ds, TOPOLOGY_CANDIDATES)
    vertices_array = vertices_shape(vertex_ds[...])
    vertices = [np.ascontiguousarray(frame, dtype=np.float32) for frame in vertices_array]

    raw_conn = np.asarray(topology_ds[...])
    if raw_conn.ndim == 2:
        one = triangles_shape(raw_conn, len(vertices[0]))
        triangles = [one.copy() for _ in vertices]
    elif raw_conn.ndim == 3:
        if raw_conn.shape[0] != len(vertices):
            raise ValueError(f'Connectivity has {raw_conn.shape[0]} frames, vertices have {len(vertices)}')
        triangles = [triangles_shape(raw_conn[i], len(vertices[i])) for i in range(len(vertices))]
    else:
        raise ValueError(f'Unsupported connectivity shape {raw_conn.shape}')

    fields = []
    for name in requested_fields:
        if name.lower() == 'radius':
            continue
        obj = next((obj for path, obj in ds.items() if path.split('/')[-1].lower() == name.lower()), None)
        if obj is None:
            print(f'WARNING: field {name} not found', flush=True)
            continue
        if len({len(v) for v in vertices}) != 1:
            raise ValueError(f'{name}: variable vertex counts require Time_* groups with one field dataset per frame')
        values = sanitize_field(obj[...], len(vertices), len(vertices[0]), name)
        fields.append(FieldBlock(name, '', values))
    return vertices, triangles, fields, list(range(len(vertices)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--input', required=True)
    ap.add_argument('--output', required=True)
    ap.add_argument('--fields', nargs='*', default=[])
    ap.add_argument('--fps', type=float, default=30.0)
    ap.add_argument('--frame-step', type=int, default=1)
    ap.add_argument('--scale', type=float, default=1.0)
    ap.add_argument('--no-swap-yz', action='store_true')
    ap.add_argument('--add-radius', action='store_true')
    ap.add_argument('--topology-mode', choices=('auto', 'static', 'dynamic'), default='auto')
    args = ap.parse_args()

    input_path = Path(args.input)
    output = Path(args.output)
    requested = [name for name in args.fields if name.lower() != 'radius']

    with h5py.File(input_path, 'r') as h5:
        loaded = load_group_layout(h5, requested)
        if loaded is None:
            loaded = load_array_layout(h5, requested)
        vertices, triangles, fields, source_keys = loaded

    step = max(1, args.frame_step)
    selected = list(range(0, len(vertices), step))
    if not selected:
        raise RuntimeError('No frames selected')
    vertices = [vertices[i] for i in selected]
    triangles = [triangles[i] for i in selected]
    fields = [FieldBlock(f.name, f.units, [f.frames()[i] for i in selected]) for f in fields]

    converted = []
    converted_triangles = []
    for points, conn in zip(vertices, triangles):
        p = points[:, [0, 2, 1]] if not args.no_swap_yz else points.copy()
        t = conn[:, [0, 2, 1]] if not args.no_swap_yz else conn.copy()
        converted.append(np.ascontiguousarray(p * np.float32(args.scale), dtype=np.float32))
        converted_triangles.append(np.ascontiguousarray(t, dtype=np.int32))

    if args.add_radius or any(name.lower() == 'radius' for name in args.fields) or not fields:
        radius = radius_field(vertices)
        radius = [np.ascontiguousarray(v * np.float32(args.scale), dtype=np.float32) for v in radius]
        fields.append(FieldBlock('Radius', 'm', radius))

    unique = {}
    for field in fields:
        unique.setdefault(field.name.lower(), field)
    fields = list(unique.values())

    for field in fields:
        frames = field.frames()
        if len(frames) != len(converted):
            raise ValueError(f'{field.name}: frame count mismatch')
        for i, values in enumerate(frames):
            if len(values) != len(converted[i]):
                raise ValueError(f'{field.name}, frame {i}: {len(values)} values for {len(converted[i])} vertices')

    mode = choose_topology_mode(args.topology_mode, converted_triangles)
    max_vertices = max(len(v) for v in converted)
    max_triangles = max(len(t) for t in converted_triangles)

    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open('wb') as f:
        f.write(MAGIC)
        f.write(struct.pack(
            '<iiiiiifii',
            VERSION,
            GEOMETRY_TYPE,
            int(mode),
            len(converted),
            max_vertices,
            max_triangles,
            args.fps,
            step,
            len(fields),
        ))
        stats = write_field_headers(f, fields)
        write_field_ranges(f, stats)

        field_frames = [field.frames() for field in fields]
        if mode == TopologyMode.STATIC:
            np.asarray(converted_triangles[0], dtype='<i4').tofile(f)
            for frame in range(len(converted)):
                np.asarray(converted[frame], dtype='<f4').tofile(f)
                for values in field_frames:
                    np.asarray(values[frame], dtype='<f4').tofile(f)
        else:
            for frame in range(len(converted)):
                f.write(struct.pack('<ii', len(converted[frame]), len(converted_triangles[frame])))
                np.asarray(converted[frame], dtype='<f4').tofile(f)
                np.asarray(converted_triangles[frame], dtype='<i4').tofile(f)
                for values in field_frames:
                    np.asarray(values[frame], dtype='<f4').tofile(f)

    print(f'Created {output}', flush=True)
    print(f'Topology: {mode.name.lower()}', flush=True)
    print('Fields: ' + ', '.join(field.name for field in fields), flush=True)


if __name__ == '__main__':
    main()
