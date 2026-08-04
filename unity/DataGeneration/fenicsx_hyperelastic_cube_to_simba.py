#!/usr/bin/env python3
"""
FEniCSx hyperelastic cube -> SIMBA HDF5
=======================================

Simula un cubo 1 x 1 x 1 in materiale iperelastico Neo-Hookeano comprimibile.

Condizioni al contorno
----------------------
- Faccia x = 0: completamente bloccata.
- Faccia x = 1: spostamento imposto lungo x.
- Stretch finale: 2.5, quindi lunghezza finale lungo x = 2.5.

Output SIMBA
------------
Il file HDF5 contiene SOLTANTO la superficie esterna della mesh:

    Nodes                    (n_frames, n_surface_nodes, 3)
    Connectivity             (n_surface_triangles, 3)
    VonMises                 (n_frames, n_surface_nodes)
    PrincipalStrainMax       (n_frames, n_surface_nodes)
    PrincipalStressMax       (n_frames, n_surface_nodes)
    EquivalentStrain         (n_frames, n_surface_nodes)
    DisplacementNorm         (n_frames, n_surface_nodes)

Sono inoltre salvati:
    Stretch
    Time
    attributi con unità, FPS e descrizione.

Dipendenze consigliate
----------------------
Con Conda/Mamba:

    conda create -n simba-fenicsx -c conda-forge \
        fenics-dolfinx mpich pyvista h5py numpy
    conda activate simba-fenicsx

Esecuzione:
    python fenicsx_hyperelastic_cube_to_simba.py

Note
----
- Lo script supporta DOLFINx 0.9 e 0.10 con mesh tetraedrica lineare.
- L'esportazione HDF5 viene eseguita sul rank MPI 0.
- Per questa demo è consigliata l'esecuzione seriale.
"""

from __future__ import annotations

from pathlib import Path
import sys
import time
from typing import Callable

import h5py
import numpy as np
import pyvista as pv
import ufl
from basix.ufl import element
from mpi4py import MPI
from petsc4py import PETSc

from dolfinx import fem, mesh
from dolfinx import __version__ as DOLFINX_VERSION

# Compatibilità DOLFINx 0.9 e 0.10:
# - fino alla 0.9 la classe si chiamava NonlinearProblem;
# - dalla 0.10 la classe destinata al vecchio NewtonSolver si chiama
#   NewtonSolverNonlinearProblem.
try:
    from dolfinx.fem.petsc import (
        NewtonSolverNonlinearProblem as DOLFINXNewtonProblem,
    )
except ImportError:
    from dolfinx.fem.petsc import (
        NonlinearProblem as DOLFINXNewtonProblem,
    )

from dolfinx.nls.petsc import NewtonSolver


# =============================================================================
# CONFIGURAZIONE
# =============================================================================

OUTPUT_H5 = Path("cube_neo_hookean_simba.h5")

# Numero di suddivisioni per direzione. Ogni cubetto viene tetraedrizzato.
MESH_RESOLUTION = (10, 10, 10)

# Materiale Neo-Hookeano comprimibile.
YOUNG_MODULUS = 1.0e6
POISSON_RATIO = 0.30

# Animazione.
FINAL_STRETCH = 2.5
N_FRAMES = 31
FPS = 15.0

# Visualizzazione.
SHOW_FINAL_PLOT = True
SHOW_ANIMATION = False
ANIMATION_DELAY_SECONDS = 1.0 / FPS
DEFAULT_PLOT_FIELD = "VonMises"

# Salva anche una schermata del frame finale.
SAVE_FINAL_SCREENSHOT = True
FINAL_SCREENSHOT = Path("cube_neo_hookean_final.png")

# Parametri Newton.
NEWTON_ABSOLUTE_TOLERANCE = 1.0e-8
NEWTON_RELATIVE_TOLERANCE = 1.0e-8
NEWTON_MAX_ITERATIONS = 40

