# Design by Example

**המהנדס מתכנן דוגמה אחת, Claude ממשיך את שאר המודל.**

Claude connects to an engineering model in which the engineer designed **one example of a repeating object** (an apartment, a room, a railway station interval, a typical floor), learns that design as rules relative to the surroundings (bed, wardrobe, door handle side, kitchen counter, track centreline), and continues the rest of the model from it: correct position, mounting height, hosting on a clean wall, and the right Schedule Level. One generic engine, one adapter per domain. Revit electrical in residential buildings is the first adapter; railway (Civil 3D), HVAC, plumbing, systems coordination and clash resolution follow. See [DESIGN.md](DESIGN.md).

Built on a fork of [mcp-servers-for-revit](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit) (MIT). Revit 2024–2026 (2025/2026 tested). No office-specific code: families, heights, standards and vocabulary live in `office-config/`.

## Layout

| Folder | What |
| --- | --- |
| `DESIGN.md` | System design: concepts, architecture, adapters, onboarding, test strategy, roadmap. |
| `mcp-server/` | The MCP server (TypeScript, `server/`) with the pattern engine in `server/src/pattern/` and its adapters, plus the Revit add-in (C#, `plugin/` + `commandset/`), install scripts and per-command docs (`ELECTRICAL.md`, `ANALYSIS.md`, `DRAWING.md`, `PRODUCTION.md`, `CHAT.md`). |
| `skill/design-by-example/` | The Claude Skill: workflow, approval gates, room rules, hosting rules, how to read the architect's model, pattern format. |
| `office-config/` | Per-office settings template (`default.json`) and how to fill it. |
| `docs/adapters.md` | How to write a new domain adapter. |
| `docs/revit-cli.md` | Revit from the command line (pyRevit CLI): install, version detection, batch runs. |

## The workflow (Revit rooms adapter)

1. The engineer designs one typical apartment in Revit, with the architect's model linked.
2. `export_model_snapshot` → `read_design_unit`: Claude shows the rooms, anchors (bed, wardrobe, TV, counter, shower…) and devices it understood.
3. `learn_design_pattern` → a table of rules per room type ("socket 1100 mm left of the bed at 400 mm", "switch 150 mm from the door frame, handle side, 1100 mm").
4. `replicate_design_pattern` for the other apartments → counts, moved points, dropped points with reasons (behind wardrobe, glass wall, wet zone, door swing).
5. Engineer approves → `apply_electrical_design` places everything with the apartment's level as Schedule Level → `review_model` → drawings.

Tools 2–4 are offline (no Revit needed) and fully unit-tested. The snapshot fields they need from links (furniture, windows, walls) are the next build of the Revit bridge; see the roadmap in `DESIGN.md`.

## Install (Windows, Revit closed)

```powershell
cd mcp-server
powershell -ExecutionPolicy Bypass -File .\install-electrical.ps1 -RevitYear 2026   # or 2025
```
In Revit: Add-Ins > Revit MCP > Settings > Select All, untick `send_code_to_revit`, Save. Add-Ins > Revit MCP > **Open Claude**.
Copy `skill/design-by-example` to `%USERPROFILE%\.claude\skills\` and `office-config/default.json` to `Documents\RevitClaude\office.json`.

## Tests

```powershell
cd mcp-server/server && npm install && npm test     # 20 tests, no Revit needed
dotnet test mcp-server/tests/production              # 7 C# geometry tests
```

## Data policy

Everything runs on the engineer's PC; snapshots, patterns and proposals stay in `Documents\RevitClaude`. Only what Claude reads in the conversation reaches the model provider. Offices with an in-tenant rule use an enterprise/API account with no training on data.

## License

MIT (upstream copyright sparx-fire; additions copyright Saher / Topaz Engineers).
