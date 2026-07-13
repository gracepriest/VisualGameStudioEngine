<#
.SYNOPSIS
    Reusable N-theme screenshot capture harness for VisualGameStudio.exe.

.DESCRIPTION
    Codifies the "launch the IDE pre-set to a theme and screenshot it" technique used
    repeatedly during the Jul 2026 Tools-menu/Settings/theme audit and fix work
    (see MEMORY.md -> tools-menu-settings-audit.md and dock-drag-float-behavior.md).

    For EACH theme name in -Themes, in order:
      1. Backs up the user settings file (~/.vgs/settings.json) with
         [System.IO.File]::Copy plus a SHA-256 hash of the original bytes.
      2. Sets the "workbench.colorTheme" key in that file to the theme name
         (all other keys are preserved as-is).
      3. Launches -ExePath and waits for its main window to appear.
      4. Captures ONLY that window's client content with PrintWindow using the
         PW_RENDERFULLCONTENT flag (0x2) -- this works for GPU/Skia-composited
         Avalonia surfaces where a plain WM_PRINT or a screen-region grab would
         come back black or capture whatever happens to be on top. The capture
         path NEVER calls SetForegroundWindow, so it never steals focus from
         whatever the operator is doing on the desktop. Saves a PNG to -OutDir.
      5. Hard-kills ONLY the process this script launched, plus any `dotnet.exe
         ... BasicLang.dll --lsp` child process whose ParentProcessId equals
         that launched process's PID (the language-server child the IDE spawns
         via LanguageService) -- matched on BOTH command line and parent PID so
         an unrelated dotnet/LSP process is never touched.
      6. Restores the original settings.json from the step-1 backup and
         verifies its SHA-256 hash matches the pre-backup hash before deleting
         the backup file. If the file did not exist before the run, it is
         removed (not left behind) instead.

    Before EVERY launch (including the first), the script refuses to run if it
    finds a VisualGameStudio process already running that it did not itself
    start -- it prints the offending process(es) (Id/StartTime/Path) and exits
    with a non-zero code rather than risk killing or screenshotting someone
    else's live IDE session.

    PowerShell 5.1 compatible: no `&&`, no PS7-only operators, only cmdlets and
    .NET APIs available on Windows PowerShell 5.1.

.PARAMETER ExePath
    Full path to VisualGameStudio.exe to launch for each theme.

.PARAMETER Themes
    Theme names to capture, in order. Must match the exact strings
    ThemeManager.Apply() understands. Default: Dark, Light, High Contrast.

.PARAMETER OutDir
    Directory to write PNGs into (created if it does not exist). One file per
    theme: <OutDir>\<slug>.png, where slug is the theme name lowercased with
    runs of whitespace turned into single hyphens (e.g. "High Contrast" ->
    "high-contrast.png").

.PARAMETER SettleSeconds
    Seconds to sleep after the main window first appears, before capturing --
    gives startup theme-apply / layout restore / welcome-page render time to
    finish. Default 5.

.PARAMETER LaunchTimeoutSeconds
    Max seconds to wait for the launched process to present a main window
    before giving up on that theme. Default 30.

.EXAMPLE
    .\capture-theme-matrix.ps1 `
        -ExePath "C:\...\VisualGameStudio.Shell\bin\Release\net8.0\VisualGameStudio.exe" `
        -OutDir "C:\scratch\theme-matrix"

    Captures Dark, Light, and High Contrast (the defaults) to
    C:\scratch\theme-matrix\dark.png / light.png / high-contrast.png.

.EXAMPLE
    .\capture-theme-matrix.ps1 -ExePath $exe -Themes "Dark","Light" -OutDir out -SettleSeconds 8

    Captures only two themes with a longer settle time.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [string[]]$Themes = @('Dark', 'Light', 'High Contrast'),

    [Parameter(Mandatory = $true)]
    [string]$OutDir,

    [int]$SettleSeconds = 5,

    [int]$LaunchTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Win32 interop: PrintWindow capture, no focus-stealing calls anywhere here.
# ---------------------------------------------------------------------------
$win32TypeDef = @'
using System;
using System.Runtime.InteropServices;

public struct ThemeCaptureRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

public static class ThemeCaptureWin32
{
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out ThemeCaptureRect lpRect);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    // SW_RESTORE only un-minimizes; unlike SetForegroundWindow it does not
    // reorder Z-order or steal input focus from another application.
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
'@
Add-Type -TypeDefinition $win32TypeDef -ErrorAction Stop

$script:PW_RENDERFULLCONTENT = 0x2
$script:SW_RESTORE = 9

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.IO.File]::ReadAllBytes($Path)
        $hashBytes = $sha.ComputeHash($bytes)
        return [System.BitConverter]::ToString($hashBytes) -replace '-', ''
    }
    finally {
        $sha.Dispose()
    }
}

