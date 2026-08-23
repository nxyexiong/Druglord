# Bannerlord Raw Model Import Instructions

Use this workflow when adding or reimporting a 3D model for Druglord. It is
based on the successful PPSh-41 import and the Bannerlord Editor failures
resolved on 2026-08-23.

## Non-negotiable rules

- Preserve the original model, textures, credit file, and license unchanged.
- Confirm the license permits modification and redistribution before committing
  the source files. Add `ATTRIBUTION.md` and `LICENSE.txt`.
- Make model and texture preparation reproducible with scripts. Do not rely on
  unrecorded Blender edits to generated files.
- Prefix every resource with `druglord_`.
- Never automate, inspect, or interact with Bannerlord Editor. Do not use
  screenshots, image recognition, UI Automation/UIA, accessibility APIs,
  simulated mouse or keyboard input, or window-control tools.
- For every Editor or Resource Browser phase, only give the user numbered
  manual instructions with absolute Windows paths, then wait for the user to
  report the result.
- Give the user absolute Windows paths for every manual Editor step.
- Development files belong in `Assets`, `AssetSources`, and
  `RuntimeDataCache`. Runtime packages must use the published client TPAC under
  `AssetPackages` and must not contain those development directories.

## Fixed project locations

```text
Repository:
C:\UserData\workspace\Druglord

Bannerlord:
C:\UserData\Steam\steamapps\common\Mount & Blade II Bannerlord

Installed development module:
C:\UserData\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Druglord

Blender:
C:\Program Files\Blender Foundation\Blender 5.2\blender.exe

Published client output:
C:\UserData\workspace\Druglord\artifacts\published\Druglord\AssetPackages

Repository runtime package:
C:\UserData\workspace\Druglord\src\Druglord\_Module\AssetPackages\pack0.tpac
```

## 1. Create the source layout

For an asset named `<AssetName>` with resource prefix `<resource>`, use:

```text
C:\UserData\workspace\Druglord\src\Druglord\_Module\AssetSources\Weapons\<AssetName>\
  ATTRIBUTION.md
  LICENSE.txt
  Original\
    <unaltered source model and textures>
  Tools\
    prepare_<resource>_model.py
    prepare_<resource>_textures.py
  Import\
    <resource>.fbx
    <resource>.report.json
    Textures\
      <resource>_d.png
      <resource>_d.tga
      <resource>_n.png
      <resource>_n.tga
      <resource>_s.png
      <resource>_s.tga
```

Also stage Editor-facing copies directly under:

```text
C:\UserData\workspace\Druglord\src\Druglord\_Module\AssetSources
```

Bannerlord development TPACs embed paths such as:

```text
$BASE/Modules/Druglord/AssetSources/<filename>
```

The matching source files must therefore exist at the installed module's root
`AssetSources` directory whenever those TPACs are active.

## 2. Inspect the raw bundle before converting it

Record and validate:

1. Model format, mesh count, units, axes, dimensions, and polygon count.
2. Whether every mesh has UV coordinates.
3. Source material names and material assignments.
4. Available base-color, normal, metallic, roughness, AO, height, and opacity
   maps.
5. Texture dimensions and color modes.
6. The desired in-game pivot, orientation, length, and vertical offset.

For Druglord firearms, compare against an existing working firearm:

- Use metric units.
- Keep Z vertical.
- Point the muzzle toward negative Y.
- Put the grip/trigger attachment point at the origin.
- Decide the final length in metres before generating the FBX.

Fail preparation when the source structure differs from what the script
expects. Do not silently accept missing UVs, materials, or textures.

## 3. Generate Bannerlord textures

Generate diffuse, normal, and packed specular textures in both PNG and TGA:

```text
<resource>_d
<resource>_n
<resource>_s
```

Use this packed specular layout:

| Channel | Value |
| --- | --- |
| R | Metallic |
| G | Glossiness (`255 - roughness`) |
| B | Ambient occlusion |
| A | Opaque white |

Require every input map to have the same dimensions. If a source has many
materials, create a padded texture atlas and remap the UVs in the Blender
preparation script. Gutters are required to prevent mip-map bleeding.

PPSh-41 command example:

