# SIMBA binary format

SIMBA uses little-endian binary files. Version 3 remains supported for
legacy static-topology files. Version 4 adds an explicit topology mode and
uses the same fixed header for ShellMesh and LineMesh.

## Magic values

- `SHMSH003`: ShellMesh v3, static topology.
- `SHMSH004`: ShellMesh v4.
- `LNMSH003`: LineMesh v3, static topology.
- `LNMSH004`: LineMesh v4.

The readers also accept the historical aliases `LINEM003` and `LINEM004`.
New files are written with `LNMSH...`.

## Version 4 header

Immediately after the eight-byte magic:

1. `int32 version`
2. `int32 geometryType` (`0` shell, `1` line)
3. `int32 topologyMode` (`0` static, `1` dynamic)
4. `int32 frameCount`
5. `int32 maximumValueCount`
6. `int32 maximumElementCount`
7. `float32 sourceFramesPerSecond`
8. `int32 frameStep`
9. `int32 fieldCount`

Each field header stores:

1. UTF-8 name (`int32 byteLength`, then bytes)
2. UTF-8 units (`int32 byteLength`, then bytes)
3. `float32 globalMinimum`
4. `float32 globalMaximum`

Field ranges contain, for each field, all frame minima followed by all frame
maxima.

## ShellMesh v4 payload

After field headers and field ranges:

### Static topology

1. one triangle connectivity array (`maximumElementCount * 3` int32 values)
2. for every frame: vertices, followed by every scalar field

### Dynamic topology

For every frame:

1. `int32 vertexCount`
2. `int32 triangleCount`
3. vertices
4. triangle connectivity
5. every scalar field

## LineMesh v4 payload

After field headers:

1. source frame indices (`frameCount` int32 values)
2. field ranges

### Static topology

1. one edge array (`maximumElementCount * 2` int32 values)
2. for every frame: nodes, followed by every scalar field

### Dynamic topology

For every frame:

1. `int32 nodeCount`
2. `int32 edgeCount`
3. nodes
4. edge connectivity
5. every scalar field

Interpolation is disabled when topology is dynamic.