# Tolleranza geometrica per individuare le facce.
BOUNDARY_TOLERANCE = 1.0e-10


# =============================================================================
# UTILITY
# =============================================================================

def log(message: str, comm: MPI.Intracomm = MPI.COMM_WORLD) -> None:
    """Stampa soltanto sul rank 0."""
    if comm.rank == 0:
        print(message, flush=True)


def rounded_key(point: np.ndarray, decimals: int = 12) -> tuple[float, float, float]:
    """Chiave stabile per associare coordinate FEM e gradi di libertà P1."""
    p = np.round(np.asarray(point, dtype=np.float64), decimals=decimals)
    return float(p[0]), float(p[1]), float(p[2])


def create_scalar_function_space(domain: mesh.Mesh):
    return fem.functionspace(
        domain,
        element("Lagrange", domain.basix_cell(), 1),
    )


def create_vector_function_space(domain: mesh.Mesh):
    return fem.functionspace(
        domain,
        element("Lagrange", domain.basix_cell(), 1, shape=(3,)),
    )


def create_dg0_space(domain: mesh.Mesh):
    return fem.functionspace(
        domain,
        element("DG", domain.basix_cell(), 0),
    )


def interpolate_expression(expression, function_space) -> fem.Function:
    """
    Interpola una espressione UFL nello spazio indicato.

    Viene usato soprattutto per quantità DG0, valutate una volta per cella.
    """
    values = fem.Function(function_space)
    interpolation_points = function_space.element.interpolation_points
    if callable(interpolation_points):
        interpolation_points = interpolation_points()
    compiled_expression = fem.Expression(expression, interpolation_points)
    values.interpolate(compiled_expression)
    values.x.scatter_forward()
    return values


def dg0_values_by_cell(function: fem.Function, number_of_cells: int) -> np.ndarray:
    """Restituisce il valore DG0 associato a ciascuna cella locale."""
    output = np.empty(number_of_cells, dtype=np.float64)
    dofmap = function.function_space.dofmap

    for cell_index in range(number_of_cells):
        dofs = dofmap.cell_dofs(cell_index)
        if len(dofs) != 1:
            raise RuntimeError(
                "Lo spazio DG0 dovrebbe avere esattamente un grado di libertà per cella."
            )
        output[cell_index] = float(function.x.array[dofs[0]])

    return output


def average_cell_values_to_vertices(
    domain: mesh.Mesh,
    cell_values: np.ndarray,
) -> np.ndarray:
    """
    Media i valori cell-centered sui vertici incidenti.

    Questo evita l'ambiguità di valutare direttamente stress e strain
    discontinui esattamente sui bordi fra tetraedri.
    """
    topology = domain.topology
    topology.create_connectivity(topology.dim, 0)
    cell_to_vertex = topology.connectivity(topology.dim, 0)

    number_of_vertices = (
        topology.index_map(0).size_local
        + topology.index_map(0).num_ghosts
    )

    sums = np.zeros(number_of_vertices, dtype=np.float64)
    counts = np.zeros(number_of_vertices, dtype=np.int32)

    for cell_index, value in enumerate(cell_values):
        vertices = cell_to_vertex.links(cell_index)
        sums[vertices] += value
        counts[vertices] += 1

    if np.any(counts == 0):
        raise RuntimeError("Sono presenti vertici senza celle incidenti.")

    return sums / counts


