# Silent machine-wide install from the unzipped release package (run elevated, Revit closed).
# Intune: package the unzipped folder, install command:
#   powershell -ExecutionPolicy Bypass -File deploy\install-silent.ps1
# Detection rule: file exists "C:\Program Files\RevitElectricalAgent\VERSION.txt".
param([string[]]$RevitYears = @('2025', '2026'))
$ErrorActionPreference = 'Stop'
$pkg = Split-Path -Parent $PSScriptRoot
if (Get-Process Revit -ErrorAction SilentlyContinue) { Write-Error 'Revit is running; the add-in DLLs are locked.'; exit 1 }

$app = 'C:\Program Files\RevitElectricalAgent'
New-Item -ItemType Directory -Force $app | Out-Null
Copy-Item "$pkg\server" "$app\server" -Recurse -Force
Copy-Item "$pkg\skill" "$app\skill" -Recurse -Force
Copy-Item "$pkg\CLAUDE-electrical.md" $app -Force

foreach ($y in $RevitYears) {
    $src = "$pkg\addin-$y"
    if (-not (Test-Path $src)) { Write-Warning "addin-$y missing in the package, skipped"; continue }
    $dst = "$env:ProgramData\Autodesk\Revit\Addins\$y"
    New-Item -ItemType Directory -Force $dst | Out-Null
    Copy-Item "$src\*" $dst -Recurse -Force
}

# Claude Code managed policy: loaded for every user, users cannot override it.
$managed = 'C:\Program Files\ClaudeCode'
New-Item -ItemType Directory -Force $managed | Out-Null
Copy-Item "$PSScriptRoot\managed-settings.json" $managed -Force
Copy-Item "$PSScriptRoot\managed-mcp.json" $managed -Force

# The release workflow writes the tag here when it builds the zip, if present.
if (Test-Path "$pkg\VERSION.txt") { Copy-Item "$pkg\VERSION.txt" $app -Force } else { Set-Content "$app\VERSION.txt" 'unversioned' }
Write-Host 'Installed. Each user still needs: claude sign-in (Team seat) and REVITCLAUDE_ROOT set to the shared folder.'
