# Deployment

## Release package
Push a tag `vX.Y.Z` (or run the **Release** workflow by hand). GitHub Actions (Windows runner) runs the server tests, builds the add-in for Revit 2025 and 2026 and the server, and publishes one zip:

| Folder | What |
| --- | --- |
| `addin-2025/`, `addin-2026/` | add-in layout (`revit_mcp_plugin`, `.addin` manifest) |
| `server/` | built MCP server with production `node_modules` |
| `skill/` | the Claude skill |
| `office-config/` | sample `office.json` and how to fill it |
| `CLAUDE-electrical.md` | rules Claude Code loads in the work folder |

## Install on a user's PC (Revit closed)
1. Copy the contents of `addin-<year>` into `%AppData%\Autodesk\Revit\Addins\<year>\`.
2. Register the server: `claude mcp add -s user mcp-server-for-revit -- node <unzipped>\server\build\index.js`.
3. Copy `CLAUDE-electrical.md` to `Documents\RevitClaude\CLAUDE.md`.
4. In Revit: Add-Ins > Revit MCP > Settings, untick `send_code_to_revit`, Save.

## Shared office folder
Set the environment variable `REVITCLAUDE_ROOT` to a network or SharePoint-synced folder. It holds `Projects\<project>\` and `office.json` for all users. Rules the tool learns go to `RULES.proposed.md`; the project lead moves approved ones to `RULES.md`. Unset = personal `Documents\RevitClaude`. Each `apply_electrical_design` run appends to `run-report.json` next to the proposal.

## Not covered yet
Code signing of the DLLs, silent install via Intune/GPO (task 15) and the Windows build itself has not run in CI yet.
