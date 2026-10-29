using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace GardenHelper.App.Infrastructure;

/// <summary>UI strings in English and Romanian. Add a language by adding a dictionary to <see cref="Strings.All"/>.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Language { get; private set; } = "en";
    public CultureInfo Culture { get; private set; } = new("en-GB");

    public string this[string key] => Lookup(key);

    public static string T(string key) => Instance.Lookup(key);
    public static string F(string key, params object[] args) => string.Format(Instance.Culture, Instance.Lookup(key), args);

    /// <summary>Translates an enum-like content value such as sun=full-partial; unknown values are shown as written.</summary>
    public static string Enum(string group, string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var key = group + "_" + value.Replace('-', '_');
        return Strings.TryGet(Instance.Language, key, out var s) ? s : value;
    }

    public void SetLanguage(string language)
    {
        if (!Strings.All.ContainsKey(language)) language = "en";
        Language = language;
        Culture = language == "ro" ? new CultureInfo("ro-RO") : new CultureInfo("en-GB");
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }

    private string Lookup(string key) =>
        Strings.TryGet(Language, key, out var s) ? s : Strings.TryGet("en", key, out var en) ? en : "⟦" + key + "⟧";
}

/// <summary>XAML: Text="{loc:L nav_home}" — updates live when the language changes.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LExtension : MarkupExtension
{
    public LExtension(string key) => Key = key;
    public string Key { get; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

/// <summary>Formats lengths in the unit system chosen in Settings.</summary>
public static class UnitFormatter
{
    public static bool Imperial { get; set; }

    public static string Length(int cm) => Imperial ? $"{Math.Round(cm / 2.54)} in" : cm >= 200 && cm % 50 == 0 ? $"{cm / 100.0:0.#} m" : $"{cm} cm";

    public static string Range(Core.Knowledge.IntRange? r) =>
        r is null ? "" : r.Min == r.Max ? Length(r.Min) : Imperial ? $"{Math.Round(r.Min / 2.54)}–{Math.Round(r.Max / 2.54)} in" : $"{r.Min}–{r.Max} cm";
}
