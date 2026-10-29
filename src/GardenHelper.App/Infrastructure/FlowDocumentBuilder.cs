using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using GardenHelper.Core.Library.Reading;

namespace GardenHelper.App.Infrastructure;

/// <summary>Draws <see cref="ReadBlock"/>s as a native WPF document: headings, paragraphs, lists, quotes, pictures, with the search words highlighted.</summary>
public static class FlowDocumentBuilder
{
    private static readonly double[] HeadingScale = { 1, 1.7, 1.45, 1.25, 1.12, 1.05, 1.0 };

    private static SolidColorBrush Brush(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }

    public static FlowDocument Build(IReadOnlyList<ReadBlock> blocks, double zoom, bool night, string? highlight, out Run? firstHit)
    {
        var ink = Brush(night ? "#E6E2D3" : "#25301F");
        var paper = Brush(night ? "#1C211A" : "#FBF8F0");
        var accent = Brush(night ? "#9BCB8A" : "#2F5D3A");
        var size = 19 * zoom;
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Georgia, Cambria, 'Times New Roman'"), FontSize = size, Foreground = ink, Background = paper,
            LineHeight = size * 1.55, PagePadding = new Thickness(32, 24, 32, 48), TextAlignment = TextAlignment.Left,
            MaxPageWidth = 860 * Math.Max(0.8, zoom), ColumnWidth = double.PositiveInfinity,
        };
        var terms = Terms(highlight);
        Run? first = null;

        void Fill(Paragraph p, ReadBlock b, bool forceBold = false, bool forceItalic = false)
        {
            foreach (var run in b.Runs)
            {
                var lines = run.Text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (i > 0) p.Inlines.Add(new LineBreak());
                    foreach (var piece in Highlighted(lines[i], terms))
                    {
                        var r = new Run(piece.Text);
                        if (run.Bold || forceBold) r.FontWeight = FontWeights.Bold;
                        if (run.Italic || forceItalic) r.FontStyle = FontStyles.Italic;
                        if (piece.Hit) { r.Background = Brush("#F3C969"); r.Foreground = Brushes.Black; first ??= r; }
                        p.Inlines.Add(r);
                    }
                }
            }
        }

        foreach (var b in blocks)
        {
            switch (b.Kind)
            {
                case BlockKind.Heading:
                {
                    var p = new Paragraph { FontSize = size * HeadingScale[Math.Clamp(b.Level, 1, 6)], FontFamily = new FontFamily("Segoe UI"), Foreground = accent, Margin = new Thickness(0, size * 0.9, 0, size * 0.4), LineHeight = double.NaN };
                    Fill(p, b, forceBold: true);
                    doc.Blocks.Add(p);
                    break;
                }
                case BlockKind.ListItem:
                {
                    var p = new Paragraph { Margin = new Thickness(size * 1.3, 0, 0, size * 0.35), TextIndent = -size * 1.0 };
                    p.Inlines.Add(new Run((b.Marker ?? "•") + " ") { Foreground = accent });
                    Fill(p, b);
                    doc.Blocks.Add(p);
                    break;
                }
                case BlockKind.Quote:
                {
                    var p = new Paragraph { Margin = new Thickness(size * 1.5, 0, 0, size * 0.7), BorderBrush = accent, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(size * 0.6, 0, 0, 0) };
                    Fill(p, b, forceItalic: true);
                    doc.Blocks.Add(p);
                    break;
                }
                case BlockKind.Preformatted:
                {
                    var p = new Paragraph { FontFamily = new FontFamily("Consolas"), FontSize = size * 0.85, LineHeight = double.NaN, Background = Brush(night ? "#2A3027" : "#EFEADB"), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, size * 0.7) };
                    Fill(p, b);
                    doc.Blocks.Add(p);
                    break;
                }
                case BlockKind.Image:
                {
                    var image = PhotoImage.Load(b.ImageSrc, 1100);
                    if (image is null)
                    {
                        if (!string.IsNullOrWhiteSpace(b.ImageAlt))
                            doc.Blocks.Add(new Paragraph(new Run("[" + b.ImageAlt + "]") { FontStyle = FontStyles.Italic, Foreground = Brushes.Gray }));
                        break;
                    }
                    var img = new Image { Source = image, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = 760 * zoom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 12) };
                    if (!string.IsNullOrWhiteSpace(b.ImageAlt)) System.Windows.Automation.AutomationProperties.SetName(img, b.ImageAlt);
                    doc.Blocks.Add(new BlockUIContainer(img));
                    break;
                }
                case BlockKind.Rule:
                    doc.Blocks.Add(new BlockUIContainer(new Border { Height = 1, Background = Brush(night ? "#4A5546" : "#CFC8B2"), Margin = new Thickness(0, 12, 0, 12) }));
                    break;
                default:
                {
                    var p = new Paragraph { Margin = new Thickness(0, 0, 0, size * 0.7) };
                    Fill(p, b);
                    doc.Blocks.Add(p);
                    break;
                }
            }
        }
        firstHit = first;
        return doc;
    }

    private static List<string> Terms(string? q) =>
        string.IsNullOrWhiteSpace(q) ? new() : q.Split(new[] { ' ', '\t', ',', ';', '.', ':', '"', '(', ')', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2 && !t.Equals("AND", StringComparison.OrdinalIgnoreCase) && !t.Equals("OR", StringComparison.OrdinalIgnoreCase)).Select(Fold).Distinct().ToList();

    /// <summary>Lower-case and without accents, one character in and one out, so positions in the folded text match the original.</summary>
    private static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            var d = c.ToString().Normalize(NormalizationForm.FormD);
            sb.Append(d.Length > 0 ? char.ToLowerInvariant(d[0]) : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static IEnumerable<(string Text, bool Hit)> Highlighted(string text, List<string> terms)
    {
        if (terms.Count == 0 || text.Length == 0) { yield return (text, false); yield break; }
        var folded = Fold(text);
        var mark = new bool[text.Length];
        foreach (var t in terms)
            for (var i = folded.IndexOf(t, StringComparison.Ordinal); i >= 0; i = folded.IndexOf(t, i + t.Length, StringComparison.Ordinal))
                for (var k = i; k < i + t.Length && k < mark.Length; k++) mark[k] = true;
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
            if (i == text.Length || mark[i] != mark[start]) { yield return (text[start..i], mark[start]); start = i; }
    }
}
