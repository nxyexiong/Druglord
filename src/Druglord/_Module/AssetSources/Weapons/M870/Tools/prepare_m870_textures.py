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


texture_prefix = "shotgun_l_MainMat.006"
diffuse = load_texture(f"{texture_prefix}_BaseColor.png", "RGBA")
normal = load_texture(f"{texture_prefix}_Normal.png", "RGBA")
metallic = load_texture(f"{texture_prefix}_Metallic.png", "L")
roughness = load_texture(f"{texture_prefix}_Roughness.png", "L")
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
            f"The M870 {name} texture size {texture.size} does not "
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
    output_name = f"druglord_m870_{suffix}"
    texture.save(os.path.join(output_dir, f"{output_name}.png"))
    texture.save(os.path.join(output_dir, f"{output_name}.tga"))
