using Tamp;
using Tamp.Pnpm.V12;
using Xunit;

namespace Tamp.Pnpm.V12.Tests;

/// <summary>
/// The <c>package.json</c> <c>pnpm</c> field guard (#4). pnpm 12 ignores that field with one warning and exit 0, so the
/// verbs that resolve dependencies refuse to plan while it holds settings <c>pnpm-workspace.yaml</c> doesn't carry.
/// </summary>
public sealed class PnpmFieldGuardTests
{
    /// <summary>What the SharePoint Framework 1.24.x generator writes for a pnpm solution.</summary>
    private const string SpfxManifest = """
        {
          "name": "spfx-solution",
          "version": "0.0.1",
          "private": true,
          "pnpm": { "overrides": { "qs": ">=6.15.2", "uuid": ">=11.1.1" } }
        }
        """;

    private static readonly Tool Fake = PnpmTests.FakeTool();

    private static string Project(TempDir tmp, string manifest, string? workspaceYaml = null, string name = "proj")
    {
        var dir = tmp.Dir(name);
        File.WriteAllText(Path.Combine(dir, "package.json"), manifest);
        if (workspaceYaml is not null) File.WriteAllText(Path.Combine(dir, "pnpm-workspace.yaml"), workspaceYaml);
        return dir;
    }

    private static InvalidOperationException Refused(string dir) =>
        Assert.Throws<InvalidOperationException>(() => Pnpm.Install(Fake, s => s.SetWorkingDirectory(dir)));

    private static void Allowed(string dir) => Pnpm.Install(Fake, s => s.SetWorkingDirectory(dir));

    // ---- the SPFx case ----

    [Fact]
    public void Spfx_Overrides_Without_Workspace_File_Are_Refused_With_The_Fix()
    {
        using var tmp = new TempDir();
        var dir = Project(tmp, SpfxManifest);
        var e = Refused(dir);

        Assert.Contains("pnpm install: pnpm 12 does not read the \"pnpm\" field", e.Message);
        Assert.Contains("\"qs\": \">=6.15.2\" is missing", e.Message);
        Assert.Contains("\"uuid\": \">=11.1.1\" is missing", e.Message);
        Assert.Contains(Path.Combine(dir, "pnpm-workspace.yaml"), e.Message);
        Assert.Contains("  packages:\n    - .", e.Message.Replace("\r\n", "\n"));
        Assert.Contains("    \"qs\": \">=6.15.2\"", e.Message);
        Assert.Contains("AllowIgnoredPnpmField", e.Message);
    }

    [Fact]
    public void Suggested_Snippet_Satisfies_The_Guard()
    {
        using var tmp = new TempDir();
        var dir = Project(tmp, """{ "name": "x", "pnpm": { "overrides": { " @scope/a>b ": "^1.2.0 || >=2", "c\"q": "npm:d@1" } } }""");
        var e = Refused(dir);

        var lines = e.Message.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("Add to ", StringComparison.Ordinal)) + 1;
        var snippet = lines.Skip(start).TakeWhile(l => l.StartsWith("  ", StringComparison.Ordinal)).Select(l => l[2..]);
        File.WriteAllText(Path.Combine(dir, "pnpm-workspace.yaml"), string.Join('\n', snippet) + "\n");

