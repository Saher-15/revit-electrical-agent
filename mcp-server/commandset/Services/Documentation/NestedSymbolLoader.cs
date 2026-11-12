using System.IO;
using Autodesk.Revit.DB;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Documentation
{
    /// <summary>
    /// Brings a 2D symbol family that is nested in a host family into the project.
    /// Two host families can nest different symbols under the same family name (same name, another fixed text):
    /// loading the second over the first would change every instance of the first, and reusing the first shows the wrong text.
    /// </summary>
    public static class NestedSymbolLoader
    {
        /// <summary>What a symbol family draws: its texts and how many lines. Tells same-named families apart.</summary>
        public static string Signature(Document familyDoc)
        {
            try
            {
                // Text with its type's colour: two symbols can differ only in the colour of the same letters.
                var texts = new FilteredElementCollector(familyDoc).OfClass(typeof(TextElement)).Cast<TextElement>()
                    .Select(t => (t.Text ?? "").Trim() + "@" + ((familyDoc.GetElement(t.GetTypeId()) as ElementType)?.get_Parameter(BuiltInParameter.LINE_COLOR)?.AsInteger() ?? -1))
                    .Where(t => !t.StartsWith("@")).OrderBy(t => t, StringComparer.Ordinal);
                // Lines per line style and colour.
                var curves = new FilteredElementCollector(familyDoc).OfClass(typeof(CurveElement)).Cast<CurveElement>()
                    .Select(c => c.LineStyle as GraphicsStyle)
                    .GroupBy(g => g == null ? "?" : g.Name + "@" + ColourOf(g.GraphicsStyleCategory))
                    .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + "x" + g.Count());
                return string.Join("|", texts) + "#" + string.Join("|", curves);
            }
            catch (Exception ex)
            {
                return "?" + ex.Message;
            }
        }

        private static string ColourOf(Category c)
        {
            try { var k = c?.LineColor; return k == null || !k.IsValid ? "-" : $"{k.Red},{k.Green},{k.Blue}"; }
            catch { return "-"; }
        }

        /// <summary>The project family for this nested symbol: the loaded one when it draws the same, else the nested one loaded as "name (host family)".</summary>
        public static Family Ensure(Document doc, Document hostFamilyDoc, Family nested, string hostFamilyName, List<string> notes, bool forceSeparate = false)
        {
            string nestedName = nested.Name;
            long cat = nested.FamilyCategory?.Id.GetValue() ?? 0;
            Family Find(string name) => new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                .FirstOrDefault(f => f.Name == name && (f.FamilyCategory?.Id.GetValue() ?? 0) == cat);

            Document nd = null;
            try
            {
                var existing = Find(nestedName);
                if (existing == null)
                {
                    nd = hostFamilyDoc.EditFamily(nested);
                    return nd.LoadFamily(doc, new FamilyBuildUtils.OverwriteFamilyLoadOptions());
                }

                // One family document at a time: both have the same title, and closing one invalidated the other (Revit 2026).
                string have;
                Document pd = null;
                try { pd = doc.EditFamily(existing); have = Signature(pd); }
                finally { try { pd?.Close(false); } catch { } }
                nd = hostFamilyDoc.EditFamily(nested);
                string wanted = Signature(nd);
                // forceSeparate: the caller knows the two differ in a way the signature cannot see (e.g. a label bound to a parameter in one and fixed text in the other).
                if (have == wanted && !forceSeparate) return existing;

                string alt = $"{nestedName} ({hostFamilyName})";
                foreach (char c in Path.GetInvalidFileNameChars()) alt = alt.Replace(c, '_');
                var already = Find(alt);
                if (already != null) return already;

                // Load the host's copy next to the loaded one without a temp file: free the name, load, then name both.
                string keep = nestedName + " __keep__";
                using (var t = new Transaction(doc, "Free symbol family name")) { t.Start(); existing.Name = keep; t.Commit(); }
                Family loaded = null;
                try { loaded = nd.LoadFamily(doc, new FamilyBuildUtils.OverwriteFamilyLoadOptions()); }
                finally
                {
                    using (var t = new Transaction(doc, "Name symbol families"))
                    {
                        t.Start();
                        if (loaded != null) loaded.Name = alt;
                        existing.Name = nestedName;
                        t.Commit();
                    }
                }
                notes.Add(have == wanted ? $"loaded the host's own '{nestedName}' as '{alt}' (separate copy requested)" : $"'{nestedName}' in the project draws something else ({have} vs {wanted}); loaded this one as '{alt}'");
                return loaded;
            }
            catch (Exception ex)
            {
                notes.Add($"'{nestedName}' could not be loaded into the project: {ex.Message}");
                return null;
            }
            finally
            {
                try { nd?.Close(false); } catch { }
            }
        }
    }
}
