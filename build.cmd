@echo off
setlocal EnableExtensions

set "CONFIGURATION=Release"

if "%~1"=="" goto arguments_done
if /I "%~1"=="-debug" (
    if not "%~2"=="" goto usage
    set "CONFIGURATION=Debug"
    goto arguments_done
)
goto usage

:arguments_done
set "REPO_ROOT=%~dp0"
set "PROJECT=%REPO_ROOT%src\Druglord\Druglord.csproj"
set "ARTIFACTS_DIR=%REPO_ROOT%artifacts"
set "MODULE_DIR=%ARTIFACTS_DIR%\Druglord"
set "ASSET_PACKAGES_DIR=%MODULE_DIR%\AssetPackages"
set "PACKAGE_DIR=%ARTIFACTS_DIR%\packages"
set "PACKAGE_PATH=%PACKAGE_DIR%\Druglord-%CONFIGURATION%.zip"

where dotnet.exe >nul 2>&1
if errorlevel 1 (
    echo Error: dotnet.exe was not found on PATH.
    exit /b 1
)

if exist "%MODULE_DIR%" (
    echo Cleaning "%MODULE_DIR%"...
    rmdir /s /q "%MODULE_DIR%"
    if exist "%MODULE_DIR%" (
        echo Error: failed to clean "%MODULE_DIR%".
        exit /b 1
    )
)

if not exist "%PACKAGE_DIR%" mkdir "%PACKAGE_DIR%"
if errorlevel 1 (
    echo Error: failed to create "%PACKAGE_DIR%".
    exit /b 1
)

if exist "%PACKAGE_PATH%" del /q "%PACKAGE_PATH%"
if exist "%PACKAGE_PATH%" (
    echo Error: failed to remove "%PACKAGE_PATH%".
    exit /b 1
)

echo Building Druglord in %CONFIGURATION% configuration...
dotnet build "%PROJECT%" --configuration "%CONFIGURATION%" --nologo
if errorlevel 1 exit /b 1

if not exist "%MODULE_DIR%\SubModule.xml" (
    echo Error: build output is missing SubModule.xml.
    exit /b 1
)

if not exist "%MODULE_DIR%\bin\Win64_Shipping_Client\Druglord.dll" (
    echo Error: build output is missing Druglord.dll.
    exit /b 1
)

for %%D in (Assets AssetSources RuntimeDataCache) do (
    if exist "%MODULE_DIR%\%%D" (
        echo Removing development-only "%MODULE_DIR%\%%D"...
        rmdir /s /q "%MODULE_DIR%\%%D"
        if exist "%MODULE_DIR%\%%D" (
            echo Error: failed to remove development-only directory "%MODULE_DIR%\%%D".
            exit /b 1
        )
    )
)

if exist "%ASSET_PACKAGES_DIR%\.gitkeep" del /q "%ASSET_PACKAGES_DIR%\.gitkeep"

set "DRUGLORD_MODULE_DIR=%MODULE_DIR%"
set "DRUGLORD_PACKAGE_PATH=%PACKAGE_PATH%"

echo Packaging "%PACKAGE_PATH%"...
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ^
    "$ErrorActionPreference = 'Stop';" ^
    "Add-Type -AssemblyName System.IO.Compression;" ^
    "Add-Type -AssemblyName System.IO.Compression.FileSystem;" ^
    "$source = [System.IO.Path]::GetFullPath($env:DRUGLORD_MODULE_DIR);" ^
    "$package = [System.IO.Path]::GetFullPath($env:DRUGLORD_PACKAGE_PATH);" ^
    "$assetPackageDirectory = Join-Path $source 'AssetPackages';" ^
    "$assetPackages = @(Get-ChildItem -LiteralPath $assetPackageDirectory -Filter '*.tpac' -File -ErrorAction SilentlyContinue);" ^
    "if ($assetPackages.Count -eq 0) {" ^
    "    throw \"Build output is missing a published TPAC under '$assetPackageDirectory'.\";" ^
    "}" ^
    "$emptyAssetPackages = @($assetPackages | Where-Object { $_.Length -eq 0 });" ^
    "if ($emptyAssetPackages.Count -gt 0) {" ^
    "    throw \"Published asset package '$($emptyAssetPackages[0].FullName)' is empty.\";" ^
    "}" ^
    "$archive = [System.IO.Compression.ZipFile]::Open($package, [System.IO.Compression.ZipArchiveMode]::Create);" ^
    "try {" ^
    "    Get-ChildItem -LiteralPath $source -Recurse -File | ForEach-Object {" ^
    "        $relativePath = $_.FullName.Substring($source.Length + 1).Replace('\', '/');" ^
    "        $entryName = 'Druglord/' + $relativePath;" ^
    "        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null;" ^
    "    }" ^
    "} finally {" ^
    "    $archive.Dispose();" ^
    "}" ^
    "$reader = [System.IO.Compression.ZipFile]::OpenRead($package);" ^
    "try {" ^
    "    foreach ($entryName in @('Druglord/SubModule.xml', 'Druglord/bin/Win64_Shipping_Client/Druglord.dll')) {" ^
    "        if ($null -eq $reader.GetEntry($entryName)) {" ^
    "            throw \"Package is missing required entry '$entryName'.\";" ^
    "        }" ^
    "    }" ^
    "    $publishedAssetEntries = @($reader.Entries | Where-Object {" ^
    "        $_.FullName.StartsWith('Druglord/AssetPackages/', [System.StringComparison]::OrdinalIgnoreCase) -and" ^
    "        $_.FullName.EndsWith('.tpac', [System.StringComparison]::OrdinalIgnoreCase) -and" ^
    "        $_.Length -gt 0" ^
    "    });" ^
    "    if ($publishedAssetEntries.Count -eq 0) {" ^
    "        throw 'Package is missing a non-empty published TPAC under Druglord/AssetPackages.';" ^
    "    }" ^
    "    foreach ($entry in $reader.Entries) {" ^
    "        foreach ($prefix in @('Druglord/Assets/', 'Druglord/AssetSources/', 'Druglord/RuntimeDataCache/')) {" ^
    "            if ($entry.FullName.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {" ^
    "                throw \"Package contains development-only entry '$($entry.FullName)'.\";" ^
    "            }" ^
    "        }" ^
    "    }" ^
    "} finally {" ^
    "    $reader.Dispose();" ^
    "}"
if errorlevel 1 (
    if exist "%PACKAGE_PATH%" del /q "%PACKAGE_PATH%"
    exit /b 1
)

for %%I in ("%PACKAGE_PATH%") do (
    echo Built %%~fI
    echo Package size: %%~zI bytes
)
exit /b 0

:usage
echo Usage: build.cmd [-debug]
echo.
echo Builds Release by default. Pass -debug to build Debug.
exit /b 2
