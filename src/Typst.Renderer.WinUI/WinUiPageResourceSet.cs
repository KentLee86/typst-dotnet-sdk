namespace Typst.Renderer.WinUI;

/// <summary>Small testable owner for the native UI resources associated with visible pages.</summary>
internal sealed class WinUiPageResourceSet<T> where T : IDisposable
{
    private readonly Dictionary<int, T> _resources = [];

    public int Count => _resources.Count;
    public IReadOnlyCollection<int> PageIndices => _resources.Keys.Order().ToArray();

    public T GetOrAdd(int pageIndex, Func<int, T> factory)
    {
        if (_resources.TryGetValue(pageIndex, out var resource)) return resource;
        resource = factory(pageIndex);
        _resources.Add(pageIndex, resource);
        return resource;
    }

    public void RetainOnly(IEnumerable<int> pageIndices)
    {
        var wanted = pageIndices.ToHashSet();
        foreach (var pageIndex in _resources.Keys.Where(key => !wanted.Contains(key)).ToArray())
        {
            _resources[pageIndex].Dispose();
            _resources.Remove(pageIndex);
        }
    }

    public void Clear()
    {
        foreach (var resource in _resources.Values) resource.Dispose();
        _resources.Clear();
    }
}
