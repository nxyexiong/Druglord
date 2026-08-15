GRID_SIZE = 4
TILE_SIZE = 1024
PADDING = 8

MATERIALS = (
    ("aiStandardSurface4SG", "achterkant"),
    ("aiStandardSurface7SG", "hendel"),
    ("aiStandardSurface8SG", "scope"),
    ("aiStandardSurface6SG", "bovenkantje"),
    ("aiStandardSurface10SG", "barrel"),
    ("aiStandardSurface11SG", "standaard"),
    ("aiStandardSurface5SG", "middenstuk"),
    ("aiStandardSurface1SG", "ringen_achter"),
    ("aiStandardSurface9SG", "magazijn"),
    ("aiStandardSurface3SG", "achter_bovenkant"),
)

MATERIAL_TILE_BY_SOURCE = {
    source_name: index
    for index, (source_name, _) in enumerate(MATERIALS)
}
