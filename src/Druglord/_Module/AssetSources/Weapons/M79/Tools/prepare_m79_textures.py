import os
import sys

from PIL import Image, ImageOps


source_dir = sys.argv[1]
output_dir = sys.argv[2]


def load_texture(file_name, mode):
    path = os.path.join(source_dir, file_name)
    if not os.path.exists(path):
        raise FileNotFoundError(path)

    return Image.open(path).convert(mode)


texture_prefix = "topaint1309_uv_overlapped_bomb1_baked"
diffuse = load_texture(f"{texture_prefix}_Base.png", "RGBA")
normal = load_texture(f"{texture_prefix}_Norm.png", "RGBA")
metallic = load_texture(f"{texture_prefix}_Meta.png", "L")
roughness = load_texture("Roughness_scetchfab.png", "L")
ambient_occlusion = load_texture(f"{texture_prefix}_AO.png", "L")

texture_size = diffuse.size
for name, texture in (
    ("normal", normal),
    ("metallic", metallic),
    ("roughness", roughness),
    ("ambient occlusion", ambient_occlusion),
):
    if texture.size != texture_size:
        raise ValueError(
            f"The M79 {name} texture size {texture.size} does not "
            f"match the diffuse texture size {texture_size}."
        )

glossiness = ImageOps.invert(roughness)
alpha = Image.new("L", texture_size, 255)
specular = Image.merge(
    "RGBA",
    (metallic, glossiness, ambient_occlusion, alpha),
)

os.makedirs(output_dir, exist_ok=True)
for suffix, texture in (
    ("d", diffuse),
    ("n", normal),
    ("s", specular),
):
    output_name = f"druglord_m79_{suffix}"
    texture.save(os.path.join(output_dir, f"{output_name}.png"))
    texture.save(os.path.join(output_dir, f"{output_name}.tga"))
