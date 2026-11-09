# Electrical command set for mcp-servers-for-revit

Six commands added on top of the upstream project so Claude can do electrical
work in Revit instead of only architecture.

| Tool | What it does |
|------|--------------|
| `place_electrical_element` | Places lighting fixtures, switches, receptacles, panels, data/fire-alarm devices with a real host: ceiling face (falls back to a reference plane when there is no ceiling), wall face/wall, floor, level, or plane. Type by `typeId` or `category` + `familyName`/`typeName`. |
| `create_cable_tray` | Polyline cable tray runs with width/height and elbow fittings. |
| `create_conduit` | Polyline conduit runs with diameter and elbow fittings. |
| `create_electrical_circuit` | Creates circuits (power, data, fire alarm...) from devices and connects them to a panel. Returns circuit number, voltage, VA. |
| `get_electrical_summary` | Panels, distribution systems, circuits and loads, unassigned devices, element counts, tray/conduit types. |
| `load_family` | Loads `.rfa` files from a path or by searching the Revit libraries. |

All lengths are millimeters. `z` is absolute elevation (level elevation + mounting height).
None of the commands ever shows a Revit dialog: warnings are swallowed, errors are
returned in the result, so Revit never blocks the request queue.

## Files

- `commandset/Models/Electrical/ElectricalModels.cs` - request/response DTOs
- `commandset/Utils/ElectricalUtils.cs` - units, type lookup, ray casting, silent transactions
- `commandset/Services/Electrical/*EventHandler.cs` - the Revit API work
- `commandset/Commands/Electrical/ElectricalCommands.cs` - command classes (one per tool)
- `server/src/tools/{place_electrical_element,create_cable_tray,create_conduit,create_electrical_circuit,get_electrical_summary,load_family}.ts` - MCP tools
- `command.json` - registers the six commands
- `install-electrical.ps1` - builds and installs everything on Windows

## Install (Windows, Revit closed)

```powershell
powershell -ExecutionPolicy Bypass -File .\install-electrical.ps1
```

Then in Revit: Add-Ins > Revit MCP > Settings > Select All, untick `send_code_to_revit`, Save.
Add-Ins > Revit MCP > Open Server.

## Smoke test prompts (Claude Code)

1. `Use get_electrical_summary on the open model.`
2. `Place two M_Plain Recessed Lighting Fixture 600x600 at (2500,1500) and (7500,4500), 2700 mm above Level 1, hostMode ceiling.`
3. `Place M_Duplex Receptacle Standard on the south wall of the 10x6 m room at x=2000 and x=8000, 300 mm above Level 1, hostMode wall.`
4. `Create a power circuit from the two receptacles and connect it to the panel named LP-1.` (needs a panel; place a panelboard first)
5. `Run a 300x100 mm cable tray from (0,3000,2900) to (10000,3000,2900) then to (10000,6000,2900).` (needs an electrical template or transferred project standards)

## "Open Claude" ribbon button

Add-Ins > Revit MCP > **Open Claude** starts the MCP server (if it is not already running) and opens a
PowerShell window running Claude Code in `Documents\RevitClaude`. That folder holds a `CLAUDE.md` with the
electrical-only rules (copied from `CLAUDE-electrical.md` by the installer, and again by the button if missing).
Claude Code must be installed and on PATH (`npm install -g @anthropic-ai/claude-code`).

## Unattended start (autostart.json)

For remote sessions, workshops and smoke tests nobody has to click in Revit. Put `Documents\RevitClaude\autostart.json` in place and launch Revit:

```json
{ "startServer": true,
  "open": { "cloud": { "region": "US", "projectGuid": "<project guid>", "modelGuid": "<model guid>" },
            "detach": "preserve", "worksets": "all" } }
```
`localPath` instead of `cloud` opens a local file. `detach`: preserve | discard | none. `saveAs` (optional) saves the opened detached
model as a local .rvt (a new local central when worksets are preserved), so all further work happens on that file. The add-in opens the model after Revit
initialises, starts the MCP server on port 8080, logs to `autostart.log` and renames the file to `autostart.done.json`.
Cloud GUIDs: `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit <year>\CollaborationCache\<account>\<projectGuid>\<modelGuid>.rvt`.
