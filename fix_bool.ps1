$file = 'C:\Users\melvi\source\repos\VisualGameStudioEngine\RaylibWrapper\RaylibWrapper.vb'
$tempFile = 'C:\Users\melvi\source\repos\VisualGameStudioEngine\RaylibWrapper\RaylibWrapper_temp.vb'

$content = Get-Content $file -Raw

# Fix Boolean return types that don't already have MarshalAs
# Match ") As Boolean" not preceded by MarshalAs and followed by newline+End Function
$content = $content -replace '(\) As )Boolean(\r?\n\s+End Function)', '$1<MarshalAs(UnmanagedType.I1)> Boolean$2'

# Fix Boolean parameters that don't already have MarshalAs
# Match ", paramName As Boolean)" or "(paramName As Boolean)"
# Only if not already preceded by MarshalAs
$lines = $content -split "`r?`n"
$newLines = @()
foreach ($line in $lines) {
    # Skip if line already has proper marshalling for all Boolean params
    if ($line -match 'As Boolean' -and $line -notmatch '<MarshalAs\(UnmanagedType\.I1\)>') {
        # Add MarshalAs to Boolean parameters (at end of param list)
        $line = $line -replace '(\(\s*|\,\s*)(\w+)( As Boolean\))', '$1<MarshalAs(UnmanagedType.I1)> $2$3'
        # Add MarshalAs to Boolean parameters (in middle of param list)
        $line = $line -replace '(\(\s*|\,\s*)(\w+)( As Boolean\,)', '$1<MarshalAs(UnmanagedType.I1)> $2$3'
        # Add MarshalAs to Boolean return (at end of line)
        $line = $line -replace '(\) As )Boolean$', '$1<MarshalAs(UnmanagedType.I1)> Boolean'
    }
    $newLines += $line
}

$content = $newLines -join "`r`n"

# Write to temp file first
Set-Content -Path $tempFile -Value $content
# Then copy to real file
Copy-Item -Path $tempFile -Destination $file -Force
# Clean up temp
Remove-Item -Path $tempFile -Force

Write-Host "Done - fixed Boolean marshalling"
