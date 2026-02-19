# Check VS installation and extensions
$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
$vsDir = Join-Path $localAppData 'Microsoft\VisualStudio'

Write-Host "Looking in: $vsDir"

Get-ChildItem $vsDir -Filter '17*' | ForEach-Object {
    Write-Host "`nVS instance: $($_.Name)"
    $extPath = Join-Path $_.FullName 'Extensions'
    if (Test-Path $extPath) {
        Write-Host "Extensions path exists: $extPath"
        $found = Get-ChildItem $extPath -Recurse -Filter '*BasicLang*' -ErrorAction SilentlyContinue | Select-Object -First 10
        if ($found) {
            Write-Host "Found BasicLang extension files:"
            $found | ForEach-Object { Write-Host "  $($_.FullName)" }
        } else {
            Write-Host "No BasicLang extension found"
        }
    }
}

# Check VS 2022 template cache
$templateCache = Join-Path $localAppData 'Microsoft\VisualStudio\17.0_*\ProjectTemplatesCache'
Write-Host "`nTemplate cache locations:"
Get-ChildItem $vsDir -Filter '17*' | ForEach-Object {
    $cache = Join-Path $_.FullName 'ProjectTemplatesCache'
    if (Test-Path $cache) {
        Write-Host "  $cache"
        $basicLangTemplates = Get-ChildItem $cache -Recurse -Filter '*BasicLang*' -ErrorAction SilentlyContinue
        if ($basicLangTemplates) {
            Write-Host "  Found BasicLang templates in cache:"
            $basicLangTemplates | ForEach-Object { Write-Host "    $($_.FullName)" }
        }
    }
}
