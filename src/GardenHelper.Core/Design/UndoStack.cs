namespace GardenHelper.Core.Design;

/// <summary>One reversible edit. Undo and Redo may touch persistence, so they are asynchronous.</summary>
public sealed record UndoEntry(string Description, Func<Task> Undo, Func<Task> Redo);

public sealed class UndoStack
{
    private readonly List<UndoEntry> _done = new();
    private readonly Stack<UndoEntry> _undone = new();
    public int Limit { get; init; } = 200;

    public bool CanUndo => _done.Count > 0;
    public bool CanRedo => _undone.Count > 0;
    public string? UndoText => _done.Count > 0 ? _done[^1].Description : null;
    public string? RedoText => _undone.Count > 0 ? _undone.Peek().Description : null;
    public event Action? Changed;

    /// <summary>Records an edit that has already been applied. Clears the redo history.</summary>
    public void Push(UndoEntry entry)
    {
        _done.Add(entry);
        if (_done.Count > Limit) _done.RemoveAt(0);
        _undone.Clear();
        Changed?.Invoke();
    }

    public async Task UndoAsync()
    {
        if (!CanUndo) return;
        var e = _done[^1]; _done.RemoveAt(_done.Count - 1);
        await e.Undo();
        _undone.Push(e);
        Changed?.Invoke();
    }

    public async Task RedoAsync()
    {
        if (!CanRedo) return;
        var e = _undone.Pop();
        await e.Redo();
        _done.Add(e);
        Changed?.Invoke();
    }

    public void Clear() { _done.Clear(); _undone.Clear(); Changed?.Invoke(); }
}
