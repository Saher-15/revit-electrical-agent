# Revit command line (pyRevit CLI)

The agent talks to a running Revit through the MCP add-in. For work outside a Revit session (which Revit versions are installed, is Revit running, batch runs on RVT files) we use **pyRevit CLI** (`pyrevit`), the open-source command-line tool from pyRevitLabs. Autodesk ships no local Revit CLI (only cloud Design Automation).

## Install (no admin)

```powershell
winget install --id pyRevit.pyRevit.CLI -e --scope user
```

Installs to `%LOCALAPPDATA%\Programs\pyRevit CLI\bin` and adds it to the user PATH (open a new terminal). The machine-wide installer (`winget install` without `--scope user`, newer version) needs an admin UAC prompt. The CLI alone does not attach pyRevit to Revit and does not change any Revit add-in.

## Useful commands

```powershell
pyrevit --version
pyrevit revits --installed   # Revit years, builds and paths on this PC
pyrevit revits               # running Revit instances
pyrevit env                  # full environment report
```

## Batch runs on models (optional)

`pyrevit run <script.py> <model.rvt> --revit=2026` opens Revit, runs an IronPython script on the model and closes it. It needs the pyRevit runtime: `pyrevit clone` (a large download). Run it only with Revit closed and no other agent session using Revit.

Tested on Saher-PC, 2026-11-09: v6.4.0 (user install) detects Revit 2024, 2025, 2026 and 2027. Known issue in 6.4.0: `pyrevit env` ends with a `System.Management` assembly error after the Revit list; the other commands are unaffected.
