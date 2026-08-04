# HDF5 input schema

SIMBA uses a deliberately small and solver-independent HDF5 schema. Dataset matching is case-insensitive and the converter recognizes several common names, but the canonical names below are recommended.

## ShellMesh

A `ShellMesh` is a triangulated surface with static connectivity and animated vertex coordinates.

| Dataset | Shape | Type | Meaning |
|---|---:|---|---|
| `Nodes` | `(F, N, 3)` | float | Vertex coordinates for `F` frames and `N` vertices. |
| `Connectivity` | `(T, 3)` | integer | Triangle vertex indices. Zero- or one-based indexing is accepted. |
| scalar field | `(F, N)` | float | One scalar value per vertex and frame. |

Static coordinates `(N, 3)` and static fields `(N,)` may be accepted and repeated across frames by the converter.

## LineMesh

A `LineMesh` is an animated node network with static edge connectivity. Unity expands each edge into a renderable tube.

| Dataset | Shape | Type | Meaning |
|---|---:|---|---|
| `Nodes` | `(F, N, 3)` | float | Node coordinates. |
| `Connectivity` or `Edges` | `(E, 2)` | integer | Node pairs defining edges. |
| scalar field | `(F, N)` | float | One scalar value per node and frame. |

## Scalar fields

Each selected field must be nodal and scalar. Examples:

- `Stress`
- `Strain`
- `Pressure`
- `Temperature`
- `DisplacementMagnitude`
- `VelocityMagnitude`

SIMBA stores field names, units and global/per-frame ranges in the generated binary. The current converters infer units only when configured; unknown units can be left as an empty string.

## Coordinate conventions

The import wizard can:

- swap Y and Z for Z-up scientific data;
- apply a global scale;
- subsample frames with `Frame Step`.

Connectivity must remain static over time. Vertex/node count must also remain constant.

## Python example

```python
from pathlib import Path
import h5py
import numpy as np

frames = 120
vertex_count = 1000
triangle_count = 1900

nodes = np.empty((frames, vertex_count, 3), dtype=np.float32)
triangles = np.empty((triangle_count, 3), dtype=np.int32)
stress = np.empty((frames, vertex_count), dtype=np.float32)
pressure = np.empty((frames, vertex_count), dtype=np.float32)

# Fill arrays from a solver, PyVista, NumPy, etc.

output = Path("simulation.h5")
with h5py.File(output, "w") as h5:
    h5.create_dataset("Nodes", data=nodes, compression="gzip")
    h5.create_dataset("Connectivity", data=triangles)
    h5.create_dataset("Stress", data=stress, compression="gzip")
    h5.create_dataset("Pressure", data=pressure, compression="gzip")
    h5["Stress"].attrs["units"] = "Pa"
    h5["Pressure"].attrs["units"] = "Pa"
```

### PyVista extraction

For a triangular `pyvista.PolyData`:

```python
import pyvista as pv
import numpy as np

mesh = pv.read("frame_000.vtp").triangulate()
points = np.asarray(mesh.points, dtype=np.float32)
triangles = np.asarray(mesh.faces).reshape(-1, 4)[:, 1:4].astype(np.int32)

stress = np.asarray(mesh.point_data["Stress"], dtype=np.float32)
```

For an animation, read every frame and stack coordinates/fields:

```python
nodes = np.stack([frame.points for frame in frames], axis=0)
stress = np.stack([frame.point_data["Stress"] for frame in frames], axis=0)
```

All frames must use identical topology and point ordering.

## Mathematica / Wolfram Language

The same numeric arrays can be prepared in Wolfram Language. The exact HDF5 export syntax can vary slightly between Mathematica versions, but the datasets and dimensions must match the schema above.

Conceptually:

```wolfram
nodes = N@animatedCoordinates;       (* {frames, vertices, 3} *)
triangles = elementConnectivity;     (* {triangles, 3} *)
stress = N@stressValues;             (* {frames, vertices} *)

Export[
  "simulation.h5",
  {
    "/Nodes" -> nodes,
    "/Connectivity" -> triangles,
    "/Stress" -> stress
  },
  "HDF5"
]
```

After export, verify the result with:

```wolfram
Import["simulation.h5", "Datasets"]
Import["simulation.h5", {"Datasets", "/Nodes"}]
```

If a Mathematica version expects a different HDF5 dataset rule syntax, preserve the same dataset paths and array dimensions. The SIMBA schema is independent of the writing API.

## Validation checklist

Before importing:

- `Nodes` contains finite numeric values;
- every connectivity index refers to an existing node;
- all fields have the same frame and node counts as `Nodes`;
- topology and node ordering are constant;
- arrays are not ragged;
- scalar fields do not contain unsupported vector/tensor dimensions.
