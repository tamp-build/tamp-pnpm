using System.Text;
using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Tamp.Pnpm.V12;

/// <summary>
/// Refuses to plan a dependency-resolving verb when the project's <c>package.json</c> still keeps settings in its
/// <c>pnpm</c> field that <c>pnpm-workspace.yaml</c> doesn't also carry.
/// </summary>
/// <remarks>
/// <para>
/// pnpm 11 and 12 no longer read the <c>pnpm</c> field. They print one warning and exit 0, so an install resolves
/// without those settings. The SharePoint Framework generator (1.24.x) writes its security overrides there
/// (<c>pnpm.overrides</c>: <c>qs</c>, <c>uuid</c>), so a pnpm 12 build of an SPFx project silently loses them. pnpm 9
/// and 10 still read the field, and pnpm 9 doesn't read overrides from <c>pnpm-workspace.yaml</c>, so a project shared
/// across versions keeps both copies. This guard checks they agree.
/// </para>
/// <list type="bullet">
///   <item><c>overrides</c>: every entry must be in <c>pnpm-workspace.yaml</c> <c>overrides</c> with the same value.</item>
///   <item><c>onlyBuiltDependencies</c>, <c>onlyBuiltDependenciesFile</c>, <c>neverBuiltDependencies</c>,
///         <c>ignoredBuiltDependencies</c>: replaced by <c>allowBuilds</c> in pnpm 11 (the old names are ignored
///         even in <c>pnpm-workspace.yaml</c>), so <c>allowBuilds</c> must be present.</item>
///   <item>Any other key: a key of the same name must be present in <c>pnpm-workspace.yaml</c>.</item>
/// </list>
/// </remarks>
internal static class PnpmFieldGuard
{
    internal const string ManifestFile = "package.json";
    internal const string WorkspaceFile = "pnpm-workspace.yaml";

    private static readonly HashSet<string> ReplacedByAllowBuilds = new(StringComparer.Ordinal)
    {
        "onlyBuiltDependencies", "onlyBuiltDependenciesFile", "neverBuiltDependencies", "ignoredBuiltDependencies",
    };

    /// <summary>Throws <see cref="InvalidOperationException"/> when pnpm 12 would ignore settings the project relies on.</summary>
    /// <param name="startDirectory">The directory pnpm will run in.</param>
    /// <param name="verb">The pnpm verb, for the message.</param>
    public static void Check(string startDirectory, string verb)
    {
        var problems = Find(startDirectory, out var root, out var missingOverrides, out var workspaceExists);
        if (problems.Count == 0) return;
        throw new InvalidOperationException(Message(verb, root!, problems, missingOverrides, workspaceExists));
    }

    /// <summary>The problems found, empty when there's nothing to report (or nothing that can be read).</summary>
    internal static List<string> Find(
        string startDirectory, out string? root, out List<KeyValuePair<string, string>> missingOverrides, out bool workspaceExists)
    {
        var problems = new List<string>();
        missingOverrides = new List<KeyValuePair<string, string>>();
        workspaceExists = false;
        root = FindRoot(startDirectory);
        if (root is null) return problems;

        var pnpmField = ReadPnpmField(Path.Combine(root, ManifestFile));
        if (pnpmField is null) return problems;

        var workspacePath = Path.Combine(root, WorkspaceFile);
        workspaceExists = File.Exists(workspacePath);
        YamlMappingNode? workspace = null;
        if (workspaceExists)
        {
            if (!TryReadYamlMapping(workspacePath, out workspace)) return problems; // unreadable: pnpm reports it better than we can
        }

        foreach (var setting in pnpmField.Value.EnumerateObject())
        {
            if (setting.NameEquals("overrides"))
            {
                CheckOverrides(setting.Value, workspace, problems, missingOverrides);
            }
            else if (ReplacedByAllowBuilds.Contains(setting.Name))
            {
                if (!HasKey(workspace, "allowBuilds"))
                    problems.Add($"pnpm.{setting.Name}: replaced by allowBuilds in pnpm 11 (the old name is ignored in {WorkspaceFile} too); add allowBuilds to {WorkspaceFile}.");
            }
            else if (!HasKey(workspace, setting.Name))
            {
                problems.Add($"pnpm.{setting.Name}: not in {WorkspaceFile}; move it there (see https://pnpm.io/settings).");
            }
        }
        return problems;
    }

