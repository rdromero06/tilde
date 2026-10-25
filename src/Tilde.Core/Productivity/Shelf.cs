namespace Tilde.Core.Productivity;

public enum ShelfKind
{
    File,
    Folder,
    Text,
}

/// <summary>
/// Un elemento del estante. Para archivos y carpetas se guarda solo la ruta (nunca una copia);
/// para texto, el propio texto.
/// </summary>
public sealed class ShelfItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ShelfKind Kind { get; set; }

    public string? Path { get; set; }

    public string? Text { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    /// <summary>Nombre que se muestra: el del archivo o carpeta, o el principio del texto.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => Kind == ShelfKind.Text
        ? (Text ?? string.Empty).ReplaceLineEndings(" ").Trim()
        : System.IO.Path.GetFileName(Path?.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Path ?? string.Empty;
}

/// <summary>Contenido del estante (shelf.json).</summary>
public sealed class ShelfData
{
    public List<ShelfItem> Items { get; set; } = [];
}

/// <summary>
/// Estante de archivos: añadir (sin duplicar rutas), quitar y selección múltiple con las reglas
/// habituales de Windows: clic selecciona uno, Ctrl+clic alterna y Mayús+clic selecciona un rango.
/// </summary>
public sealed class ShelfList(ShelfData data)
{
    /// <summary>Máximo de elementos (los más antiguos salen al superarlo).</summary>
    public const int MaxItems = 60;

    private readonly HashSet<Guid> _selected = [];
    private Guid? _anchor;

    public ShelfData Data { get; } = data;

    public IReadOnlyList<ShelfItem> Items => Data.Items;

    public IReadOnlyCollection<Guid> Selected => _selected;

    public bool IsSelected(Guid id) => _selected.Contains(id);

    /// <summary>Añade rutas. Si una ya estaba, se mueve al final. Devuelve cuántas se añadieron.</summary>
    public int AddPaths(IEnumerable<string> paths, Func<string, bool> isDirectory, DateTimeOffset now)
    {
        int added = 0;
        foreach (var raw in paths)
        {
            var path = raw.Trim();
            if (path.Length == 0)
            {
                continue;
            }
            Data.Items.RemoveAll(i => i.Kind != ShelfKind.Text && string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            Data.Items.Add(new ShelfItem { Kind = isDirectory(path) ? ShelfKind.Folder : ShelfKind.File, Path = path, AddedAt = now });
            added++;
        }
        Trim();
        return added;
    }

    public ShelfItem? AddText(string text, DateTimeOffset now)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return null;
        }
        Data.Items.RemoveAll(i => i.Kind == ShelfKind.Text && i.Text == text);
        var item = new ShelfItem { Kind = ShelfKind.Text, Text = text.Length > 20_000 ? text[..20_000] : text, AddedAt = now };
        Data.Items.Add(item);
        Trim();
        return item;
    }

    public void Remove(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        Data.Items.RemoveAll(i => set.Contains(i.Id));
        _selected.ExceptWith(set);
        if (_anchor is { } a && set.Contains(a))
        {
            _anchor = null;
        }
    }

    public void Clear()
    {
        Data.Items.Clear();
        _selected.Clear();
        _anchor = null;
    }

    /// <summary>Aplica un clic de selección sobre un elemento.</summary>
    public void Click(Guid id, bool ctrl, bool shift)
    {
        int index = Data.Items.FindIndex(i => i.Id == id);
        if (index < 0)
        {
            return;
        }
        if (shift && _anchor is { } anchor && Data.Items.FindIndex(i => i.Id == anchor) is int from and >= 0)
        {
            if (!ctrl)
            {
                _selected.Clear();
            }
            int lo = Math.Min(from, index), hi = Math.Max(from, index);
            for (int i = lo; i <= hi; i++)
            {
                _selected.Add(Data.Items[i].Id);
            }
            return; // El ancla no cambia con Mayús.
        }
        if (ctrl)
        {
            if (!_selected.Remove(id))
            {
                _selected.Add(id);
            }
        }
        else
        {
            _selected.Clear();
            _selected.Add(id);
        }
        _anchor = id;
    }

    public void ClearSelection()
    {
        _selected.Clear();
        _anchor = null;
    }

    /// <summary>
    /// Qué se arrastra al empezar a arrastrar <paramref name="id"/>: la selección entera si el
    /// elemento estaba seleccionado; si no, solo ese (y pasa a ser la selección).
    /// </summary>
    public IReadOnlyList<ShelfItem> DragSet(Guid id)
    {
        if (!_selected.Contains(id))
        {
            Click(id, ctrl: false, shift: false);
        }
        return [.. Data.Items.Where(i => _selected.Contains(i.Id))];
    }

    private void Trim()
    {
        while (Data.Items.Count > MaxItems)
        {
            _selected.Remove(Data.Items[0].Id);
            Data.Items.RemoveAt(0);
        }
    }
}
