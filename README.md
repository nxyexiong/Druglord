# Druglord

Mount & Blade II: Bannerlord single-player mod scaffold.

## Requirements

- .NET SDK
- .NET Framework 4.7.2 targeting pack
- Mount & Blade II: Bannerlord v1.4.8

The project compiles against the installed game assemblies. This machine is
configured by automatically detecting Steam through the Windows registry.

For a non-Steam installation or a secondary Steam library, copy
`Directory.Build.props.user.example` to `Directory.Build.props.user` and set
`BannerlordGameDir`, or set the `BANNERLORD_GAME_DIR` environment variable.

## Cold build and package

`build.cmd` is the canonical way to produce a distributable module. A cold
build does not depend on files left in Bannerlord's `Modules\Druglord`
directory or on previously generated files under `artifacts`.

Before building, configure `BannerlordGameDir` as described above and confirm
that the published client asset package exists:

```text
src\Druglord\_Module\AssetPackages\pack0.tpac
```

This TPAC is a required build input. It contains the runtime AKM and AWP
models, materials, and textures. The files under `Assets` and `AssetSources`
are editor inputs and are not substitutes for a published client package.

From the repository root, run:

```powershell
# Release package (default)
.\build.cmd

# Debug package, including the Druglord Debug Battle menu option
.\build.cmd -debug
```

The packages are written to:

```text
artifacts\packages\Druglord-Release.zip
artifacts\packages\Druglord-Debug.zip
```

Each ZIP has a single top-level `Druglord` directory. During every invocation,
`build.cmd`:

1. Deletes the previous `artifacts\Druglord` output and target ZIP.
2. Builds `src\Druglord\Druglord.csproj` in the selected configuration.
3. Removes the development-only `Assets`, `AssetSources`, and
   `RuntimeDataCache` directories from the runtime output.
4. Requires at least one non-empty TPAC under `AssetPackages`.
5. Creates the ZIP and verifies the module manifest, DLL, and published TPAC.
6. Rejects the package if it contains any development-only directories.

Do not distribute `artifacts\Druglord` directly or use the MSBuild `Deploy`
target to prepare a release. The project output initially includes authoring
files; `build.cmd` performs the filtering and package validation required for
a clean runtime installation.

### Publishing changed assets

This step is needed only after changing or reimporting models, materials, or
textures. Ordinary C# or XML changes can use the existing published TPAC.

1. Open the development module in the Bannerlord editor and verify the
   imported resources.
2. Select **File > Publish Module**.
3. Publish the `Druglord` module for **Client** to
   `artifacts\published`.
4. Copy the generated
   `artifacts\published\Druglord\AssetPackages\*.tpac` files to
   `src\Druglord\_Module\AssetPackages`, replacing the previous package.
5. Run `build.cmd` again.

Do not copy the published `RuntimeDataCache` into the source module. Do not
move development TPACs from `Assets` into `AssetPackages`; only the editor's
client publication is self-contained for runtime use.

### Clean package deployment

Close Bannerlord and its editor before replacing the installed module. Then
extract the ZIP into the game's `Modules` directory, not into an additional
`Druglord` subdirectory:

```powershell
$game = 'C:\path\to\Mount & Blade II Bannerlord'
$installedModule = Join-Path $game 'Modules\Druglord'

if (Test-Path -LiteralPath $installedModule) {
    Remove-Item -LiteralPath $installedModule -Recurse -Force
}

Expand-Archive `
    -LiteralPath .\artifacts\packages\Druglord-Release.zip `
    -DestinationPath (Join-Path $game 'Modules') `
    -Force
