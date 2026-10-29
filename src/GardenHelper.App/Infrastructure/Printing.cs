using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GardenHelper.App.Infrastructure;

/// <summary>
/// Printing through the Windows print dialog. Choose "Microsoft Print to PDF" in the dialog to save a PDF instead of printing.
/// Plans are printed as they look on screen (zoom and scroll to frame what you want first); lists and reports as formatted pages.
/// </summary>
public static class Printing
{
    private static readonly SolidColorBrush Ink = Frozen("#FF25301F"), Muted = Frozen("#FF6C7A62"), Line = Frozen("#FFD9D2BC"), Card = Frozen("#FFF4F1E6");
    private static SolidColorBrush Frozen(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }

    /// <summary>Prints what an element currently shows (a plan, for example), fitted to the page with a title.</summary>
    public static bool PrintPicture(FrameworkElement source, string title, string? subtitle = null)
    {
        if (source.ActualWidth < 10 || source.ActualHeight < 10) return false;
        const double dpi = 192;
        var bmp = new RenderTargetBitmap((int)(source.ActualWidth * dpi / 96), (int)(source.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(source); bmp.Freeze();
        var dlg = new PrintDialog { PageRangeSelection = PageRangeSelection.AllPages };
        if (dlg.ShowDialog() != true) return false;

        var page = new Grid { Width = dlg.PrintableAreaWidth, Height = dlg.PrintableAreaHeight, Background = Brushes.White };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); page.RowDefinitions.Add(new RowDefinition()); page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new StackPanel { Margin = new Thickness(24, 20, 24, 8) };
        head.Children.Add(new TextBlock { Text = title, FontFamily = new FontFamily("Georgia"), FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Ink });
        if (!string.IsNullOrWhiteSpace(subtitle)) head.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = Muted });
        var picture = new Image { Source = bmp, Stretch = Stretch.Uniform, Margin = new Thickness(24, 0, 24, 0) }; Grid.SetRow(picture, 1);
        var foot = new TextBlock { Text = $"Garden Helper · {DateTime.Today.ToString("d MMMM yyyy", Loc.Instance.Culture)}", FontSize = 10, Foreground = Muted, Margin = new Thickness(24, 6, 24, 14) }; Grid.SetRow(foot, 2);
        page.Children.Add(head); page.Children.Add(picture); page.Children.Add(foot);
        page.Measure(new Size(page.Width, page.Height)); page.Arrange(new Rect(0, 0, page.Width, page.Height)); page.UpdateLayout();
        dlg.PrintVisual(page, title);
        return true;
    }

    /// <summary>A light, printable report: a title, a row of key numbers and titled lists.</summary>
    public static FlowDocument ReportDocument(string title, IEnumerable<(string Value, string Label)> tiles, IEnumerable<(string Title, IEnumerable<string> Lines)> sections)
    {
        var doc = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5, Foreground = Ink, Background = Brushes.White, PagePadding = new Thickness(48) };
        doc.Blocks.Add(new Paragraph(new Run(title)) { FontFamily = new FontFamily("Georgia"), FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        doc.Blocks.Add(new Paragraph(new Run($"Garden Helper · {DateTime.Today.ToString("d MMMM yyyy", Loc.Instance.Culture)}")) { Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 0, 0, 12) });
        var tileList = tiles.ToList();
        if (tileList.Count > 0)
        {
            var table = new Table { CellSpacing = 8, Margin = new Thickness(0, 0, 0, 10) };
            for (var i = 0; i < tileList.Count; i++) table.Columns.Add(new TableColumn());
            var row = new TableRow(); var group = new TableRowGroup(); group.Rows.Add(row); table.RowGroups.Add(group);
            foreach (var (value, label) in tileList)
            {
                var p = new Paragraph { Margin = new Thickness(0) };
                p.Inlines.Add(new Run(value) { FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Frozen("#FF2F5D3A") }); p.Inlines.Add(new LineBreak()); p.Inlines.Add(new Run(label) { FontSize = 10.5, Foreground = Muted });
                row.Cells.Add(new TableCell(p) { Background = Card, Padding = new Thickness(10, 6, 10, 6) });
            }
            doc.Blocks.Add(table);
        }
        foreach (var (sectionTitle, lines) in sections)
        {
            doc.Blocks.Add(new Paragraph(new Run(sectionTitle)) { FontFamily = new FontFamily("Georgia"), FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 3), BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), KeepWithNext = true });
            foreach (var line in lines) doc.Blocks.Add(new Paragraph(new Run("•  " + line)) { Margin = new Thickness(8, 0, 0, 3), TextIndent = -14 });
        }
        return doc;
    }

    public static bool PrintDocument(FlowDocument doc, string title)
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return false;
        doc.PageHeight = dlg.PrintableAreaHeight; doc.PageWidth = dlg.PrintableAreaWidth; doc.ColumnWidth = dlg.PrintableAreaWidth;
        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, title);
        return true;
    }
}
