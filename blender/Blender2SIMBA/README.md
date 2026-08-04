# Blender2SIMBA 0.3.3 test build

This build intentionally uses one Python module to avoid cross-module
registration and import errors while the addon is being tested.

## Supported evaluation

The exporter reads Blender's final evaluated mesh from the dependency
graph. This includes modifier results such as:

- Geometry Nodes producing a mesh;
- Array;
- Armature;
- Boolean;
- Build;
- Cloth and cached simulations;
- Decimate;
- Displace;
- Lattice;
- Mirror;
- Remesh;
- Screw;
- Skin;
- Solidify;
- Subdivision Surface;
- Triangulate;
- Weld;
- shape keys;
- other modifiers that Blender can convert to an evaluated mesh.

Geometry Nodes instances must currently be converted with a **Realize
Instances** node before Group Output. This is explicit because exporting
dependency-graph instances independently can duplicate geometry in some
node graphs.

## Install

1. Remove or disable older Blender2SIMBA versions.
2. Delete the old addon folder if Blender retained it.
3. Install this ZIP from **Edit → Preferences → Add-ons → Install from Disk**.
4. Enable Blender2SIMBA.
5. Restart Blender if dependencies were installed.
6. Press `N` in the 3D Viewport and open the **SIMBA** tab.

## Export

Select the source object in the addon panel, choose a frame range and an
HDF5 path, then press **Export SIMBA HDF5**.

Every frame writes:

```text
Time_XXXXXX/
├── Nodes
└── Connectivity
```

Vertex and triangle counts may change at every frame.


## 0.3.3 changes

- Requests a fresh evaluated dependency graph for every exported frame.
- Removes the explicit `depsgraph.update()` call.
- Exports the active object and synchronizes the object picker.
- Logs evaluated object name, frame, vertex count and triangle count.
- Error messages now identify the exact empty frame.


- Empty frames are exported as empty datasets instead of raising an error.
