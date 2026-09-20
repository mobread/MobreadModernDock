param(
    [string]$Version = "1.2.0"
)

$ErrorActionPreference = "Stop"

$installerDir = $PSScriptRoot
$solutionDir = Split-Path $PSScriptRoot -Parent

Write-Host "Publishing MobreadModernDock (Release, win-x64, self-contained single-file)..."
dotnet publish "$solutionDir\src\MobreadModernDock\MobreadModernDock.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
    -o "$installerDir\publish"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

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