def p1_scalar_values_in_geometry_order(
    domain: mesh.Mesh,
    function: fem.Function,
) -> np.ndarray:
    """
    Riordina una funzione scalare P1 secondo l'ordine dei nodi geometrici.

    Per la mesh tetraedrica lineare creata in questo script esiste una
    corrispondenza uno-a-uno fra nodi geometrici e gradi di libertà CG1.
    """
    geometry_points = np.asarray(domain.geometry.x, dtype=np.float64)
    dof_points = np.asarray(
        function.function_space.tabulate_dof_coordinates(),
        dtype=np.float64,
    )

    if len(function.x.array) != len(dof_points):
        raise RuntimeError(
            "La funzione P1 scalare non presenta un valore per coordinata DOF."
        )

    coordinate_to_dof: dict[tuple[float, float, float], int] = {}
    for dof_index, point in enumerate(dof_points):
        coordinate_to_dof[rounded_key(point)] = dof_index

    result = np.empty(len(geometry_points), dtype=np.float64)
    for geometry_index, point in enumerate(geometry_points):
        key = rounded_key(point)
        try:
            dof_index = coordinate_to_dof[key]
        except KeyError as exc:
            raise RuntimeError(
                f"Impossibile associare il nodo geometrico {geometry_index}: {point}"
            ) from exc
        result[geometry_index] = float(function.x.array[dof_index])

    return result


def displacement_in_geometry_order(
    domain: mesh.Mesh,
    displacement,
    scalar_space,
) -> np.ndarray:
    """Valuta le tre componenti dello spostamento sui nodi geometrici."""
    components: list[np.ndarray] = []

    for component_index in range(3):
        component_function = interpolate_expression(
            displacement[component_index],
            scalar_space,
        )
        components.append(
            p1_scalar_values_in_geometry_order(domain, component_function)
        )

    return np.column_stack(components)


def extract_surface_pyvista(domain):
    """
    Robust surface extraction using PyVista/VTK.
    Returns:
        surface_points, surface_triangles, original_point_ids
    """
    import pyvista as pv
    from vtk import VTK_TETRA

    points = np.asarray(domain.geometry.x, dtype=np.float64)

    topology = domain.topology
    topology.create_connectivity(topology.dim, 0)
    cell_to_vertex = topology.connectivity(topology.dim, 0)

    n_cells = topology.index_map(topology.dim).size_local + topology.index_map(topology.dim).num_ghosts

    cells = np.empty((n_cells, 5), dtype=np.int64)
    for c in range(n_cells):
        verts = np.asarray(cell_to_vertex.links(c), dtype=np.int64)
        cells[c,0] = 4
        cells[c,1:] = verts

    celltypes = np.full(n_cells, VTK_TETRA, dtype=np.uint8)

    grid = pv.UnstructuredGrid(cells.ravel(), celltypes, points)

    # Robust extraction with consistently outward-oriented normals.
    surface = (
        grid.extract_surface(pass_pointid=True)
            .triangulate()
            .compute_normals(
                cell_normals=False,
                point_normals=True,
                consistent_normals=True,
                auto_orient_normals=True,
                split_vertices=False,
                inplace=False,
            )
            .clean()
    )

    # Ensure every triangle normal points away from the mesh centroid.
    center = surface.points.mean(axis=0)
    faces = surface.faces.reshape((-1, 4)).copy()

    for i in range(faces.shape[0]):
        i0, i1, i2 = faces[i, 1:]
        p0 = surface.points[i0]
        p1 = surface.points[i1]
        p2 = surface.points[i2]

        n = __import__("numpy").cross(p1 - p0, p2 - p0)
        tri_center = (p0 + p1 + p2) / 3.0

        if __import__("numpy").dot(n, tri_center - center) < 0.0:
            faces[i, 2], faces[i, 3] = faces[i, 3], faces[i, 2]

    surface.faces = faces.ravel()

    triangles = surface.faces.reshape((-1,4))[:,1:].astype(np.int32)
    original_point_ids = np.asarray(surface["vtkOriginalPointIds"],dtype=np.int32)

    return np.asarray(surface.points), triangles, original_point_ids


def tensor_components_dg0(
    tensor_expression,
    dg0_space,
    number_of_cells: int,
) -> np.ndarray:
    """
    Valuta un tensore simmetrico 3x3 una volta per cella.

    Output shape: (n_cells, 3, 3)
    """
    tensor_values = np.zeros((number_of_cells, 3, 3), dtype=np.float64)

    for i in range(3):
        for j in range(i, 3):
            component = interpolate_expression(
                tensor_expression[i, j],
                dg0_space,
            )
            values = dg0_values_by_cell(component, number_of_cells)
            tensor_values[:, i, j] = values
            tensor_values[:, j, i] = values

    return tensor_values


