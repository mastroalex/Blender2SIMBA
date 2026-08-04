bl_info = {
    "name": "Blender2SIMBA",
    "author": "Alessandro Mastrofini",
    "version": (0, 1, 0),
    "blender": (4, 0, 0),
    "location": "3D Viewport > Sidebar > SIMBA",
    "description": "Export evaluated animated mesh geometry to SIMBA HDF5",
    "category": "Import-Export",
}

import importlib.util
import subprocess
import sys
from pathlib import Path

import bpy
from bpy.props import (
    BoolProperty,
    EnumProperty,
    FloatProperty,
    IntProperty,
    PointerProperty,
    StringProperty,
)
from bpy.types import Operator, Panel, PropertyGroup


def _module_available(name):
    return importlib.util.find_spec(name) is not None


def _require_dependencies():
    if not _module_available("numpy"):
        raise RuntimeError("NumPy is not installed in Blender's Python.")
    if not _module_available("h5py"):
        raise RuntimeError("h5py is not installed in Blender's Python.")

    import numpy as np
    import h5py
    return np, h5py


class B2S_Settings(PropertyGroup):
    source_object: PointerProperty(
        name="Object",
        description="Object whose evaluated geometry will be exported",
        type=bpy.types.Object,
    )

    output_path: StringProperty(
        name="Output",
        subtype="FILE_PATH",
        default="//blender2simba_animation.h5",
    )

    frame_start: IntProperty(name="Start", default=1)
    frame_end: IntProperty(name="End", default=250)
    frame_step: IntProperty(name="Step", min=1, default=1)

    coordinate_space: EnumProperty(
        name="Coordinates",
        items=(
            ("WORLD", "World", "Apply object world transform"),
            ("LOCAL", "Local", "Keep evaluated local coordinates"),
        ),
        default="WORLD",
    )

    scale: FloatProperty(
        name="Scale",
        description="Coordinate multiplier",
        default=1.0,
    )

    include_last_frame: BoolProperty(
        name="Always include end frame",
        default=True,
    )

    restore_frame: BoolProperty(
        name="Restore current frame",
        default=True,
    )

    overwrite: BoolProperty(
        name="Overwrite existing file",
        default=False,
    )

    compression: EnumProperty(
        name="Compression",
        items=(
            ("GZIP", "GZIP", "Compressed HDF5 datasets"),
            ("NONE", "None", "Uncompressed HDF5 datasets"),
        ),
        default="GZIP",
    )


class B2S_OT_InstallDependencies(Operator):
    bl_idname = "blender2simba.install_dependencies"
    bl_label = "Install NumPy and h5py"
    bl_description = "Install required packages into Blender's Python"

    def execute(self, context):
        try:
            subprocess.check_call(
                [sys.executable, "-m", "ensurepip", "--upgrade"]
            )
            subprocess.check_call(
                [
                    sys.executable,
                    "-m",
                    "pip",
                    "install",
                    "--upgrade",
                    "pip",
                    "numpy",
                    "h5py",
                ]
            )
        except Exception as exc:
            self.report({"ERROR"}, f"Installation failed: {exc}")
            return {"CANCELLED"}

        self.report(
            {"INFO"},
            "Dependencies installed. Restart Blender.",
        )
        return {"FINISHED"}