```powershell
python `
  "C:\UserData\workspace\Druglord\src\Druglord\_Module\AssetSources\Weapons\PPSh41\Tools\prepare_ppsh41_textures.py" `
  "C:\UserData\workspace\Druglord\src\Druglord\_Module\AssetSources\Weapons\PPSh41\Original\textures" `
  "C:\UserData\workspace\Druglord\src\Druglord\_Module\AssetSources\Weapons\PPSh41\Import\Textures"
```

## 4. Generate the import-ready FBX

The Blender script must:

1. Start from factory settings.
2. Import the raw FBX or OBJ.
3. Validate mesh count, UVs, and expected source materials.
4. Apply source transforms and join meshes when appropriate.
5. Apply a deterministic pivot, scale, orientation, and offset.
6. Rename final materials to their `druglord_` resource names.
7. Create a dedicated muzzle/barrel material when gameplay code needs a
   reliable firing-point marker.
8. Generate `.lod1`, `.lod2`, and `.lod3` meshes.
9. Generate a simple collision mesh named `bo_<resource>`.
10. Export only generated objects as a metric FBX.
11. Write a JSON report containing transforms, bounds, dimensions, mesh
    statistics, material names, and collision information.

Recommended LOD starting ratios are 60%, 32%, and 14%. Review the result rather
than assuming decimation preserved important silhouettes.

PPSh-41 command example:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" `
  --background `
  --python "C:\UserData\workspace\Druglord\src\Druglord\_Module\AssetSources\Weapons\PPSh41\Tools\prepare_ppsh41_model.py" `
  -- `
  "C:\UserData\workspace\Druglord\src\Druglord\_Module\AssetSources\Weapons\PPSh41\Original\source\ppsh-41.fbx" `
  "C:\UserData\workspace\Druglord\src\Druglord\_Module\AssetSources\Weapons\PPSh41\Import\druglord_ppsh41.fbx"
```

Transform order matters. The successful PPSh workflow was:

1. Move the grip pivot to the origin.
2. Uniformly scale to the target length.
3. Rotate the source muzzle from negative X to Bannerlord negative Y.
4. Apply the requested vertical offset.

## 5. Validate without Bannerlord Editor

Before staging the asset:

- Reimport the generated FBX in headless Blender.
- Confirm exact mesh names, material slots, UVs, and collision name.
- Confirm dimensions and world-space bounds against the JSON report.
- Confirm LOD triangle counts decrease in order.
- Confirm the pivot, hand placement, muzzle direction, and vertical offset.
- Confirm the generated FBX and root Editor copy have matching SHA-256 hashes.
- For weapons, update XML `weapon_length` to the rounded centimetre length.

Do not proceed to Resource Browser until these checks pass.

## 6. Stage a development installation

Copy the generated FBX and TGA files to the repository root authoring folder,
for example:

```text
C:\UserData\workspace\Druglord\src\Druglord\_Module\AssetSources\druglord_ppsh41.fbx
C:\UserData\workspace\Druglord\src\Druglord\_Module\AssetSources\druglord_ppsh41_d.tga
C:\UserData\workspace\Druglord\src\Druglord\_Module\AssetSources\druglord_ppsh41_n.tga
C:\UserData\workspace\Druglord\src\Druglord\_Module\AssetSources\druglord_ppsh41_s.tga
```

Use the dedicated MSBuild Editor deploy target. It retains development
directories, restores legacy source aliases, restores published runtime
caches, mirrors the Editor binaries, and fails if any active TPAC source path
is missing:

```powershell
dotnet build `
  "C:\UserData\workspace\Druglord\src\Druglord\Druglord.csproj" `
  -c Debug `
  -t:DeployEditor `
  --nologo
