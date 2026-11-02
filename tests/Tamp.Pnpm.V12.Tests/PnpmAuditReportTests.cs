using Tamp.Pnpm.V12;
using Xunit;

namespace Tamp.Pnpm.V12.Tests;

public sealed class PnpmAuditReportTests
{
    // Captured from `pnpm audit --json` (pnpm 12.16.1) against a project with is-odd 3.0.1.
    private const string Clean = """
        { "advisories": {}, "metadata": { "vulnerabilities": { "info": 0, "low": 0, "moderate": 0, "high": 0, "critical": 0 },
          "dependencies": 2, "devDependencies": 0, "optionalDependencies": 0, "totalDependencies": 2 } }
        """;

    // Hand-written (no vulnerable package was available to capture): same envelope, non-zero counts.
    private const string Findings = """
        { "advisories": { "1": { "id": 1 }, "2": { "id": 2 } },
          "metadata": { "vulnerabilities": { "info": 0, "low": 3, "moderate": 2, "high": 1, "critical": 0 } } }
        """;

    [Fact]
    public void Clean_Report()
    {
        var r = PnpmAuditReport.Parse(Clean);
        Assert.Equal(0, r.Total);
        Assert.Equal(0, r.AdvisoryCount);
        Assert.Equal(0, r.AtOrAbove("info"));
    }

    [Fact]
    public void Findings_Report()
    {
        var r = PnpmAuditReport.Parse(Findings);
        Assert.Equal(6, r.Total);
        Assert.Equal(2, r.AdvisoryCount);
        Assert.Equal(3, r.AtOrAbove("moderate"));
        Assert.Equal(1, r.AtOrAbove("high"));
        Assert.Equal(0, r.AtOrAbove("critical"));
        Assert.Equal(3, r.Vulnerabilities["low"]);
    }

    [Fact]
    public void Missing_Fields_Default_To_Zero()
    {
        var r = PnpmAuditReport.Parse("""{ "metadata": { "vulnerabilities": { "high": 4 } } }""");
        Assert.Equal(4, r.AtOrAbove("high"));
        Assert.Equal(0, r.AdvisoryCount);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "metadata": {} }""")]
    [InlineData("""{ "metadata": { "vulnerabilities": 3 } }""")]
    public void Bad_Input_Is_FormatException(string json) =>
        Assert.Throws<FormatException>(() => PnpmAuditReport.Parse(json));

    [Fact]
    public void Unknown_Severity_Throws() =>
        Assert.Throws<ArgumentException>(() => PnpmAuditReport.Parse(Clean).AtOrAbove("severe"));
}
