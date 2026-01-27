# BasicLang Visual Studio Extension Build Script
# Run this script to build the VSIX and SDK NuGet package

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BasicLang Visual Studio Extension Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptDir

try {
    # Step 1: Build the SDK NuGet package
    Write-Host "[1/3] Building BasicLang.SDK NuGet package..." -ForegroundColor Yellow
    dotnet pack src/BasicLang.SDK/BasicLang.SDK.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "SDK build failed" }
    Write-Host "  SDK package built successfully" -ForegroundColor Green
    Write-Host ""

    # Step 2: Restore the VSIX project
    Write-Host "[2/3] Restoring BasicLang.VisualStudio project..." -ForegroundColor Yellow
    dotnet restore src/BasicLang.VisualStudio/BasicLang.VisualStudio.csproj
    if ($LASTEXITCODE -ne 0) { throw "Restore failed" }
    Write-Host "  Restore completed" -ForegroundColor Green
    Write-Host ""

    # Step 3: Build the VSIX project
    Write-Host "[3/3] Building BasicLang.VisualStudio VSIX..." -ForegroundColor Yellow

    # Try MSBuild first (required for VSIX projects)
    $msbuildPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1

    if ($msbuildPath -and (Test-Path $msbuildPath)) {
        Write-Host "  Using MSBuild: $msbuildPath" -ForegroundColor Gray
        & $msbuildPath src/BasicLang.VisualStudio/BasicLang.VisualStudio.csproj /p:Configuration=$Configuration /v:minimal
        if ($LASTEXITCODE -ne 0) { throw "VSIX build failed" }
    } else {
        # Fall back to dotnet build
        Write-Host "  MSBuild not found, using dotnet build..." -ForegroundColor Gray
        dotnet build src/BasicLang.VisualStudio/BasicLang.VisualStudio.csproj -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "VSIX build failed" }
    }
    Write-Host "  VSIX built successfully" -ForegroundColor Green
    Write-Host ""

    # Summary
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Build Completed Successfully!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Output files:" -ForegroundColor White
    Write-Host "  SDK:  src/BasicLang.SDK/bin/$Configuration/*.nupkg" -ForegroundColor Gray
    Write-Host "  VSIX: src/BasicLang.VisualStudio/bin/$Configuration/*.vsix" -ForegroundColor Gray
    Write-Host ""
    Write-Host "To install the VSIX:" -ForegroundColor Yellow
    Write-Host "  1. Close Visual Studio" -ForegroundColor Gray
    Write-Host "  2. Double-click the .vsix file" -ForegroundColor Gray
    Write-Host "  3. Restart Visual Studio" -ForegroundColor Gray
    Write-Host ""

} catch {
    Write-Host "Build failed: $_" -ForegroundColor Red
    exit 1
} finally {
    Pop-Location
}
