# Deploying to an engineering office

How to roll the Revit add-in, the MCP server and Claude Code out to every Revit workstation in a firm of a few dozen users.
Facts about Claude plans, settings and data handling were checked against the official docs on 2026-11-10 (sources at the end); anything marked *assumption* still needs checking in a pilot.

## Recommendation in short

1. **Decide the data path first.** If the firm's policy says AI may only run inside its own cloud tenant, use Claude through that cloud (Microsoft Foundry on Azure, Amazon Bedrock or Google Cloud). Otherwise use **Claude Team** (or Enterprise if Zero Data Retention or SCIM is required). Everything below works with either.
2. **Do not deploy by running the developer install scripts on each PC.** Ship a prebuilt, signed release package from GitHub Releases and push it with Intune / GPO / SCCM.
3. **Lock Claude Code down centrally** with `managed-settings.json` (login restricted to the firm's org, `send_code_to_revit` denied, bypass mode disabled).
4. **Pilot with 4–5 people for 3–4 weeks**, then one department, then all Revit users.

## Today vs. what a fleet needs

| Part | Today (`install-electrical.ps1`, `install-chat.ps1`) | Fleet |
| --- | --- | --- |
| Revit add-in (plugin + command set) | `dotnet build` on the PC, deployed to `%AppData%\Autodesk\Revit\Addins\<year>` | Prebuilt per Revit year, in `C:\Program Files\<Firm>\RevitClaude\<version>`, `.addin` manifests in `C:\ProgramData\Autodesk\Revit\Addins\<year>` (all users). No .NET SDK on the PC. |
| MCP server | `npm install` + `npm run build` on the PC | `build\index.js` + `node_modules` inside the package. Node.js 22+ only. |
| MCP registration | `claude mcp add -s user` per user | `managed-mcp.json` next to `managed-settings.json` (one file for all users) |
| Command selection | Manual: Settings > Select All, untick `send_code_to_revit` | Default config in the package, `send_code_to_revit` off and denied by policy |
| Office config | Copy `office-config/default.json` by hand | The firm's `office.json` in the package; project `RULES.md` still overrides it |
| Updates | `git pull` + rebuild | Tagged GitHub Releases; add-in shows "new version available"; IT pushes it |

Already fleet-ready: Revit ↔ server traffic is local only (`127.0.0.1`), and the Claude Chat pane already restricts tools (`mcp__mcp-server-for-revit`, `Read`, `Glob`, `Grep`; `Bash`, `Write`, `Edit`, web tools blocked).

## Data privacy and connection options

The `.rvt` file is never uploaded. What leaves the PC is the user's prompts and the tool results Claude reads: room names, coordinates, family names, element counts, PDF markup text, project instruction documents. That is client data, so it needs commercial terms.

| Option | Sign-in | Used for training | Retention at provider | Fits "AI only in our tenant"? |
| --- | --- | --- | --- | --- |
| Claude Team | Each engineer's org account (SSO) | No | 30 days | No |
| Claude Enterprise | Same, plus SCIM | No | 30 days, or zero (ZDR) for approved accounts | Partly (with ZDR) |
| Firm's cloud (Foundry / Bedrock / Google Cloud) | Cloud identity (e.g. Entra ID) | No | Per the cloud provider's terms | Yes, closest fit |
| Personal Pro/Max | Personal | Per user setting | Consumer terms | No; not for client work |

On the PC, Claude Code keeps plain-text transcripts under `%USERPROFILE%\.claude\projects` for 30 days (`cleanupPeriodDays` shortens this). `/feedback` sends a transcript that is kept for 5 years; disable it.

Headless caveat: the Chat pane runs `claude -p`. In that mode an `ANTHROPIC_API_KEY` environment variable, if present, wins over the org login. The installer should make sure it is not set (unless the API-key path is chosen on purpose).

## Licensing (prices from claude.com/pricing, 2026-11-10, USD, may change)

| Plan | Price | Minimum | Claude Code |
| --- | --- | --- | --- |
| Team standard seat | $20/seat/month annual, $25 monthly | 2 seats (up to 150) | Included |
| Team premium seat | $100/seat/month annual, $125 monthly | with Team | Included, higher usage allowance |
| Enterprise | $20/seat/month annual + usage at API rates | 20 seats | Included |
| Cloud provider / API | Per token, e.g. Opus 5.5 $4 / $20, Sonnet 5.5 $2 / $10 per million input / output tokens | none | Via environment variables |

Seat allowances reset on 5-hour and weekly windows. Engineers who design whole floors daily will likely need premium seats; drafters who ask questions and produce sheets will likely be fine on standard seats (*assumption*, measure in the pilot). Team and Enterprise have a usage dashboard and spend limits per org, group or member; on a cloud provider you need your own monitoring (OpenTelemetry).

## Silent install

**Machine context (admin, once per PC, via Intune Win32 app / GPO / SCCM):**

1. Prerequisites: Node.js LTS (22+) via winget; Git for Windows optional.
2. Release package unpacked to `C:\Program Files\<Firm>\RevitClaude\<version>`: add-in DLLs per Revit year, prebuilt server, `CLAUDE.md`, skill, `office.json`.
3. `.addin` manifests in `C:\ProgramData\Autodesk\Revit\Addins\2025` and `\2026` with absolute assembly paths.
4. `C:\Program Files\ClaudeCode\managed-settings.json` and `managed-mcp.json` (registers `mcp-server-for-revit` → `node "C:\Program Files\<Firm>\RevitClaude\current\server\build\index.js"`). Users cannot override these.
5. Code signing: sign the DLLs with the firm's code-signing certificate, otherwise every user gets Revit's unsigned add-in prompt.

**User context (first logon, no admin):**

1. Claude Code: `irm https://claude.ai/install.ps1 | iex` → `%USERPROFILE%\.local\bin\claude.exe` (or `winget install Anthropic.ClaudeCode`). A machine-wide Claude Code install is not documented, so it is per user.
2. `Documents\RevitClaude` with `CLAUDE.md` and `office.json`.
3. Sign-in: the Chat pane should detect "not logged in" and offer a button that runs `claude auth login` (*to build*). On the cloud-provider path there is no personal login; the managed settings carry the provider variables.

The existing scripts stay as the developer install. A new `deploy/` installer and a GitHub Actions workflow (Windows runner) that builds and signs the release package are the work items for this.

## Updates

- Add-in: every tested change gets a version tag; CI builds the package and attaches it to the GitHub Release. On Revit start the add-in checks the latest release and only shows a notice. IT installs the update (Program Files needs admin, and DLLs cannot be replaced while Revit runs). Pilot group first, everyone a week later.
- Claude Code: the native installer auto-updates in the background. Keep that on for security fixes; if an update breaks something, `DISABLE_UPDATES=1` in managed `env` freezes the version until fixed.

## Permissions

Two profiles:

- **Drafters / designers:** Chat pane only. Revit tools and file reading. The "Open Claude" terminal button hidden.
- **Lead engineers:** also the terminal, with approval for every command and file edit.

Both get central locks. Starting point, to be verified against the Chat pane's `--allowedTools` in the pilot:

```json
{
  "forceLoginMethod": "claudeai",
  "forceLoginOrgUUID": "<firm org UUID>",
  "permissions": {
    "allow": ["mcp__mcp-server-for-revit", "Read", "Glob", "Grep"],
    "deny": ["mcp__mcp-server-for-revit__send_code_to_revit", "WebFetch", "WebSearch"],
    "disableBypassPermissionsMode": "disable"
  },
  "availableModels": ["opus", "sonnet"],
  "cleanupPeriodDays": 14,
  "env": {
    "DISABLE_FEEDBACK_COMMAND": "1",
    "DISABLE_ERROR_REPORTING": "1"
  }
}
```

On Team/Enterprise the same settings can be managed from the claude.ai admin console (Admin Settings > Claude Code > Managed settings; fetched at start and hourly). On the cloud-provider path that does not apply, and the file on the PC is the only route.

Working rules that go in `CLAUDE.md` and the training: never let Claude Sync a cloud (ACC) model; always approve a preview before placement; stamps only after written engineer approval (already enforced by `place_stamp`).

## Rollout stages

1. **Preparation (2 weeks).** Data-path and licensing decision with management and IT. Release CI, managed settings, the firm's `office.json`. Gate: clean-PC install works with no manual step; written management approval of the data policy.
2. **Pilot (3–4 weeks, 4–5 people):** the product owner, two electrical engineers, one drafter, one IT person. One Revit version (2025), local copies only. Measure: time to design a floor vs. manual, device-count difference vs. the engineer's design (target ±10%), hosting errors, Revit crashes, cost per user per month. Gate: 3 real projects end to end, no work-losing crash, participants keep using it by choice.
3. **Wave 1: electrical department (10–15 people, 1 month).** Push via Intune, workshop, one champion per team as first line of support. Gate: fewer than one support call per user per week; one update delivered automatically to everyone.
4. **Wave 2: all Revit users.** Add Revit 2026; drafters get the narrower profile.

Training: the "AI in Revit" deck (17 slides) fits a 90-minute workshop. Add a one-page cheat sheet (what to ask, approval gates, what never to upload) and the cloud-model rule above.

## Verified vs. assumed

- Verified in the official docs: plan prices and Claude Code inclusion; no training on commercial data; 30-day retention and ZDR for approved Enterprise accounts; managed-settings paths and keys on Windows; auth precedence; native installer and winget; SCIM on Enterprise only.
- Verified in this repo: current install flow, Chat pane tool restrictions, local-only Revit traffic.
- Assumptions: the firm uses Microsoft 365 / Intune; the managed allow/deny rules combine as expected with the Chat pane's CLI flags; premium vs. standard seat split; token cost on the cloud path; no documented machine-wide Claude Code install.
- Ask Anthropic before signing: ZDR eligibility, regional data residency, whether list prices match the contract.

## Sources

- https://claude.com/pricing
- https://code.claude.com/docs/en/managed-settings
- https://code.claude.com/docs/en/server-managed-settings
- https://code.claude.com/docs/en/data-usage
- https://code.claude.com/docs/en/authentication
- https://code.claude.com/docs/en/setup
- https://code.claude.com/docs/en/costs
- https://support.claude.com/en/articles/13133195-set-up-jit-or-scim-provisioning
