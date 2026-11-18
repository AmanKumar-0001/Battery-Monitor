# Battery Monitor Build Script
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") {
    $env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
}

$repoRoot = $PSScriptRoot
Set-Location $repoRoot

Write-Host "==> Resolving Project Version..." -ForegroundColor Cyan
$projPath = Join-Path $repoRoot "src\BatteryMonitor\BatteryMonitor.csproj"
$version = (& dotnet msbuild $projPath -getProperty:Version)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($version)) {
    throw "Failed to extract Version property from project."
}
$version = $version.Trim()
Write-Host "Target Version: $version" -ForegroundColor Green

Write-Host "==> Compiling Solution (Warnings as Errors)..." -ForegroundColor Cyan
& dotnet build (Join-Path $repoRoot "BatteryMonitor.sln") -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with errors or warnings."
}

Write-Host "==> Running Test Suite..." -ForegroundColor Cyan
& dotnet test (Join-Path $repoRoot "BatteryMonitor.sln") -c Release --no-build
if ($LASTEXITCODE -ne 0) {
    throw "Test execution failed."
}

Write-Host "==> Preparing Distribution Directory..." -ForegroundColor Cyan
$distDir = Join-Path $repoRoot "dist"
if (Test-Path $distDir) {
    Remove-Item -Recurse -Force $distDir
}
New-Item -ItemType Directory -Path $distDir | Out-Null

$scPubDir = Join-Path $distDir "pub-selfcontained"
$fdPubDir = Join-Path $distDir "pub-frameworkdependent"

Write-Host "==> Publishing Self-Contained Single-File (win-x64)..." -ForegroundColor Cyan
& dotnet publish $projPath -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:PublishTrimmed=false `
    -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded `
    -o $scPubDir
if ($LASTEXITCODE -ne 0) {
    throw "Self-contained publish failed."
}

Write-Host "==> Publishing Framework-Dependent Single-File (win-x64)..." -ForegroundColor Cyan
& dotnet publish $projPath -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=embedded `
    -o $fdPubDir
if ($LASTEXITCODE -ne 0) {
    throw "Framework-dependent publish failed."
}

# Copy targets
$scExe = Join-Path $distDir "BatteryMonitor-$version-win-x64-portable.exe"
$fdExe = Join-Path $distDir "BatteryMonitor-$version-win-x64-lightweight.exe"

Copy-Item (Join-Path $scPubDir "BatteryMonitor.exe") $scExe
Copy-Item (Join-Path $fdPubDir "BatteryMonitor.exe") $fdExe

# Package Zip files
Write-Host "==> Packaging Release Archives..." -ForegroundColor Cyan
$readmeFile = Join-Path $repoRoot "README.md"
$licenseFile = Join-Path $repoRoot "LICENSE"

$scZip = Join-Path $distDir "BatteryMonitor-$version-win-x64-portable.zip"
$fdZip = Join-Path $distDir "BatteryMonitor-$version-win-x64-lightweight.zip"

Compress-Archive -Path $scExe, $readmeFile, $licenseFile -DestinationPath $scZip
Compress-Archive -Path $fdExe, $readmeFile, $licenseFile -DestinationPath $fdZip

# Compute SHA256 Hashes
Write-Host "==> Generating SHA256SUMS.txt..." -ForegroundColor Cyan
$hashScExe = (Get-FileHash -Path $scExe -Algorithm SHA256).Hash
$hashFdExe = (Get-FileHash -Path $fdExe -Algorithm SHA256).Hash
$hashScZip = (Get-FileHash -Path $scZip -Algorithm SHA256).Hash
$hashFdZip = (Get-FileHash -Path $fdZip -Algorithm SHA256).Hash

$checksumLines = @(
    "$hashScExe  BatteryMonitor-$version-win-x64-portable.exe",
    "$hashFdExe  BatteryMonitor-$version-win-x64-lightweight.exe",
    "$hashScZip  BatteryMonitor-$version-win-x64-portable.zip",
    "$hashFdZip  BatteryMonitor-$version-win-x64-lightweight.zip"
)
Set-Content -Path (Join-Path $distDir "SHA256SUMS.txt") -Value ($checksumLines -join "`n")

$scSizeMB = [math]::Round(((Get-Item $scExe).Length / 1MB), 2)
$fdSizeMB = [math]::Round(((Get-Item $fdExe).Length / 1MB), 2)

Write-Host "--------------------------------------------------------" -ForegroundColor Gray
Write-Host "BUILD COMPLETED SUCCESSFULLY" -ForegroundColor Green
Write-Host "Portable (Self-Contained): $scSizeMB MB | SHA256: $hashScExe"
Write-Host "Lightweight (Framework):   $fdSizeMB MB | SHA256: $hashFdExe"
Write-Host "Checksum file generated at dist\SHA256SUMS.txt"
Write-Host "--------------------------------------------------------" -ForegroundColor Gray
