bl_info = {
    "name": "Blender2SIMBA Test",
    "author": "Alessandro Mastrofini",
    "version": (1, 0, 0),
    "blender": (4, 0, 0),
    "location": "3D Viewport > Sidebar > SIMBA",
    "description": "Export evaluated Blender geometry to SIMBA-compatible HDF5",
    "category": "Import-Export",
}

import importlib.util
import json
import re
import subprocess
import sys
import time
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


SUPPORTED_OBJECT_TYPES = {
    "MESH",
    "CURVE",
    "SURFACE",
    "FONT",
    "META",
    "POINTCLOUD",
}


def module_available(name):
    return importlib.util.find_spec(name) is not None


def require_dependencies():
    if not module_available("numpy"):
        raise RuntimeError("NumPy is missing from Blender's Python.")
    if not module_available("h5py"):
        raise RuntimeError("h5py is missing from Blender's Python.")

    import numpy as np
    import h5py
    return np, h5py


def safe_filename(name):
    value = re.sub(r"[^A-Za-z0-9._-]+", "_", name.strip())
    return value or "object"


def collect_objects(context, settings):
    if settings.source_mode == "ACTIVE":
        candidates = [context.active_object] if context.active_object else []
    elif settings.source_mode == "SELECTED":
        candidates = list(context.selected_objects)
    elif settings.source_mode == "COLLECTION":
        collection = settings.source_collection
        if collection is None:
            candidates = []
        elif settings.include_nested_collections:
            candidates = list(collection.all_objects)
        else:
            candidates = list(collection.objects)
    else:
        candidates = []

    result = []
    seen = set()

    for obj in candidates:
        if obj is None or obj.name_full in seen:
            continue

        seen.add(obj.name_full)

        if obj.type not in SUPPORTED_OBJECT_TYPES:
            continue

        if obj.hide_render and not settings.include_hidden_render:
            continue

        result.append(obj)

    return result


def build_frames(settings):
    frames = list(
        range(
            settings.frame_start,
            settings.frame_end + 1,
            max(1, settings.frame_step),
        )
    )

    if (
        settings.include_last_frame
        and frames
        and frames[-1] != settings.frame_end
    ):
        frames.append(settings.frame_end)

    return frames


def compression_options(settings):
    if settings.hdf5_compression == "GZIP":
        return {
            "compression": "gzip",
            "compression_opts": int(settings.gzip_level),
            "shuffle": True,
        }

    if settings.hdf5_compression == "LZF":
        return {
            "compression": "lzf",
            "shuffle": True,
        }

    return {}


def evaluate_object(context, source, coordinate_space, scale, np):
    context.view_layer.update()
    depsgraph = context.evaluated_depsgraph_get()
    evaluated = source.evaluated_get(depsgraph)

    temporary_mesh = None

    try:
        try:
            temporary_mesh = bpy.data.meshes.new_from_object(
                evaluated,
                preserve_all_data_layers=True,
                depsgraph=depsgraph,
            )
        except TypeError:
            temporary_mesh = bpy.data.meshes.new_from_object(
                evaluated,
                depsgraph=depsgraph,
            )

        if temporary_mesh is None:
            raise RuntimeError(
                f"Blender could not evaluate '{source.name}' as a mesh."
            )

        temporary_mesh.calc_loop_triangles()

        vertex_count = len(temporary_mesh.vertices)
        triangle_count = len(temporary_mesh.loop_triangles)

        nodes = np.empty((vertex_count, 3), dtype=np.float32)
        connectivity = np.empty((triangle_count, 3), dtype=np.int32)

        if coordinate_space == "WORLD":
            matrix = evaluated.matrix_world

            for index, vertex in enumerate(temporary_mesh.vertices):
                point = matrix @ vertex.co
                nodes[index] = (
                    float(point.x) * scale,
                    float(point.y) * scale,
                    float(point.z) * scale,
                )
        else:
            for index, vertex in enumerate(temporary_mesh.vertices):
                point = vertex.co
                nodes[index] = (
                    float(point.x) * scale,
                    float(point.y) * scale,
                    float(point.z) * scale,
                )

        for index, triangle in enumerate(temporary_mesh.loop_triangles):
            connectivity[index] = triangle.vertices

        return (
            np.ascontiguousarray(nodes, dtype=np.float32),
            np.ascontiguousarray(connectivity, dtype=np.int32),
        )
    finally:
        if temporary_mesh is not None:
            bpy.data.meshes.remove(temporary_mesh)


