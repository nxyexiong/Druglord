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
- `druglord_cartridge`: shared firearm ammunition

Both weapons temporarily use the light-crossbow animations. The handgun uses
`crossbow_a`, while the AKM uses the imported `druglord_akm` mesh. Cartridges
use the vanilla sling-ammo projectile. Gunshots produce a vanilla smoke burst
and placeholder siege sounds through `FirearmMissionLogic`.

An import-ready AKM model is included under
`src\Druglord\_Module\AssetSources\Weapons\AKM`. The original model is by
[Armored Wave](https://sketchfab.com/armoredwave) and is licensed under
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). See the attribution
file in that directory for modification details.

The main menu also includes **Druglord Debug Battle**. It launches a small land
custom battle where every human agent on both sides receives a loaded AKM
and reserve cartridges, including reinforcements. Passing
`DruglordDebugBattle` on Bannerlord's command line triggers the same debug flow
automatically.

The AKM is fully automatic, has a 30-round magazine, and reloads automatically
only after the magazine reaches zero. Hold the right mouse button to raise the
AKM and keep it ready without changing the camera FOV. Pressing fire while
lowered raises the AKM before shooting. Sustained fire kicks the view upward
and adds random dispersion, reaching maximum recoil on the tenth consecutive
shot. AKM input is hooked with Harmony.

Rifle behavior is data-driven through
`src\Druglord\_Module\ModuleData\druglord_rifles.xml`. Each rifle item has its
own ammunition, fire mode, magazine, timing, recoil, and spread values, so
additional rifles can use the same `RifleControlMissionLogic`. Rifles marked
`debug_loadout="true"` are distributed across debug-battle soldiers.

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
