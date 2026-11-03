using System.Net;
using System.Text;
using Tamp;
using Tamp.Pnpm.V12;
using Xunit;

namespace Tamp.Pnpm.V12.Tests;

/// <summary>Skips when pnpm 12 isn't on PATH, so the suite stays green on machines without it.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Live.Pnpm is null) Skip = "pnpm 12 is not on PATH (live tests need `npm i -g pnpm@12`).";
    }
}

internal static class Live
{
    public static readonly Tool? Pnpm = Resolve();

    private static Tool? Resolve()
    {
        try
        {
            var tool = Tool.TryFromPath("pnpm");
            if (tool is null) return null;
            var r = ProcessRunner.Capture(Tamp.Pnpm.V12.Pnpm.Raw(tool, "--version"));
            return r.ExitCode == 0 && r.StdoutText.Trim().StartsWith("12.", StringComparison.Ordinal) ? tool : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Copies the two-package workspace fixture to a throwaway directory (pnpm writes node_modules).</summary>
    public static string CopyWorkspace(TempDir tmp)
    {
        var src = Fixture.Path("workspace");
        var dst = tmp.Dir("workspace");
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, f);
            if (rel.Split(Path.DirectorySeparatorChar).Contains("node_modules")) continue;
            var to = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
        }
        return dst;
    }

    public static CaptureResult Run(CommandPlan plan) => ProcessRunner.Capture(plan);

    public static string All(CaptureResult r) => string.Join('\n', r.Lines.Select(l => l.Text));
}

/// <summary>
/// End-to-end runs of real pnpm 12 against a two-package workspace (<c>@fixture/app</c> depends on <c>@fixture/lib</c>
/// through <c>workspace:*</c>, so nothing touches a registry). Run <c>dotnet test --filter Category=Live</c>.
/// </summary>
[Trait("Category", "Live")]
public sealed class LiveTests
{
    private static Tool P => Live.Pnpm!;

    private static CommandPlan WithCi(CommandPlan plan, string? ci)
    {
        var env = new Dictionary<string, string>(plan.Environment);
        if (ci is not null) env["CI"] = ci;
        return plan with { Environment = env };
    }

    [LiveFact]
    public void Reports_Version_12()
    {
        var r = Live.Run(Pnpm.Raw(P, "--version"));
        Assert.Equal(0, r.ExitCode);
        Assert.StartsWith("12.", r.StdoutText.Trim());
    }

    [LiveFact]
    public void FrozenInstall_Succeeds_With_And_Without_CI()
    {
        using var tmp = new TempDir();
        var ws = Live.CopyWorkspace(tmp);
        foreach (var ci in new string?[] { null, "true", "false" })
        {
            var plan = WithCi(Pnpm.Install(P, s => s.SetWorkingDirectory(ws).SetStoreDir(tmp.Dir("store")).SetReporter("append-only")), ci);
            var r = Live.Run(plan);
            Assert.True(r.ExitCode == 0, $"CI={ci}: {Live.All(r)}");
        }
        Assert.True(Directory.Exists(Path.Combine(ws, "packages", "app", "node_modules", "@fixture", "lib")));
    }

    [LiveFact]
    public void Frozen_Fails_On_Drift_Regardless_Of_CI_And_NoFrozen_Succeeds_Regardless_Of_CI()
    {
        using var tmp = new TempDir();
        var ws = Live.CopyWorkspace(tmp);
        var appJson = Path.Combine(ws, "packages", "app", "package.json");
        File.WriteAllText(appJson, File.ReadAllText(appJson).Replace("workspace:*", "workspace:^"));
        var store = tmp.Dir("store");

        foreach (var ci in new string?[] { null, "true", "false" })
        {
            var frozen = Live.Run(WithCi(Pnpm.Install(P, s => s.SetWorkingDirectory(ws).SetStoreDir(store)), ci));
            Assert.NotEqual(0, frozen.ExitCode);
            Assert.Contains("ERR_PNPM_OUTDATED_LOCKFILE", Live.All(frozen));
        }
        foreach (var ci in new string?[] { null, "true" })
        {
            var update = Live.Run(WithCi(Pnpm.Install(P, s => s.SetWorkingDirectory(ws).SetStoreDir(store).SetNoFrozenLockfile()), ci));
            Assert.True(update.ExitCode == 0, Live.All(update));
        }
    }

    [LiveFact]
    public void Run_With_Filter_Runs_Only_That_Project_And_Forwards_Args_Verbatim()
    {
        using var tmp = new TempDir();
        var ws = Live.CopyWorkspace(tmp);
        Assert.Equal(0, Live.Run(Pnpm.Install(P, s => s.SetWorkingDirectory(ws).SetStoreDir(tmp.Dir("store")))).ExitCode);

        var r = Live.Run(Pnpm.Run(P, s => s.SetWorkingDirectory(ws).AddFilter("@fixture/app").SetScript("build").AddScriptArgs("--filter", "nothing", "x y")));
        var text = Live.All(r);
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("app-build", text);
        Assert.DoesNotContain("lib-build", text);
        Assert.Contains("[\"--filter\",\"nothing\",\"x y\"]", text);
    }

