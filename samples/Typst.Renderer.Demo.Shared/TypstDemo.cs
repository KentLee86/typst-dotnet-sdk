using System.Collections.ObjectModel;
using System.Reflection;

namespace Typst.Renderer.Demo.Shared;

/// <summary>A renderer example and all of its in-memory project files.</summary>
public sealed class TypstDemo
{
    private readonly IReadOnlyDictionary<string, DemoFile> _files;

    internal TypstDemo(
        string id,
        string displayName,
        string description,
        string mainFileName,
        params string[] additionalFileNames)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        MainPath = $"{id}/{mainFileName}";

        var names = new[] { mainFileName }.Concat(additionalFileNames);
        _files = new ReadOnlyDictionary<string, DemoFile>(names.ToDictionary(
            fileName => $"{id}/{fileName}",
            fileName => LoadFile(id, fileName),
            StringComparer.Ordinal));
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string MainPath { get; }
    public string Source => _files[MainPath].Text
        ?? throw new InvalidOperationException($"Demo main file '{MainPath}' is not text.");
    public IReadOnlyCollection<string> Paths => _files.Keys.ToArray();

    /// <summary>Creates a fresh in-memory project, optionally replacing its main source.</summary>
    public TypstProject CreateProject(string? mainSource = null)
    {
        var builder = new TypstProjectBuilder().WithMainFile(MainPath);
        foreach (var (path, file) in _files)
        {
            if (file.Text is not null)
                builder.AddText(path, path == MainPath && mainSource is not null ? mainSource : file.Text);
            else
                builder.AddBinary(path, file.Data);
        }
        return builder.Build();
    }

    public override string ToString() => DisplayName;

    private static DemoFile LoadFile(string id, string fileName)
    {
        var resourceDirectory = id.Replace('-', '_');
        var resourceName = $"Typst.Renderer.Demo.Shared.Resources.{resourceDirectory}.{fileName}";
        using var stream = typeof(TypstDemo).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded demo resource not found: {resourceName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var data = memory.ToArray();
        return Path.GetExtension(fileName).Equals(".typ", StringComparison.OrdinalIgnoreCase)
            ? new DemoFile(System.Text.Encoding.UTF8.GetString(data), data)
            : new DemoFile(null, data);
    }

    private sealed record DemoFile(string? Text, byte[] Data);
}