function Assert-NoPreexistingProcess {
    param([Parameter(Mandatory = $true)][string]$ExeBaseName)

    $existing = Get-Process -Name $ExeBaseName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "REFUSING TO RUN: found pre-existing '$ExeBaseName' process(es) this script did not start:" -ForegroundColor Red
        $existing | Select-Object Id, StartTime, Path | Format-Table -AutoSize | Out-String | Write-Host
        Write-Host "Close them (or let this script own the only instance) and re-run." -ForegroundColor Red
        exit 1
    }
}

function Backup-SettingsFile {
    param([Parameter(Mandatory = $true)][string]$SettingsPath)

    $existed = Test-Path -LiteralPath $SettingsPath
    $info = [PSCustomObject]@{
        SettingsPath = $SettingsPath
        Existed      = $existed
        BackupPath   = $null
        OriginalHash = $null
    }

    if ($existed) {
        $stamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff')
        $backupPath = "$SettingsPath.theme-matrix-backup-$stamp"
        [System.IO.File]::Copy($SettingsPath, $backupPath, $true)
        $info.BackupPath = $backupPath
        $info.OriginalHash = Get-FileSha256 -Path $SettingsPath
        Write-Host "Backed up settings.json -> $backupPath (sha256 $($info.OriginalHash))"
    }
    else {
        Write-Host "No existing settings.json at $SettingsPath -- will remove any created copy afterward."
    }

    return $info
}

function Restore-SettingsFile {
    param([Parameter(Mandatory = $true)]$BackupInfo)

    $path = $BackupInfo.SettingsPath

    if ($BackupInfo.Existed) {
        [System.IO.File]::Copy($BackupInfo.BackupPath, $path, $true)
        $restoredHash = Get-FileSha256 -Path $path
        if ($restoredHash -ne $BackupInfo.OriginalHash) {
            throw "Settings restore hash MISMATCH for $path. Original=$($BackupInfo.OriginalHash) Restored=$restoredHash. Backup preserved at $($BackupInfo.BackupPath) -- restore it manually."
        }
        [System.IO.File]::Delete($BackupInfo.BackupPath)
        Write-Host "Restored settings.json (sha256 verified: $restoredHash)" -ForegroundColor Green
    }
    elseif (Test-Path -LiteralPath $path) {
        [System.IO.File]::Delete($path)
        Write-Host "Removed transient settings.json (none existed before this run)." -ForegroundColor Green
    }
}

function Set-ThemeInSettingsFile {
    param(
        [Parameter(Mandatory = $true)][string]$SettingsPath,
        [Parameter(Mandatory = $true)][string]$Theme
    )

    if (Test-Path -LiteralPath $SettingsPath) {
        $raw = [System.IO.File]::ReadAllText($SettingsPath)
        if ([string]::IsNullOrWhiteSpace($raw)) {
            $obj = New-Object PSObject
        }
        else {
            $obj = $raw | ConvertFrom-Json
        }
    }
    else {
        $dir = [System.IO.Path]::GetDirectoryName($SettingsPath)
        if (-not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        $obj = New-Object PSObject
    }

    if ($obj.PSObject.Properties.Name -contains 'workbench.colorTheme') {
        $obj.'workbench.colorTheme' = $Theme
    }
    else {
        $obj | Add-Member -NotePropertyName 'workbench.colorTheme' -NotePropertyValue $Theme -Force
    }

    $json = $obj | ConvertTo-Json -Depth 20
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($SettingsPath, $json, $utf8NoBom)
    Write-Host "Set workbench.colorTheme = '$Theme' in $SettingsPath"
}

function Start-IdeProcess {
    param([Parameter(Mandatory = $true)][string]$ExePath)

    $proc = Start-Process -FilePath $ExePath -PassThru
    Write-Host "Launched PID $($proc.Id): $ExePath"
    return $proc
}

function Wait-ForMainWindow {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Process PID $($Process.Id) exited before showing a window (exit code $($Process.ExitCode))."
        }
        if ($Process.MainWindowHandle -ne [IntPtr]::Zero) {
            return $Process.MainWindowHandle
        }
    }

    throw "Timed out after $TimeoutSeconds s waiting for PID $($Process.Id) to show a main window."
}