        Allowed(dir);
    }

    [Theory]
    [InlineData("packages:\n  - .\noverrides:\n  qs: \">=6.15.2\"\n  uuid: \">=11.1.1\"\n")]
    [InlineData("overrides:\n  qs: '>=6.15.2'\n  uuid: '>=11.1.1'\n  extra: 1.0.0\n")]
    [InlineData("overrides: { qs: \">=6.15.2\", uuid: \">=11.1.1\" }\n")]
    public void Matching_Workspace_Overrides_Are_Allowed(string yaml)
    {
        using var tmp = new TempDir();
        Allowed(Project(tmp, SpfxManifest, yaml));
    }

    [Fact]
    public void A_Partly_Migrated_Workspace_Names_Only_What_Is_Missing()
    {
        using var tmp = new TempDir();
        var e = Refused(Project(tmp, SpfxManifest, "overrides:\n  qs: \">=6.15.2\"\n"));
        Assert.Contains("\"uuid\"", e.Message);
        Assert.DoesNotContain("\"qs\": \">=6.15.2\" is missing", e.Message);
        Assert.DoesNotContain("packages:", e.Message); // the file exists already
    }

    [Fact]
    public void Diverged_Values_Are_Refused()
    {
        // e.g. a newer SPFx generator bumped package.json, and pnpm-workspace.yaml still has the old pin
        using var tmp = new TempDir();
        var e = Refused(Project(tmp, SpfxManifest, "overrides:\n  qs: \">=6.14.0\"\n  uuid: \">=11.1.1\"\n"));
        Assert.Contains("\"qs\": \">=6.15.2\" differs from pnpm-workspace.yaml overrides (\">=6.14.0\")", e.Message);
    }

    [Fact]
    public void Empty_Overrides_Block_In_Workspace_Counts_As_Missing()
    {
        using var tmp = new TempDir();
        var e = Refused(Project(tmp, SpfxManifest, "overrides:\n"));
        Assert.Contains("\"qs\": \">=6.15.2\" is missing", e.Message);
    }

    // ---- other keys ----

    [Theory]
    [InlineData("onlyBuiltDependencies")]
    [InlineData("neverBuiltDependencies")]
    [InlineData("onlyBuiltDependenciesFile")]
    [InlineData("ignoredBuiltDependencies")]
    public void Build_Lists_Need_AllowBuilds(string key)
    {
        using var tmp = new TempDir();
        var manifest = $$"""{ "name": "x", "pnpm": { "{{key}}": ["esbuild"] } }""";

        // Copying the old name across isn't enough: pnpm 11+ ignores it in pnpm-workspace.yaml too.
        var e = Refused(Project(tmp, manifest, $"{key}:\n  - esbuild\n", "old-name"));
        Assert.Contains($"pnpm.{key}: replaced by allowBuilds", e.Message);

        Allowed(Project(tmp, manifest, "allowBuilds:\n  esbuild: true\n", "migrated"));
    }

    [Fact]
    public void Other_Keys_Need_The_Same_Key_In_The_Workspace()
    {
        using var tmp = new TempDir();
        var manifest = """{ "name": "x", "pnpm": { "packageExtensions": { "a": { "peerDependencies": { "b": "*" } } } } }""";

        var e = Refused(Project(tmp, manifest, null, "missing"));
        Assert.Contains("pnpm.packageExtensions: not in pnpm-workspace.yaml", e.Message);
        Assert.DoesNotContain("Add to ", e.Message); // the snippet is only for overrides

        Allowed(Project(tmp, manifest, "packageExtensions:\n  a:\n    peerDependencies:\n      b: '*'\n", "present"));
    }

    // ---- nothing to check ----

    [Theory]
    [InlineData("""{ "name": "x" }""")]
    [InlineData("""{ "name": "x", "pnpm": {} }""")]
    [InlineData("""{ "name": "x", "pnpm": "not an object" }""")]
    [InlineData("""{ "name": "x", "pnpm": { "overrides": """)] // broken JSON: pnpm reports it
    public void Manifests_Without_Pnpm_Settings_Are_Allowed(string manifest)
    {
        using var tmp = new TempDir();
        Allowed(Project(tmp, manifest));
    }

    [Fact]
    public void Unparseable_Workspace_File_Is_Left_To_Pnpm()
    {
        using var tmp = new TempDir();
        Allowed(Project(tmp, SpfxManifest, "overrides: [unterminated\n"));
    }

    [Fact]
    public void No_Manifest_Anywhere_Is_Allowed()
    {
        using var tmp = new TempDir();
        Allowed(tmp.Dir("empty"));
    }

    // ---- where it looks ----

    [Fact]
    public void Workspace_Root_Is_Found_From_A_Member_Package()
    {
        using var tmp = new TempDir();
        var root = Project(tmp, SpfxManifest, "packages:\n  - packages/*\n");
        var member = tmp.Dir("proj", "packages", "web");
        File.WriteAllText(Path.Combine(member, "package.json"), """{ "name": "web" }""");

        var e = Refused(member);
        Assert.Contains(Path.Combine(root, "package.json"), e.Message);
    }

    [Fact]
    public void Tool_Working_Directory_Is_Used_When_Settings_Have_None()
    {
        using var tmp = new TempDir();
        var dir = Project(tmp, SpfxManifest);
        var tool = new Tool(AbsolutePath.Create("/fake/pnpm"), dir);
        Assert.Throws<InvalidOperationException>(() => Pnpm.Install(tool));
    }

    // ---- which verbs ----

    public static TheoryData<string> CheckedVerbs => new() { "install", "add", "remove", "deploy", "fetch" };

    private static CommandPlan PlanVerb(string verb, string dir, bool allow) => verb switch
    {
        "install" => Pnpm.Install(Fake, s => s.SetWorkingDirectory(dir).SetAllowIgnoredPnpmField(allow)),
        "add" => Pnpm.Add(Fake, s => s.SetWorkingDirectory(dir).AddPackage("left-pad@1.3.0").SetAllowIgnoredPnpmField(allow)),
        "remove" => Pnpm.Remove(Fake, s => s.SetWorkingDirectory(dir).AddPackage("left-pad").SetAllowIgnoredPnpmField(allow)),
        "deploy" => Pnpm.Deploy(Fake, s => s.SetWorkingDirectory(dir).AddFilter("app").SetDirectory("out").SetAllowIgnoredPnpmField(allow)),
        "fetch" => Pnpm.Fetch(Fake, s => s.SetWorkingDirectory(dir).SetAllowIgnoredPnpmField(allow)),
        _ => throw new ArgumentOutOfRangeException(nameof(verb)),
    };

    [Theory]
    [MemberData(nameof(CheckedVerbs))]
    public void Resolving_Verbs_Are_Checked(string verb)
    {
        using var tmp = new TempDir();
        var dir = Project(tmp, SpfxManifest);
        var e = Assert.Throws<InvalidOperationException>(() => PlanVerb(verb, dir, allow: false));
        Assert.StartsWith($"pnpm {verb}: ", e.Message);
    }

    [Theory]
    [MemberData(nameof(CheckedVerbs))]
    public void AllowIgnoredPnpmField_Skips_The_Check(string verb)
    {
        using var tmp = new TempDir();
        var plan = PlanVerb(verb, Project(tmp, SpfxManifest), allow: true);
        Assert.Equal(verb, plan.Arguments[0]);
    }

    [Fact]
    public void Corepack_Message_Names_The_Pnpm_Verb()
    {
        using var tmp = new TempDir();
        var dir = Project(tmp, SpfxManifest);
        var e = Assert.Throws<InvalidOperationException>(() => Pnpm.Install(Fake, s => s.SetWorkingDirectory(dir).SetUseCorepack()));
        Assert.StartsWith("pnpm install: ", e.Message);
    }

    [Fact]
    public void Other_Verbs_Are_Not_Checked()
    {
        using var tmp = new TempDir();
        var dir = Project(tmp, SpfxManifest);
        Pnpm.Run(Fake, s => s.SetWorkingDirectory(dir).SetScript("build"));
        Pnpm.Exec(Fake, s => s.SetWorkingDirectory(dir).SetCommand("tsc"));
        Pnpm.Pack(Fake, s => s.SetWorkingDirectory(dir));
        Pnpm.Audit(Fake, s => s.SetWorkingDirectory(dir));
        Pnpm.SetVersion(Fake, s => s.SetWorkingDirectory(dir).SetVersion("1.0.0"));
        Pnpm.Raw(new Tool(AbsolutePath.Create("/fake/pnpm"), dir), "install");
    }

    [Fact]
    public void Argument_Validation_Still_Comes_First()
    {
        using var tmp = new TempDir();
        var dir = Project(tmp, SpfxManifest);
        var e = Assert.Throws<InvalidOperationException>(() =>
            Pnpm.Install(Fake, s => s.SetWorkingDirectory(dir).SetProduction().SetDevelopment()));
        Assert.Contains("mutually exclusive", e.Message);
    }
}
