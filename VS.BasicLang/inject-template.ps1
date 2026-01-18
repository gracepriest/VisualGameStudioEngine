$VsixPath = 'C:\Users\melvi\source\repos\VisualGameStudioEngine\VS.BasicLang\bin\Release\net48\VS.BasicLang.vsix'
$TemplateZipPath = 'C:\Users\melvi\source\repos\VisualGameStudioEngine\VS.BasicLang\ProjectTemplates\BasicLang\BasicLangConsoleApp.zip'
$TempDir = 'C:\Users\melvi\source\repos\VisualGameStudioEngine\VS.BasicLang\bin\Release\net48\vsix_inject_temp'

Remove-Item -Recurse -Force $TempDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

# Copy VSIX as zip
Copy-Item $VsixPath "$TempDir\temp.zip"

# Extract
Expand-Archive -Path "$TempDir\temp.zip" -DestinationPath "$TempDir\extracted" -Force

# Add template
New-Item -ItemType Directory -Force -Path "$TempDir\extracted\ProjectTemplates\BasicLang" | Out-Null
Copy-Item $TemplateZipPath "$TempDir\extracted\ProjectTemplates\BasicLang\"

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
