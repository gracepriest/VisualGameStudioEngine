$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = $ScriptDir

# Find the VSIX - try project root first, then bin folders
$VsixPath = "$ProjectDir\VS.BasicLang.vsix"
if (-not (Test-Path $VsixPath)) {
    $VsixPath = "$ProjectDir\bin\Release\net48\VS.BasicLang.vsix"
}
if (-not (Test-Path $VsixPath)) {
    $VsixPath = "$ProjectDir\bin\Debug\net48\VS.BasicLang.vsix"
}

$TemplateSourceDir = "$ProjectDir\Templates\Projects\BasicLangConsoleApp"
$TemplateZipDir = "$ProjectDir\ProjectTemplates\BasicLang"
$TemplateZipPath = "$TemplateZipDir\BasicLangConsoleApp.zip"
$TempDir = "$ProjectDir\bin\vsix_inject_temp"

Write-Host "VSIX Path: $VsixPath"
Write-Host "Template Source: $TemplateSourceDir"

if (-not (Test-Path $VsixPath)) {
    Write-Host "ERROR: VSIX file not found at $VsixPath"
    exit 1
}

Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

# Rebuild template zip from source files
Write-Host "Rebuilding template zip from source..."
New-Item -ItemType Directory -Force -Path $TemplateZipDir | Out-Null
Remove-Item $TemplateZipPath -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$TemplateSourceDir\*" -DestinationPath $TemplateZipPath
Write-Host "Template zip created: $TemplateZipPath"

# Copy VSIX as zip
Copy-Item $VsixPath "$TempDir\temp.zip"

# Extract
Expand-Archive -Path "$TempDir\temp.zip" -DestinationPath "$TempDir\extracted" -Force

# Add template
New-Item -ItemType Directory -Force -Path "$TempDir\extracted\ProjectTemplates\BasicLang" | Out-Null
Copy-Item $TemplateZipPath "$TempDir\extracted\ProjectTemplates\BasicLang\"

# Update DLL and pkgdef from build output if available
$BuildOutputDir = "$ProjectDir\bin\Release\net48"
if (-not (Test-Path "$BuildOutputDir\VS.BasicLang.dll")) {
    $BuildOutputDir = "$ProjectDir\bin\Debug\net48"
}
if (Test-Path "$BuildOutputDir\VS.BasicLang.dll") {
    Write-Host "Updating DLL from build output: $BuildOutputDir"
    Copy-Item "$BuildOutputDir\VS.BasicLang.dll" "$TempDir\extracted\" -Force
}
if (Test-Path "$BuildOutputDir\BasicLang.pkgdef") {
    Write-Host "Updating pkgdef from build output"
    Copy-Item "$BuildOutputDir\BasicLang.pkgdef" "$TempDir\extracted\" -Force
} elseif (Test-Path "$ProjectDir\BasicLang.pkgdef") {
    Write-Host "Updating pkgdef from project directory"
    Copy-Item "$ProjectDir\BasicLang.pkgdef" "$TempDir\extracted\" -Force
}

# Update extension.vsixmanifest to include ProjectTemplate asset
$manifestPath = "$TempDir\extracted\extension.vsixmanifest"
$manifest = Get-Content $manifestPath -Raw

# Check if ProjectTemplate asset is missing and add it
if ($manifest -notmatch 'Microsoft\.VisualStudio\.ProjectTemplate') {
    Write-Host 'Adding ProjectTemplate asset to manifest...'
    # Insert the ProjectTemplate asset before the closing </Assets> tag
    $manifest = $manifest -replace '(</Assets>)', '    <Asset Type="Microsoft.VisualStudio.ProjectTemplate" Path="ProjectTemplates" />
  $1'
    Set-Content -Path $manifestPath -Value $manifest -NoNewline
}

# Update [Content_Types].xml to include .zip content type
$contentTypesPath = "$TempDir\extracted\[Content_Types].xml"
$contentTypes = [System.IO.File]::ReadAllText($contentTypesPath)

# Check if .zip content type is missing and add it
if ($contentTypes -notmatch 'Extension="zip"') {
    Write-Host 'Adding .zip content type to [Content_Types].xml...'
    # Insert the zip content type before the closing </Types> tag
    $contentTypes = $contentTypes -replace '(</Types>)', '<Default Extension="zip" ContentType="application/zip" />$1'
    [System.IO.File]::WriteAllText($contentTypesPath, $contentTypes)
}

# List what's in extracted
Write-Host 'Contents after injection:'
Get-ChildItem -Recurse "$TempDir\extracted" | ForEach-Object { Write-Host $_.FullName }

# Repack
Remove-Item $VsixPath -Force
Compress-Archive -Path "$TempDir\extracted\*" -DestinationPath "$TempDir\new.zip"
Move-Item "$TempDir\new.zip" $VsixPath

# Cleanup
Remove-Item -Recurse -Force $TempDir

Write-Host 'Done! Template injected into VSIX.'
