Add-Type -AssemblyName WindowsBase
$vsixPath = 'C:\Users\melvi\source\repos\VisualGameStudioEngine\VS.BasicLang\VS.BasicLang.vsix'

try {
    $pkg = [System.IO.Packaging.Package]::Open($vsixPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read)
    Write-Host "Package opened successfully"
    Write-Host "Parts in package:"
    $pkg.GetParts() | ForEach-Object { Write-Host "  $($_.Uri)" }
    $pkg.Close()
    Write-Host "Package is valid OPC format"
} catch {
    Write-Host "Error: $($_.Exception.Message)"
}