```

Do not use `build.cmd` for this stage; it intentionally removes `Assets`,
`AssetSources`, and `RuntimeDataCache`.

Before the user launches the Editor, confirm:

- Every source path referenced by every active development TPAC exists.
- The newly generated files are present under the installed root
  `AssetSources`.
- Druglord still skips runtime Harmony patches when running from
  `Win64_Shipping_wEditor`.

## 7. Manual Resource Browser workflow

The user must perform these actions:

1. Launch Bannerlord Editor with the Druglord module enabled.
2. Open Resource Browser.
3. Import `<resource>_d.tga`, `<resource>_n.tga`, and `<resource>_s.tga`.
4. Create or update the main material and assign those three textures.
5. Create or update any dedicated muzzle/barrel material.
6. Import or reimport `<resource>.fbx`.
7. Verify the base mesh, LOD chain, `bo_` collision mesh, material
   assignments, scale, orientation, and pivot.
8. Save every changed development package.

For a geometry-only update, reimport the FBX into the existing mesh resource.
Do not recreate unchanged texture or material resources.

## 8. Recover safely from Editor asset crashes

### Missing source warnings

If the Editor reports a missing path under:

```text
$BASE/Modules/Druglord/AssetSources
```

stage the exact referenced source file at:

```text
C:\UserData\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Druglord\AssetSources
```

Do not rename the file or dismiss repeated warnings without restoring the
source.

### Native assertion while textures update

The PPSh import produced:

```text
rglIntrusive_ptr.h:151
Expression: px != nullptr
```

The log showed a texture compiling immediately before
`rglAsset_manager::signal_package_item_change`. The safe recovery was:

1. Have the user close Bannerlord Editor.
2. Back up and temporarily isolate the asset's material and geometry TPACs.
   Move them; do not delete them.
3. Leave only the asset's diffuse, normal, and specular texture TPACs active.
4. Restore any already generated texture cache for that asset.
5. Have the user launch the Editor and wait for Edit Mode so all missing
   texture caches compile without dependent materials loaded.
6. Have the user close the Editor.
7. Confirm the expected new `.rdc` cache files exist.
8. Restore the material, marker-material, and geometry TPACs.
9. Have the user relaunch the Editor, then reimport the FBX.

For PPSh-41, the dependency order was:

```text
Texture packages first:
druglord_ppsh41_d_tex.tpac
druglord_ppsh41_n_tex.tpac
druglord_ppsh41_s_tex.tpac

Dependent packages second:
druglord_ppsh41_mtl.tpac
druglord_ppsh41_muzzle_mtl.tpac
druglord_ppsh41_geo.tpac
```

Use timestamps and the Editor log to identify the relevant cache files. Do not
delete unrelated `RuntimeDataCache` files.

Bannerlord Editor logs are under:

```text
C:\ProgramData\Mount and Blade II Bannerlord\logs
```

## 9. Publish the runtime package

After the resources work in Resource Browser, the user must:

1. Select **File > Publish Module**.
2. Publish `Druglord` for **Client**.
3. Use this output directory:

```text
C:\UserData\workspace\Druglord\artifacts\published
```

After publishing:

1. Copy every changed development TPAC from the installed module's `Assets`
   directory back to:

```text
C:\UserData\workspace\Druglord\src\Druglord\_Module\Assets
```

2. Copy the combined published package from:

```text
C:\UserData\workspace\Druglord\artifacts\published\Druglord\AssetPackages\pack0.tpac
```

to:

```text
C:\UserData\workspace\Druglord\src\Druglord\_Module\AssetPackages\pack0.tpac
```

Verify the timestamp, size, SHA-256 hash, and expected resource names. A small
development TPAC is not a substitute for the combined published client
package. Never copy published `RuntimeDataCache` files into the source module.

## 10. Build and deploy the runtime module

Have the user close Bannerlord and Bannerlord Editor before replacing the
installed module. Then run:

```powershell
& "C:\UserData\workspace\Druglord\build.cmd" -debug
```

Deploy the generated ZIP cleanly to:

```text
C:\UserData\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules
```

Confirm:

- Installed files match the build artifact hashes.
- `AssetPackages\pack0.tpac` is the newly published package.
- The runtime module contains no `Assets`, `AssetSources`, or
  `RuntimeDataCache`.
- XML dimensions and resource names match the generated model.
- The asset renders correctly in game at the expected position and scale.
- Collision, LODs, materials, animations, sounds, and gameplay behavior work.

## Completion checklist

- [ ] License and attribution are committed.
- [ ] Original source files are preserved.
- [ ] Texture and model generation are scripted.
- [ ] Generated textures use the correct channel packing.
- [ ] FBX report and headless reimport checks pass.
- [ ] Root Editor source copies are staged.
- [ ] Development TPACs and caches load without warnings or assertions.
- [ ] The user verifies the asset in Resource Browser.
- [ ] Changed development TPACs are copied back into the repository.
- [ ] The combined client `pack0.tpac` is published and copied back.
- [ ] `build.cmd -debug` succeeds.
- [ ] The clean installed module matches the build output.
