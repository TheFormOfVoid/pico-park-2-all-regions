# Builds AllRegions.dll and the player download, dist\AllRegions-<version>.zip.
#   .\build.ps1                       game in the default Steam folder
#   .\build.ps1 -GameDir "D:\Steam\steamapps\common\PICO PARK 2"
# The game needs BepInEx 6 installed and started once, so BepInEx\interop exists to build against.

param([string]$GameDir = "")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$version = [regex]::Match((Get-Content "$root\Plugin.cs" -Raw), 'Version = "([^"]+)"').Groups[1].Value
if (-not $version) { throw "No Version in Plugin.cs." }

$buildArgs = @("build", "$root\AllRegions.csproj", "-c", "Release", "--nologo", "-v", "q")
if ($GameDir) { $buildArgs += "-p:GameDir=$GameDir" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$stage = "$root\dist\AllRegions"
if (Test-Path "$root\dist") { Remove-Item "$root\dist" -Recurse -Force }
New-Item -ItemType Directory $stage -Force | Out-Null
Copy-Item "$root\release\*" $stage
Copy-Item "$root\bin\Release\AllRegions.dll" $stage
$zip = "$root\dist\AllRegions-$version.zip"
Compress-Archive -Path $stage -DestinationPath $zip
Write-Host "Built $zip"
