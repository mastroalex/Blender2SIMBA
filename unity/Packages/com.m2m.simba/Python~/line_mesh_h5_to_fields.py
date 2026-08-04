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
    sanitize_scalar_frame,
    write_field_headers,
    write_field_ranges,
)

MAGIC = b'LNMSH004'
VERSION = 4
GEOMETRY_TYPE = 1


def time_number(key):
    match = re.findall(r'\d+', key)
    return int(match[0]) if match else 0


def normalize_nodes(nodes):
    a = np.asarray(nodes, dtype=np.float32)
    if a.ndim != 2:
        raise ValueError(f'Nodes shape {a.shape}, expected (n,3)')
    if a.shape[1] != 3 and a.shape[0] == 3:
        a = a.T
    if a.shape[1] != 3:
        raise ValueError(f'Nodes shape {a.shape}, expected (n,3)')
    return np.ascontiguousarray(a, dtype=np.float32)


def normalize_edges(edges, node_count):
    e = np.asarray(edges, dtype=np.int64).copy()
    if e.ndim != 2:
        raise ValueError(f'Connectivity shape {e.shape}, expected (n,2)')
    if e.shape[1] != 2 and e.shape[0] == 2:
        e = e.T
    if e.shape[1] != 2:
        raise ValueError(f'Connectivity shape {e.shape}, expected (n,2)')
    if e.size and e.min() == 1:
        e -= 1
    e = e[e[:, 0] != e[:, 1]]
    if e.size == 0 or e.min() < 0 or e.max() >= node_count:
        raise ValueError('Connectivity out of range')
    return np.ascontiguousarray(e, dtype=np.int32)


def find_dataset(group, name):
    target = name.lower()
    for key, obj in group.items():
        if isinstance(obj, h5py.Dataset) and key.lower() == target:
            return obj
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--input', required=True)
    ap.add_argument('--output', required=True)
    ap.add_argument('--fields', nargs='*', default=[])
    ap.add_argument('--fps', type=float, default=30.0)
    ap.add_argument('--frame-step', type=int, default=1)
    ap.add_argument('--scale', type=float, default=1.0)
    ap.add_argument('--no-swap-yz', action='store_true')
    ap.add_argument('--negate-z', action='store_true')
    ap.add_argument('--exclude-last-frame', action='store_true')
    ap.add_argument('--add-radius', action='store_true')
    ap.add_argument('--topology-mode', choices=('auto', 'static', 'dynamic'), default='auto')
    args = ap.parse_args()

    inp = Path(args.input)
    out = Path(args.output)
    requested = [name for name in args.fields if name.lower() != 'radius']

    with h5py.File(inp, 'r') as h5:
        keys = sorted(
            [key for key, obj in h5.items() if isinstance(obj, h5py.Group) and key.startswith('Time_')],
            key=time_number,
        )
        if not keys:
            raise RuntimeError('No Time_* groups found')

        nodes_all = []
        edges_all = []
        field_values = {name: [] for name in requested}

        global_connectivity = h5.get('Connectivity')
        for key in keys:
            group = h5[key]
            node_ds = find_dataset(group, 'Nodes')
            if node_ds is None:
                raise KeyError(f'{key}/Nodes missing')
            nodes = normalize_nodes(node_ds[...])

            edge_ds = find_dataset(group, 'Connectivity') or find_dataset(group, 'Edges')
            if edge_ds is None:
                edge_ds = global_connectivity
            if edge_ds is None:
                raise KeyError(f'{key}: Connectivity missing')

            nodes_all.append(nodes)
            edges_all.append(normalize_edges(edge_ds[...], len(nodes)))

            for name in list(field_values):
                ds = find_dataset(group, name)
                if ds is None:
                    field_values.pop(name, None)
                    print(f'WARNING: field {name} not found in every frame', flush=True)
                else:
                    field_values[name].append(sanitize_scalar_frame(ds[...], len(nodes), name))

    step = max(1, args.frame_step)
    selected = list(range(0, len(keys), step))
    if not args.exclude_last_frame and selected[-1] != len(keys) - 1:
        selected.append(len(keys) - 1)

    original = [nodes_all[i] for i in selected]
    edges = [edges_all[i] for i in selected]
    source_indices = np.asarray(selected, dtype=np.int32)

    converted = []
    for points in original:
        p = points[:, [0, 2, 1]] if not args.no_swap_yz else points.copy()
        if args.negate_z:
            p[:, 2] *= -1
        converted.append(np.ascontiguousarray(p * np.float32(args.scale), dtype=np.float32))

    fields = [
        FieldBlock(name, '', [values[i] for i in selected])
        for name, values in field_values.items()
    ]
    if args.add_radius or any(name.lower() == 'radius' for name in args.fields) or not fields:
        radius = radius_field(original)
        radius = [np.ascontiguousarray(v * np.float32(args.scale), dtype=np.float32) for v in radius]
        fields.append(FieldBlock('Radius', 'm', radius))

    unique = {}
    for field in fields:
        unique.setdefault(field.name.lower(), field)
    fields = list(unique.values())

    mode = choose_topology_mode(args.topology_mode, edges)
    max_nodes = max(len(frame) for frame in converted)
    max_edges = max(len(frame) for frame in edges)

    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open('wb') as f:
        f.write(MAGIC)
        f.write(struct.pack(
            '<iiiiiifii',
            VERSION,
            GEOMETRY_TYPE,
            int(mode),
            len(converted),
            max_nodes,
            max_edges,
            args.fps,
            step,
            len(fields),
        ))
        stats = write_field_headers(f, fields)
        np.asarray(source_indices, dtype='<i4').tofile(f)
        write_field_ranges(f, stats)

        field_frames = [field.frames() for field in fields]
        if mode == TopologyMode.STATIC:
            np.asarray(edges[0], dtype='<i4').tofile(f)
            for frame in range(len(converted)):
                np.asarray(converted[frame], dtype='<f4').tofile(f)
                for values in field_frames:
                    np.asarray(values[frame], dtype='<f4').tofile(f)
        else:
            for frame in range(len(converted)):
                f.write(struct.pack('<ii', len(converted[frame]), len(edges[frame])))
                np.asarray(converted[frame], dtype='<f4').tofile(f)
                np.asarray(edges[frame], dtype='<i4').tofile(f)
                for values in field_frames:
                    np.asarray(values[frame], dtype='<f4').tofile(f)

    print(f'Created {out}', flush=True)
    print(f'Topology: {mode.name.lower()}', flush=True)
    print('Fields: ' + ', '.join(field.name for field in fields), flush=True)


if __name__ == '__main__':
    main()
