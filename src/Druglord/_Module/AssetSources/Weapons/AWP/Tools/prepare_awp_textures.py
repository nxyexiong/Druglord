import os
import sys

from PIL import Image

from awp_materials import GRID_SIZE, MATERIALS, PADDING, TILE_SIZE


source_dir = sys.argv[1]
output_dir = sys.argv[2]

atlas_size = GRID_SIZE * TILE_SIZE
content_size = TILE_SIZE - (PADDING * 2)
resampling = Image.Resampling.LANCZOS


def load_texture(material_name, suffix, fallback):
    path = os.path.join(
        source_dir,
        f"AWP_export_{material_name}_{suffix}.png",
    )
    if not os.path.exists(path):
        return Image.new("RGBA", (TILE_SIZE, TILE_SIZE), fallback)

    return Image.open(path).convert("RGBA")


def paste_with_gutter(atlas, image, column, row):
    image = image.resize((content_size, content_size), resampling)
    cell_x = column * TILE_SIZE
    cell_y = row * TILE_SIZE
    left = cell_x + PADDING
    top = cell_y + PADDING
    right = left + content_size
    bottom = top + content_size

    atlas.paste(image, (left, top))
    atlas.paste(
        image.crop((0, 0, 1, content_size)).resize(
            (PADDING, content_size)
        ),
        (cell_x, top),
    )
    atlas.paste(
        image.crop(
            (content_size - 1, 0, content_size, content_size)
        ).resize((PADDING, content_size)),
        (right, top),
    )
    atlas.paste(
        image.crop((0, 0, content_size, 1)).resize(
            (content_size, PADDING)
        ),
        (left, cell_y),
    )
    atlas.paste(
        image.crop(
            (0, content_size - 1, content_size, content_size)
        ).resize((content_size, PADDING)),
        (left, bottom),
    )

    corners = (
        ((0, 0, 1, 1), (cell_x, cell_y)),
        (
            (content_size - 1, 0, content_size, 1),
            (right, cell_y),
        ),
        (
            (0, content_size - 1, 1, content_size),
            (cell_x, bottom),
        ),
        (
            (
                content_size - 1,
                content_size - 1,
                content_size,
                content_size,
            ),
            (right, bottom),
        ),
    )
    for crop_box, position in corners:
        atlas.paste(
            image.crop(crop_box).resize((PADDING, PADDING)),
            position,
        )


def build_specular(material_name):
    metallic = load_texture(
        material_name,
        "Metallic",
        (0, 0, 0, 255),
    ).convert("L")
    roughness = load_texture(
        material_name,
        "Roughness",
        (128, 128, 128, 255),
    ).convert("L")
    glossiness = roughness.point(lambda value: 255 - value)
    ambient_occlusion = Image.new("L", metallic.size, 255)
    alpha = Image.new("L", metallic.size, 255)
    return Image.merge(
        "RGBA",
        (metallic, glossiness, ambient_occlusion, alpha),
    )


atlases = {
    "d": Image.new(
        "RGBA",
        (atlas_size, atlas_size),
        (0, 0, 0, 255),
    ),
    "n": Image.new(
        "RGBA",
        (atlas_size, atlas_size),
        (128, 128, 255, 255),
    ),
    "s": Image.new(
        "RGBA",
        (atlas_size, atlas_size),
        (0, 127, 255, 255),
    ),
}

for index, (_, material_name) in enumerate(MATERIALS):
    column = index % GRID_SIZE
    row = index // GRID_SIZE
    textures = {
        "d": load_texture(
            material_name,
            "BaseColor",
            (255, 255, 255, 255),
        ),
        "n": load_texture(
            material_name,
            "Normal",
            (128, 128, 255, 255),
        ),
        "s": build_specular(material_name),
    }

    for suffix, texture in textures.items():
        paste_with_gutter(
            atlases[suffix],
            texture,
            column,
            row,
        )

os.makedirs(output_dir, exist_ok=True)
for suffix, atlas in atlases.items():
    output_name = f"druglord_awp_{suffix}"
    atlas.save(os.path.join(output_dir, f"{output_name}.png"))
    atlas.save(os.path.join(output_dir, f"{output_name}.tga"))
