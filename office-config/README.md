# Office config

Copy `default.json` to `Documents\RevitClaude\office.json` on the engineer's PC and fill the `families` block with the office's Revit family/type names (`get_available_family_types` lists them). Heights, hosting clearances, room-name keywords and furniture keywords can be changed without touching code. The Skill reads this file at the start of every session; a project's `RULES.md` overrides it per project.
