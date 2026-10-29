using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Design;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;

namespace GardenHelper.App.ViewModels;

// Plant icons, hover details, zoom-to-element, duplicate and lock for beds and objects.
public sealed partial class DesignerViewModel
{
    /// <summary>Lays the bed's plants out on the real spacing grid (one strip per planting) and prepares the hover summary.</summary>
    private void RebuildMarkers(BedItem item)
    {
        var active = VisiblePlantings(item.Bed).ToList();
        var lang = Loc.Instance.Language; var culture = Loc.Instance.Culture;
        var tip = new List<string> { $"{item.Name} · {item.SizeText}" };
        var markers = new List<PlantMarker>();
        var growing = !item.IsStructure && item.Kind is not ("path" or "pond" or "compost" or "structure");
        for (var i = 0; i < active.Count; i++)
        {
            var p = active[i]; var plant = _kb.Get(p.PlantId);
            var name = plant?.DisplayName(lang) ?? p.PlantId;
            var expected = p.PlantedDate is { } d && plant?.DaysToMaturity is { } m
                ? $"  ({d.AddDays(m.Min).ToString("d MMM", culture)} – {d.AddDays(m.Max).ToString("d MMM", culture)})" : "";
            tip.Add($"{name} ×{p.Quantity} — {Loc.T("status_" + p.Status)}{expected}");
            if (!growing) continue;
            var (shape, color) = PlantIcons.For(plant);
            var spacing = Math.Clamp(plant?.SpacingCm?.Max ?? 30, 8, 400);
            var size = Math.Clamp(spacing * 0.85, 10, 140);
            var strip = item.Height / active.Count;
            var pos = PlantLayout.Positions(item.Width, strip, spacing, plant?.RowSpacingCm?.Max, Math.Min(10, spacing / 2)).ToList();
            if (pos.Count == 0 && strip >= size && item.Width >= size) pos.Add(new Pt(item.Width / 2, strip / 2));
            var take = Math.Min(Math.Max(1, p.Quantity), pos.Count);
            for (var k = 0; k < take; k++)
            {
                var at = pos[take == pos.Count ? k : (int)((long)k * pos.Count / take)];
                markers.Add(new PlantMarker(at.X, i * strip + at.Y, size, shape, color, p.Id, $"{name}" + (p.PlantedDate is { } pdt ? $" · {pdt.ToString("d MMM", culture)}" : "") + $" · {Loc.T("status_" + p.Status)}"));
            }
        }
        if (active.Count == 0) tip.Add(Loc.T("design_tip_empty"));
        item.TipText = string.Join("\n", tip);
        item.Markers = markers;
    }

    /// <summary>Frames the selected bed or object so its plants are visible in detail.</summary>
    [RelayCommand]
    private void ZoomToSelected()
    {
        if (Selected is null) return;
        var c = Selected.Corners;
        double minX = c.Min(p => p.X), maxX = c.Max(p => p.X), minY = c.Min(p => p.Y), maxY = c.Max(p => p.Y);
        var w = Math.Max(100, maxX - minX) * 1.3; var h = Math.Max(100, maxY - minY) * 1.3;
        Scale = Math.Clamp(Math.Round(Math.Min(ViewportWidth / w, ViewportHeight / h), 4), MinScale, MaxScale);
        RequestCenter((minX + maxX) / 2, (minY + maxY) / 2);
    }

    [RelayCommand]
    private Task DuplicateSelectedAsync() => RunAsync(async () =>
    {
        if (Selected is not { } s || _garden is null) return;
        var o = s.Bed;
        var bed = await _repo.SaveAsync(new GardenBed
        {
            GardenId = o.GardenId, Kind = o.Kind, Name = o.Name + " " + Loc.T("design_copy_suffix"), X = o.X + 100, Y = o.Y + 100,
            Width = o.Width, Height = o.Height, Rotation = o.Rotation, ShadePercent = o.ShadePercent, Notes = o.Notes,
        });
        var item = Wrap(bed);
        Beds.Add(item); _snapshots[bed.Id] = Capture(bed); Selected = item;
        History.Push(new UndoEntry(Loc.F("design_undo_add", item.Name), () => RemoveAsync(item), async () => item = await RecreateAsync(item)));
        StatusMessage = Loc.T("design_duplicated");
    }, busy: false);

    [RelayCommand]
    private Task ToggleLockAsync() => RunAsync(async () =>
    {
        if (Selected is not { } s) return;
        s.Locked = !s.Locked;
        await PersistAsync(s.Bed);
        StatusMessage = Loc.T(s.Locked ? "design_locked_now" : "design_unlocked_now");
    }, busy: false);
}
