# Drawing production: project rules, PDF markups, architect DWG, title blocks, stamps

Added 2026-11-09 on top of the PC tree C:\Topaz\revit-mcp-electrical (snapshot of 2026-11-09 16:28).
Only new files, plus appended entries in command.json, CLAUDE-electrical.md and server/package.json.

## New MCP tools
| Tool | Runs in | What it does |
| --- | --- | --- |
| get_project_setup | server | Project folder, RULES.md, title block values, documents waiting |
| read_project_documents | server | Text of PDF/DOCX/XLSX/TXT instructions, in pieces |
| save_project_rules | server | Saves RULES.md (keeps RULES.bak.md) and project.json (fields, title block, stamp image) |
| read_pdf_markups | server | Bluebeam/Acrobat markups -> markups.json, tasks.xlsx, REPLY.md (status survives re-reading) |
| update_markup_tasks | server | Status per markup (done / not_done / question / skipped) + element ids; rewrites xlsx and REPLY.md |
| sheet_point_to_model | Revit | Paper mm on a sheet (or PDF page) -> viewport, model XYZ, room, nearest elements |
| read_cad_link | Revit | Linked DWG: layers, lines, arcs, polylines, blocks (model mm), wall detection; full JSON in Documents\RevitClaude\cad |
| get_title_block_fields | Revit | Field names and values on Project Information, sheets and title blocks |
| fill_title_blocks | Revit | Fills fields on many sheets (project values + perSheet), can switch title block, reports empty required fields |
| place_stamp | Revit | Engineer stamp image on named sheets; refused without engineerApproved=true, logged in stamps.log |

Folders: Documents\RevitClaude\Projects\<project>\{instructions, markups, RULES.md, project.json, stamps.log},
Documents\RevitClaude\tasks\<pdf name>\{markups.json, tasks.xlsx, REPLY.md}, Documents\RevitClaude\cad\.

## Limits
- DWG text and block attributes are not exposed by the Revit API; block names come from the DWG symbol name.
- PDF markups burned into the drawing (flattened) are not comments and cannot be read.
- sheet_point_to_model assumes the PDF page is the title block (PDF printed from Revit). Other PDFs: give sheet mm directly.
- Revit 2021 and older: sheet_point_to_model works for cropped plan views only.

## Tests
- `cd server && npm test` : 12 tests (PDF markups with replies/clouds/links, task files and status merge, DOCX/XLSX/PDF text, project data, tools over an in-memory MCP client, stamp refusals).
- `dotnet test tests/production` : 7 tests of the wall detector and PDF-page-to-sheet mapping.
- Command set compiles against the Revit 2025 and 2026 APIs with 0 errors. Not yet run inside Revit.
