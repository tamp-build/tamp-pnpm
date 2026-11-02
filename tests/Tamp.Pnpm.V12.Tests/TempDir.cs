namespace Tamp.Pnpm.V12.Tests;

/// <summary>A throwaway directory tree.</summary>
internal sealed class TempDir : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "tamp-pnpm-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Root);

    public string Dir(params string[] parts)
    {
        var p = Path.Combine(new[] { Root }.Concat(parts).ToArray());
        Directory.CreateDirectory(p);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

internal static class Fixture
{
    public static string Path(params string[] parts) =>
        System.IO.Path.Combine(new[] { AppContext.BaseDirectory, "Fixtures" }.Concat(parts).ToArray());
}
