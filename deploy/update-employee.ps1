# Keeps an employee PC on origin/main: fetch, and when main moved and Revit is closed, rebuild the add-in + server.
# First run (installs and registers the hourly task):
#   powershell -ExecutionPolicy Bypass -File deploy\update-employee.ps1 -Force -Register
# -DryRun changes nothing and prints what a run would do.
param(
    [switch]$Force,
    [switch]$Register,
    [switch]$DryRun,
    [string]$Shared = 'Q:\CLAUDE AUTODESK',
    [string[]]$RevitYears = @('2025', '2026')
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$state = Join-Path $env:LOCALAPPDATA 'TopazClaude'
New-Item -ItemType Directory -Force $state | Out-Null
$marker = Join-Path $state 'installed.txt'
function Log($m) {
    Write-Host $m
    if (-not $DryRun) { Add-Content -Path (Join-Path $state 'update.log') -Encoding UTF8 -Value ("{0:yyyy-MM-dd HH:mm}  $m" -f (Get-Date)) }
}

# Commands the add-in may load are listed in its own settings file, which an install never touches:
# without this a tool added on main stays "not found" until someone ticks it in Settings.
function Sync-CommandRegistry($year) {
    $set = Get-Content "$repo\mcp-server\command.json" -Raw -Encoding UTF8 | ConvertFrom-Json
    $file = "$env:APPDATA\Autodesk\Revit\Addins\$year\revit_mcp_plugin\Commands\commandRegistry.json"
    $reg = if (Test-Path $file) { Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json } else { [pscustomobject]@{ Commands = @() } }
    $have = @($reg.Commands | ForEach-Object { $_.commandName })
    # send_code_to_revit runs arbitrary code in the model; it is never enabled from here.
    $new = @($set.commands | Where-Object { $_.commandName -ne 'send_code_to_revit' -and $have -notcontains $_.commandName } | ForEach-Object {
            [pscustomobject]@{
                commandName            = $_.commandName
                assemblyPath           = 'RevitMCPCommandSet\{VERSION}\RevitMCPCommandSet.dll'
                enabled                = $true
                supportedRevitVersions = @($year)
                developer              = $set.developer
                description            = $_.description
            }
        })
    if ($new.Count -eq 0) { return }
    Log "Revit ${year}: enabling $($new.Count) new command(s): $(($new | ForEach-Object { $_.commandName }) -join ', ')"
    if ($DryRun) { return }
    $reg.Commands = @($reg.Commands) + $new
    [IO.File]::WriteAllText($file, ($reg | ConvertTo-Json -Depth 8))
}

if ((git -C $repo rev-parse --abbrev-ref HEAD) -ne 'main') { Log 'Not on main: this is a development checkout, nothing done.'; exit 1 }
git -C $repo fetch --quiet origin main
if ($LASTEXITCODE -ne 0) { Log 'git fetch failed (no network?). Will retry.'; exit 0 }
$remote = git -C $repo rev-parse origin/main
$installed = if (Test-Path $marker) { (Get-Content $marker -Raw).Trim() } else { '' }
if ($remote -eq $installed -and -not $Force) { if ($DryRun) { Write-Host "Up to date ($($remote.Substring(0, 8)))." }; exit 0 }

# The add-in DLLs are locked while Revit runs; the next hourly run or the next logon picks the update up.
if (Get-Process Revit -ErrorAction SilentlyContinue) { Log "Update $($remote.Substring(0, 8)) is waiting: Revit is open."; exit 0 }
$years = @($RevitYears | Where-Object { Test-Path "C:\Program Files\Autodesk\Revit $_\Revit.exe" })
if ($years.Count -eq 0) { Log "No Revit of $($RevitYears -join '/') found on this PC."; exit 1 }

if ($DryRun) {
    Write-Host "Would update $($installed.Substring(0, [Math]::Min(8, $installed.Length))) -> $($remote.Substring(0, 8)) for Revit $($years -join ', ')."
    $years | ForEach-Object { Sync-CommandRegistry $_ }
    exit 0
}

git -C $repo reset --hard --quiet origin/main
foreach ($y in $years) {
    powershell -NoProfile -ExecutionPolicy Bypass -File "$repo\mcp-server\install-electrical.ps1" -RevitYear $y *>> (Join-Path $state "build-$y.log")
    if ($LASTEXITCODE -ne 0) { Log "Install for Revit $y FAILED (see $state\build-$y.log). Will retry."; exit 1 }
    Sync-CommandRegistry $y
}

if ($Register) {
    # One-time link to the shared knowledge folder and the shared skills.
    powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Shared 'setup-employee.ps1')
    if ($LASTEXITCODE -ne 0) { Log 'setup-employee.ps1 failed.'; exit 1 }
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    $t1 = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
    $t2 = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Hours 1)
    Register-ScheduledTask -TaskName 'Topaz Revit agent update' -Action $action -Trigger $t1, $t2 -Force | Out-Null
}
else {
    # install-electrical.ps1 rewrites Documents\RevitClaude\CLAUDE.md; put the shared-folder line back (same text as setup-employee.ps1).
    $root = [Environment]::GetEnvironmentVariable('REVITCLAUDE_ROOT', 'User')
    $file = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'RevitClaude\CLAUDE.md'
    if ($root -and -not (Select-String -Path $file -SimpleMatch "$root\CLAUDE.md" -Quiet)) {
        Add-Content -Path $file -Encoding UTF8 -Value "`r`n## Shared company knowledge`r`nAt the start of every session read the file `"$root\CLAUDE.md`" and follow it. It is Topaz's shared knowledge folder."
    }
}

Set-Content -Path $marker -Value $remote
Log "Updated to $($remote.Substring(0, 8)): $(git -C $repo log -1 --format=%s)"
