param(
    # Defaults to <Version> in MobreadModernDock.csproj so the MSI, the exe's
    # file version and the in-app update check can never disagree.
    [string]$Version = "",
    # Skip the MSI (WiX not installed, or you only want the portable ZIP).
    [switch]$NoMsi,
    # Skip the portable ZIP.
    [switch]$NoPortable
)

$ErrorActionPreference = "Stop"

$installerDir = $PSScriptRoot
$solutionDir = Split-Path $PSScriptRoot -Parent
$csproj = "$solutionDir\src\MobreadModernDock\MobreadModernDock.csproj"

if (-not $Version) {
    $Version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { throw "No <Version> in $csproj and none passed with -Version." }
}

Write-Host "Publishing MobreadModernDock $Version (Release, win-x64, self-contained single-file)..."
if (Test-Path "$installerDir\publish") { Remove-Item "$installerDir\publish" -Recurse -Force }
dotnet publish $csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version `
    -o "$installerDir\publish"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# WiX v6 (MIT). v7 requires the paid OSMF EULA — don't upgrade the tool.
if (-not $NoMsi) {
    if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
        throw "wix CLI not found. Install with: dotnet tool install --global wix --version 6.0.2"
    }

    Write-Host "Building MSI $Version..."
    $msi = Join-Path $installerDir "MobreadModernDock-$Version-x64.msi"
    Push-Location $installerDir
    try {
        wix build "Installer.wxs" -o $msi -arch x64 `
            -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext -d "ProductVersion=$Version"
        if ($LASTEXITCODE -ne 0) { throw "wix build failed." }
    }
    finally {
        Pop-Location
    }
    Write-Host "Built: $msi"
}

# Portable ZIP: the same single-file exe plus the portable.marker that makes
# AppDataLocator keep config.json and iconsCache beside the exe instead of in
# %APPDATA%. Unzip and run — no install, no admin, deletes cleanly.
if (-not $NoPortable) {
    Write-Host "Building portable ZIP $Version..."
    $staging = Join-Path $installerDir "portable-staging"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Path $staging | Out-Null

    Copy-Item "$installerDir\publish\MobreadModernDock.exe" $staging

    # Empty marker file — its presence is the whole signal.
    New-Item -ItemType File -Path (Join-Path $staging "portable.marker") | Out-Null

    # ASCII only, CRLF, no BOM: this is opened in Notepad more than anywhere
    # else, and PowerShell 5's "-Encoding UTF8" writes a BOM that shows up as
    # stray characters.
    @"
Mobread Modern Dock $Version - portable
=======================================

Run MobreadModernDock.exe. No installation required.

Because portable.marker sits next to the exe, all settings live in THIS
FOLDER (config.json, iconsCache) instead of %APPDATA%. Move the folder and
your setup travels with it; delete the folder and nothing is left behind.

To exit:      right-click the tray icon -> Exit
To uninstall: exit, then delete this folder.

Delete portable.marker if you would rather store settings in
%APPDATA%\MobreadModernDock (the same place the installer version uses).

https://github.com/mobread/MobreadModernDock
"@ | Set-Content -Path (Join-Path $staging "README.txt") -Encoding Ascii

    $zip = Join-Path $installerDir "MobreadModernDock-$Version-x64-portable.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path "$staging\*" -DestinationPath $zip -CompressionLevel Optimal
    Remove-Item $staging -Recurse -Force

    Write-Host "Built: $zip"
}
