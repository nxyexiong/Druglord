import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector


arguments = sys.argv[sys.argv.index("--") + 1:]
if len(arguments) != 2:
    raise ValueError(
        "Expected the source FBX and output FBX paths after '--'."
    )

source_path, output_path = arguments

base_target_length = 0.731
scale_multiplier = 1.2
target_length = base_target_length * scale_multiplier
vertical_offset = -0.04
muzzle_section_length = 0.20
expected_mesh_count = 40
expected_materials = {"baked"}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=source_path)

source_meshes = [
    obj for obj in bpy.context.scene.objects if obj.type == "MESH"
]
if len(source_meshes) != expected_mesh_count:
    raise RuntimeError(
        f"The M79 source FBX must contain {expected_mesh_count} mesh "
        f"objects, but contains {len(source_meshes)}."
    )

for source_mesh in source_meshes:
    if source_mesh.data.uv_layers.active is None:
        raise RuntimeError(
            f"The M79 source mesh '{source_mesh.name}' has no UV map."
        )

actual_materials = {
    material.name
    for source_mesh in source_meshes
    for material in source_mesh.data.materials
    if material is not None
}
if actual_materials != expected_materials:
    raise RuntimeError(
        f"Unexpected M79 source materials: {sorted(actual_materials)}"
    )

trigger = next(
    (obj for obj in source_meshes if obj.name == "kurok"),
    None,
)
if trigger is None:
    raise RuntimeError(
        "The M79 source trigger mesh 'kurok' was not found."
    )

trigger_points = [
    trigger.matrix_world @ Vector(corner)
    for corner in trigger.bound_box
]
grip_pivot = Vector(
    tuple(
        (
            min(point[axis] for point in trigger_points) +
            max(point[axis] for point in trigger_points)
        ) * 0.5
        for axis in range(3)
    )
)

for source_mesh in source_meshes:
    bpy.ops.object.select_all(action="DESELECT")
    source_mesh.select_set(True)
    bpy.context.view_layer.objects.active = source_mesh
    bpy.ops.object.transform_apply(
        location=True,
        rotation=True,
        scale=True,
    )

bpy.ops.object.select_all(action="DESELECT")
for source_mesh in source_meshes:
    source_mesh.select_set(True)
bpy.context.view_layer.objects.active = source_meshes[0]
bpy.ops.object.join()

base = bpy.context.object
base.data.calc_loop_triangles()
source_report = {
    "mesh_count": expected_mesh_count,
    "vertices": len(base.data.vertices),
    "triangles": len(base.data.loop_triangles),
}

source_points = [vertex.co.copy() for vertex in base.data.vertices]
source_minimum = Vector(
    tuple(
        min(point[axis] for point in source_points)
        for axis in range(3)
    )
)
source_maximum = Vector(
    tuple(
        max(point[axis] for point in source_points)
        for axis in range(3)
    )
)
source_dimensions = source_maximum - source_minimum
source_length = source_dimensions.x
if source_length <= source_dimensions.y * 4.0 or \
        source_length <= source_dimensions.z * 2.0:
    raise RuntimeError(
        "The M79 source is no longer aligned along its X axis."
    )

scale = target_length / source_length

main_material = bpy.data.materials.new("druglord_m79")
muzzle_material = bpy.data.materials.new("druglord_m79_muzzle")
base.data.materials.clear()
base.data.materials.append(main_material)
base.data.materials.append(muzzle_material)

muzzle_limit = source_maximum.x - muzzle_section_length
muzzle_polygon_count = 0
for polygon in base.data.polygons:
    center_x = sum(
        base.data.vertices[index].co.x
        for index in polygon.vertices
    ) / len(polygon.vertices)
    if center_x >= muzzle_limit:
        polygon.material_index = 1
        muzzle_polygon_count += 1
    else:
        polygon.material_index = 0

if muzzle_polygon_count == 0:
    raise RuntimeError(
        "The M79 muzzle material did not receive any polygons."
    )

source_to_bannerlord = (
    Matrix.Translation((0.0, 0.0, vertical_offset))
    @ Matrix.Rotation(-math.pi / 2.0, 4, "Z")
    @ Matrix.Scale(scale, 4)
    @ Matrix.Translation(-grip_pivot)
)
base.data.transform(source_to_bannerlord)
base.data.update()
base.name = "druglord_m79"
base.data.name = "druglord_m79"

lod_ratios = (
    ("druglord_m79.lod1", 0.60),
    ("druglord_m79.lod2", 0.32),
    ("druglord_m79.lod3", 0.14),
)

generated = [base]
for name, ratio in lod_ratios:
    lod = base.copy()
    lod.data = base.data.copy()
    lod.name = name
    lod.data.name = name
    bpy.context.collection.objects.link(lod)

    bpy.ops.object.select_all(action="DESELECT")
    lod.select_set(True)
    bpy.context.view_layer.objects.active = lod
    modifier = lod.modifiers.new(name="LOD Decimate", type="DECIMATE")
    modifier.ratio = ratio
    modifier.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    generated.append(lod)

bounds = [
    base.matrix_world @ Vector(corner)
    for corner in base.bound_box
]
minimum = Vector(
    tuple(min(point[axis] for point in bounds) for axis in range(3))
)
maximum = Vector(
    tuple(max(point[axis] for point in bounds) for axis in range(3))
)

bpy.ops.mesh.primitive_cube_add(location=(minimum + maximum) * 0.5)
collision = bpy.context.object
collision.name = "bo_druglord_m79"
collision.data.name = "bo_druglord_m79"
collision.dimensions = maximum - minimum
bpy.ops.object.transform_apply(
    location=False,
    rotation=False,
    scale=True,
)
generated.append(collision)

bpy.ops.object.select_all(action="DESELECT")
for obj in generated:
    obj.select_set(True)

bpy.context.view_layer.objects.active = base
os.makedirs(os.path.dirname(output_path), exist_ok=True)

try:
    bpy.ops.preferences.addon_enable(module="io_scene_fbx")
except Exception:
    pass

bpy.context.scene.unit_settings.system = "METRIC"
bpy.context.scene.unit_settings.scale_length = 1.0

bpy.ops.export_scene.fbx(
    filepath=output_path,
    use_selection=True,
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_UNITS",
    axis_forward="-Z",
    axis_up="Y",
    add_leaf_bones=False,
    bake_anim=False,
)

report = {
    "source": source_path,
    "output": output_path,
    "base_target_length": base_target_length,
    "scale_multiplier": scale_multiplier,
    "target_length": target_length,
    "vertical_offset": vertical_offset,
    "source_length": source_length,
    "scale": scale,
    "grip_pivot": list(grip_pivot),
    "muzzle_section_length": muzzle_section_length,
    "muzzle_polygon_count": muzzle_polygon_count,
    "source_bounds": {
        "minimum": list(source_minimum),
        "maximum": list(source_maximum),
        "dimensions": list(source_dimensions),
    },
    "bounds": {
        "minimum": list(minimum),
        "maximum": list(maximum),
        "dimensions": list(maximum - minimum),
    },
    "source_mesh": source_report,
    "meshes": [],
}

for obj in generated:
    if obj.type != "MESH":
        continue

    obj.data.calc_loop_triangles()
    report["meshes"].append(
        {
            "name": obj.name,
            "vertices": len(obj.data.vertices),
            "triangles": len(obj.data.loop_triangles),
            "materials": [
                material.name if material else None
                for material in obj.data.materials
            ],
        }
    )

with open(
    os.path.splitext(output_path)[0] + ".report.json",
    "w",
    encoding="utf-8",
) as output:
    json.dump(report, output, indent=2)
