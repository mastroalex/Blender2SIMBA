# Blender2SIMBA 1.0.2 test

This build uses a new module name and a single Python file to avoid
stale Blender addon-module caches. The UI is split into independent
child panels:

- Source
- Animation
- Geometry
- Output

It exports one synchronized HDF5 file per object with Geometry Nodes,
modifiers, dynamic topology, empty frames, Float16/Float32 metadata,
HDF5 compression, progress reporting, manifest and export log.

Install this ZIP without removing the old addon first if desired:
it appears as **Blender2SIMBA Test** and uses a separate module name.
