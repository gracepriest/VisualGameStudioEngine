# Reinstall VS.BasicLang extension
# Run this with VS closed!

Write-Host "This will remove old BasicLang extensions and prepare for reinstall."
Write-Host "Make sure Visual Studio is COMPLETELY CLOSED before running this script!"
Write-Host ""
Read-Host "Press Enter to continue (or Ctrl+C to cancel)"

$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
$vsDir = Join-Path $localAppData 'Microsoft\VisualStudio'

# Find and remove old BasicLang extensions
Get-ChildItem $vsDir -Filter '17.0_*' | ForEach-Object {
    $extPath = Join-Path $_.FullName 'Extensions'
    if (Test-Path $extPath) {
        $basicLangDirs = Get-ChildItem $extPath -Recurse -Directory | Where-Object {
            (Test-Path (Join-Path $_.FullName 'BasicLang.pkgdef')) -or
            (Test-Path (Join-Path $_.FullName 'VS.BasicLang.dll'))
        }
        foreach ($dir in $basicLangDirs) {
            Write-Host "Removing: $($dir.FullName)"
            Remove-Item $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }

        # Also clean extensions.manifest cache
        $manifestCache = Join-Path $_.FullName 'extensions.manifest'
        if (Test-Path $manifestCache) {
            Write-Host "Removing extension manifest cache: $manifestCache"
            Remove-Item $manifestCache -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host ""
Write-Host "Old extensions removed."
Write-Host ""
Write-Host "Now double-click the VSIX file to install:"
Write-Host "  $PSScriptRoot\VS.BasicLang.vsix"
Write-Host ""

# Open the folder
Start-Process explorer.exe $PSScriptRoot
