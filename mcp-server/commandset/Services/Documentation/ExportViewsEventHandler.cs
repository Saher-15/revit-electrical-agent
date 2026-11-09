using System.IO;
using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Documentation;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Documentation
{
    /// <summary>
    /// Exports views or sheets to PNG (so Claude can look at what it drew) or PDF (deliverables).
    /// Default folder Documents\RevitClaude\exports.
    /// </summary>
    public class ExportViewsEventHandler : ElectricalHandlerBase<ExportViewsRequest, ExportViewsResult>
    {
        public override string GetName() => "export_views";

        protected override AIResult<ExportViewsResult> Run()
        {
            var req = Input ?? new ExportViewsRequest();
            var ids = new List<ElementId>();
            foreach (long id in req.ViewIds ?? new List<long>())
            {
                if (Doc.GetElement(ElectricalUtils.ToElementId(id)) is View v && !v.IsTemplate) ids.Add(v.Id);
                else Warnings.Add($"{id} is not a view; skipped.");
            }
            if (ids.Count == 0)
            {
                var active = UiApp.ActiveUIDocument.ActiveView;
                if (active == null) return Fail("No view to export.");
                ids.Add(active.Id);
            }

            string folder = string.IsNullOrWhiteSpace(req.Folder) ? DocumentationUtils.DefaultFolder("exports") : req.Folder;
            Directory.CreateDirectory(folder);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string baseName = DocumentationUtils.SafeFileName(string.IsNullOrWhiteSpace(req.FileName) ? $"{Doc.Title}_{stamp}" : req.FileName);
            var result = new ExportViewsResult { Folder = folder };
            var before = new HashSet<string>(Directory.GetFiles(folder), StringComparer.OrdinalIgnoreCase);
            DateTime start = DateTime.Now.AddSeconds(-2);

            string format = (req.Format ?? "png").Trim().ToLowerInvariant();
            if (format == "pdf")
            {
                var opts = new PDFExportOptions
                {
                    Combine = req.Combine && ids.Count > 1,
                    FileName = baseName,
                    ZoomType = ZoomType.Zoom,
                    ZoomPercentage = 100,
                    HideCropBoundaries = true,
                    HideScopeBoxes = true,
                    HideReferencePlane = true,
                };
                if (!Doc.Export(folder, ids, opts)) return Fail("Revit did not export the PDF.");
            }
            else if (format == "png" || format == "jpg")
            {
                var opts = new ImageExportOptions
                {
                    ExportRange = ExportRange.SetOfViews,
                    FilePath = Path.Combine(folder, baseName),
                    HLRandWFViewsFileType = format == "jpg" ? ImageFileType.JPEGLossless : ImageFileType.PNG,
                    ShadowViewsFileType = format == "jpg" ? ImageFileType.JPEGLossless : ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                    ZoomType = ZoomFitType.FitToPage,
                    FitDirection = FitDirectionType.Horizontal,
                    PixelSize = Math.Max(400, Math.Min(8000, req.PixelSize)),
                };
                opts.SetViewsAndSheets(ids);
                Doc.ExportImage(opts);
            }
            else return Fail($"Unknown format '{req.Format}'. Use png, jpg or pdf.");

            result.Files = Directory.GetFiles(folder)
                .Where(f => !before.Contains(f) || File.GetLastWriteTime(f) >= start)
                .OrderBy(f => f).ToList();
            if (result.Files.Count == 0) return Fail($"Export finished but no new file was found in {folder}.");
            return Ok(result, $"Exported {ids.Count} view(s) to {result.Files.Count} file(s) in {folder}.");
        }
    }
}
