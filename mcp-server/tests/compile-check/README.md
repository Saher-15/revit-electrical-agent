# Compile check (no Revit, Linux OK)

Compiles the whole command set against the Revit API packages without WPF, so it runs in CI or any container:

```
dotnet build mcp-server/tests/compile-check -c Release -p:RevitVersion=2026
dotnet build mcp-server/tests/compile-check -c Release -p:RevitVersion=2025
```
The real add-in build (with WPF, deployed into Revit's Addins folder) is `install-electrical.ps1` on Windows.
