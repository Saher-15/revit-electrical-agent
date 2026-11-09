# Drawing, documentation and family tools

Twelve tools added on top of the electrical and analysis command sets so Claude can produce drawings and build families.
All lengths are millimeters. None of them shows a Revit dialog; problems come back in the result.

## Drawing and documentation

| Tool | What it does |
|------|--------------|
| `get_documentation_info` | Read-only. Active view, levels, views (scale, discipline, template, sheet), view templates and view types, sheets with their views, title blocks, tag types per category, text types, line styles, wire types, schedules, panel schedule templates, panels. |
| `create_views` | Floor plans and reflected ceiling plans per level, drafting views, or duplicates (with detailing / dependent). Name, template, scale, discipline, sub-discipline, crop region. |
| `create_sheets` | New sheets (number, name, title block, sheet parameters) or an existing sheet; places views (grid layout inside the title block or exact positions), schedules and panel schedules; reports viewports that run past the title block. |
| `tag_elements` | Tags all visible elements of given categories or given ids in a view, skipping tagged ones; tags rooms of the linked architectural model too. |
| `create_schedule` | Schedules from presets (lighting, devices, switches, panels, circuits, conduit, cableTray) or explicit fields, with filters, sorting/grouping, totals; optionally on a sheet. |
| `create_panel_schedules` | Panel schedules for chosen panels or all panels (reuses existing ones), optional template, laid out side by side on a sheet. |
| `create_wires` | Arc/chamfer wiring for existing circuits in a plan, device to device from the panel side, plus a home run. |
| `create_annotations` | Text notes, detail polylines and circles in plans, drafting views (single-line/riser diagrams, legends) or on sheets. |
| `export_views` | PNG/JPG (so Claude can look at its own drawing) or PDF (deliverable) of views and sheets, to Documents\RevitClaude\exports. |

## Families

| Tool | What it does |
|------|--------------|
| `create_family` | New .rfa from a family template: box/cylinder solids with material, 2D plan symbol (symbolic lines/circles), parameters with values or formulas, an electrical connector (power with voltage/poles/apparent load linked to type parameters, or data/fire alarm/...), part type (panelboard, switchboard, junction box...), several types. Saved to Documents\RevitClaude\families and loaded into the project. |
| `edit_family` | Reads a loaded family (parameters, values per type, formulas, connectors) or changes it: add parameters, create/rename/delete types, set values and formulas, add/update the electrical connector, part type; reloads it. |
| `set_family_type` | Duplicates a project type under a new name and/or sets type parameters without opening the family. |

Limits: solid sizes are fixed per family (not yet driven by dimension parameters), only boxes and cylinders, no nested
annotation symbols. For a detailed luminaire, load a manufacturer family and adjust it with `edit_family`.

## Files

- `commandset/Models/Documentation/DocumentationModels.cs`, `commandset/Models/Families/FamilyModels.cs` - request/result DTOs
- `commandset/Utils/DocumentationUtils.cs`, `commandset/Utils/FamilyBuildUtils.cs` - helpers
- `commandset/Services/Documentation/*EventHandler.cs`, `commandset/Services/Families/*EventHandler.cs` - the Revit API work
- `commandset/Commands/Documentation/DocumentationCommands.cs`, `commandset/Commands/Families/FamilyCommands.cs` - command classes
- `server/src/tools/{get_documentation_info,create_views,create_sheets,tag_elements,create_schedule,create_panel_schedules,create_wires,create_annotations,export_views,create_family,edit_family,set_family_type}.ts`
- `command.json` - twelve entries appended; `CLAUDE-electrical.md` - drawing and family rules appended

## Smoke test prompts (Claude Code, Revit 2026 with an electrical model open)

1. `get_documentation_info`
2. `Create an electrical power plan and a lighting ceiling plan for Level 1 at 1:50, tag all lighting fixtures, sockets and rooms, then export both to PNG and show me.`
3. `Create a lighting fixture schedule and panel schedules for all panels, put the Level 1 plans and the schedules on a new sheet E-101 and export it to PDF.`
4. `Build a family "TPZ Socket Double 16A": wall based electrical fixture, 80x80x40 box, plan symbol of a socket, power connector 230 V 1 pole 200 VA, types 16A and 25A. Place one on the south wall.`
5. `edit_family on "TPZ Socket Double 16A": add type "16A IP55" with Type Mark S2.`
