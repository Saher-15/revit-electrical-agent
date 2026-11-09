using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Documentation;
using RevitMCPCommandSet.Services.Electrical;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Documentation
{
    /// <summary>Creates floor plans, reflected ceiling plans, drafting views, or duplicates of existing views.</summary>
    public class CreateViewsEventHandler : ElectricalHandlerBase<List<ViewRequest>, List<CreatedView>>
    {
        public override string GetName() => "create_views";

        protected override AIResult<List<CreatedView>> Run()
        {
            var requests = Input ?? new List<ViewRequest>();
            if (requests.Count == 0) return Fail("No views requested.");
            var results = new List<CreatedView>();
            View toOpen = null;

            using (var t = new Transaction(Doc, "MCP: create views"))
            {
                t.Start();
                var pre = ElectricalUtils.MakeSilent(t);
                foreach (var req in requests)
                {
                    var r = new CreatedView { Name = req.Name };
                    try
                    {
                        View view = Create(req, out string error);
                        if (view == null) { r.Error = error; results.Add(r); continue; }
                        Configure(view, req);
                        r.ViewId = view.Id.GetValue();
                        r.Name = view.Name;
                        r.ViewType = view.ViewType.ToString();
                        r.Level = view.GenLevel?.Name;
                        if (req.Open) toOpen = view;
                    }
                    catch (Exception ex) { r.Error = ex.Message; }
                    results.Add(r);
                }
                t.Commit();
                Warnings.AddRange(pre.Messages);
                if (pre.RolledBack) return Fail("Revit rolled the views back.");
            }

            if (toOpen != null)
            {
                try { UiApp.ActiveUIDocument.RequestViewChange(toOpen); } catch (Exception ex) { Warnings.Add($"Could not open the view: {ex.Message}"); }
            }

            int ok = results.Count(r => r.Error == null);
            if (ok == 0) return Fail("No view was created: " + string.Join("; ", results.Select(r => r.Error)));
            return Ok(results, $"Created {ok} of {results.Count} view(s).");
        }

        private View Create(ViewRequest req, out string error)
        {
            error = null;
            string kind = (req.ViewType ?? "floorPlan").Trim().ToLowerInvariant();

            if (kind == "duplicate")
            {
                if (!(Doc.GetElement(ElectricalUtils.ToElementId(req.SourceViewId)) is View source))
                {
                    error = $"sourceViewId {req.SourceViewId} is not a view.";
                    return null;
                }
                var option = ViewDuplicateOption.Duplicate;
                string opt = (req.DuplicateOption ?? "").ToLowerInvariant();
                if (opt.Contains("detail")) option = ViewDuplicateOption.WithDetailing;
                else if (opt.Contains("depend")) option = ViewDuplicateOption.AsDependent;
                if (!source.CanViewBeDuplicated(option))
                {
                    error = $"View '{source.Name}' cannot be duplicated with option {option}.";
                    return null;
                }
                return Doc.GetElement(source.Duplicate(option)) as View;
            }

            ViewFamily family;
            switch (kind)
            {
                case "floorplan":
                case "plan": family = ViewFamily.FloorPlan; break;
                case "ceilingplan":
                case "rcp":
                case "reflectedceilingplan": family = ViewFamily.CeilingPlan; break;
                case "drafting": family = ViewFamily.Drafting; break;
                default:
                    error = $"Unknown viewType '{req.ViewType}'. Use floorPlan, ceilingPlan, drafting or duplicate.";
                    return null;
            }

            var types = new FilteredElementCollector(Doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .Where(t => t.ViewFamily == family).ToList();
            var vft = DocumentationUtils.PickByName(types, t => t.Name, req.ViewFamilyTypeName) ?? types.FirstOrDefault();
            if (vft == null)
            {
                error = $"The project has no {family} view type.";
                return null;
            }
            if (!string.IsNullOrWhiteSpace(req.ViewFamilyTypeName) && !DocumentationUtils.Contains(vft.Name, req.ViewFamilyTypeName))
                Warnings.Add($"View type '{req.ViewFamilyTypeName}' not found, used '{vft.Name}'.");

            if (family == ViewFamily.Drafting) return ViewDrafting.Create(Doc, vft.Id);

            var levels = new FilteredElementCollector(Doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            var level = DocumentationUtils.PickByName(levels, l => l.Name, req.LevelName);
            if (level == null)
            {
                error = string.IsNullOrWhiteSpace(req.LevelName)
                    ? "levelName is required for plan views."
                    : $"Level '{req.LevelName}' not found. Levels: {string.Join(", ", levels.Select(l => l.Name))}";
                return null;
            }
            return ViewPlan.Create(Doc, vft.Id, level.Id);
        }

        private void Configure(View view, ViewRequest req)
        {
            if (!string.IsNullOrWhiteSpace(req.Name))
            {
                string name = DocumentationUtils.UniqueViewName(Doc, req.Name.Trim());
                if (name != req.Name.Trim()) Warnings.Add($"A view named '{req.Name}' exists, named it '{name}'.");
                view.Name = name;
            }

            if (!string.IsNullOrWhiteSpace(req.TemplateName))
            {
                var templates = new FilteredElementCollector(Doc).OfClass(typeof(View)).Cast<View>()
                    .Where(v => v.IsTemplate && v.ViewType == view.ViewType).ToList();
                var template = DocumentationUtils.PickByName(templates, v => v.Name, req.TemplateName);
                if (template == null)
                    Warnings.Add($"View template '{req.TemplateName}' for {view.ViewType} not found. Templates: {string.Join(", ", templates.Select(v => v.Name))}");
                else
                    view.ViewTemplateId = template.Id;
            }

            if (req.Scale > 0)
            {
                try { view.Scale = req.Scale; } catch (Exception ex) { Warnings.Add($"{view.Name}: scale not set ({ex.Message}). A view template may control it."); }
            }

            if (!string.IsNullOrWhiteSpace(req.Discipline) && view.HasViewDiscipline())
            {
                var map = new Dictionary<string, ViewDiscipline>(StringComparer.OrdinalIgnoreCase)
                {
                    { "electrical", ViewDiscipline.Electrical }, { "architectural", ViewDiscipline.Architectural },
                    { "mechanical", ViewDiscipline.Mechanical }, { "plumbing", ViewDiscipline.Plumbing },
                    { "structural", ViewDiscipline.Structural }, { "coordination", ViewDiscipline.Coordination },
                };
                if (map.TryGetValue(req.Discipline.Trim(), out var d))
                {
                    try { view.Discipline = d; } catch (Exception ex) { Warnings.Add($"{view.Name}: discipline not set ({ex.Message})."); }
                }
                else Warnings.Add($"Unknown discipline '{req.Discipline}'.");
            }

            if (!string.IsNullOrWhiteSpace(req.SubDiscipline))
            {
                var p = view.LookupParameter("Sub-Discipline");
                if (p != null && !p.IsReadOnly) p.Set(req.SubDiscipline);
                else Warnings.Add($"{view.Name}: no writable 'Sub-Discipline' parameter (a view template may control it).");
            }

            if (req.CropMin != null && req.CropMax != null && view is ViewPlan)
            {
                try
                {
                    var box = view.CropBox;
                    var inverse = box.Transform.Inverse;
                    XYZ a = inverse.OfPoint(new XYZ(ElectricalUtils.MmToFt(req.CropMin.X), ElectricalUtils.MmToFt(req.CropMin.Y), 0));
                    XYZ b = inverse.OfPoint(new XYZ(ElectricalUtils.MmToFt(req.CropMax.X), ElectricalUtils.MmToFt(req.CropMax.Y), 0));
                    box.Min = new XYZ(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), box.Min.Z);
                    box.Max = new XYZ(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), box.Max.Z);
                    view.CropBoxActive = true;
                    view.CropBox = box;
                    view.CropBoxVisible = false;
                }
                catch (Exception ex) { Warnings.Add($"{view.Name}: crop not set ({ex.Message})."); }
            }
        }
    }
}