def _evaluated_mesh(context, source, coordinate_space, scale, np):
    """
    Obtain the final evaluated mesh at the current frame.

    A fresh dependency graph is requested for every frame. This is important
    for Geometry Nodes and animated modifier stacks in Blender 4.x.
    """
    context.view_layer.update()

    # Never reuse a dependency graph between frames.
    depsgraph = context.evaluated_depsgraph_get()
    evaluated = source.evaluated_get(depsgraph)

    temp_mesh = None

    try:
        try:
            temp_mesh = bpy.data.meshes.new_from_object(
                evaluated,
                preserve_all_data_layers=True,
                depsgraph=depsgraph,
            )
        except TypeError:
            temp_mesh = bpy.data.meshes.new_from_object(
                evaluated,
                depsgraph=depsgraph,
            )

        if temp_mesh is None:
            raise RuntimeError(
                f"Blender could not convert '{source.name}' to an evaluated mesh."
            )

        # Explicitly build loop triangles before checking them.
        temp_mesh.calc_loop_triangles()

        vertex_count = len(temp_mesh.vertices)
        triangle_count = len(temp_mesh.loop_triangles)

        print(
            f"[Blender2SIMBA] frame={context.scene.frame_current} "
            f"object={source.name} evaluated={evaluated.name} "
            f"vertices={vertex_count} triangles={triangle_count}"
        )

        # if vertex_count == 0:
        #     raise RuntimeError(
        #         f"Frame {context.scene.frame_current}: evaluated object "
        #         f"'{source.name}' has no vertices."
        #     )

        # if triangle_count == 0:
        #     raise RuntimeError(
        #         f"Frame {context.scene.frame_current}: evaluated object "
        #         f"'{source.name}' has no triangles."
        #     )
        if vertex_count == 0 or triangle_count == 0:
            print(
                f"[Blender2SIMBA] Frame {context.scene.frame_current}: empty geometry."
            )
        nodes = np.empty((vertex_count, 3), dtype=np.float32)

        if coordinate_space == "WORLD":
            matrix = evaluated.matrix_world
            for index, vertex in enumerate(temp_mesh.vertices):
                point = matrix @ vertex.co
                nodes[index] = (
                    float(point.x) * scale,
                    float(point.y) * scale,
                    float(point.z) * scale,
                )
        else:
            for index, vertex in enumerate(temp_mesh.vertices):
                point = vertex.co
                nodes[index] = (
                    float(point.x) * scale,
                    float(point.y) * scale,
                    float(point.z) * scale,
                )

        connectivity = np.empty(
            (triangle_count, 3),
            dtype=np.int32,
        )

        for index, triangle in enumerate(temp_mesh.loop_triangles):
            connectivity[index] = triangle.vertices

        return (
            np.ascontiguousarray(nodes, dtype=np.float32),
            np.ascontiguousarray(connectivity, dtype=np.int32),
        )
    finally:
        if temp_mesh is not None:
            bpy.data.meshes.remove(temp_mesh)


class B2S_OT_ExportH5(Operator):
    bl_idname = "blender2simba.export_h5"
    bl_label = "Export SIMBA HDF5"
    bl_description = "Export evaluated triangles frame by frame"

    @classmethod
    def poll(cls, context):
        settings = getattr(context.scene, "blender2simba_settings", None)
        return settings is not None and settings.source_object is not None

    def execute(self, context):
        settings = context.scene.blender2simba_settings
        scene = context.scene
        source = context.active_object or settings.source_object

        if source is None:
            self.report({"ERROR"}, "No active object selected.")
            return {"CANCELLED"}

        # Keep the UI picker synchronized with the actual object being exported.
        settings.source_object = source

        try:
            np, h5py = _require_dependencies()
        except Exception as exc:
            self.report({"ERROR"}, str(exc))
            return {"CANCELLED"}

        if settings.frame_end < settings.frame_start:
            self.report({"ERROR"}, "End frame must be >= start frame.")
            return {"CANCELLED"}

        output = Path(bpy.path.abspath(settings.output_path)).expanduser()
        if output.suffix.lower() not in {".h5", ".hdf5"}:
            output = output.with_suffix(".h5")

        if output.exists() and not settings.overwrite:
            self.report(
                {"ERROR"},
                "Output exists. Enable overwrite or select another path.",
            )
            return {"CANCELLED"}

        output.parent.mkdir(parents=True, exist_ok=True)

        frames = list(
            range(
                settings.frame_start,
                settings.frame_end + 1,
                settings.frame_step,
            )
        )

        if (
            settings.include_last_frame
            and frames[-1] != settings.frame_end
        ):
            frames.append(settings.frame_end)

        fps = float(scene.render.fps) / float(scene.render.fps_base)
        original_frame = scene.frame_current
        compression = (
            dict(compression="gzip", compression_opts=4, shuffle=True)
            if settings.compression == "GZIP"
            else {}
        )

        wm = context.window_manager
        wm.progress_begin(0, len(frames))

        try:
            with h5py.File(output, "w") as h5:
                h5.attrs["format"] = "SIMBA Intermediate HDF5"
                h5.attrs["format_version"] = 1
                h5.attrs["geometry_type"] = "ShellMesh"
                h5.attrs["topology"] = "frame_by_frame"
                h5.attrs["object_name"] = source.name
                h5.attrs["frames_per_second"] = fps
                h5.attrs["frame_start"] = settings.frame_start
                h5.attrs["frame_end"] = settings.frame_end
                h5.attrs["frame_step"] = settings.frame_step
                h5.attrs["coordinate_space"] = settings.coordinate_space
                h5.attrs["scale"] = settings.scale

                previous_signature = None
                topology_changes = False

                for export_index, frame in enumerate(frames):
                    scene.frame_set(frame)
                    context.view_layer.update()

                    print(
                        f"[Blender2SIMBA] evaluating frame {frame} "
                        f"from object '{source.name}'"
                    )

                    nodes, connectivity = _evaluated_mesh(
                        context,
                        source,
                        settings.coordinate_space,
                        settings.scale,
                        np,
                    )

                    signature = (
                        int(nodes.shape[0]),
                        int(connectivity.shape[0]),
                        connectivity.tobytes(),
                    )
                    if previous_signature is not None and signature != previous_signature:
                        topology_changes = True
                    previous_signature = signature

                    group = h5.create_group(f"Time_{export_index:06d}")
                    group.attrs["blender_frame"] = int(frame)
                    group.attrs["time_seconds"] = float(frame / fps)
                    group.attrs["vertex_count"] = int(nodes.shape[0])
                    group.attrs["triangle_count"] = int(connectivity.shape[0])

                    group.create_dataset(
                        "Nodes",
                        data=nodes.astype("<f4", copy=False),
                        dtype="<f4",
                        **compression,
                    )
                    group.create_dataset(
                        "Connectivity",
                        data=connectivity.astype("<i4", copy=False),
                        dtype="<i4",
                        **compression,
                    )

                    wm.progress_update(export_index + 1)

                h5.attrs["detected_dynamic_topology"] = bool(topology_changes)

        except Exception as exc:
            self.report({"ERROR"}, f"Export failed: {exc}")
            return {"CANCELLED"}
        finally:
            wm.progress_end()
            if settings.restore_frame:
                scene.frame_set(original_frame)
                context.view_layer.update()

        self.report(
            {"INFO"},
            f"Exported {len(frames)} frames to {output}",
        )
        return {"FINISHED"}


