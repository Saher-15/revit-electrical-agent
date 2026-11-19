// Power BI / Excel Power Query. Source: the xlsx written by mcp-server/scripts/run_report_dashboard.py
// Setup: Home > Transform data > New Source > Blank Query > Advanced Editor, paste this as query "Runs".
// Change FilePath to the shared folder file, e.g. \server\Revit\metrics\run-report-dashboard.xlsx
// NOT tested in Power BI Desktop (not installed here); column names are taken from run_report_dashboard.py.
let
    FilePath = "C:\Revit\metrics\run-report-dashboard.xlsx",
    Source = Excel.Workbook(File.Contents(FilePath), true, true),
    RunsSheet = Source{[Item = "Runs", Kind = "Sheet"]}[Data],
    Typed = Table.TransformColumnTypes(RunsSheet, {
        {"at", type text}, {"project", type text}, {"user", type text}, {"proposal", type text},
        {"attempted", Int64.Type}, {"placed", Int64.Type}, {"failed", Int64.Type},
        {"remaining", Int64.Type}, {"coverage", type number}, {"seconds", type number}}),
    // "at" is ISO text from run-report.json; keep the original and add real date/week columns
    WithDate = Table.AddColumn(Typed, "Date", each try Date.From(DateTimeZone.FromText([at])) otherwise null, type date),
    WithWeek = Table.AddColumn(WithDate, "WeekStart", each if [Date] = null then null else Date.StartOfWeek([Date], Day.Sunday), type date),
    Clean = Table.SelectRows(WithWeek, each [Date] <> null)
in
    Clean
