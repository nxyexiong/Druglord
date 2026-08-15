import json
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))

from awp_materials import (  # noqa: E402
    GRID_SIZE,
    MATERIAL_TILE_BY_SOURCE,
    PADDING,
    TILE_SIZE,
)


source_path = sys.argv[sys.argv.index("--") + 1]
output_path = sys.argv[sys.argv.index("--") + 2]

base_target_length = 1.27
model_scale_multiplier = 1.2
downward_shift_meters = 0.08
target_length = base_target_length * model_scale_multiplier
vertical_offset = Vector((0.0, 0.0, -downward_shift_meters))
grip_pivot = Vector((0.0, 5.2, -0.1))
barrel_source_material = "aiStandardSurface10SG"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(
    filepath=source_path,
    forward_axis="NEGATIVE_Z",
    up_axis="Y",
)

source_meshes = [
    obj for obj in bpy.context.scene.objects if obj.type == "MESH"
]
if len(source_meshes) != 1:
    raise RuntimeError(
        "The AWP source OBJ must contain exactly one mesh object."
    )

base = source_meshes[0]
bpy.ops.object.select_all(action="DESELECT")
base.select_set(True)
bpy.context.view_layer.objects.active = base
bpy.ops.object.transform_apply(
    location=False,
    rotation=True,
    scale=True,
)

actual_materials = {
    material.name
    for material in base.data.materials
    if material is not None
}
expected_materials = set(MATERIAL_TILE_BY_SOURCE)
if actual_materials != expected_materials:
    raise RuntimeError(
        "Unexpected AWP source materials: "
        f"{sorted(actual_materials)}"
    )

uv_layer = base.data.uv_layers.active
if uv_layer is None:
    raise RuntimeError("The AWP source OBJ does not have a UV map.")

atlas_size = GRID_SIZE * TILE_SIZE
content_size = TILE_SIZE - (PADDING * 2)
output_material_indices = []

for polygon in base.data.polygons:
    source_material = base.data.materials[polygon.material_index]
    if source_material is None:
        raise RuntimeError("The AWP source has an empty material slot.")

    source_name = source_material.name
    tile_index = MATERIAL_TILE_BY_SOURCE[source_name]
    column = tile_index % GRID_SIZE
    image_row = tile_index // GRID_SIZE
    uv_row = GRID_SIZE - 1 - image_row

    for loop_index in polygon.loop_indices:
        uv = uv_layer.data[loop_index].uv
        uv.x = (
            column * TILE_SIZE +
            PADDING +
            uv.x * content_size
        ) / atlas_size
        uv.y = (
            uv_row * TILE_SIZE +
            PADDING +
            uv.y * content_size
        ) / atlas_size

    output_material_indices.append(
        1 if source_name == barrel_source_material else 0
    )

base.data.materials.clear()
base.data.materials.append(
    bpy.data.materials.new("druglord_awp")
)
base.data.materials.append(
    bpy.data.materials.new("druglord_awp_barrel")
)
for polygon, material_index in zip(
    base.data.polygons,
    output_material_indices,
):
    polygon.material_index = material_index

source_points = [vertex.co for vertex in base.data.vertices]
source_minimum = Vector(
    tuple(min(point[axis] for point in source_points) for axis in range(3))
)
source_maximum = Vector(
    tuple(max(point[axis] for point in source_points) for axis in range(3))
)
source_length = source_maximum.y - source_minimum.y
scale = target_length / source_length

base.data.transform(
    Matrix.Translation(vertical_offset)
    @ Matrix.Scale(scale, 4)
    @ Matrix.Translation(-grip_pivot)
)
base.data.update()
base.name = "druglord_awp"
base.data.name = "druglord_awp"

lod_ratios = (
    ("druglord_awp.lod1", 0.60),
    ("druglord_awp.lod2", 0.32),
    ("druglord_awp.lod3", 0.14),
)

generated = [base]
for name, ratio in lod_ratios:
    lod = base.copy()
    lod.data = base.data.copy()
    lod.name = name
    lod.data.name = name
    bpy.context.collection.objects.link(lod)

    modifier = lod.modifiers.new(name="LOD Decimate", type="DECIMATE")
    modifier.ratio = ratio
    modifier.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = lod
    lod.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    lod.select_set(False)
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
collision.name = "bo_druglord_awp"
collision.data.name = "bo_druglord_awp"
collision.dimensions = maximum - minimum
bpy.ops.object.transform_apply(
    location=False,
    rotation=False,
    scale=True,
)
generated.append(collision)

for obj in bpy.context.scene.objects:
    obj.select_set(obj in generated)

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
    "target_length": target_length,
    "model_scale_multiplier": model_scale_multiplier,
    "downward_shift_meters": downward_shift_meters,
    "vertical_offset": list(vertical_offset),
    "source_length": source_length,
    "scale": scale,
    "grip_pivot": list(grip_pivot),
    "bounds": {
        "minimum": list(minimum),
        "maximum": list(maximum),
        "dimensions": list(maximum - minimum),
    },
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
