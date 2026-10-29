using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Design;

namespace GardenHelper.App.ViewModels;

// Undo and redo for everything done on the building plan: adding, deleting, moving, resizing, rotating, renaming.
public sealed partial class InteriorViewModel
{
    private sealed record ItemState(double X, double Y, double W, double H, double Rot, string Name, string? Notes, int? PlantId);
    private static ItemState StateOf(InteriorItem i) => new(i.X, i.Y, i.Width, i.Height, i.Rotation, i.Name, i.Notes, i.InventoryPlantId);

    private readonly Dictionary<int, ItemState> _snap = new();
    public UndoStack History { get; } = new();

    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;
    public string UndoTip => History.UndoText is { } t ? Loc.F("design_undo_tip", t) : Loc.T("design_undo");
    public string RedoTip => History.RedoText is { } t ? Loc.F("design_redo_tip", t) : Loc.T("design_redo");

    private void InitUndo()
    {
        History.Changed += () => { OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo)); OnPropertyChanged(nameof(UndoTip)); OnPropertyChanged(nameof(RedoTip)); };
    }

    private void TakeSnapshots() { _snap.Clear(); foreach (var i in _all) _snap[i.Id] = StateOf(i); }

    private static InteriorItem Copy(InteriorItem i) => new()
    {
        FeatureId = i.FeatureId, Floor = i.Floor, Kind = i.Kind, Name = i.Name, X = i.X, Y = i.Y, Width = i.Width, Height = i.Height, Rotation = i.Rotation, InventoryPlantId = i.InventoryPlantId, Notes = i.Notes,
    };

    private async Task RefreshAfterUndoAsync() { _all = await _repo.ListAsync<InteriorItem>(q => q.Where(i => i.FeatureId == BuildingId).OrderBy(i => i.Id)); Rebuild(); }

    /// <summary>Remembers that an item was just added (it is already saved).</summary>
    private void RecordAdd(InteriorItem item)
    {
        var id = new[] { item.Id }; var template = Copy(item);
        History.Push(new UndoEntry(Loc.F("int_undo_add", NameOf(item)),
            async () => { if (_all.FirstOrDefault(x => x.Id == id[0]) is { } cur) await _repo.DeleteAsync(cur); await RefreshAfterUndoAsync(); },
            async () => { var again = await _repo.SaveAsync(Copy(template)); id[0] = again.Id; await RefreshAfterUndoAsync(); }));
    }

    private void RecordDelete(InteriorItem item)
    {
        var id = new[] { item.Id }; var template = Copy(item);
        History.Push(new UndoEntry(Loc.F("int_undo_delete", NameOf(item)),
            async () => { var again = await _repo.SaveAsync(Copy(template)); id[0] = again.Id; await RefreshAfterUndoAsync(); },
            async () => { if (_all.FirstOrDefault(x => x.Id == id[0]) is { } cur) await _repo.DeleteAsync(cur); await RefreshAfterUndoAsync(); }));
    }

    /// <summary>After an item was changed and saved: records the change (from the last remembered state) so it can be undone.</summary>
    private void RecordChange(InteriorItem item)
    {
        var now = StateOf(item);
        if (_snap.TryGetValue(item.Id, out var before) && before != now)
        {
            var id = item.Id;
            async Task Apply(ItemState s)
            {
                var cur = _all.FirstOrDefault(x => x.Id == id); if (cur is null) return;
                (cur.X, cur.Y, cur.Width, cur.Height, cur.Rotation, cur.Name, cur.Notes, cur.InventoryPlantId) = (s.X, s.Y, s.W, s.H, s.Rot, s.Name, s.Notes, s.PlantId);
                await _repo.SaveAsync(cur); await RefreshAfterUndoAsync();
            }
            History.Push(new UndoEntry(Loc.F("int_undo_change", NameOf(item)), () => Apply(before), () => Apply(now)));
        }
        _snap[item.Id] = now;
    }

    private static string NameOf(InteriorItem i) => string.IsNullOrWhiteSpace(i.Name) ? Loc.T("int_k_" + i.Kind) : i.Name;

    [RelayCommand] private Task UndoAsync() => RunAsync(() => History.UndoAsync(), busy: false);
    [RelayCommand] private Task RedoAsync() => RunAsync(() => History.RedoAsync(), busy: false);
}
