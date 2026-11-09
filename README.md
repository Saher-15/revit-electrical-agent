# Revit Electrical Agent

**סוכן תכנון חשמל ל-Revit: המהנדס מתכנן דירה אחת, Claude משלים את שאר הבניין.**

An MCP server + Claude Skill that lets Claude read the architect's Revit model (rooms, walls, furniture, fixtures), learn how an engineer wired one typical apartment, and replicate that design to every other apartment and floor of a residential building, with correct hosting, mounting heights and Schedule Level.

Built on a fork of [mcp-servers-for-revit](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit) (MIT) with ~30 added electrical, analysis, drawing-production and chat commands. Works with Revit 2024–2026 (2025/2026 tested). No office-specific code: family names, heights and standards live in `office-config/`, so the same tool runs in any office.

## מבנה / Layout

| Folder | What |
| --- | --- |
| `mcp-server/` | The Revit add-in (C#, `plugin/` + `commandset/`) and the MCP server (TypeScript, `server/`). Install scripts, docs per command group (`ELECTRICAL.md`, `ANALYSIS.md`, `DRAWING.md`, `PRODUCTION.md`, `CHAT.md`). |
| `skill/revit-electrical-agent/` | The Claude Skill: workflow, room-by-room rules, hosting rules, heights, Schedule Level. Copy to `~/.claude/skills/` or the project's `.claude/skills/`. |
| `office-config/` | Per-office settings (families, heights, standards, room-name vocabulary). `default.json` is the Israeli residential default. |
| `docs/ROADMAP.md` | What exists, what is next (`read_apartment`, `learn_apartment_pattern`, `replicate_apartment`, `check_hosting`). |

## Core workflow (the idea)

1. Engineer opens the building model in Revit with the architect's model linked, and designs **one** typical apartment by hand.
2. `export_model_snapshot` + `read_apartment` give Claude the rooms, furniture and the engineer's devices of that apartment.
3. `learn_apartment_pattern` turns every device into a rule *relative to the architecture*: "socket 300 mm right of the bed, 400 mm AFF", "switch 150 mm from the door handle side, 1100 mm AFF", "TV outlet behind the TV unit at 1200 mm".
4. `replicate_apartment` applies the pattern to every other apartment (same type or mapped type), checks hosting (never behind a wardrobe, never on a wall with a door/window in the way, never on a non-clean wall), writes a preview, and after approval places the devices with `place_electrical_element` and sets the **Schedule Level** to the apartment's level.
5. `review_model` checks the result; `create_views` / `create_sheets` / `export_views` produce the drawings.

Steps 1, 2 and 5 work today. Steps 3–4 are the next build (see `docs/ROADMAP.md`); until then Claude does them with `export_model_snapshot` + `propose_electrical_design` + `place_electrical_element` following the Skill's rules.

## Install (Windows, Revit closed)

```powershell
cd mcp-server
powershell -ExecutionPolicy Bypass -File .\install-electrical.ps1 -RevitYear 2026   # or 2025
```
Then in Revit: Add-Ins > Revit MCP > Settings > Select All, untick `send_code_to_revit`, Save. Add-Ins > Revit MCP > **Open Claude**.

Copy `skill/revit-electrical-agent` to `%USERPROFILE%\.claude\skills\` and `office-config/default.json` to `Documents\RevitClaude\office.json` (edit for the office).

## Data policy

The MCP server runs on the engineer's PC and talks to Revit locally. Model data reaches the AI model only through what Claude reads in the conversation. Offices with an in-tenant-only rule should run Claude through an enterprise/API account with no training on data, and keep `export_model_snapshot` outputs on the PC.

## License

MIT (upstream copyright sparx-fire; additions copyright Saher / Topaz Engineers).