    private static void CheckOverrides(
        JsonElement overrides, YamlMappingNode? workspace, List<string> problems, List<KeyValuePair<string, string>> missing)
    {
        if (overrides.ValueKind != JsonValueKind.Object) return;
        var wsOverrides = Child(workspace, "overrides") as YamlMappingNode;
        foreach (var entry in overrides.EnumerateObject())
        {
            var selector = entry.Name.Trim();
            var want = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString()! : entry.Value.GetRawText();
            var have = ScalarValue(wsOverrides, selector);
            if (have is null)
            {
                problems.Add($"pnpm.overrides \"{selector}\": \"{want}\" is missing from {WorkspaceFile} overrides.");
                missing.Add(new(selector, want));
            }
            else if (!string.Equals(have, want, StringComparison.Ordinal))
            {
                problems.Add($"pnpm.overrides \"{selector}\": \"{want}\" differs from {WorkspaceFile} overrides (\"{have}\"); make them agree.");
            }
        }
    }

    /// <summary>The workspace root pnpm will use: the nearest ancestor with <c>pnpm-workspace.yaml</c>, else the nearest with <c>package.json</c>.</summary>
    internal static string? FindRoot(string startDirectory)
    {
        string? full;
        try { full = Path.GetFullPath(startDirectory); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }

        for (var d = full; d is not null; d = Path.GetDirectoryName(d))
            if (File.Exists(Path.Combine(d, WorkspaceFile))) return d;
        for (var d = full; d is not null; d = Path.GetDirectoryName(d))
            if (File.Exists(Path.Combine(d, ManifestFile))) return d;
        return null;
    }

    /// <summary>The non-empty <c>pnpm</c> object of <paramref name="manifestPath"/>, or null (missing, empty, or unreadable).</summary>
    private static JsonElement? ReadPnpmField(string manifestPath)
    {
        if (!File.Exists(manifestPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("pnpm", out var field)
                || field.ValueKind != JsonValueKind.Object
                || !field.EnumerateObject().Any())
                return null;
            return field.Clone();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null; // pnpm itself will report a broken manifest
        }
    }

    private static bool TryReadYamlMapping(string path, out YamlMappingNode? mapping)
    {
        mapping = null;
        try
        {
            using var reader = new StreamReader(path);
            var stream = new YamlStream();
            stream.Load(reader);
            if (stream.Documents.Count == 0) { mapping = new YamlMappingNode(); return true; } // empty file
            switch (stream.Documents[0].RootNode)
            {
                case YamlMappingNode m: mapping = m; return true;
                case YamlScalarNode s when string.IsNullOrEmpty(s.Value) || s.Value == "~" || s.Value == "null":
                    mapping = new YamlMappingNode(); return true;
                default: return false;
            }
        }
        catch (Exception e) when (e is YamlException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static YamlNode? Child(YamlMappingNode? map, string key)
    {
        if (map is null) return null;
        foreach (var kv in map.Children)
            if (kv.Key is YamlScalarNode k && string.Equals(k.Value?.Trim(), key, StringComparison.Ordinal)) return kv.Value;
        return null;
    }

    private static bool HasKey(YamlMappingNode? map, string key) => Child(map, key) is not null;

    private static string? ScalarValue(YamlMappingNode? map, string key) => (Child(map, key) as YamlScalarNode)?.Value;

    private static string Message(
        string verb, string root, List<string> problems, List<KeyValuePair<string, string>> missingOverrides, bool workspaceExists)
    {
        var sb = new StringBuilder();
        sb.Append("pnpm ").Append(verb).Append(": pnpm 12 does not read the \"pnpm\" field of ")
          .Append(Path.Combine(root, ManifestFile))
          .AppendLine(". It would warn once, run without these settings and exit 0:");
        foreach (var p in problems) sb.Append("  - ").AppendLine(p);

        if (missingOverrides.Count > 0)
        {
            sb.Append("Add to ").Append(Path.Combine(root, WorkspaceFile)).AppendLine(":");
            if (!workspaceExists)
                sb.AppendLine("  packages:").AppendLine("    - .");
            sb.AppendLine("  overrides:");
            foreach (var kv in missingOverrides)
                sb.Append("    ").Append(YamlString(kv.Key)).Append(": ").AppendLine(YamlString(kv.Value));
        }

        sb.AppendLine("Keep the package.json copy while anyone builds with pnpm 10 or older: pnpm 9 doesn't read overrides from pnpm-workspace.yaml.");
        sb.Append("To run anyway, set AllowIgnoredPnpmField.");
        return sb.ToString();
    }

    /// <summary>A double-quoted YAML scalar (always safe for selectors like <c>@scope/pkg&gt;dep</c> and ranges like <c>&gt;=1</c>).</summary>
    internal static string YamlString(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
