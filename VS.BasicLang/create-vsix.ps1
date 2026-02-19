param(
    [Parameter(Mandatory=$true)]
    [string]$BuildDir,

    [Parameter(Mandatory=$true)]
    [string]$OutputPath
)

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Remove existing VSIX if present
if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Force
}

$extensionId = "VS.BasicLang.95a8f3e1-1234-4567-8901-abcdef123456"
$version = "1.3.0"

# Create catalog.json (required for VSIX v3)
$catalog = @"
{"manifestVersion":"1.1","info":{"id":"$extensionId,version=$version","manifestType":"Extension"},"packages":[{"id":"Component.$extensionId","version":"$version","type":"Component","extension":true,"dependencies":{"$extensionId":"$version","Microsoft.VisualStudio.Component.CoreEditor":"[17.0,19.0)"},"localizedResources":[{"language":"en-US","title":"BasicLang Language Support","description":"Language support for BasicLang."}]},{"id":"$extensionId","version":"$version","type":"Vsix","payloads":[{"fileName":"VS.BasicLang.vsix","size":0}],"vsixId":"$extensionId","extensionDir":"[installdir]\\Common7\\IDE\\Extensions\\basiclang","installSizes":{"targetDrive":50000}}]}
"@
Set-Content -Path (Join-Path $BuildDir "catalog.json") -Value $catalog -Encoding UTF8 -NoNewline

# Create manifest.json (required for VSIX v3)
$manifest = @"
{"id":"$extensionId","version":"$version","type":"Vsix","vsixId":"$extensionId","extensionDir":"[installdir]\\Common7\\IDE\\Extensions\\basiclang","files":[{"fileName":"/extension.vsixmanifest","sha256":null},{"fileName":"/VS.BasicLang.dll","sha256":null}],"installSizes":{"targetDrive":50000},"dependencies":{"Microsoft.VisualStudio.Component.CoreEditor":"[17.0,19.0)"}}
"@
Set-Content -Path (Join-Path $BuildDir "manifest.json") -Value $manifest -Encoding UTF8 -NoNewline

# Create [Content_Types].xml
$contentTypes = @"
<?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="vsixmanifest" ContentType="text/xml" /><Default Extension="dll" ContentType="application/octet-stream" /><Default Extension="json" ContentType="application/json" /></Types>
"@
$utf8Bom = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllText((Join-Path $BuildDir "[Content_Types].xml"), $contentTypes, $utf8Bom)

# Create the VSIX (ZIP archive)
[System.IO.Compression.ZipFile]::CreateFromDirectory($BuildDir, $OutputPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host "Created VSIX: $OutputPath"
