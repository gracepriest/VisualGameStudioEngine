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
