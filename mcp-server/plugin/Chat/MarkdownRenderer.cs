using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace revit_mcp_plugin.Chat
{
    /// <summary>
    /// Small Markdown to FlowDocument renderer for Claude's answers: headings, paragraphs,
    /// bullet/numbered lists, tables, code blocks, **bold**, *italic* and `code`.
    /// Each block gets right-to-left direction when it starts with Hebrew/Arabic text.
    /// </summary>
    internal static class MarkdownRenderer
    {
        private static readonly Regex Numbered = new Regex(@"^\s*\d+[.)]\s+");
        private static readonly Regex Bullet = new Regex(@"^\s*[-*•]\s+");
        private static readonly Regex Inline = new Regex(@"(\*\*[^*\n]+\*\*|`[^`\n]+`|\*[^*\s][^*\n]*\*)");
        private static readonly Regex TableSeparator = new Regex(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$");

        public static FlowDocument Render(string markdown)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontFamily = ChatTheme.Font,
                FontSize = 13.5,
                Foreground = ChatTheme.Text,
                LineHeight = 20,
                TextAlignment = TextAlignment.Left,
            };
            var lines = (markdown ?? "").Replace("\r", "").Split('\n');
            int i = 0;
            var para = new List<string>();

            void FlushPara()
            {
                if (para.Count == 0) return;
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                for (int k = 0; k < para.Count; k++)
                {
                    if (k > 0) p.Inlines.Add(new LineBreak());
                    AddInlines(p.Inlines, para[k]);
                }
                SetDirection(p, string.Join(" ", para));
                doc.Blocks.Add(p);
                para.Clear();
            }

            while (i < lines.Length)
            {
                string line = lines[i];
                string t = line.Trim();

                if (t.StartsWith("```"))
                {
                    FlushPara();
                    var code = new List<string>();
                    i++;
                    while (i < lines.Length && !lines[i].Trim().StartsWith("```")) code.Add(lines[i++]);
                    i++;
                    doc.Blocks.Add(new Paragraph(new Run(string.Join("\n", code)))
                    {
                        FontFamily = ChatTheme.Mono,
                        FontSize = 12,
                        LineHeight = 17,
                        Background = ChatTheme.CodeBg,
                        Padding = new Thickness(10, 8, 10, 8),
                        Margin = new Thickness(0, 2, 0, 10),
                        FlowDirection = FlowDirection.LeftToRight,
                    });
                    continue;
                }

                if (t.StartsWith("|") && i + 1 < lines.Length && TableSeparator.IsMatch(lines[i + 1]))
                {
                    FlushPara();
                    var rows = new List<string> { t };
                    i += 2;
                    while (i < lines.Length && lines[i].Trim().StartsWith("|")) rows.Add(lines[i++].Trim());
                    doc.Blocks.Add(BuildTable(rows));
                    continue;
                }

                if (t.StartsWith("#"))
                {
                    FlushPara();
                    int level = t.TakeWhile(c => c == '#').Count();
                    string text = t.Substring(level).Trim();
                    var h = new Paragraph { FontWeight = FontWeights.SemiBold, FontSize = level <= 1 ? 17 : level == 2 ? 15.5 : 14, Margin = new Thickness(0, 6, 0, 6) };
                    AddInlines(h.Inlines, text);
                    SetDirection(h, text);
                    doc.Blocks.Add(h);
                    i++;
                    continue;
                }

                bool isBullet = Bullet.IsMatch(line), isNum = Numbered.IsMatch(line);
                if (isBullet || isNum)
                {
                    FlushPara();
                    var list = new List
                    {
                        MarkerStyle = isNum ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Margin = new Thickness(0, 0, 0, 8),
                        Padding = new Thickness(20, 0, 0, 0),
                    };
                    var regex = isNum ? Numbered : Bullet;
                    string first = null;
                    while (i < lines.Length && regex.IsMatch(lines[i]))
                    {
                        string item = regex.Replace(lines[i], "", 1);
                        first ??= item;
                        var p = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                        AddInlines(p.Inlines, item);
                        list.ListItems.Add(new ListItem(p));
                        i++;
                    }
                    SetDirection(list, first);
                    doc.Blocks.Add(list);
                    continue;
                }

                if (t.Length == 0) { FlushPara(); i++; continue; }
                if (t == "---" || t == "***") { FlushPara(); i++; continue; }

                para.Add(line.TrimEnd());
                i++;
            }
            FlushPara();
            return doc;
        }

        private static Table BuildTable(List<string> rows)
        {
            var cells = rows.Select(r => r.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList()).ToList();
            int cols = cells.Max(c => c.Count);
            var table = new Table
            {
                CellSpacing = 0,
                BorderBrush = ChatTheme.Border,
                BorderThickness = new Thickness(1, 1, 0, 0),
                Margin = new Thickness(0, 2, 0, 10),
                FontSize = 12.5,
            };
            SetDirection(table, string.Join(" ", cells[0]));
            for (int c = 0; c < cols; c++) table.Columns.Add(new TableColumn());
            var group = new TableRowGroup();
            for (int r = 0; r < cells.Count; r++)
            {
                var row = new TableRow();
                if (r == 0) { row.FontWeight = FontWeights.SemiBold; row.Background = ChatTheme.ToolBg; }
                for (int c = 0; c < cols; c++)
                {
                    var p = new Paragraph { Margin = new Thickness(0) };
                    AddInlines(p.Inlines, c < cells[r].Count ? cells[r][c] : "");
                    row.Cells.Add(new TableCell(p)
                    {
                        BorderBrush = ChatTheme.Border,
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        Padding = new Thickness(6, 3, 6, 3),
                    });
                }
                group.Rows.Add(row);
            }
            table.RowGroups.Add(group);
            return table;
        }

        private static void AddInlines(InlineCollection target, string text)
        {
            int pos = 0;
            foreach (Match m in Inline.Matches(text))
            {
                if (m.Index > pos) target.Add(new Run(text.Substring(pos, m.Index - pos)));
                string v = m.Value;
                if (v.StartsWith("**")) target.Add(new Bold(new Run(v.Substring(2, v.Length - 4))));
                else if (v.StartsWith("`"))
                    target.Add(new Run(v.Substring(1, v.Length - 2)) { FontFamily = ChatTheme.Mono, FontSize = 12, Background = ChatTheme.CodeBg });
                else target.Add(new Italic(new Run(v.Substring(1, v.Length - 2))));
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) target.Add(new Run(text.Substring(pos)));
        }

        private static void SetDirection(Block block, string text) =>
            block.FlowDirection = IsRtl(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        /// <summary>True when the first strong letter is Hebrew or Arabic.</summary>
        public static bool IsRtl(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                if (c >= '֐' && c <= 'ࣿ') return true;
                if (char.IsLetter(c)) return false;
            }
            return false;
        }
    }
}
