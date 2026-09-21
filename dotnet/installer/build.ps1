param(
    # Defaults to <Version> in MobreadModernDock.csproj so the MSI, the exe's
    # file version and the in-app update check can never disagree.
    [string]$Version = ""
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