class B2S_Settings(PropertyGroup):
    source_mode: EnumProperty(
        name="Source",
        items=(
            ("ACTIVE", "Active Object", "Export the active object"),
            ("SELECTED", "Selected Objects", "Export selected supported objects"),
            ("COLLECTION", "Collection", "Export supported collection objects"),
        ),
        default="ACTIVE",
    )

    source_collection: PointerProperty(
        name="Collection",
        type=bpy.types.Collection,
    )

    include_nested_collections: BoolProperty(
        name="Include nested collections",
        default=True,
    )

    include_hidden_render: BoolProperty(
        name="Include render-hidden objects",
        default=False,
    )

    frame_start: IntProperty(name="Start", default=1)
    frame_end: IntProperty(name="End", default=250)
    frame_step: IntProperty(name="Step", min=1, default=1)

    include_last_frame: BoolProperty(
        name="Always include end frame",
        default=True,
    )

    restore_frame: BoolProperty(
        name="Restore current frame",
        default=True,
    )

    coordinate_space: EnumProperty(
        name="Coordinates",
        items=(
            ("WORLD", "World", "Apply evaluated world transform"),
            ("LOCAL", "Local", "Keep evaluated local coordinates"),
        ),
        default="WORLD",
    )

    scale: FloatProperty(name="Scale", default=1.0)

    preferred_vertex_precision: EnumProperty(
        name="SIMBA Precision",
        items=(
            ("FLOAT32", "Float32", "Single-precision SIMBA vertices"),
            ("FLOAT16", "Float16", "Half-precision SIMBA vertices"),
        ),
        default="FLOAT32",
    )

    store_hdf5_precision: EnumProperty(
        name="HDF5 Node Storage",
        items=(
            ("FLOAT32", "Float32", "Preserve a Float32 HDF5 master"),
            ("MATCH_SIMBA", "Match SIMBA", "Use selected SIMBA precision"),
        ),
        default="FLOAT32",
    )

    output_directory: StringProperty(
        name="Output Folder",
        subtype="DIR_PATH",
        default="//Blender2SIMBA_Export/",
    )

    file_prefix: StringProperty(
        name="Filename Prefix",
        default="",
    )

    hdf5_compression: EnumProperty(
        name="Compression",
        items=(
            ("GZIP", "GZIP", "Good compression"),
            ("LZF", "LZF", "Fast compression"),
            ("NONE", "None", "No compression"),
        ),
        default="GZIP",
    )

    gzip_level: IntProperty(
        name="GZIP Level",
        min=1,
        max=9,
        default=4,
    )

    overwrite: BoolProperty(
        name="Overwrite",
        default=False,
    )

    write_manifest: BoolProperty(
        name="Write JSON manifest",
        default=True,
    )

    write_export_log: BoolProperty(
        name="Write export log",
        default=True,
    )

    console_log_each_frame: BoolProperty(
        name="Log every frame",
        default=False,
    )


class B2S_OT_InstallDependencies(Operator):
    bl_idname = "blender2simba_v102.install_dependencies"
    bl_label = "Install NumPy and h5py"

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

        self.report({"INFO"}, "Dependencies installed. Restart Blender.")
        return {"FINISHED"}


class B2S_OT_Validate(Operator):
    bl_idname = "blender2simba_v102.validate"
    bl_label = "Validate"

    def execute(self, context):
        settings = context.scene.blender2simba_v102_settings
        objects = collect_objects(context, settings)
        frames = build_frames(settings)

        if settings.frame_end < settings.frame_start:
            self.report({"ERROR"}, "End frame must be >= start frame.")
            return {"CANCELLED"}

        if not objects:
            self.report({"ERROR"}, "No supported objects found.")
            return {"CANCELLED"}

        self.report(
            {"INFO"},
            f"Ready: {len(objects)} object(s), {len(frames)} frame(s).",
        )
        return {"FINISHED"}


