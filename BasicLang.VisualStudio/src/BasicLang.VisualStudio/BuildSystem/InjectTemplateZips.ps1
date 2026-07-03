# Injects template zip files into the VSIX as proper OPC parts.
#
# Why this exists: VsixUtil.exe (VSSDK BuildTools 17.9) silently drops *.zip files
# located under ProjectTemplates/ or ItemTemplates/ when packaging, even when they
# are listed in the files.json manifest (it expects the createTemplateManifests
# pipeline to have expanded them). Raw System.IO.Compression appending (the old
# approach) produces zip entries that are invisible to OPC/System.IO.Packaging
# because [Content_Types].xml and manifest.json are not updated, so the VSIX
# installer never installs them.
#
# This script uses System.IO.Packaging so that:
#   - [Content_Types].xml gains a Default mapping for the "zip" extension
#   - the parts are real OPC parts visible to the VSIX installer
# and it appends the new part paths to the files array in manifest.json.
param(
    [Parameter(Mandatory = $true)][string]$VsixPath,
    [Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName WindowsBase

if (-not (Test-Path $VsixPath)) { throw "VSIX not found: $VsixPath" }

$zipFiles = New-Object System.Collections.ArrayList
foreach ($sub in 'ProjectTemplates', 'ItemTemplates') {
    $dir = Join-Path $OutDir (Join-Path $sub 'BasicLang')
    foreach ($file in (Get-ChildItem -Path (Join-Path $dir '*.zip') -File | Sort-Object Name)) {
        [void]$zipFiles.Add(@{ PartPath = '/' + $sub + '/BasicLang/' + $file.Name; FullName = $file.FullName })
    }
}
if ($zipFiles.Count -eq 0) { throw "No template zips found under $OutDir" }

$pkg = [System.IO.Packaging.Package]::Open($VsixPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite)
try {
    # Add each template zip as an OPC part (idempotent: replace if present).
    foreach ($zip in $zipFiles) {
        $relUri = New-Object System.Uri($zip.PartPath, [System.UriKind]::Relative)
        $partUri = [System.IO.Packaging.PackUriHelper]::CreatePartUri($relUri)
        if ($pkg.PartExists($partUri)) { $pkg.DeletePart($partUri) }
        $part = $pkg.CreatePart($partUri, 'application/zip', [System.IO.Packaging.CompressionOption]::Normal)
        $src = [System.IO.File]::OpenRead($zip.FullName)
        try {
            $dst = $part.GetStream()
            $src.CopyTo($dst)
            $dst.Close()
        }
        finally { $src.Close() }
    }

    # Register the new parts in manifest.json's files array (idempotent).
    $manUri = [System.IO.Packaging.PackUriHelper]::CreatePartUri((New-Object System.Uri('/manifest.json', [System.UriKind]::Relative)))
    $manPart = $pkg.GetPart($manUri)
    $reader = New-Object System.IO.StreamReader($manPart.GetStream([System.IO.FileMode]::Open, [System.IO.FileAccess]::Read))
    $json = $reader.ReadToEnd()
    $reader.Close()

    $newEntries = New-Object System.Collections.ArrayList
    foreach ($zip in $zipFiles) {
        if (-not $json.Contains('"' + $zip.PartPath + '"')) {
            [void]$newEntries.Add('{"fileName":"' + $zip.PartPath + '","sha256":null}')
        }
    }
    if ($newEntries.Count -gt 0) {
        $anchor = '],"installSizes"'
        if (-not $json.Contains($anchor)) { throw 'manifest.json anchor not found; cannot register template zips' }
        $json = $json.Replace($anchor, ',' + ($newEntries -join ',') + $anchor)
        $writer = New-Object System.IO.StreamWriter($manPart.GetStream([System.IO.FileMode]::Create, [System.IO.FileAccess]::Write))
        $writer.Write($json)
        $writer.Close()
    }
}
finally { $pkg.Close() }

Write-Host ('Injected ' + $zipFiles.Count + ' template zips into ' + $VsixPath + ' as OPC parts.')
