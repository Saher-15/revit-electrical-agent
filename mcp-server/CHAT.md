# Claude Chat pane inside Revit

A dockable "Claude" pane in Revit 2026 (Add-Ins > Revit MCP Plugin > **Claude Chat**).

## How it works
- The pane runs the local Claude Code CLI in headless mode
  (`claude -p --input-format stream-json --output-format stream-json --verbose --include-partial-messages`)
  in `Documents\RevitClaude`, so `CLAUDE.md` there sets the rules. It uses the claude.ai sign-in of Claude Code on
  this PC: no API key.
- One `claude` process holds the conversation. Stop (or Esc) kills it; the next message resumes the same
  conversation with `--resume`. "New chat" forgets it.
- The button starts the Revit MCP socket server (port 8080) if needed. The status dot in the header shows it;
  click the text to start it from the pane.
- Allowed tools: the Revit MCP tools (`mcp__mcp-server-for-revit`), Read, Glob, Grep, TodoWrite.
  Blocked: Bash, Write, Edit, MultiEdit, NotebookEdit, WebFetch, WebSearch. Change in
  `Documents\RevitClaude\chat-settings.json` (also `Model`, `ClaudePath`, `WorkingFolder`).
- Answers stream in with Markdown (headings, lists, tables, code). Each tool call shows as a chip
  (⋯ running, ✓ done, ✕ error); click it for the input and result.
- Hebrew text is shown right-to-left automatically.

## Data
Messages and whatever the Revit tools return are sent to Anthropic through the claude.ai account,
exactly like running `claude` in PowerShell.

## Files
- `plugin/Chat/ChatPane.cs` - pane registration, ribbon command `ShowChatPane`, ExternalEvent that starts the server
- `plugin/Chat/ChatPaneView.cs` - the WPF UI (built in code)
- `plugin/Chat/ClaudeSession.cs` - claude process and stream-json parsing
- `plugin/Chat/MarkdownRenderer.cs`, `ChatTheme.cs`, `ChatSettings.cs`
- `plugin/Core/Application.cs` - ribbon button + `RegisterDockablePane`
- `install-chat.ps1` - builds and deploys the plugin