    [LiveFact]
    public void Recursive_Run_Stream_And_Parallel()
    {
        using var tmp = new TempDir();
        var ws = Live.CopyWorkspace(tmp);
        Assert.Equal(0, Live.Run(Pnpm.Install(P, s => s.SetWorkingDirectory(ws).SetStoreDir(tmp.Dir("store")))).ExitCode);

        var r = Live.Run(Pnpm.Run(P, s => s.SetWorkingDirectory(ws).SetScript("build").SetRecursive().SetStream().SetParallel().SetWorkspaceConcurrency(2)));
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("lib-build", Live.All(r));
        Assert.Contains("app-build", Live.All(r));
    }

    [LiveFact]
    public void Run_IfPresent_Skips_Missing_Script_And_Missing_Script_Fails_Otherwise()
    {
        using var tmp = new TempDir();
        var ws = Live.CopyWorkspace(tmp);
        Assert.Equal(0, Live.Run(Pnpm.Install(P, s => s.SetWorkingDirectory(ws).SetStoreDir(tmp.Dir("store")))).ExitCode);

        Assert.Equal(0, Live.Run(Pnpm.Run(P, s => s.SetWorkingDirectory(ws).SetScript("nosuch").SetRecursive().SetIfPresent())).ExitCode);
        Assert.NotEqual(0, Live.Run(Pnpm.Run(P, s => s.SetWorkingDirectory(ws).SetScript("nosuch"))).ExitCode);
    }

    [LiveFact]
    public void Filter_Matching_Nothing_Fails_By_Default_And_Passes_When_Opted_Out()
    {
        using var tmp = new TempDir();
        var ws = Live.CopyWorkspace(tmp);
        Assert.Equal(0, Live.Run(Pnpm.Install(P, s => s.SetWorkingDirectory(ws).SetStoreDir(tmp.Dir("store")))).ExitCode);

        Assert.NotEqual(0, Live.Run(Pnpm.Run(P, s => s.SetWorkingDirectory(ws).AddFilter("@fixture/renamed").SetScript("build"))).ExitCode);
        Assert.Equal(0, Live.Run(Pnpm.Run(P, s => s.SetWorkingDirectory(ws).AddFilter("@fixture/renamed").SetFailIfNoMatch(false).SetScript("build"))).ExitCode);
    }

    [LiveFact]
    public void WorkspaceRoot_Run()
    {
        using var tmp = new TempDir();
        var ws = Live.CopyWorkspace(tmp);
        Assert.Equal(0, Live.Run(Pnpm.Install(P, s => s.SetWorkingDirectory(ws).SetStoreDir(tmp.Dir("store")))).ExitCode);
        var r = Live.Run(Pnpm.Run(P, s => s.SetWorkingDirectory(ws).SetWorkspaceRoot().SetScript("hello").AddScriptArg("a")));
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("root-hello", Live.All(r));
    }

    [LiveFact]
    public void Exec_Runs_In_Project()
    {
        using var tmp = new TempDir();
        var ws = Live.CopyWorkspace(tmp);
        Assert.Equal(0, Live.Run(Pnpm.Install(P, s => s.SetWorkingDirectory(ws).SetStoreDir(tmp.Dir("store")))).ExitCode);
        var r = Live.Run(Pnpm.Exec(P, s => s.SetWorkingDirectory(ws).AddFilter("@fixture/lib").SetCommand("node").AddArgs("-e", "console.log('cwd=' + require('path').basename(process.cwd()))")));
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("cwd=lib", Live.All(r));
    }

    [LiveFact]
    public void SetVersion_Is_Idempotent_Even_In_A_Dirty_Git_Tree()
    {
        using var tmp = new TempDir();
        var dir = tmp.Dir("pkg");
        File.WriteAllText(Path.Combine(dir, "package.json"), """{ "name": "v", "version": "1.0.7" }""");
        Live.Run(new CommandPlan { Executable = "git", Arguments = new[] { "init", "-q", dir } });

        // Plain `pnpm version` fails on a no-op: that is why the wrapper exists.
        var plain = Live.Run(Pnpm.Raw(P, "version", "1.0.7", "--no-git-tag-version") with { WorkingDirectory = dir });
        Assert.NotEqual(0, plain.ExitCode);

        for (var i = 0; i < 2; i++)
            Assert.Equal(0, Live.Run(Pnpm.SetVersion(P, s => s.SetWorkingDirectory(dir).SetVersion("1.0.7"))).ExitCode);
        Assert.Equal(0, Live.Run(Pnpm.SetVersion(P, s => s.SetWorkingDirectory(dir).SetVersion("1.0.8"))).ExitCode);
        Assert.Contains("\"version\":\"1.0.8\"", File.ReadAllText(Path.Combine(dir, "package.json")).Replace(" ", "").Replace("\n", "").Replace("\r", ""));
    }

