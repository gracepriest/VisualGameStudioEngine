# Builds the async stepping test programs (AsyncChain, SyncMain) used by
# async_stepout_client.js / dap_client.js.
#
#   powershell -File build_programs.ps1 [-BasicLangExe <path>]
#
# Output: programs\bin\<Name>\<Name>.exe (+ portable PDB mapping to the .bas
# via the #line directives in the generated C#).
param(
    [string]$BasicLangExe = (Join-Path $PSScriptRoot "..\..\..\bin\Release\net8.0\BasicLang.exe")
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

foreach ($name in @("AsyncChain", "SyncMain")) {
    Write-Host "=== $name ==="
    & $BasicLangExe compile "$name.bas" --target=csharp
    if ($LASTEXITCODE -ne 0) { throw "BasicLang compile failed for $name" }

    $proj = Join-Path $PSScriptRoot "obj_$name"
    New-Item -ItemType Directory -Force $proj | Out-Null
    Copy-Item "$name.cs" $proj -Force

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>$name</AssemblyName>
    <DebugType>portable</DebugType>
    <Optimize>false</Optimize>
    <Nullable>disable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$name.cs" />
  </ItemGroup>
</Project>
"@ | Out-File -Encoding utf8 (Join-Path $proj "$name.csproj")

    dotnet build (Join-Path $proj "$name.csproj") -c Debug -v q -o (Join-Path $PSScriptRoot "bin\$name")
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for $name" }

    Remove-Item -Recurse -Force $proj
    Remove-Item "$name.cs" -Force
}
Write-Host "Done. Programs in $PSScriptRoot\bin"
