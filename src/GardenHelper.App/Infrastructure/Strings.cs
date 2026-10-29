namespace GardenHelper.App.Infrastructure;

/// <summary>
/// All UI text, one file per area in the Strings folder, each entry holding English and Romanian side by side.
/// To add a language, add a column to the entries and a dictionary under its two-letter code; missing keys fall back to English.
/// Enum-like content values use the key pattern group_value (hyphens become underscores).
/// </summary>
public static partial class Strings
{
    public static IReadOnlyDictionary<string, Dictionary<string, string>> All { get; } = Build();

    public static bool TryGet(string language, string key, out string value)
    {
        value = "";
        return All.TryGetValue(language, out var d) && d.TryGetValue(key, out value!);
    }

    private static Dictionary<string, Dictionary<string, string>> Build()
    {
        var en = new Dictionary<string, string>(); var ro = new Dictionary<string, string>();
        foreach (var area in new[] { Animals, Common, Design, Devices, Garden, Library, Misc, Plants, Today })
            foreach (var (key, e, r) in area) { en.Add(key, e); ro.Add(key, r); }
        return new Dictionary<string, Dictionary<string, string>> { ["en"] = en, ["ro"] = ro };
    }
}
