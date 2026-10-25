using Tilde.Core;
using Tilde.Core.Productivity;
using Tilde.Core.Storage;
using Tilde.Platform.Shell;

namespace Tilde.App.Productivity;

/// <summary>
/// Estante de archivos persistente (shelf.json). Solo guarda rutas y textos: nunca copia ni mueve
/// archivos. La selección vive solo en memoria.
/// </summary>
internal sealed class ShelfService
{
    private readonly JsonStore<ShelfData> _store;

    public ShelfService()
    {
        _store = new JsonStore<ShelfData>(Path.Combine(AppPaths.DataDirectory, "shelf.json"), DataJsonContext.Default.ShelfData);
        List = new ShelfList(_store.Load());
    }

    public ShelfList List { get; }

    /// <summary>Cambió el contenido o la selección.</summary>
    public event Action? Changed;

    public void AddDropped(DroppedData data)
    {
        var now = DateTimeOffset.Now;
        if (data.Paths.Count > 0)
        {
            List.AddPaths(data.Paths, Directory.Exists, now);
        }
        else if (data.Text is { } text)
        {
            List.AddText(text, now);
        }
        Commit();
    }

    public void Remove(IEnumerable<Guid> ids)
    {
        List.Remove(ids);
        Commit();
    }

    public void Clear()
    {
        List.Clear();
        Commit();
    }

    public void Click(Guid id, bool ctrl, bool shift)
    {
        List.Click(id, ctrl, shift);
        Changed?.Invoke();
    }

    public void ClearSelection()
    {
        if (List.Selected.Count > 0)
        {
            List.ClearSelection();
            Changed?.Invoke();
        }
    }

    public void SelectAll()
    {
        foreach (var item in List.Items)
        {
            if (!List.IsSelected(item.Id))
            {
                List.Click(item.Id, ctrl: true, shift: false);
            }
        }
        Changed?.Invoke();
    }

    private void Commit()
    {
        _store.Save(List.Data);
        Changed?.Invoke();
    }
}