def scalar_expression_to_surface(
    domain: mesh.Mesh,
    scalar_expression,
    dg0_space,
    number_of_cells: int,
    original_point_ids: np.ndarray,
) -> np.ndarray:
    """Valuta uno scalare per cella, lo media sui vertici e prende la superficie."""
    cell_function = interpolate_expression(scalar_expression, dg0_space)
    cell_values = dg0_values_by_cell(cell_function, number_of_cells)
    vertex_values = average_cell_values_to_vertices(domain, cell_values)
    return vertex_values[original_point_ids].astype(np.float32)


def tensor_max_principal_to_surface(
    domain: mesh.Mesh,
    tensor_expression,
    dg0_space,
    number_of_cells: int,
    original_point_ids: np.ndarray,
) -> np.ndarray:
    """Calcola il massimo autovalore del tensore e lo media sui vertici."""
    tensor_values = tensor_components_dg0(
        tensor_expression,
        dg0_space,
        number_of_cells,
    )
    principal_values = np.linalg.eigvalsh(tensor_values)[:, -1]
    vertex_values = average_cell_values_to_vertices(
        domain,
        principal_values,
    )
    return vertex_values[original_point_ids].astype(np.float32)


def make_pyvista_surface(
    points: np.ndarray,
    triangles: np.ndarray,
) -> pv.PolyData:
    """Costruisce una PolyData triangolare PyVista."""
    faces = np.column_stack(
        (
            np.full(len(triangles), 3, dtype=np.int64),
            triangles.astype(np.int64),
        )
    ).reshape(-1)

    return pv.PolyData(points, faces)


# =============================================================================
# MODELLO FEM
# =============================================================================

