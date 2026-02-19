$vsixPath = 'C:\Users\melvi\source\repos\VisualGameStudioEngine\VS.BasicLang\VS.BasicLang.vsix'
$tempDir = 'C:\Users\melvi\source\repos\VisualGameStudioEngine\VS.BasicLang\vsix_update_temp'

# Remove temp dir if exists
Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

# Extract VSIX
Copy-Item $vsixPath "$tempDir\temp.zip"
Expand-Archive -Path "$tempDir\temp.zip" -DestinationPath "$tempDir\extracted" -Force

# Copy updated pkgdef
Copy-Item 'C:\Users\melvi\source\repos\VisualGameStudioEngine\VS.BasicLang\BasicLang.pkgdef' "$tempDir\extracted\BasicLang.pkgdef" -Force

# Copy updated template
New-Item -ItemType Directory -Force -Path "$tempDir\extracted\ProjectTemplates\BasicLang" | Out-Null
Copy-Item 'C:\Users\melvi\source\repos\VisualGameStudioEngine\VS.BasicLang\ProjectTemplates\BasicLang\BasicLangConsoleApp.zip' "$tempDir\extracted\ProjectTemplates\BasicLang\BasicLangConsoleApp.zip" -Force

# Make sure ProjectTemplate asset is in manifest
$manifestPath = "$tempDir\extracted\extension.vsixmanifest"
$manifest = Get-Content $manifestPath -Raw
if ($manifest -notmatch 'Microsoft\.VisualStudio\.ProjectTemplate') {
    Write-Host 'Adding ProjectTemplate asset to manifest...'
    $manifest = $manifest -replace '(</Assets>)', '    <Asset Type="Microsoft.VisualStudio.ProjectTemplate" Path="ProjectTemplates" />`n  $1'
    Set-Content -Path $manifestPath -Value $manifest -NoNewline
}

# List contents
Write-Host 'Updated VSIX contents:'
Get-ChildItem -Recurse "$tempDir\extracted" -File | ForEach-Object { Write-Host $_.FullName }

# Repack
Remove-Item $vsixPath -Force
Compress-Archive -Path "$tempDir\extracted\*" -DestinationPath "$tempDir\new.zip"
Move-Item "$tempDir\new.zip" $vsixPath -Force

# Cleanup
Remove-Item -Recurse -Force $tempDir

Write-Host 'VSIX updated successfully!'