    [LiveFact]
    public void Store_Path_Honours_StoreDir_And_Prune_Runs()
    {
        using var tmp = new TempDir();
        var store = tmp.Dir("mystore");
        var r = Live.Run(Pnpm.StorePath(P, s => s.SetStoreDir(store)));
        Assert.Equal(0, r.ExitCode);
        Assert.StartsWith(Path.GetFullPath(store), r.StdoutText.Trim(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, Live.Run(Pnpm.StorePrune(P, s => s.SetStoreDir(store))).ExitCode);
    }

    [LiveFact]
    public void Pack_Creates_Tarball()
    {
        using var tmp = new TempDir();
        var ws = Live.CopyWorkspace(tmp);
        var outDir = tmp.Dir("out");
        var r = Live.Run(Pnpm.Pack(P, s => s.SetWorkingDirectory(Path.Combine(ws, "packages", "lib")).SetPackDestination(outDir)));
        Assert.True(r.ExitCode == 0, Live.All(r));
        Assert.Single(Directory.GetFiles(outDir, "*.tgz"));
    }

    [LiveFact]
    public void Fetch_Then_Offline_Frozen_Install()
    {
        using var tmp = new TempDir();
        var ws = Live.CopyWorkspace(tmp);
        var store = tmp.Dir("store");
        var f = Live.Run(Pnpm.Fetch(P, s => s.SetWorkingDirectory(ws).SetStoreDir(store)));
        Assert.True(f.ExitCode == 0, Live.All(f));
        var i = Live.Run(Pnpm.Install(P, s => s.SetWorkingDirectory(ws).SetStoreDir(store).SetOffline()));
        Assert.True(i.ExitCode == 0, Live.All(i));
    }

    [LiveFact]
    public void Dlx_Refusal_Happens_Before_Anything_Runs() =>
        Assert.Throws<InvalidOperationException>(() => Pnpm.Dlx(P, s => s.SetPackage("cowsay")));

    // ---- credentials, against a loopback registry that records what it receives ----

    private sealed class FakeRegistry : IDisposable
    {
        private readonly HttpListener _listener = new();
        public List<(string Method, string Path, string? Auth, string? Otp)> Requests { get; } = new();
        public string Url { get; }

        public FakeRegistry()
        {
            var port = new Random().Next(20000, 40000);
            while (true)
            {
                try { _listener.Prefixes.Clear(); _listener.Prefixes.Add($"http://localhost:{port}/"); _listener.Start(); break; }
                catch (HttpListenerException) { port++; }
            }
            Url = $"http://localhost:{port}/";
            _ = Task.Run(Loop);
        }

        private async Task Loop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext c;
                try { c = await _listener.GetContextAsync(); } catch (Exception) { return; }
                lock (Requests)
                    Requests.Add((c.Request.HttpMethod, c.Request.Url!.AbsolutePath, c.Request.Headers["Authorization"], c.Request.Headers["npm-otp"]));
                c.Response.StatusCode = 404;
                var body = Encoding.UTF8.GetBytes("{}");
                c.Response.OutputStream.Write(body, 0, body.Length);
                c.Response.Close();
            }
        }