class B2S_OT_Export(Operator):
    bl_idname = "blender2simba_v102.export"
    bl_label = "Export SIMBA HDF5"

    def execute(self, context):
        settings = context.scene.blender2simba_v102_settings
        scene = context.scene

        try:
            np, h5py = require_dependencies()
        except Exception as exc:
            self.report({"ERROR"}, str(exc))
            return {"CANCELLED"}

        if settings.frame_end < settings.frame_start:
            self.report({"ERROR"}, "End frame must be >= start frame.")
            return {"CANCELLED"}

        objects = collect_objects(context, settings)
        frames = build_frames(settings)

        if not objects:
            self.report({"ERROR"}, "No supported objects found.")
            return {"CANCELLED"}

        output_directory = Path(
            bpy.path.abspath(settings.output_directory)
        ).expanduser()
        output_directory.mkdir(parents=True, exist_ok=True)

        original_frame = scene.frame_current
        fps = float(scene.render.fps) / float(scene.render.fps_base)
        compression = compression_options(settings)

        total_steps = len(objects) * len(frames)
        completed = 0
        wm = context.window_manager
        wm.progress_begin(0, total_steps)

        started = time.perf_counter()
        log_lines = []
        manifest = {
            "format": "Blender2SIMBA export manifest",
            "addon_version": "1.0.2",
            "blender_version": bpy.app.version_string,
            "fps": fps,
            "frame_start": settings.frame_start,
            "frame_end": settings.frame_end,
            "frame_step": settings.frame_step,
            "preferred_vertex_format": (
                settings.preferred_vertex_precision.lower()
            ),
            "objects": [],
        }

        try:
            for source in objects:
                file_name = (
                    settings.file_prefix
                    + safe_filename(source.name)
                    + ".h5"
                )
                output_path = output_directory / file_name

                if output_path.exists() and not settings.overwrite:
                    raise RuntimeError(
                        f"Output exists: {output_path}"
                    )

                record = {
                    "name": source.name,
                    "file": file_name,
                    "frames": len(frames),
                    "empty_frames": 0,
                    "max_vertices": 0,
                    "max_triangles": 0,
                }

                with h5py.File(output_path, "w") as h5:
                    attrs = h5.attrs
                    attrs["format"] = "SIMBA Intermediate HDF5"
                    attrs["format_version"] = 2
                    attrs["geometry_type"] = "ShellMesh"
                    attrs["topology"] = "frame_by_frame"
                    attrs["object_name"] = source.name
                    attrs["frames_per_second"] = fps
                    attrs["frame_start"] = settings.frame_start
                    attrs["frame_end"] = settings.frame_end
                    attrs["frame_step"] = settings.frame_step
                    attrs["coordinate_space"] = settings.coordinate_space
                    attrs["scale"] = settings.scale
                    attrs["preferred_vertex_format"] = (
                        settings.preferred_vertex_precision.lower()
                    )
                    attrs["recommended_simba_format"] = "SHMSH005"
                    attrs["simba_converter"] = "shell_mesh_h5_to_fields.py"

                    for export_index, blender_frame in enumerate(frames):
                        scene.frame_set(blender_frame)
                        context.view_layer.update()

                        nodes, connectivity = evaluate_object(
                            context,
                            source,
                            settings.coordinate_space,
                            settings.scale,
                            np,
                        )

                        vertex_count = int(nodes.shape[0])
                        triangle_count = int(connectivity.shape[0])

                        if vertex_count == 0 or triangle_count == 0:
                            record["empty_frames"] += 1

                        record["max_vertices"] = max(
                            record["max_vertices"],
                            vertex_count,
                        )
                        record["max_triangles"] = max(
                            record["max_triangles"],
                            triangle_count,
                        )

                        group = h5.create_group(
                            f"Time_{export_index:06d}"
                        )
                        group.attrs["blender_frame"] = blender_frame
                        group.attrs["time_seconds"] = blender_frame / fps
                        group.attrs["vertex_count"] = vertex_count
                        group.attrs["triangle_count"] = triangle_count

                        use_half = (
                            settings.store_hdf5_precision == "MATCH_SIMBA"
                            and settings.preferred_vertex_precision == "FLOAT16"
                        )

                        group.create_dataset(
                            "Nodes",
                            data=nodes.astype(
                                "<f2" if use_half else "<f4",
                                copy=False,
                            ),
                            dtype="<f2" if use_half else "<f4",
                            **compression,
                        )
                        group.create_dataset(
                            "Connectivity",
                            data=connectivity.astype("<i4", copy=False),
                            dtype="<i4",
                            **compression,
                        )

                        completed += 1
                        wm.progress_update(completed)

                        context.workspace.status_text_set(
                            f"Blender2SIMBA | {source.name} | "
                            f"frame {blender_frame} | "
                            f"{completed}/{total_steps}"
                        )

                        line = (
                            f"{source.name}\tframe={blender_frame}\t"
                            f"vertices={vertex_count}\t"
                            f"triangles={triangle_count}"
                        )
                        log_lines.append(line)

                        if settings.console_log_each_frame:
                            print("[Blender2SIMBA] " + line)

                manifest["objects"].append(record)

        except Exception as exc:
            self.report({"ERROR"}, f"Export failed: {exc}")
            return {"CANCELLED"}

        finally:
            wm.progress_end()
            context.workspace.status_text_set(None)

            if settings.restore_frame:
                scene.frame_set(original_frame)
                context.view_layer.update()

        manifest["elapsed_seconds"] = time.perf_counter() - started

        if settings.write_manifest:
            (output_directory / "blender2simba_manifest.json").write_text(
                json.dumps(manifest, indent=2),
                encoding="utf-8",
            )

        if settings.write_export_log:
            (output_directory / "blender2simba_export.log").write_text(
                "\n".join(log_lines) + "\n",
                encoding="utf-8",
            )

        self.report(
            {"INFO"},
            f"Exported {len(objects)} object(s).",
        )
        return {"FINISHED"}


