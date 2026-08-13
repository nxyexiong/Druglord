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

## Project layout

- `src\Druglord\SubModule.cs`: C# module entry point
- `src\Druglord\_Module\SubModule.xml`: Bannerlord module manifest
- `artifacts\Druglord`: generated installable module

The project currently targets Bannerlord v1.4.8. Update the manifest dependency
versions when targeting another game release.