def build_problem():
    comm = MPI.COMM_WORLD

    domain = mesh.create_box(
        comm,
        [
            np.array([0.0, 0.0, 0.0], dtype=np.float64),
            np.array([1.0, 1.0, 1.0], dtype=np.float64),
        ],
        MESH_RESOLUTION,
        cell_type=mesh.CellType.tetrahedron,
    )

    if comm.size != 1:
        raise RuntimeError(
            "Questa demo esporta un HDF5 globale e deve essere eseguita in seriale. "
            "Usa: python script.py, non mpirun."
        )

    vector_space = create_vector_function_space(domain)
    scalar_space = create_scalar_function_space(domain)
    dg0_space = create_dg0_space(domain)

    displacement = fem.Function(vector_space, name="Displacement")
    test_function = ufl.TestFunction(vector_space)
    trial_increment = ufl.TrialFunction(vector_space)

    identity = ufl.Identity(3)
    deformation_gradient = identity + ufl.grad(displacement)
    right_cauchy_green = deformation_gradient.T * deformation_gradient
    left_cauchy_green = deformation_gradient * deformation_gradient.T
    jacobian = ufl.det(deformation_gradient)
    first_invariant = ufl.tr(right_cauchy_green)

    young = PETSc.ScalarType(YOUNG_MODULUS)
    poisson = PETSc.ScalarType(POISSON_RATIO)

    shear_modulus = young / (2.0 * (1.0 + poisson))
    lame_lambda = (
        young * poisson
        / ((1.0 + poisson) * (1.0 - 2.0 * poisson))
    )

    # Energia Neo-Hookeana comprimibile.
    strain_energy_density = (
        (shear_modulus / 2.0) * (first_invariant - 3.0)
        - shear_modulus * ufl.ln(jacobian)
        + (lame_lambda / 2.0) * ufl.ln(jacobian) ** 2
    )

    total_potential = strain_energy_density * ufl.dx
    residual = ufl.derivative(
        total_potential,
        displacement,
        test_function,
    )
    tangent = ufl.derivative(
        residual,
        displacement,
        trial_increment,
    )

    facet_dimension = domain.topology.dim - 1

    left_facets = mesh.locate_entities_boundary(
        domain,
        facet_dimension,
        lambda x: np.isclose(x[0], 0.0, atol=BOUNDARY_TOLERANCE),
    )
    right_facets = mesh.locate_entities_boundary(
        domain,
        facet_dimension,
        lambda x: np.isclose(x[0], 1.0, atol=BOUNDARY_TOLERANCE),
    )

    # Blocco completo sulla faccia sinistra.
    left_dofs = fem.locate_dofs_topological(
        vector_space,
        facet_dimension,
        left_facets,
    )
    left_bc = fem.dirichletbc(
        np.zeros(3, dtype=PETSc.ScalarType),
        left_dofs,
        vector_space,
    )

    # Spostamento solo della componente x sulla faccia destra.
    x_subspace, _ = vector_space.sub(0).collapse()
    right_x_value = fem.Function(x_subspace, name="RightPull")
    right_x_dofs = fem.locate_dofs_topological(
        (vector_space.sub(0), x_subspace),
        facet_dimension,
        right_facets,
    )
    right_bc = fem.dirichletbc(
        right_x_value,
        right_x_dofs,
        vector_space.sub(0),
    )

    boundary_conditions = [left_bc, right_bc]

    nonlinear_problem = DOLFINXNewtonProblem(
        residual,
        displacement,
        bcs=boundary_conditions,
        J=tangent,
    )

    newton_solver = NewtonSolver(comm, nonlinear_problem)
    newton_solver.atol = NEWTON_ABSOLUTE_TOLERANCE
    newton_solver.rtol = NEWTON_RELATIVE_TOLERANCE
    newton_solver.max_it = NEWTON_MAX_ITERATIONS
    newton_solver.convergence_criterion = "incremental"
    newton_solver.report = True

    # Solutore diretto robusto, compatibile anche con macOS/Conda.
    # Non imponiamo MUMPS: lo selezioniamo soltanto se PETSc dichiara
    # esplicitamente di averlo disponibile.
    krylov_solver = newton_solver.krylov_solver
    options_prefix = krylov_solver.getOptionsPrefix()
    petsc_options = PETSc.Options()
    petsc_options[f"{options_prefix}ksp_type"] = "preonly"
    petsc_options[f"{options_prefix}pc_type"] = "lu"

    petsc_system = PETSc.Sys()
    if petsc_system.hasExternalPackage("superlu_dist"):
        petsc_options[
            f"{options_prefix}pc_factor_mat_solver_type"
        ] = "superlu_dist"
        selected_factorization = "superlu_dist"
    elif petsc_system.hasExternalPackage("mumps"):
        petsc_options[
            f"{options_prefix}pc_factor_mat_solver_type"
        ] = "mumps"
        selected_factorization = "mumps"
    else:
        # PETSc userà il fattorizzatore LU disponibile nella build corrente.
        selected_factorization = "PETSc default LU"

    krylov_solver.setFromOptions()

    log(
        f"DOLFINx version          : {DOLFINX_VERSION}\n"
        f"PETSc factorization      : {selected_factorization}",
        comm,
    )

    # Misure di deformazione e tensione.
    green_lagrange_strain = 0.5 * (right_cauchy_green - identity)

    cauchy_stress = (
        (shear_modulus / jacobian) * (left_cauchy_green - identity)
        + (lame_lambda * ufl.ln(jacobian) / jacobian) * identity
    )

    deviatoric_stress = ufl.dev(cauchy_stress)
    von_mises = ufl.sqrt(
        1.5 * ufl.inner(deviatoric_stress, deviatoric_stress)
    )

    deviatoric_strain = ufl.dev(green_lagrange_strain)
    equivalent_strain = ufl.sqrt(
        (2.0 / 3.0)
        * ufl.inner(deviatoric_strain, deviatoric_strain)
    )

    return {
        "comm": comm,
        "domain": domain,
        "vector_space": vector_space,
        "scalar_space": scalar_space,
        "dg0_space": dg0_space,
        "displacement": displacement,
        "right_x_value": right_x_value,
        "solver": newton_solver,
        "green_lagrange_strain": green_lagrange_strain,
        "cauchy_stress": cauchy_stress,
        "von_mises": von_mises,
        "equivalent_strain": equivalent_strain,
    }


