# Getting started

## 1. Install SIMBA

In Unity Package Manager choose **Add package from disk** and select `com.m2m.simba/package.json`. A Git repository can later be installed using **Add package from Git URL**.

## 2. Configure Python

Open **Tools → SIMBA → Settings…**.

- Use **Browse** to select the exact interpreter, including a Conda environment interpreter.
- On macOS/Linux this is commonly `.../envs/simba/bin/python`.
- On Windows it is commonly `...\\envs\\simba\\python.exe`.
- Press **Validate** to check Python, NumPy and h5py.

The path is stored in Unity `EditorPrefs`; it remains the default on that workstation without committing a machine-specific absolute path to Git.

## 3. Import a simulation

Open **Tools → SIMBA → Import Simulation…**.

For HDF5 input, the wizard:

1. inspects the datasets;
2. proposes a geometry type;
3. lists compatible scalar fields;
4. lets you select frame step, FPS, scale and coordinate conversion;
5. runs the converter;
6. saves the binary in `Assets/StreamingAssets`;
7. creates the configured player.

A pre-converted `.bin`/`.bytes` file can also be selected directly.

## 4. Configure the player

The generated GameObject includes:

- `ShellMeshLoader` + `ShellMeshAnimator`, or `LineMeshPlayer`;
- `MeshFilter`;
- `MeshRenderer`;
- `FieldColorController`;
- a runtime material using `SIMBA/FieldGradientURP`.

Select `FieldColorController` to choose a field and colormap from dropdowns. Field names are read from the configured binary even before entering Play Mode.

## 5. Control from scripts

```csharp
controller.SetField("Pressure");
controller.SetField(0);
controller.SetColorMap(SIMBAColorMap.Plasma);
```

Use the `FieldChanged` event to synchronize labels, legends or other UI.
