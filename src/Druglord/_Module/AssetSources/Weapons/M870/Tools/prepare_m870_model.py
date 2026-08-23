import json
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

base_target_length = 1.05
scale_multiplier = 1.2
target_length = base_target_length * scale_multiplier
vertical_offset = -0.04
grip_pivot = Vector((0.0, -0.60, 0.70))
muzzle_section_length = 0.12

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=source_path)

source_meshes = [
    obj for obj in bpy.context.scene.objects if obj.type == "MESH"
]
if len(source_meshes) != 1:
    raise RuntimeError(
        "The M870 source FBX must contain exactly one mesh object."
    )

base = source_meshes[0]
if base.data.uv_layers.active is None:
    raise RuntimeError("The M870 source mesh does not have a UV map.")

actual_materials = {
    material.name
    for material in base.data.materials
    if material is not None
}
if actual_materials != {"MainMat.006"}:
    raise RuntimeError(
        f"Unexpected M870 source materials: {sorted(actual_materials)}"
    )

bpy.ops.object.select_all(action="DESELECT")
base.select_set(True)
bpy.context.view_layer.objects.active = base
bpy.ops.object.transform_apply(
    location=True,
    rotation=True,
    scale=True,
)

base.data.calc_loop_triangles()
source_report = {
    "name": base.name,
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
source_length = source_dimensions.y
if source_length <= source_dimensions.x * 4.0 or \
        source_length <= source_dimensions.z * 4.0:
    raise RuntimeError(
        "The M870 source is no longer aligned along its Y axis."
    )

scale = target_length / source_length

main_material = bpy.data.materials.new("druglord_m870")
muzzle_material = bpy.data.materials.new("druglord_m870_muzzle")
base.data.materials.clear()
base.data.materials.append(main_material)
base.data.materials.append(muzzle_material)

muzzle_limit = source_minimum.y + muzzle_section_length
muzzle_polygon_count = 0
for polygon in base.data.polygons:
    center_y = sum(
        base.data.vertices[index].co.y
        for index in polygon.vertices
    ) / len(polygon.vertices)
    if center_y <= muzzle_limit:
        polygon.material_index = 1
        muzzle_polygon_count += 1
    else:
        polygon.material_index = 0

if muzzle_polygon_count == 0:
    raise RuntimeError(
        "The M870 muzzle material did not receive any polygons."
    )

source_to_bannerlord = (
    Matrix.Translation((0.0, 0.0, vertical_offset))
    @ Matrix.Scale(scale, 4)
    @ Matrix.Translation(-grip_pivot)
)
base.data.transform(source_to_bannerlord)
base.data.update()
base.name = "druglord_m870"
base.data.name = "druglord_m870"

lod_ratios = (
    ("druglord_m870.lod1", 0.60),
    ("druglord_m870.lod2", 0.32),
    ("druglord_m870.lod3", 0.14),
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
collision.name = "bo_druglord_m870"
collision.data.name = "bo_druglord_m870"
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