```

Removing the old module first is important: otherwise stale editor resources
can hide missing files in the package and make a non-reproducible build appear
to work.

## Outlaw party growth

Every active bandit party grows once per campaign day. Member and prisoner
growth are calculated independently as
`max(0, floor(-0.1 * current_size + 5))`.

New members are randomly selected from the party's existing non-hero troop
types, weighted by each type's current count. New prisoners are peasants
matching the outlaw culture: looters use Empire peasants, while sea raiders,
mountain bandits, forest bandits, desert bandits, and steppe bandits use
Sturgian, Vlandian, Battanian, Aserai, and Khuzait peasants respectively.

## Firearms

The module currently adds:

- `druglord_akm`: `Musket` weapon class
- `druglord_awp`: `Musket` weapon class
- `druglord_ppsh41`: `Musket` weapon class
- `druglord_cartridge`: shared firearm ammunition

The firearms temporarily use the light-crossbow animations and their imported
meshes. Cartridges use the vanilla sling-ammo projectile. Gunshots produce a
vanilla smoke burst and per-rifle custom sounds through `FirearmMissionLogic`.

An import-ready AKM model is included under
`src\Druglord\_Module\AssetSources\Weapons\AKM`. The original model is by
[Armored Wave](https://sketchfab.com/armoredwave) and is licensed under
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). See the attribution
file in that directory for modification details.

The AWP model is by
[forestie](https://sketchfab.com/forestie), is licensed under
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), and is included
under `src\Druglord\_Module\AssetSources\Weapons\AWP`. See its attribution
file for modification details.

The PPSh-41 model is by Artem Goyko, is licensed under
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), and is included
under `src\Druglord\_Module\AssetSources\Weapons\PPSh41`. See its attribution
file in that directory for modification details.

The main menu also includes **Druglord Debug Battle**. It launches a small land
custom battle where every human agent on both sides receives a configured
debug rifle and reserve cartridges, including reinforcements. The player
commander receives the loaded AKM, AWP, and PPSh-41 with reserve cartridges.
Passing `DruglordDebugBattle` on Bannerlord's command line triggers the same
debug flow automatically.

The AKM is fully automatic, has a 30-round magazine, and reloads automatically
only after the magazine reaches zero. Hold the right mouse button to raise the
AKM and keep it ready without changing the camera FOV. Pressing fire while
lowered raises the AKM before shooting. Sustained fire kicks the view upward
and adds random dispersion, reaching maximum recoil on the tenth consecutive
shot. AKM input is hooked with Harmony.

The AWP is fully automatic at 0.5 rounds per second, has a five-round
magazine, fires at 900 m/s, and uses a large vertical camera kick with minimal
horizontal recoil and projectile spread.

The PPSh-41 is fully automatic at approximately 900 rounds per minute, uses a
71-round drum magazine, and fires at 488 m/s. It trades per-shot damage and
accuracy for close-range volume of fire.

Rifle behavior is data-driven through
`src\Druglord\_Module\ModuleData\druglord_rifles.xml`. Each rifle item has its
own ammunition, fire mode, magazine, timing, recoil, and spread values, so
additional rifles can use the same `RifleControlMissionLogic`. Rifles marked
`debug_loadout="true"` are distributed across debug-battle soldiers.
Each rifle also defines its muzzle point in model-local coordinates; firing
selects a configured material submesh and bounding-box face, transforms that
point through the live weapon `MetaMesh`, then fires from that exact barrel
position along Bannerlord's native character aim direction. Mounted shots
always fire, but horizontal direction is clamped to the same body-rotation
limits that separate the crosshair from the character's aim.

The campaign troop tree adds **Assault**, equipped with either an AKM or
PPSh-41, cartridges, a short sword, and Imperial Veteran Archer armor, and
**Sniper**, equipped with an AWP, cartridges, a short sword, and Imperial
Palatine Guard armor.
Every culture's male peasant keeps its normal recruit upgrade and also gains
the **Peasant > Assault > Sniper** firearm branch.

Enable Bannerlord cheat mode to locate the mod items immediately in the
inventory, or wait for merchandise refreshes to add them to shops.

## Project layout

- `src\Druglord\SubModule.cs`: C# module entry point
- `src\Druglord\DebugBattleLauncher.cs`: debug custom-battle startup
- `src\Druglord\DebugFirearmLoadoutMissionLogic.cs`: configurable firearm test loadouts
- `src\Druglord\RifleControlMissionLogic.cs`: shared rifle controls
- `src\Druglord\RifleSettings.cs`: per-rifle XML settings loader
- `src\Druglord\TroopUpgradeRegistry.cs`: peasant firearm upgrade branch
- `src\Druglord\OutlawPartyGrowthCampaignBehavior.cs`: daily outlaw growth
- `src\Druglord\FirearmMissionLogic.cs`: firearm shot effects and sound handling
- `src\Druglord\_Module\ModuleData\druglord_items.xml`: firearm items
- `src\Druglord\_Module\ModuleData\druglord_rifles.xml`: per-rifle behavior
- `src\Druglord\_Module\ModuleData\druglord_troops.xml`: firearm troops
- `src\Druglord\_Module\SubModule.xml`: Bannerlord module manifest
- `artifacts\Druglord`: generated installable module

The project currently targets Bannerlord v1.4.8. Update the manifest dependency
versions when targeting another game release.