class B2S_PT_MainPanel(Panel):
    bl_label = "Blender2SIMBA"
    bl_idname = "B2S_PT_main"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "SIMBA"

    def draw(self, context):
        layout = self.layout
        settings = context.scene.blender2simba_settings

        deps = layout.box()
        deps.label(text="Dependencies")
        ready = _module_available("numpy") and _module_available("h5py")

        if ready:
            deps.label(text="NumPy and h5py ready", icon="CHECKMARK")
        else:
            deps.label(text="Dependencies missing", icon="ERROR")
            deps.operator(
                "blender2simba.install_dependencies",
                icon="IMPORT",
            )

        source = layout.box()
        source.label(text="Evaluated geometry")
        source.prop(settings, "source_object")
        source.prop(settings, "coordinate_space")
        source.prop(settings, "scale")

        warning = source.column()
        warning.alert = True
        warning.label(
            text="Geometry Nodes: realize instances before output",
            icon="INFO",
        )

        animation = layout.box()
        animation.label(text="Animation")
        row = animation.row(align=True)
        row.prop(settings, "frame_start")
        row.prop(settings, "frame_end")
        animation.prop(settings, "frame_step")
        animation.prop(settings, "include_last_frame")
        animation.prop(settings, "restore_frame")

        output = layout.box()
        output.label(text="HDF5 output")
        output.prop(settings, "output_path")
        output.prop(settings, "compression")
        output.prop(settings, "overwrite")

        layout.operator(
            "blender2simba.export_h5",
            icon="EXPORT",
        )


_CLASSES = (
    B2S_Settings,
    B2S_OT_InstallDependencies,
    B2S_OT_ExportH5,
    B2S_PT_MainPanel,
)


def register():
    for cls in _CLASSES:
        bpy.utils.register_class(cls)

    bpy.types.Scene.blender2simba_settings = PointerProperty(
        type=B2S_Settings
    )


def unregister():
    if hasattr(bpy.types.Scene, "blender2simba_settings"):
        del bpy.types.Scene.blender2simba_settings

    for cls in reversed(_CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
