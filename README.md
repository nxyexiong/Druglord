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

## Firearm prototype

The module currently adds:

- `druglord_prototype_handgun`: `Pistol` weapon class
- `druglord_prototype_rifle`: `Musket` weapon class
- `druglord_cartridge`: shared `Cartridge` ammunition

Both weapons temporarily use the light-crossbow animations and `crossbow_a`
mesh. Cartridges use the vanilla sling-ammo projectile. Gunshots produce a
vanilla smoke burst and placeholder siege sounds through
`FirearmMissionLogic`.

The main menu also includes **Druglord Debug Battle**. It launches a small land
custom battle where every human agent on both sides receives a loaded rifle
and reserve cartridges, including reinforcements. Passing
`DruglordDebugBattle` on Bannerlord's command line triggers the same debug flow
automatically.

The rifle is fully automatic, has a 30-round magazine, and reloads automatically
only after the magazine reaches zero. Hold the right mouse button to raise the
rifle and keep it ready without changing the camera FOV. Pressing fire while
lowered raises the rifle before shooting. Sustained fire kicks the view upward
and adds random dispersion, reaching maximum recoil on the tenth consecutive
shot. Rifle input is hooked with Harmony.

Enable Bannerlord cheat mode to locate the prototype items immediately in the
inventory, or wait for merchandise refreshes to add them to shops.

## Project layout

- `src\Druglord\SubModule.cs`: C# module entry point
- `src\Druglord\DebugBattleLauncher.cs`: debug custom-battle startup
- `src\Druglord\DebugFirearmLoadoutMissionLogic.cs`: all-agent test loadouts
- `src\Druglord\FirearmMissionLogic.cs`: firearm shot effects and sound handling
- `src\Druglord\_Module\ModuleData\druglord_items.xml`: prototype firearm items
- `src\Druglord\_Module\SubModule.xml`: Bannerlord module manifest
- `artifacts\Druglord`: generated installable module

The project currently targets Bannerlord v1.4.8. Update the manifest dependency
versions when targeting another game release.
