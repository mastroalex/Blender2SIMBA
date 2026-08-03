# SIMBA

**SIMulation Buffered Animation** — a Unity framework for interactive visualization of animated scientific simulations.

SIMBA converts generic HDF5 datasets into compact buffered animation files and renders animated geometry with one or more scalar fields in Unity URP. It supports surface meshes (`ShellMesh`) and animated line networks rendered as tubes (`LineMesh`).

## Main features

- Direct HDF5 inspection and conversion from the Unity Editor.
- Persistent Python or Conda interpreter selection.
- Multiple scalar fields in one animation file.
- ShellMesh and LineMesh geometry.
- GPU colormap rendering with scientific Matplotlib-style maps.
- Automatic materials, shaders and player creation.
- Unified `SIMBAPlayer` runtime API.
- Dynamic field and colormap selection.
- Global, per-frame and manual value ranges.
- Unity 2022.3 LTS and Universal Render Pipeline.

## Installation during development

Extract or clone the package as `com.m2m.simba`, then use:

```text
Window → Package Manager → + → Add package from disk
```

Select `com.m2m.simba/package.json`.

## First use

1. Open `Tools → SIMBA → Settings...`.
2. Select the Python executable from the desired Conda or virtual environment.
3. Verify that NumPy and h5py are installed.
4. Open `Tools → SIMBA → Import Simulation...`.
5. Select an HDF5 file or an existing SIMBA binary.
6. Select fields, geometry type and conversion settings.
7. Choose the initial field and colormap.
8. Create the player and enter Play Mode.

The generated GameObject contains the geometry-specific components, `FieldColorController`, and the unified `SIMBAPlayer` API.

## Runtime API

```csharp
using M2M.SIMBA;
using UnityEngine;

public sealed class SimulationControls : MonoBehaviour
{
    [SerializeField] private SIMBAPlayer player;

    private void Start()
    {
        player.SetField("Stress");
        player.SetColorMap(SIMBAColorMap.Plasma);
        player.UseGlobalRange();
        player.SetLoop(true);
        player.SetSpeed(1.0f);
        player.Play();
    }

    public void ShowPressure() => player.SetField("Pressure");
    public void Pause() => player.Pause();
    public void Seek(float normalizedTime) => player.SetNormalizedTime(normalizedTime);
}
```

Useful methods include:

```csharp
player.Play();
player.Pause();
player.Stop();
player.Restart();
player.SetFrame(20);
player.SetNormalizedTime(0.5f);
player.SetSpeed(2f);
player.SetLoop(true);
player.SetField("Stress");
player.SetField(0);
player.SetColorMap(SIMBAColorMap.Viridis);
player.SetManualRange(0f, 10f);
player.UseGlobalRange();
player.UsePerFrameRange();
player.SetMetallic(0f);
player.SetSmoothness(0.35f);
```

The same API can be connected to Unity UI buttons, sliders, mouse interaction, XR grab interactions, or application-specific scripts.

## Included colormaps

Turbo, Viridis, Plasma, Inferno, Magma, Cividis, Jet, Coolwarm, Hot, Gray, Rainbow, Spring, Summer, Autumn and Winter.

## Python environment

Create the provided Conda environment:

```bash
conda env create -f Python~/environment.yml
conda activate simba
```

Or install with pip:

```bash
python -m pip install -r Python~/requirements.txt
```

## Documentation

- `Documentation~/GettingStarted.md`
- `Documentation~/RuntimeAPI.md`
- `Documentation~/HDF5Schema.md`
- `Documentation~/BinaryFormat.md`
- `Documentation~/PythonEnvironment.md`
- `Documentation~/HDF5Importer.md`

## Icon replacement

Placeholder icons are located in `Editor/Icons/` and at `icon.png`. Replace them while preserving the filenames. A transparent square source image of at least 512×512 is recommended.

## Samples

Sample scenes and binaries can be added under `Samples~`. Recommended first samples are:

- animated cube or plate;
- ShellMesh scientific surface;
- LineMesh network.

## License

MIT License. See `LICENSE.md`.
