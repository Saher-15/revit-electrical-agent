# Installs the electrical command set + MCP server from this source tree.
# Run in PowerShell (not as admin) with Revit CLOSED:
#   powershell -ExecutionPolicy Bypass -File .\install-electrical.ps1
param(
    [string]$RevitYear = "2026"
)
$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

if (Get-Process Revit -ErrorAction SilentlyContinue) {
    Write-Host "Revit is running. Close Revit first (the add-in DLLs are locked while it runs)." -ForegroundColor Red
    exit 1
}

foreach ($tool in @("dotnet", "node", "npm", "claude")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Host "'$tool' was not found in PATH." -ForegroundColor Red
        exit 1
    }
}

Write-Host "== 0/3 Building the Revit MCP plugin (ribbon: Open Server / Settings / Open Claude)" -ForegroundColor Cyan
$config = "Debug R" + $RevitYear.Substring(2)
dotnet build "$Root\plugin\RevitMCPPlugin.csproj" -c $config -p:DeployToRevit=true
if ($LASTEXITCODE -ne 0) { Write-Host "Plugin build failed." -ForegroundColor Red; exit 1 }
$pluginDir = "$env:APPDATA\Autodesk\Revit\Addins\$RevitYear\revit_mcp_plugin"
if (-not (Test-Path "$pluginDir\RevitMCPPlugin.dll")) {
    Write-Host "Expected $pluginDir\RevitMCPPlugin.dll after the build but it is missing." -ForegroundColor Red
    exit 1
}
# Electrical-only rules for Claude Code; the "Open Claude" button copies this into Documents\RevitClaude\CLAUDE.md on first use.
Copy-Item "$Root\CLAUDE-electrical.md" "$pluginDir\CLAUDE.md" -Force
$claudeDir = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "RevitClaude"
New-Item -ItemType Directory -Force $claudeDir | Out-Null
Copy-Item "$Root\CLAUDE-electrical.md" "$claudeDir\CLAUDE.md" -Force
# Remove duplicate manifests from an older manual install so Revit loads the add-in once.
Get-ChildItem "$env:APPDATA\Autodesk\Revit\Addins\$RevitYear\*.addin" | Where-Object {
    $_.Name -ne "mcp-servers-for-revit.addin" -and (Select-String -Path $_.FullName -Pattern "revit_mcp_plugin" -Quiet)
} | ForEach-Object { Write-Host "Removing duplicate manifest $($_.Name)"; Remove-Item $_.FullName -Force }

Write-Host "== 1/3 Building the Revit command set for Revit $RevitYear (Debug R$($RevitYear.Substring(2)) deploys straight into %AppData%\Autodesk\Revit\Addins\$RevitYear)" -ForegroundColor Cyan
dotnet build "$Root\commandset\RevitMCPCommandSet.csproj" -c $config -p:DeployToRevit=true
if ($LASTEXITCODE -ne 0) { Write-Host "Command set build failed." -ForegroundColor Red; exit 1 }

$cmdDir = "$env:APPDATA\Autodesk\Revit\Addins\$RevitYear\revit_mcp_plugin\Commands\RevitMCPCommandSet"
if (-not (Test-Path "$cmdDir\$RevitYear\RevitMCPCommandSet.dll")) {
    Write-Host "Expected $cmdDir\$RevitYear\RevitMCPCommandSet.dll after the build but it is missing." -ForegroundColor Red
    exit 1
}
Copy-Item "$Root\command.json" "$cmdDir\command.json" -Force

Write-Host "== 2/3 Building the MCP server" -ForegroundColor Cyan
Push-Location "$Root\server"
npm install --no-audit --no-fund
if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "npm install failed." -ForegroundColor Red; exit 1 }
npm run build
if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "Server build failed." -ForegroundColor Red; exit 1 }
Pop-Location

Write-Host "== 3/3 Pointing Claude Code at the local server" -ForegroundColor Cyan
claude mcp remove -s user mcp-server-for-revit 2>$null
claude mcp add -s user mcp-server-for-revit -- node "$Root\server\build\index.js"
claude mcp list

Write-Host ""
Write-Host "Done. Next, in Revit $RevitYear :" -ForegroundColor Green
Write-Host "  1. Open a project, then Add-Ins > Revit MCP > Settings."
Write-Host "  2. Select All, untick send_code_to_revit, Save. The electrical, analysis and drawing/family commands should be in the list."
Write-Host "  3. Add-Ins > Revit MCP > Open Claude  (starts the server and opens Claude Code in Documents\RevitClaude)."
Write-Host "  4. Ask: 'get_electrical_summary of the open model'."
