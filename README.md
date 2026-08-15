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

## Build and deploy

```powershell
dotnet build .\Druglord.sln -c Debug
dotnet msbuild .\src\Druglord\Druglord.csproj /t:Deploy /p:Configuration=Debug
```

The installable module is generated at `artifacts\Druglord`. The `Deploy` target
copies it to `<BannerlordGameDir>\Modules\Druglord`.

## Firearms

The module currently adds:

- `druglord_prototype_handgun`: `Pistol` weapon class
- `druglord_akm`: `Musket` weapon class
- `druglord_awp`: `Musket` weapon class
- `druglord_cartridge`: shared firearm ammunition

All three weapons temporarily use the light-crossbow animations. The handgun uses
`crossbow_a`, while the rifles use their imported meshes. Cartridges use the
vanilla sling-ammo projectile. Gunshots produce a vanilla smoke burst and
per-rifle custom sounds through `FirearmMissionLogic`.

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

The main menu also includes **Druglord Debug Battle**. It launches a small land
custom battle where every human agent on both sides receives a configured
debug rifle and reserve cartridges, including reinforcements. The player
commander receives both a loaded AKM and loaded AWP with reserve cartridges.
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

Enable Bannerlord cheat mode to locate the mod items immediately in the
inventory, or wait for merchandise refreshes to add them to shops.

## Project layout

- `src\Druglord\SubModule.cs`: C# module entry point
- `src\Druglord\DebugBattleLauncher.cs`: debug custom-battle startup
- `src\Druglord\DebugFirearmLoadoutMissionLogic.cs`: configurable firearm test loadouts
- `src\Druglord\RifleControlMissionLogic.cs`: shared rifle controls
- `src\Druglord\RifleSettings.cs`: per-rifle XML settings loader
- `src\Druglord\FirearmMissionLogic.cs`: firearm shot effects and sound handling
- `src\Druglord\_Module\ModuleData\druglord_items.xml`: firearm items
- `src\Druglord\_Module\ModuleData\druglord_rifles.xml`: per-rifle behavior
- `src\Druglord\_Module\SubModule.xml`: Bannerlord module manifest
- `artifacts\Druglord`: generated installable module

The project currently targets Bannerlord v1.4.8. Update the manifest dependency
versions when targeting another game release.