# =============================================================================
# SIMULAZIONE ED ESPORTAZIONE
# =============================================================================

def run_simulation() -> dict[str, np.ndarray]:
    problem = build_problem()

    comm = problem["comm"]
    domain = problem["domain"]
    scalar_space = problem["scalar_space"]
    dg0_space = problem["dg0_space"]
    displacement = problem["displacement"]
    right_x_value = problem["right_x_value"]
    solver = problem["solver"]

    topology = domain.topology
    number_of_cells = (
        topology.index_map(topology.dim).size_local
        + topology.index_map(topology.dim).num_ghosts
    )

    reference_surface_points, surface_triangles, original_point_ids = extract_surface_pyvista(domain)

    stretch_values = np.linspace(
        1.0,
        FINAL_STRETCH,
        N_FRAMES,
        dtype=np.float64,
    )
    time_values = np.arange(N_FRAMES, dtype=np.float64) / FPS

    number_of_surface_nodes = len(original_point_ids)

    output = {
        "Nodes": np.empty(
            (N_FRAMES, number_of_surface_nodes, 3),
            dtype=np.float32,
        ),
        "Connectivity": surface_triangles.astype(np.int32),
        "VonMises": np.empty(
            (N_FRAMES, number_of_surface_nodes),
            dtype=np.float32,
        ),
        "PrincipalStrainMax": np.empty(
            (N_FRAMES, number_of_surface_nodes),
            dtype=np.float32,
        ),
        "PrincipalStressMax": np.empty(
            (N_FRAMES, number_of_surface_nodes),
            dtype=np.float32,
        ),
        "EquivalentStrain": np.empty(
            (N_FRAMES, number_of_surface_nodes),
            dtype=np.float32,
        ),
        "DisplacementNorm": np.empty(
            (N_FRAMES, number_of_surface_nodes),
            dtype=np.float32,
        ),
        "Stretch": stretch_values.astype(np.float32),
        "Time": time_values.astype(np.float32),
    }

    log(
        "\n"
        f"Volume mesh: {len(domain.geometry.x)} nodi geometrici, "
        f"{number_of_cells} tetraedri\n"
        f"Surface mesh: {number_of_surface_nodes} nodi, "
        f"{len(surface_triangles)} triangoli\n"
    )

    start_time = time.perf_counter()

    for frame_index, stretch in enumerate(stretch_values):
        prescribed_displacement = float(stretch - 1.0)

        # La funzione sul sottospazio scalare ha valore uniforme.
        right_x_value.x.array[:] = PETSc.ScalarType(
            prescribed_displacement
        )
        right_x_value.x.scatter_forward()

        if frame_index == 0:
            # Configurazione iniziale esatta.
            displacement.x.array[:] = 0.0
            displacement.x.scatter_forward()
            iterations = 0
            converged = True
        else:
            iterations, converged = solver.solve(displacement)
            displacement.x.scatter_forward()

        if not converged:
            raise RuntimeError(
                f"Newton non converge al frame {frame_index}, "
                f"stretch={stretch:.6f}, iterazioni={iterations}."
            )

        nodal_displacement = displacement_in_geometry_order(
            domain,
            displacement,
            scalar_space,
        )
        surface_displacement = nodal_displacement[
            original_point_ids
        ]

        output["Nodes"][frame_index] = (
            reference_surface_points + surface_displacement
        ).astype(np.float32)

        output["DisplacementNorm"][frame_index] = np.linalg.norm(
            surface_displacement,
            axis=1,
        ).astype(np.float32)

        if frame_index == 0:
            # Evita rumore numerico nella configurazione non deformata.
            output["VonMises"][frame_index].fill(0.0)
            output["PrincipalStrainMax"][frame_index].fill(0.0)
            output["PrincipalStressMax"][frame_index].fill(0.0)
            output["EquivalentStrain"][frame_index].fill(0.0)
        else:
            output["VonMises"][frame_index] = (
                scalar_expression_to_surface(
                    domain,
                    problem["von_mises"],
                    dg0_space,
                    number_of_cells,
                    original_point_ids,
                )
            )

            output["EquivalentStrain"][frame_index] = (
                scalar_expression_to_surface(
                    domain,
                    problem["equivalent_strain"],
                    dg0_space,
                    number_of_cells,
                    original_point_ids,
                )
            )

            output["PrincipalStrainMax"][frame_index] = (
                tensor_max_principal_to_surface(
                    domain,
                    problem["green_lagrange_strain"],
                    dg0_space,
                    number_of_cells,
                    original_point_ids,
                )
            )

            output["PrincipalStressMax"][frame_index] = (
                tensor_max_principal_to_surface(
                    domain,
                    problem["cauchy_stress"],
                    dg0_space,
                    number_of_cells,
                    original_point_ids,
                )
            )

        elapsed = time.perf_counter() - start_time
        log(
            f"[{frame_index + 1:02d}/{N_FRAMES}] "
            f"stretch={stretch:.4f}, "
            f"ux_right={prescribed_displacement:.4f}, "
            f"Newton={iterations:02d}, elapsed={elapsed:.1f}s",
            comm,
        )

    return output