class B2S_PT_Main(Panel):
    bl_label = "Blender2SIMBA 1.0.2"
    bl_idname = "B2S_PT_v102_main"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "SIMBA"

    def draw(self, context):
        layout = self.layout
        settings = context.scene.blender2simba_v102_settings

        layout.label(text="Test build loaded", icon="CHECKMARK")

        box = layout.box()
        box.label(text="Dependencies")

        ready = module_available("numpy") and module_available("h5py")

        if ready:
            box.label(text="NumPy and h5py ready", icon="CHECKMARK")
        else:
            box.label(text="Dependencies missing", icon="ERROR")
            box.operator(
                "blender2simba_v102.install_dependencies",
                icon="IMPORT",
            )

        box = layout.box()
        box.label(text="Source")
        box.prop(settings, "source_mode")

        if settings.source_mode == "COLLECTION":
            box.prop(settings, "source_collection")
            box.prop(settings, "include_nested_collections")

        box.prop(settings, "include_hidden_render")


class B2S_PT_Animation(Panel):
    bl_label = "Animation"
    bl_idname = "B2S_PT_v102_animation"
    bl_parent_id = "B2S_PT_v102_main"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "SIMBA"

    def draw(self, context):
        settings = context.scene.blender2simba_v102_settings
        layout = self.layout

        row = layout.row(align=True)
        row.prop(settings, "frame_start")
        row.prop(settings, "frame_end")
        layout.prop(settings, "frame_step")
        layout.prop(settings, "include_last_frame")
        layout.prop(settings, "restore_frame")


class B2S_PT_Geometry(Panel):
    bl_label = "Geometry"
    bl_idname = "B2S_PT_v102_geometry"
    bl_parent_id = "B2S_PT_v102_main"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "SIMBA"

    def draw(self, context):
        settings = context.scene.blender2simba_v102_settings
        layout = self.layout

        layout.prop(settings, "coordinate_space")
        layout.prop(settings, "scale")
        layout.prop(settings, "preferred_vertex_precision")
        layout.prop(settings, "store_hdf5_precision")


class B2S_PT_Output(Panel):
    bl_label = "Output"
    bl_idname = "B2S_PT_v102_output"
    bl_parent_id = "B2S_PT_v102_main"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "SIMBA"

    def draw(self, context):
        settings = context.scene.blender2simba_v102_settings
        layout = self.layout

        layout.prop(settings, "output_directory")
        layout.prop(settings, "file_prefix")
        layout.prop(settings, "hdf5_compression")

        if settings.hdf5_compression == "GZIP":
            layout.prop(settings, "gzip_level")

        layout.prop(settings, "overwrite")
        layout.prop(settings, "write_manifest")
        layout.prop(settings, "write_export_log")
        layout.prop(settings, "console_log_each_frame")

        row = layout.row(align=True)
        row.operator("blender2simba_v102.validate", icon="CHECKMARK")
        row.operator("blender2simba_v102.export", icon="EXPORT")


CLASSES = (
    B2S_Settings,
    B2S_OT_InstallDependencies,
    B2S_OT_Validate,
    B2S_OT_Export,
    B2S_PT_Main,
    B2S_PT_Animation,
    B2S_PT_Geometry,
    B2S_PT_Output,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)

    bpy.types.Scene.blender2simba_v102_settings = PointerProperty(
        type=B2S_Settings
    )


def unregister():
    if hasattr(bpy.types.Scene, "blender2simba_v102_settings"):
        del bpy.types.Scene.blender2simba_v102_settings

    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
