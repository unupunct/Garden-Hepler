using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace GardenHelper.App.Infrastructure;

public sealed class BoolToVisibility : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        (value is true) ^ (p as string == "invert") ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Visible when the value is a non-empty string, non-null object, or non-zero number.</summary>
public sealed class HasValueToVisibility : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var has = value switch { null => false, string s => s.Length > 0, int i => i != 0, System.Collections.ICollection col => col.Count > 0, _ => true };
        return has ^ (p as string == "invert") ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Two-way radio-button helper: IsChecked when the bound string equals ConverterParameter.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => Equals(value?.ToString(), p?.ToString());
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is true ? p : Binding.DoNothing;
}

public sealed class DateConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        DateOnly d => d.ToString(p as string ?? "d MMM yyyy", Loc.Instance.Culture),
        DateTime dt => dt.ToLocalTime().ToString(p as string ?? "d MMM yyyy", Loc.Instance.Culture),
        _ => "",
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class NotConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is not true;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is not true;
}

public sealed class DoneGlyphConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? "✔" : "○";
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Book details are shown when no search is active and a book is selected.</summary>
public sealed class DetailsVisibility : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c) =>
        values.Length == 2 && values[0] is false && values[1] is not null && values[1] != DependencyProperty.UnsetValue ? Visibility.Visible : Visibility.Collapsed;
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Visible only when every bound count is zero.</summary>
public sealed class NoItemsToVisibility : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c) =>
        values.All(v => v is int i && i == 0) ? Visibility.Visible : Visibility.Collapsed;
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class BytesConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var b = System.Convert.ToDouble(value);
        return b >= 1048576 ? $"{b / 1048576:0.#} MB" : $"{Math.Max(1, b / 1024):0} KB";
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Renders text containing [[match]] markers with the match in bold, e.g. full-text search snippets.</summary>
public static class Highlight
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Highlight), new PropertyMetadata(null, OnChanged));

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string? v) => d.SetValue(TextProperty, v);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();
        var text = e.NewValue as string ?? "";
        var pos = 0;
        foreach (Match m in Regex.Matches(text, @"\[\[(.+?)\]\]"))
        {
            if (m.Index > pos) tb.Inlines.Add(new Run(text[pos..m.Index]));
            var run = new Run(m.Groups[1].Value) { FontWeight = FontWeights.Bold };
            run.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
            tb.Inlines.Add(run);
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) tb.Inlines.Add(new Run(text[pos..]));
    }
}

public sealed class InteriorKindName : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Loc.T("int_k_" + value);
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CountToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is int n && n > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