def write_simba_hdf5(data: dict[str, np.ndarray]) -> None:
    """Scrive il file HDF5 generico consumato dai converter SIMBA."""
    OUTPUT_H5.parent.mkdir(parents=True, exist_ok=True)

    with h5py.File(OUTPUT_H5, "w") as h5:
        h5.attrs["format"] = "SIMBA generic HDF5 input"
        h5.attrs["geometry_type"] = "ShellMesh"
        h5.attrs["description"] = (
            "Surface of a 3D compressible Neo-Hookean cube under "
            "displacement-controlled uniaxial extension."
        )
        h5.attrs["fps"] = FPS
        h5.attrs["young_modulus"] = YOUNG_MODULUS
        h5.attrs["poisson_ratio"] = POISSON_RATIO
        h5.attrs["final_stretch"] = FINAL_STRETCH
        h5.attrs["coordinate_units"] = "m"
        h5.attrs["stress_units"] = "Pa"
        h5.attrs["strain_units"] = "1"

        h5.create_dataset(
            "Nodes",
            data=data["Nodes"],
            compression="gzip",
            compression_opts=4,
            shuffle=True,
        )
        h5.create_dataset(
            "Connectivity",
            data=data["Connectivity"],
            compression="gzip",
            compression_opts=4,
            shuffle=True,
        )

        field_units = {
            "VonMises": "Pa",
            "PrincipalStrainMax": "1",
            "PrincipalStressMax": "Pa",
            "EquivalentStrain": "1",
            "DisplacementNorm": "m",
        }

        for field_name, units in field_units.items():
            dataset = h5.create_dataset(
                field_name,
                data=data[field_name],
                compression="gzip",
                compression_opts=4,
                shuffle=True,
            )
            dataset.attrs["units"] = units
            dataset.attrs["association"] = "vertex"
            dataset.attrs["time_dependent"] = True

        h5.create_dataset("Stretch", data=data["Stretch"])
        h5.create_dataset("Time", data=data["Time"])

    print(f"\nSIMBA HDF5 scritto in:\n{OUTPUT_H5.resolve()}")


