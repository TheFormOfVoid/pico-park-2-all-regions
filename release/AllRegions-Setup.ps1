# All Regions for PICO PARK 2: install, turn mods off and on, or uninstall.
# Run it with Setup.bat next to it. It only touches the game's folder.

param([string]$Action = "", [string]$GameDir = "")

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# BepInEx 6 for IL2CPP games, 64-bit, the build the mod is made for. Checked against its SHA-256 before use.
$BepInExUrl = "https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip"
$BepInExSha = "f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a"
# What that zip puts in the game folder, removed again on uninstall.
$BepInExItems = @(".doorstop_version", "BepInEx", "changelog.txt", "doorstop_config.ini", "dotnet", "winhttp.dll")
$Marker = "BepInEx\AllRegions-installed-BepInEx.txt"

function Find-Game {
    if ($GameDir) { return $GameDir }
    $steam = $null
    try { $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction Stop).SteamPath } catch { }
    $libraries = @()
    if ($steam) {
        $libraries += $steam
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libraries += $m.Groups[1].Value -replace '\\\\', '\'
            }
        }
    }
    foreach ($lib in $libraries | Select-Object -Unique) {
        $dir = Join-Path $lib "steamapps\common\PICO PARK 2"
        if (Test-Path (Join-Path $dir "pico_park_2.exe")) { return $dir }
    }
    Write-Host "PICO PARK 2 was not found in your Steam libraries."
    $dir = Read-Host "Paste the game's folder (the one with pico_park_2.exe)"
    return $dir.Trim('"', ' ')
}

function Assert-Closed([string]$dir) {
    $exe = (Resolve-Path (Join-Path $dir "pico_park_2.exe")).Path
    foreach ($p in Get-Process -Name "pico_park_2" -ErrorAction SilentlyContinue) {
        # A process whose path can't be read is taken to be this game.
        if (-not $p.Path -or $p.Path -eq $exe) { throw "PICO PARK 2 is running. Close it first, then run this again." }
    }
}

function Set-ModsEnabled([string]$dir, [bool]$on) {
    $ini = Join-Path $dir "doorstop_config.ini"
    if (-not (Test-Path $ini)) { throw "BepInEx is not installed in $dir." }
    $value = if ($on) { "true" } else { "false" }
    $text = Get-Content $ini -Raw
    $text = [regex]::Replace($text, '(?m)^(\s*enabled\s*=\s*)\S+', "`${1}$value", 1)
    [IO.File]::WriteAllText($ini, $text)
}

function Install([string]$dir) {
    $dll = Join-Path $here "AllRegions.dll"
    if (-not (Test-Path $dll)) { throw "AllRegions.dll must be next to this script." }
    $core = Join-Path $dir "BepInEx\core"
    if (Test-Path (Join-Path $core "BepInEx.Unity.IL2CPP.dll")) {
        Write-Host "BepInEx is already installed; keeping it."
    } elseif (Test-Path $core) {
        throw "This game folder has a different BepInEx (not the IL2CPP build). Remove it first."
    } else {
        Write-Host "Downloading BepInEx 6 (be.788)..."
        $zip = Join-Path $env:TEMP "AllRegions-BepInEx-be788.zip"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $BepInExUrl -OutFile $zip -UseBasicParsing
        $sha = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($sha -ne $BepInExSha) { Remove-Item $zip; throw "The BepInEx download did not match its checksum. Not installing it." }
        # Note what was already there, so uninstall never removes a file that isn't BepInEx's.
        $present = $BepInExItems | Where-Object { Test-Path (Join-Path $dir $_) }
        if ($present) { throw "These are already in the game folder: $($present -join ', '). Remove them first." }
        Expand-Archive -Path $zip -DestinationPath $dir -Force
        Remove-Item $zip
        Set-Content -Path (Join-Path $dir $Marker) -Value "Installed by the All Regions setup on $(Get-Date -Format s). Uninstall removes:`r`n$($BepInExItems -join "`r`n")"
    }
    $plugins = Join-Path $dir "BepInEx\plugins\AllRegions"
    New-Item -ItemType Directory -Force $plugins | Out-Null
    Copy-Item $dll $plugins -Force
    Set-ModsEnabled $dir $true
    Write-Host ""
    Write-Host "Installed. The first start after installing BepInEx takes a few minutes (it prepares the game's code once)."
}

function Uninstall([string]$dir) {
    foreach ($p in @("BepInEx\plugins\AllRegions", "BepInEx\config\modwright.picopark2.allregions.cfg", "BepInEx\AllRegions.log", "BepInEx\AllRegions.prev.log")) {
        $full = Join-Path $dir $p
        if (Test-Path $full) { Remove-Item $full -Recurse -Force }
    }
    if (Test-Path (Join-Path $dir $Marker)) {
        foreach ($item in $BepInExItems) {
            $full = Join-Path $dir $item
            if (Test-Path $full) { Remove-Item $full -Recurse -Force }
        }
        Write-Host "Removed All Regions and BepInEx. The game is back to how Steam installed it."
    } else {
        Write-Host "Removed All Regions. BepInEx was there before this setup installed anything, so it was left in place."
    }
}

try {
    Write-Host "All Regions for PICO PARK 2"
    Write-Host ""
    $dir = Find-Game
    if (-not (Test-Path (Join-Path $dir "pico_park_2.exe"))) { throw "No pico_park_2.exe in '$dir'." }
    Write-Host "Game folder: $dir"
    if (-not $Action) {
        Write-Host ""
        Write-Host "  1  Install or update"
        Write-Host "  2  Turn mods off (the game starts without them)"
        Write-Host "  3  Turn mods back on"
        Write-Host "  4  Uninstall"
        Write-Host ""
        $Action = switch (Read-Host "Choose 1-4") { "1" { "install" } "2" { "off" } "3" { "on" } "4" { "uninstall" } default { "" } }
    }
    switch ($Action) {
        "install"   { Assert-Closed $dir; Install $dir }
        "off"       { Assert-Closed $dir; Set-ModsEnabled $dir $false; Write-Host "Mods are off. The game starts as normal; choose 3 to turn them back on." }
        "on"        { Assert-Closed $dir; Set-ModsEnabled $dir $true; Write-Host "Mods are on." }
        "uninstall" { Assert-Closed $dir; Uninstall $dir }
        default     { Write-Host "Nothing done." }
    }
} catch {
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
