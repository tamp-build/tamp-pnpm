using System.Text.Json;

namespace Tamp.Pnpm.V12;

/// <summary>
/// A minimal reader for <c>pnpm audit --json</c>: the severity counts from <c>metadata.vulnerabilities</c> and the
/// number of entries in <c>advisories</c>. Only those fields are read; the rest of the report is left alone.
/// </summary>
public sealed class PnpmAuditReport
{
    private static readonly string[] Order = { "info", "low", "moderate", "high", "critical" };

    /// <summary>Counts by severity (<c>info</c>, <c>low</c>, <c>moderate</c>, <c>high</c>, <c>critical</c>).</summary>
    public IReadOnlyDictionary<string, int> Vulnerabilities { get; }

    /// <summary>Number of entries under <c>advisories</c>.</summary>
    public int AdvisoryCount { get; }

    private PnpmAuditReport(IReadOnlyDictionary<string, int> counts, int advisories)
    {
        Vulnerabilities = counts;
        AdvisoryCount = advisories;
    }

    /// <summary>Total vulnerabilities across all severities.</summary>
    public int Total => Vulnerabilities.Values.Sum();

    /// <summary>Vulnerabilities at <paramref name="level"/> or worse.</summary>
    public int AtOrAbove(string level)
    {
        var i = Array.IndexOf(Order, level);
        if (i < 0) throw new ArgumentException($"Unknown severity '{level}'.", nameof(level));
        return Order.Skip(i).Sum(s => Vulnerabilities.TryGetValue(s, out var n) ? n : 0);
    }

    /// <summary>Parse the JSON printed by <c>pnpm audit --json</c>. Throws <see cref="FormatException"/> when it isn't an audit report.</summary>
    public static PnpmAuditReport Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException e) { throw new FormatException("pnpm audit output is not JSON: " + e.Message, e); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("metadata", out var meta) || meta.ValueKind != JsonValueKind.Object
                || !meta.TryGetProperty("vulnerabilities", out var vulns) || vulns.ValueKind != JsonValueKind.Object)
                throw new FormatException("pnpm audit JSON has no metadata.vulnerabilities object.");

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var s in Order)
                counts[s] = vulns.TryGetProperty(s, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

            var advisories = root.TryGetProperty("advisories", out var adv) && adv.ValueKind == JsonValueKind.Object
                ? adv.EnumerateObject().Count()
                : 0;
            return new PnpmAuditReport(counts, advisories);
        }
    }
}
