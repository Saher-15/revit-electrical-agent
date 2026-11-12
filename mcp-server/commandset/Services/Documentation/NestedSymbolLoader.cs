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
                var texts = new FilteredElementCollector(familyDoc).OfClass(typeof(TextElement)).Cast<TextElement>()
                    .Select(t => (t.Text ?? "").Trim()).Where(t => t.Length > 0).OrderBy(t => t, StringComparer.Ordinal);
                int curves = new FilteredElementCollector(familyDoc).OfClass(typeof(CurveElement)).GetElementCount();
                return string.Join("|", texts) + "#" + curves;
            }
            catch (Exception ex)
            {
                return "?" + ex.Message;
            }
        }

        /// <summary>The project family for this nested symbol: the loaded one when it draws the same, else the nested one loaded as "name (host family)".</summary>
        public static Family Ensure(Document doc, Document hostFamilyDoc, Family nested, string hostFamilyName, List<string> notes)
        {
            long cat = nested.FamilyCategory?.Id.GetValue() ?? 0;
            Family Find(string name) => new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                .FirstOrDefault(f => f.Name == name && (f.FamilyCategory?.Id.GetValue() ?? 0) == cat);

            Document nd = null;
            try
            {
                nd = hostFamilyDoc.EditFamily(nested);
                var existing = Find(nested.Name);
                if (existing == null)
                    return nd.LoadFamily(doc, new FamilyBuildUtils.OverwriteFamilyLoadOptions());

                string wanted = Signature(nd);
                Document pd = null;
                string have;
                try { pd = doc.EditFamily(existing); have = Signature(pd); }
                finally { try { pd?.Close(false); } catch { } }
                if (have == wanted) return existing;

                string alt = $"{nested.Name} ({hostFamilyName})";
                foreach (char c in Path.GetInvalidFileNameChars()) alt = alt.Replace(c, '_');
                var already = Find(alt);
                if (already != null) return already;

                string path = Path.Combine(Path.GetTempPath(), alt + ".rfa");
                nd.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
                var loaded = nd.LoadFamily(doc, new FamilyBuildUtils.OverwriteFamilyLoadOptions());
                notes.Add($"'{nested.Name}' in the project draws something else ({have} vs {wanted}); loaded this one as '{alt}'");
                try { nd.Close(false); nd = null; File.Delete(path); } catch { }
                return loaded;
            }
            catch (Exception ex)
            {
                notes.Add($"'{nested.Name}' could not be loaded into the project: {ex.Message}");
                return null;
            }
            finally
            {
                try { nd?.Close(false); } catch { }
            }
        }
    }
}
