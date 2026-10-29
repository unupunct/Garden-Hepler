using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace GardenHelper.App.Infrastructure;

/// <summary>
/// Turns the simple Markdown used in the app's own guides (headings, paragraphs, bullets, numbered steps, code blocks, tables, quotes, bold and inline code)
/// into a FlowDocument that follows the light/dark theme. It is not a general Markdown engine.
/// </summary>
public static class MarkdownLite
{
    private static readonly Regex Inline = new(@"(\*\*(?<b>.+?)\*\*)|(`(?<c>[^`]+)`)", RegexOptions.Compiled);

    public static FlowDocument Build(string markdown)
    {
        var doc = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 15, PagePadding = new Thickness(28, 16, 28, 28), ColumnWidth = 5000 };
        doc.SetResourceReference(FlowDocument.BackgroundProperty, "BgBrush");
        doc.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var para = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i]; var line = raw.TrimEnd(); var trimmed = line.TrimStart(); var indent = line.Length - trimmed.Length;
            if (trimmed.StartsWith("```"))
            {
                Flush(doc, para);
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```"); i++) code.Add(lines[i].Length >= indent ? lines[i][Math.Min(indent, lines[i].Length - lines[i].TrimStart().Length)..] : lines[i].TrimStart());
                doc.Blocks.Add(CodeBlock(string.Join("\n", code), indent > 0));
            }
            else if (trimmed.StartsWith('|'))
            {
                Flush(doc, para);
                var rows = new List<string>();
                for (; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++) rows.Add(lines[i].Trim());
                i--; doc.Blocks.Add(TableBlock(rows));
            }
            else if (trimmed.Length == 0) Flush(doc, para);
            else if (trimmed.StartsWith("# ")) { Flush(doc, para); doc.Blocks.Add(Heading(trimmed[2..], 28, new Thickness(0, 4, 0, 8))); }
            else if (trimmed.StartsWith("## ")) { Flush(doc, para); doc.Blocks.Add(Heading(trimmed[3..], 21, new Thickness(0, 20, 0, 6))); }
            else if (trimmed.StartsWith("### ")) { Flush(doc, para); doc.Blocks.Add(Heading(trimmed[4..], 17, new Thickness(0, 14, 0, 4))); }
            else if (trimmed == "---") { Flush(doc, para); var rule = new Paragraph { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 10, 0, 10), FontSize = 2 }; rule.SetResourceReference(Block.BorderBrushProperty, "BorderBrush"); doc.Blocks.Add(rule); }
            else if (trimmed.StartsWith("> ")) { Flush(doc, para); doc.Blocks.Add(Quote(trimmed[2..])); }
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ")) { Flush(doc, para); doc.Blocks.Add(Item("•  ", trimmed[2..], indent)); }
            else if (Regex.Match(trimmed, @"^(\d+)\.\s+(.*)$") is { Success: true } m) { Flush(doc, para); doc.Blocks.Add(Item(m.Groups[1].Value + ".  ", m.Groups[2].Value, indent)); }
            else para.Add(trimmed);
        }
        Flush(doc, para);
        return doc;
    }

    private static void Flush(FlowDocument doc, List<string> para)
    {
        if (para.Count == 0) return;
        var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8), LineHeight = 22 };
        AddInlines(p, string.Join(" ", para)); doc.Blocks.Add(p); para.Clear();
    }

    private static Paragraph Heading(string text, double size, Thickness margin)
    {
        var p = new Paragraph { FontSize = size, FontWeight = FontWeights.SemiBold, Margin = margin, FontFamily = new FontFamily("Georgia") };
        AddInlines(p, text); return p;
    }

    private static Paragraph Item(string marker, string text, int indent)
    {
        var p = new Paragraph { Margin = new Thickness(8 + indent * 6, 0, 0, 5), TextIndent = -22, LineHeight = 22 };
        p.Inlines.Add(new Run(marker) { FontWeight = FontWeights.SemiBold }); AddInlines(p, text); return p;
    }

    private static Paragraph Quote(string text)
    {
        var p = new Paragraph { Margin = new Thickness(0, 4, 0, 10), Padding = new Thickness(14, 8, 10, 8), BorderThickness = new Thickness(4, 0, 0, 0) };
        p.SetResourceReference(Block.BorderBrushProperty, "WarnBrush"); p.SetResourceReference(Block.BackgroundProperty, "Surface2Brush");
        AddInlines(p, text); return p;
    }

    private static Paragraph CodeBlock(string code, bool indented)
    {
        var p = new Paragraph { FontFamily = new FontFamily("Consolas"), FontSize = 14, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(indented ? 30 : 0, 2, 0, 10), LineHeight = 20 };
        p.SetResourceReference(Block.BackgroundProperty, "Surface2Brush");
        var lines = code.Split('\n');
        for (var i = 0; i < lines.Length; i++) { if (i > 0) p.Inlines.Add(new LineBreak()); p.Inlines.Add(new Run(lines[i])); }
        return p;
    }

    private static Table TableBlock(List<string> rows)
    {
        var cells = rows.Where(r => !Regex.IsMatch(r, @"^\|[\s\-:|]+\|?$")).Select(r => r.Trim('|').Split('|').Select(c => c.Trim()).ToList()).ToList();
        var cols = cells.Max(c => c.Count);
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 12) };
        for (var c = 0; c < cols; c++) table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var group = new TableRowGroup(); table.RowGroups.Add(group);
        for (var r = 0; r < cells.Count; r++)
        {
            var row = new TableRow();
            for (var c = 0; c < cols; c++)
            {
                var p = new Paragraph { Margin = new Thickness(0) };
                if (c < cells[r].Count) AddInlines(p, cells[r][c]);
                if (r == 0) p.FontWeight = FontWeights.SemiBold;
                var cell = new TableCell(p) { Padding = new Thickness(8, 5, 8, 5), BorderThickness = new Thickness(0, 0, 0, 1) };
                cell.SetResourceReference(TableCell.BorderBrushProperty, "BorderBrush");
                if (r == 0) cell.SetResourceReference(TableCell.BackgroundProperty, "Surface2Brush");
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }
        return table;
    }

    private static void AddInlines(Paragraph p, string text)
    {
        var pos = 0;
        foreach (Match m in Inline.Matches(text))
        {
            if (m.Index > pos) p.Inlines.Add(new Run(text[pos..m.Index]));
            if (m.Groups["b"].Success) p.Inlines.Add(new Bold(new Run(m.Groups["b"].Value)));
            else
            {
                var code = new Run(m.Groups["c"].Value) { FontFamily = new FontFamily("Consolas") };
                code.SetResourceReference(TextElement.BackgroundProperty, "Surface2Brush");
                p.Inlines.Add(code);
            }
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) p.Inlines.Add(new Run(text[pos..]));
    }
}