        public void Dispose() { try { _listener.Close(); } catch (ObjectDisposedException) { } }
    }

    private static string Project(TempDir tmp, string registryInNpmrc)
    {
        var dir = tmp.Dir("proj");
        File.WriteAllText(Path.Combine(dir, "package.json"), """{ "name": "p", "version": "1.0.0", "dependencies": { "fakepkg-zz": "1.0.0" } }""");
        if (registryInNpmrc.Length > 0) File.WriteAllText(Path.Combine(dir, ".npmrc"), $"registry={registryInNpmrc}\n");
        return dir;
    }

    [LiveFact]
    public void AuthToken_Reaches_Registry_As_Bearer_Via_Env()
    {
        using var reg = new FakeRegistry();
        using var tmp = new TempDir();
        var token = new Secret("t", "FAKE-LIVE-TOKEN-0001");
        var dir = Project(tmp, "");
        var plan = Pnpm.Install(P, s => s.SetWorkingDirectory(dir).SetStoreDir(tmp.Dir("store")).SetRegistry(reg.Url).SetAuthToken(token).SetNoFrozenLockfile());
        Assert.DoesNotContain(plan.Arguments, a => a.Contains(token.Reveal()));
        Live.Run(plan); // the fake registry answers 404; we only care what it was sent
        lock (reg.Requests) Assert.Contains(reg.Requests, q => q.Path.Contains("fakepkg-zz") && q.Auth == "Bearer " + token.Reveal());
    }

    [LiveFact]
    public void Inherited_Registry_Variable_Cannot_Redirect_A_Wrapped_Command()
    {
        using var reg = new FakeRegistry();
        using var tmp = new TempDir();
        var dir = Project(tmp, reg.Url);
        // The caller's environment tries to send the install to an unroutable registry; the wrapper blanks it, so .npmrc wins.
        var plan = Pnpm.Install(P, s => s.SetWorkingDirectory(dir).SetStoreDir(tmp.Dir("store")).SetNoFrozenLockfile()
            .SetEnvironmentVariable("PNPM_CONFIG_REGISTRY", "http://127.0.0.1:1/"));
        Live.Run(plan);
        lock (reg.Requests) Assert.Contains(reg.Requests, q => q.Path.Contains("fakepkg-zz"));
    }

    /// <summary>
    /// A registry-free override case: <c>mid</c> depends on <c>leaf</c> 1.0.0, and the override points <c>leaf</c> at
    /// 2.0.0. Both are local <c>file:</c> packages.
    /// </summary>
    private static string OverrideProject(TempDir tmp, string name, bool workspaceCopy)
    {
        var dir = tmp.Dir(name);
        void Write(string rel, string json)
        {
            var path = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        Write("vendor/v1/package.json", """{ "name": "leaf", "version": "1.0.0" }""");
        Write("vendor/v2/package.json", """{ "name": "leaf", "version": "2.0.0" }""");
        Write("vendor/mid/package.json", """{ "name": "mid", "version": "1.0.0", "dependencies": { "leaf": "file:../v1" } }""");
        Write("package.json", """
            { "name": "root", "version": "1.0.0", "dependencies": { "mid": "file:./vendor/mid" },
              "pnpm": { "overrides": { "leaf": "file:./vendor/v2" } } }
            """);
        if (workspaceCopy)
            Write("pnpm-workspace.yaml", "packages:\n  - .\noverrides:\n  leaf: file:./vendor/v2\n");
        return dir;
    }

    private static string InstalledLeafVersion(string dir) =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "node_modules", ".pnpm", "node_modules", "leaf", "package.json")))
            .RootElement.GetProperty("version").GetString()!;

    [LiveFact]
    public void Pnpm12_Ignores_The_PackageJson_Pnpm_Field_And_Exits_Zero()
    {
        // Why the guard exists (#4). If a future pnpm 12 reads the field again, this fails and the guard can be relaxed.
        using var tmp = new TempDir();
        var dir = OverrideProject(tmp, "field-only", workspaceCopy: false);
        Assert.Throws<InvalidOperationException>(() => Pnpm.Install(P, s => s.SetWorkingDirectory(dir).SetNoFrozenLockfile()));

        var r = Live.Run(Pnpm.Install(P, s => s.SetWorkingDirectory(dir).SetNoFrozenLockfile()
            .SetStoreDir(tmp.Dir("store")).SetAllowIgnoredPnpmField()));
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("pnpm.overrides", Live.All(r));
        Assert.Equal("1.0.0", InstalledLeafVersion(dir));
    }

    [LiveFact]
    public void Override_Kept_In_Both_Places_Passes_The_Guard_And_Applies()
    {
        using var tmp = new TempDir();
        var dir = OverrideProject(tmp, "dual", workspaceCopy: true);
        var r = Live.Run(Pnpm.Install(P, s => s.SetWorkingDirectory(dir).SetNoFrozenLockfile().SetStoreDir(tmp.Dir("store"))));
        Assert.True(r.ExitCode == 0, Live.All(r));
        Assert.Equal("2.0.0", InstalledLeafVersion(dir));
    }

    [LiveFact]
    public void Otp_Reaches_Registry_As_Header_Via_Env()
    {
        using var reg = new FakeRegistry();
        using var tmp = new TempDir();
        var dir = tmp.Dir("pub");
        File.WriteAllText(Path.Combine(dir, "package.json"), """{ "name": "zz-pub-test", "version": "1.0.0" }""");
        var otp = new Secret("otp", "424242");
        var plan = Pnpm.Publish(P, s => s.SetWorkingDirectory(dir).SetRegistry(reg.Url).SetOtp(otp));
        Assert.DoesNotContain("--otp", plan.Arguments);
        Live.Run(plan);
        lock (reg.Requests) Assert.Contains(reg.Requests, q => q.Method == "PUT" && q.Otp == "424242");
    }
}