function Save-WindowScreenshot {
    param(
        [Parameter(Mandatory = $true)][IntPtr]$Hwnd,
        [Parameter(Mandatory = $true)][string]$OutFile
    )

    if ([ThemeCaptureWin32]::IsIconic($Hwnd)) {
        # Un-minimize only; does not steal focus (unlike SetForegroundWindow).
        [void][ThemeCaptureWin32]::ShowWindow($Hwnd, $script:SW_RESTORE)
        Start-Sleep -Milliseconds 500
    }

    $rect = New-Object ThemeCaptureRect
    [void][ThemeCaptureWin32]::GetWindowRect($Hwnd, [ref]$rect)
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) {
        throw "Invalid window rect for handle $Hwnd (${width}x${height})."
    }

    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $hdc = $gfx.GetHdc()
        try {
            # PW_RENDERFULLCONTENT: required for GPU/Skia-composited (Avalonia)
            # surfaces -- a plain PrintWindow(0) call comes back blank for these.
            $ok = [ThemeCaptureWin32]::PrintWindow($Hwnd, $hdc, $script:PW_RENDERFULLCONTENT)
        }
        finally {
            $gfx.ReleaseHdc($hdc)
        }
        if (-not $ok) {
            throw "PrintWindow failed for handle $Hwnd."
        }
        $bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $gfx.Dispose()
        $bmp.Dispose()
    }

    Write-Host "Saved screenshot -> $OutFile" -ForegroundColor Green
}

function Stop-LaunchedProcessTree {
    param([Parameter(Mandatory = $true)][System.Diagnostics.Process]$RootProcess)

    $rootPid = $RootProcess.Id

    # Find the LSP child (LanguageService spawns `dotnet <path>\BasicLang.dll --lsp`)
    # matched on BOTH parent PID and command line so an unrelated dotnet/LSP
    # process anywhere else on the machine is never touched.
    $candidates = Get-CimInstance -ClassName Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue
    foreach ($c in $candidates) {
        if ($c.ParentProcessId -eq $rootPid -and
            $c.CommandLine -and
            $c.CommandLine -match 'BasicLang\.dll' -and
            $c.CommandLine -match '--lsp') {
            try {
                Stop-Process -Id $c.ProcessId -Force -ErrorAction Stop
                # Block until the OS actually reaps it -- Stop-Process returns as soon as
                # termination is *requested*, not once the process table entry is gone, and
                # the very next loop iteration's Assert-NoPreexistingProcess check would
                # otherwise sometimes still see it.
                try { Wait-Process -Id $c.ProcessId -Timeout 10 -ErrorAction SilentlyContinue } catch { }
                Write-Host "Killed LSP child PID $($c.ProcessId) (parent $rootPid)"
            }
            catch {
                Write-Warning "Failed to stop LSP child PID $($c.ProcessId): $_"
            }
        }
    }

    $RootProcess.Refresh()
    if (-not $RootProcess.HasExited) {
        try {
            Stop-Process -Id $rootPid -Force -ErrorAction Stop
            # Same reasoning as above: wait for the real exit, not just the kill request.
            [void]$RootProcess.WaitForExit(10000)
            Write-Host "Killed launched PID $rootPid"
        }
        catch {
            Write-Warning "Failed to stop launched PID ${rootPid}: $_"
        }
    }
}

function Get-ThemeSlug {
    param([Parameter(Mandatory = $true)][string]$Theme)
    return ($Theme.ToLowerInvariant() -replace '\s+', '-')
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "ExePath not found: $ExePath"
}
if (-not $Themes -or $Themes.Count -eq 0) {
    throw "-Themes must contain at least one theme name."
}

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}

$settingsPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.vgs\settings.json'
$exeBaseName = [System.IO.Path]::GetFileNameWithoutExtension($ExePath)

$results = @()

foreach ($theme in $Themes) {
    Write-Host ''
    Write-Host "=== Theme: $theme ===" -ForegroundColor Cyan

    Assert-NoPreexistingProcess -ExeBaseName $exeBaseName

    $backupInfo = Backup-SettingsFile -SettingsPath $settingsPath
    $proc = $null
    $themeResult = [PSCustomObject]@{
        Theme   = $theme
        Success = $false
        OutFile = $null
        Error   = $null
    }

    try {
        Set-ThemeInSettingsFile -SettingsPath $settingsPath -Theme $theme

        $proc = Start-IdeProcess -ExePath $ExePath
        $hwnd = Wait-ForMainWindow -Process $proc -TimeoutSeconds $LaunchTimeoutSeconds
        Start-Sleep -Seconds $SettleSeconds

        $slug = Get-ThemeSlug -Theme $theme
        $outFile = Join-Path $OutDir "$slug.png"
        Save-WindowScreenshot -Hwnd $hwnd -OutFile $outFile

        $themeResult.Success = $true
        $themeResult.OutFile = $outFile
    }
    catch {
        $themeResult.Error = $_.Exception.Message
        Write-Warning "Theme '$theme' failed: $($_.Exception.Message)"
    }
    finally {
        if ($proc) {
            Stop-LaunchedProcessTree -RootProcess $proc
        }
        Restore-SettingsFile -BackupInfo $backupInfo
    }

    $results += $themeResult
}

Write-Host ''
Write-Host '=== Summary ===' -ForegroundColor Cyan
$results | Format-Table -AutoSize

$failures = $results | Where-Object { -not $_.Success }
if ($failures) {
    exit 1
}
exit 0
