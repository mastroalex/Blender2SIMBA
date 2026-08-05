<div align="center">

# Blender2SIMBA

### Official Blender exporter for SIMBA

[![Blender](https://img.shields.io/badge/Blender-4.0+-orange.svg)]()
[![Version](https://img.shields.io/badge/version-v1.0.0-success.svg)]()

</div>

---

Blender2SIMBA exports animated Blender geometry into the intermediate HDF5 format used by SIMBA.

The addon evaluates the complete dependency graph, making it compatible with Geometry Nodes, modifiers, animated meshes and dynamic topology.


<p align="center">
  <a href="https://github.com/multi2mech/SIMBA">
    <img src="https://raw.githubusercontent.com/multi2mech/SIMBA/main/Logo/logo.png" width="300">
  </a>
</p>

**Blender2SIMBA** is the official Blender export pipeline for **SIMBA** (**SIM**ulation **B**uffered **A**nimation), an open-source Unity framework for interactive visualization of animated scientific simulations create by  the [multi2mech](https://github.com/multi2mech) research group.

➡️ **SIMBA GitHub Repository:**  https://github.com/multi2mech/SIMBA

---

## Features

- Geometry Nodes
- Modifier evaluation
- Dynamic topology
- Animated meshes
- Empty frame support
- Collection export
- Multiple object export
- Local or world coordinates
- Float16 / Float32 metadata
- HDF5 compression
- Export manifest
- Export logs



## Workflow

```text
Blender Scene
      │
      ▼
Evaluated Geometry
      │
      ▼
Blender2SIMBA
      │
      ▼
Intermediate HDF5
      │
      ▼
SIMBA Converter
      │
      ▼
SHMSH005
      │
      ▼
Unity
```

## How to install?

Edit

```
Preferences → Add-ons → Install...
```

Select

```
Blender2SIMBA.zip
```

Enable

```
Blender2SIMBA
```

The addon appears under

```
View3D → Sidebar → SIMBA
```

<p align="center">
  <img src="documentation/gif/install.gif" width="900">
</p>

## How to use?

Export from Blender:

<p align="center">
  <img src="documentation/gif/blender.gif" width="900">
</p>

Import in Unity:

<p align="center">
  <img src="documentation/gif/unity.gif" width="900">
</p>

## Supported Blender Features

- Geometry Nodes
- Modifiers
- Armatures
- Shape Keys
- Curve objects
- Surface objects
- Text objects
- Meta objects


## Output

The addon exports one synchronized HDF5 file for every object.

Each file stores

- vertices
- connectivity
- animated topology
- timing information
- metadata

Multiple objects remain perfectly synchronized through empty frames.


## Compatibility

Compatible with

- Blender 4.x
- SIMBA v1.1+



## Future Work

- Direct SHMSH005 export
- Volume meshes
- Scalar field export
- Native SIMBA package export

# Citation

If you use **Blender2SIMBA** to generate data for academic research or scientific publications, please cite this software.

```text
Mastrofini A.

Blender2SIMBA:
Official Blender export pipeline for the SIMBA framework.

Multiscale and Multiphysics Mechanics Group (M2M)
Department of Civil Engineering and Computer Science Engineering
University of Rome Tor Vergata
Rome, Italy

https://github.com/multi2mech/Blender2SIMBA
```

A `CITATION.cff` file is included in this repository for automatic citation support on GitHub.