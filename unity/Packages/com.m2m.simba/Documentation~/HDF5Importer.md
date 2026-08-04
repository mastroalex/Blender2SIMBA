# HDF5 importer

The v0.3 editor workflow invokes the Python tools as child processes. Unity itself does not parse HDF5.

## ShellMesh layout

The inspector searches recursively for common names:

- vertices: `Nodes`, `Vertices`, `Coordinates`, `Positions`
- topology: `Connectivity`, `Triangles`, `Elements`, `Faces`

Vertex arrays may be `(frames, vertices, 3)` or compatible transpositions. Scalar fields may be `(frames, vertices)`, `(vertices, frames)` or static `(vertices,)`.

## LineMesh layout

The current LineMesh convention is:

- root dataset `Connectivity` with shape `(edges, 2)`;
- groups named `Time_*`;
- a `Nodes` dataset inside each time group;
- scalar fields with the same dataset name inside every time group.

## Python settings

The interpreter path is stored per editor user. Select the executable inside the intended Conda or virtual environment, not the `conda` command itself.