# =============================================================================
# PYVISTA
# =============================================================================

def visualize_with_pyvista(data: dict[str, np.ndarray]) -> None:
    if DEFAULT_PLOT_FIELD not in data:
        raise KeyError(
            f"Campo PyVista '{DEFAULT_PLOT_FIELD}' non disponibile."
        )

    triangles = data["Connectivity"]
    final_surface = make_pyvista_surface(
        data["Nodes"][-1],
        triangles,
    )
    final_surface.point_data[DEFAULT_PLOT_FIELD] = data[
        DEFAULT_PLOT_FIELD
    ][-1]

    if SAVE_FINAL_SCREENSHOT:
        plotter = pv.Plotter(off_screen=True, window_size=(1400, 900))
        plotter.add_mesh(
            final_surface,
            scalars=DEFAULT_PLOT_FIELD,
            cmap="turbo",
            smooth_shading=True,
            show_edges=False,
            scalar_bar_args={
                "title": f"{DEFAULT_PLOT_FIELD}",
                "vertical": True,
            },
        )
        plotter.add_axes()
        plotter.view_isometric()
        plotter.show(
            screenshot=str(FINAL_SCREENSHOT),
            auto_close=True,
        )
        print(
            f"Screenshot finale scritto in:\n"
            f"{FINAL_SCREENSHOT.resolve()}"
        )

    if SHOW_ANIMATION:
        animated_surface = make_pyvista_surface(
            data["Nodes"][0],
            triangles,
        )
        animated_surface.point_data[DEFAULT_PLOT_FIELD] = data[
            DEFAULT_PLOT_FIELD
        ][0]

        plotter = pv.Plotter()
        plotter.add_mesh(
            animated_surface,
            scalars=DEFAULT_PLOT_FIELD,
            cmap="turbo",
            smooth_shading=True,
            clim=(
                float(np.nanmin(data[DEFAULT_PLOT_FIELD])),
                float(np.nanmax(data[DEFAULT_PLOT_FIELD])),
            ),
            scalar_bar_args={"title": DEFAULT_PLOT_FIELD},
        )
        plotter.add_axes()
        plotter.view_isometric()
        plotter.show(interactive_update=True, auto_close=False)

        for frame_index in range(len(data["Nodes"])):
            animated_surface.points = data["Nodes"][frame_index]
            animated_surface.point_data[DEFAULT_PLOT_FIELD] = data[
                DEFAULT_PLOT_FIELD
            ][frame_index]
            animated_surface.modified()
            plotter.render()
            time.sleep(ANIMATION_DELAY_SECONDS)

        plotter.show(interactive=True, auto_close=True)

    elif SHOW_FINAL_PLOT:
        plotter = pv.Plotter()
        plotter.add_mesh(
            final_surface,
            scalars=DEFAULT_PLOT_FIELD,
            cmap="turbo",
            smooth_shading=True,
            show_edges=False,
            scalar_bar_args={"title": DEFAULT_PLOT_FIELD},
        )
        plotter.add_axes()
        plotter.view_isometric()
        plotter.show()


# =============================================================================
# MAIN
# =============================================================================

def main() -> None:
    log("SIMBA — FEniCSx Neo-Hookean cube example")
    log("------------------------------------------")
    log(f"DOLFINx mesh resolution : {MESH_RESOLUTION}")
    log(f"Young modulus           : {YOUNG_MODULUS:g} Pa")
    log(f"Poisson ratio           : {POISSON_RATIO:g}")
    log(f"Final stretch           : {FINAL_STRETCH:g}")
    log(f"Frames                  : {N_FRAMES}")
    log("")

    data = run_simulation()

    if MPI.COMM_WORLD.rank == 0:
        write_simba_hdf5(data)
        visualize_with_pyvista(data)


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        if MPI.COMM_WORLD.rank == 0:
            print(f"\nERRORE: {exc}", file=sys.stderr)
        raise
