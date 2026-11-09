# Installs the "Claude Chat" pane into the Revit MCP add-in (Revit 2026 by default).
# Run in PowerShell (not as admin) with Revit CLOSED:
#   powershell -ExecutionPolicy Bypass -File .\install-chat.ps1
param(
    [string]$RevitYear = "2026"
)
$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

if (Get-Process Revit -ErrorAction SilentlyContinue) {
    Write-Host "Revit is running. Close Revit first (the add-in DLLs are locked while it runs)." -ForegroundColor Red
    exit 1
}
foreach ($tool in @("dotnet", "claude")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Host "'$tool' was not found in PATH." -ForegroundColor Red
        exit 1
    }
}

Write-Host "== 1/3 Building the Revit MCP plugin with the Claude Chat pane" -ForegroundColor Cyan
$config = "Debug R" + $RevitYear.Substring(2)
dotnet build "$Root\plugin\RevitMCPPlugin.csproj" -c $config
if ($LASTEXITCODE -ne 0) { Write-Host "Plugin build failed." -ForegroundColor Red; exit 1 }
$pluginDir = "$env:APPDATA\Autodesk\Revit\Addins\$RevitYear\revit_mcp_plugin"
if (-not (Test-Path "$pluginDir\RevitMCPPlugin.dll")) {
    Write-Host "Expected $pluginDir\RevitMCPPlugin.dll after the build but it is missing." -ForegroundColor Red
    exit 1
}

Write-Host "== 2/3 Working folder Documents\RevitClaude" -ForegroundColor Cyan
$claudeDir = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "RevitClaude"
New-Item -ItemType Directory -Force $claudeDir | Out-Null
Copy-Item "$Root\CLAUDE-electrical.md" "$pluginDir\CLAUDE.md" -Force
if (-not (Test-Path "$claudeDir\CLAUDE.md")) { Copy-Item "$Root\CLAUDE-electrical.md" "$claudeDir\CLAUDE.md" }

Write-Host "== 3/3 Checking Claude Code" -ForegroundColor Cyan
$list = (claude mcp list 2>&1) -join "`n"
if ($list -notmatch "mcp-server-for-revit") {
    Write-Host "Registering the Revit MCP server with Claude Code" -ForegroundColor Yellow
    if (-not (Test-Path "$Root\server\build\index.js")) {
        Write-Host "server\build\index.js is missing. Run install-electrical.ps1 once first." -ForegroundColor Red
        exit 1
    }
    claude mcp add -s user mcp-server-for-revit -- node "$Root\server\build\index.js"
} else {
    Write-Host "mcp-server-for-revit is registered."
}

Write-Host ""
Write-Host "Done. In Revit $RevitYear :" -ForegroundColor Green
Write-Host "  1. Open a project."
Write-Host "  2. Add-Ins > Revit MCP Plugin > Claude Chat. The pane opens next to Properties; drag it anywhere or dock it."
Write-Host "  3. Write in Hebrew or English. Enter sends, Shift+Enter new line, Esc stops."
