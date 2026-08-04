# Python and Conda environment

SIMBA invokes Python as an external process from the Unity Editor. This keeps HDF5 and scientific Python dependencies outside the Unity runtime and works with system Python, Conda and virtual environments.

## Conda

From the package directory:

```bash
conda env create -f Python~/environment.yml
conda activate simba
```

Locate the interpreter:

```bash
python -c "import sys; print(sys.executable)"
```

Select that exact path in **Tools → SIMBA → Settings…**.

## pip / venv

```bash
python -m venv .venv
source .venv/bin/activate        # macOS/Linux
.venv\\Scripts\\activate         # Windows
python -m pip install -r Python~/requirements.txt
```

## Validation

The Settings window checks:

- interpreter existence;
- Python version;
- NumPy import;
- h5py import.

The selected path is stored locally in `EditorPrefs`, not in the package or repository.

## Command-line conversion

The converter scripts can also be run independently from Unity. Use `--help` to list current arguments:

```bash
python Python~/shell_mesh_h5_to_fields.py --help
python Python~/line_mesh_h5_to_fields.py --help
python Python~/simba_h5_inspect.py --help
```

This makes the same conversion pipeline usable in CI, batch processing and reproducible research workflows.
