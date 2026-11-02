using Tamp;
using Tamp.Pnpm.V12;
using Xunit;

namespace Tamp.Pnpm.V12.Tests;

public sealed class PnpmTests
{
    internal static Tool FakeTool() => new(AbsolutePath.Create("/fake/pnpm"));

    private static void AssertInOrder(IReadOnlyList<string> args, params string[] expected)
    {
        var at = -1;
        foreach (var e in expected)
        {
            var next = -1;
            for (var i = at + 1; i < args.Count; i++) if (args[i] == e) { next = i; break; }
            Assert.True(next > at, $"expected '{e}' after index {at} in: {string.Join(' ', args)}");
            at = next;
        }
    }

    // ---- install ----

    [Fact]
    public void Install_Bare_Is_Frozen_Explicitly()
    {
        var plan = Pnpm.Install(FakeTool());
        Assert.Equal(new[] { "install", "--frozen-lockfile" }, plan.Arguments);
        Assert.Equal("/fake/pnpm", plan.Executable);
    }

    [Fact]
    public void Install_NoFrozen_Is_Explicit()
    {
        var plan = Pnpm.Install(FakeTool(), s => s.SetNoFrozenLockfile());
        Assert.Contains("--no-frozen-lockfile", plan.Arguments);
        Assert.DoesNotContain("--frozen-lockfile", plan.Arguments);
    }

    [Fact]
    public void Install_SetFrozenLockfile_False_Means_NoFrozen()
    {
        var plan = Pnpm.Install(FakeTool(), s => s.SetFrozenLockfile(false));
        Assert.Contains("--no-frozen-lockfile", plan.Arguments);
    }

    [Fact]
    public void Install_Never_Emits_Both_Lockfile_Flags()
    {
        foreach (bool? mode in new bool?[] { null, true, false })
        {
            var plan = Pnpm.Install(FakeTool(), new PnpmInstallSettings { FrozenLockfile = mode });
            Assert.Equal(1, plan.Arguments.Count(a => a is "--frozen-lockfile" or "--no-frozen-lockfile"));
        }
    }

    [Fact]
    public void Install_LockfileOnly_Defaults_To_NoFrozen()
    {
        var plan = Pnpm.Install(FakeTool(), s => s.SetLockfileOnly());
        Assert.Contains("--no-frozen-lockfile", plan.Arguments);
        Assert.Contains("--lockfile-only", plan.Arguments);
    }

    [Fact]
    public void Install_LockfileOnly_With_Explicit_Frozen_Throws()
    {
        var s = new PnpmInstallSettings { LockfileOnly = true, FrozenLockfile = true };
        Assert.Throws<InvalidOperationException>(() => Pnpm.Install(FakeTool(), s));
    }

    [Fact]
    public void Install_All_Flags()
    {
        var plan = Pnpm.Install(FakeTool(), s => s
            .SetPreferOffline().SetIgnoreScripts().SetProduction()
            .AddFilter("@scope/*").AddFilter("./apps/web").SetRecursive());
        AssertInOrder(plan.Arguments, "install", "--frozen-lockfile", "--prefer-offline", "--ignore-scripts", "--prod");
        AssertInOrder(plan.Arguments, "--filter", "@scope/*", "--filter", "./apps/web", "--fail-if-no-match", "--recursive");
    }

    [Fact]
    public void Install_Offline_Dev_And_Extras()
    {
        var plan = Pnpm.Install(FakeTool(), s => s.SetOffline().SetDevelopment().AddExtraArgument("--force"));
        Assert.Contains("--offline", plan.Arguments);
        Assert.Contains("--dev", plan.Arguments);
        Assert.Contains("--force", plan.Arguments);
    }

    [Fact]
    public void Install_ObjectInit()
    {
        var plan = Pnpm.Install(FakeTool(), new PnpmInstallSettings { Production = true, IgnoreScripts = true, Offline = true });
        Assert.Contains("--prod", plan.Arguments);
        Assert.Contains("--ignore-scripts", plan.Arguments);
        Assert.Contains("--offline", plan.Arguments);
    }

    [Fact]
    public void Install_Prod_And_Dev_Conflict() =>
        Assert.Throws<InvalidOperationException>(() => Pnpm.Install(FakeTool(), s => s.SetProduction().SetDevelopment()));

    [Fact]
    public void Install_Offline_And_PreferOffline_Conflict() =>
        Assert.Throws<InvalidOperationException>(() => Pnpm.Install(FakeTool(), s => s.SetOffline().SetPreferOffline()));

    [Fact]
    public void Install_Blank_Filter_Throws() =>
        Assert.Throws<InvalidOperationException>(() => Pnpm.Install(FakeTool(), s => s.AddFilter(" ")));

    [Fact]
    public void Filter_Without_FailIfNoMatch()
    {
        var plan = Pnpm.Install(FakeTool(), s => s.AddFilter("a").SetFailIfNoMatch(false));
        Assert.DoesNotContain("--fail-if-no-match", plan.Arguments);
    }

    [Fact]
    public void FailIfNoMatch_Not_Emitted_Without_Filter()
    {
        var plan = Pnpm.Install(FakeTool(), s => s.SetRecursive());
        Assert.DoesNotContain("--fail-if-no-match", plan.Arguments);
    }

    // ---- add / remove ----

    [Fact]
    public void Add_Packages_After_Flags()
    {
        var plan = Pnpm.Add(FakeTool(), s => s.AddPackage("react@18.3.1").AddPackages("zod", "left-pad").SetSaveDev().SetSaveExact().SetIgnoreScripts());
        Assert.Equal("add", plan.Arguments[0]);
        Assert.Equal(new[] { "react@18.3.1", "zod", "left-pad" }, plan.Arguments.TakeLast(3));
        Assert.Contains("--save-dev", plan.Arguments);
        Assert.Contains("--save-exact", plan.Arguments);
        Assert.Contains("--ignore-scripts", plan.Arguments);
    }

    [Fact]
    public void Add_Optional_And_Workspace_Root()
    {
        var plan = Pnpm.Add(FakeTool(), s => s.AddPackage("fsevents").SetSaveOptional().SetWorkspaceRoot().AddFilter("web"));
        Assert.Contains("--save-optional", plan.Arguments);
        Assert.Contains("--workspace-root", plan.Arguments);
        AssertInOrder(plan.Arguments, "--filter", "web");
    }

    [Fact]
    public void Add_Requires_A_Package()
    {
        Assert.Throws<InvalidOperationException>(() => Pnpm.Add(FakeTool(), s => { }));
        Assert.Throws<InvalidOperationException>(() => Pnpm.Add(FakeTool(), s => s.AddPackage("")));
    }

    [Fact]
    public void Add_Dev_And_Optional_Conflict() =>
        Assert.Throws<InvalidOperationException>(() => Pnpm.Add(FakeTool(), s => s.AddPackage("x").SetSaveDev().SetSaveOptional()));

    [Fact]
    public void Add_ObjectInit()
    {
        var s = new PnpmAddSettings { SaveDev = true };
        s.Packages.Add("typescript@5.6.3");
        var plan = Pnpm.Add(FakeTool(), s);
        Assert.Equal(new[] { "add", "--save-dev", "typescript@5.6.3" }, plan.Arguments);
    }

    [Fact]
    public void Remove_Flags_And_Packages()
    {
        var plan = Pnpm.Remove(FakeTool(), s => s.AddPackage("a").AddPackages("b").SetSaveDev().SetWorkspaceRoot().AddFilter("x").SetRecursive());
        Assert.Equal("remove", plan.Arguments[0]);
        Assert.Contains("--save-dev", plan.Arguments);
        Assert.Contains("--workspace-root", plan.Arguments);
        Assert.Contains("--recursive", plan.Arguments);
        Assert.Equal(new[] { "a", "b" }, plan.Arguments.TakeLast(2));
    }

    [Fact]
    public void Remove_Optional_And_Prod()
    {
        Assert.Contains("--save-optional", Pnpm.Remove(FakeTool(), s => s.AddPackage("a").SetSaveOptional()).Arguments);
        Assert.Contains("--save-prod", Pnpm.Remove(FakeTool(), s => s.AddPackage("a").SetSaveProd()).Arguments);
    }

    [Fact]
    public void Remove_Validation()
    {
        Assert.Throws<InvalidOperationException>(() => Pnpm.Remove(FakeTool(), s => { }));
        Assert.Throws<InvalidOperationException>(() => Pnpm.Remove(FakeTool(), s => s.AddPackage("a").SetSaveDev().SetSaveProd()));
    }

    // ---- run ----

    [Fact]
    public void Run_Script_Last_And_No_Separator()
    {
        var plan = Pnpm.Run(FakeTool(), s => s.SetScript("build").AddScriptArg("--mode").AddScriptArgs("prod", "--filter"));
        Assert.Equal(new[] { "run", "build", "--mode", "prod", "--filter" }, plan.Arguments);
        Assert.DoesNotContain("--", plan.Arguments);
    }

    [Fact]
    public void Run_Pnpm_Flags_Precede_The_Script()
    {
        var plan = Pnpm.Run(FakeTool(), s => s
            .SetScript("test").AddFilter("@app/*").SetRecursive().SetIfPresent().SetParallel().SetWorkspaceConcurrency(4).SetStream()
            .AddScriptArg("--watch"));
        var script = plan.Arguments.ToList().IndexOf("test");
        foreach (var f in new[] { "--filter", "--recursive", "--if-present", "--parallel", "--workspace-concurrency", "--stream" })
            Assert.True(plan.Arguments.ToList().IndexOf(f) is > 0 && plan.Arguments.ToList().IndexOf(f) < script, f);
        Assert.Equal("4", plan.Arguments[plan.Arguments.ToList().IndexOf("--workspace-concurrency") + 1]);
        Assert.Equal("--watch", plan.Arguments[^1]);
    }

    [Fact]
    public void Run_Validation()
    {
        Assert.Throws<InvalidOperationException>(() => Pnpm.Run(FakeTool(), s => { }));
        Assert.Throws<InvalidOperationException>(() => Pnpm.Run(FakeTool(), s => s.SetScript("b").SetWorkspaceConcurrency(0)));
    }

    [Fact]
    public void Run_ObjectInit_WorkspaceRoot()
    {
        var plan = Pnpm.Run(FakeTool(), new PnpmRunSettings { Script = "hello", WorkspaceRoot = true });
        Assert.Equal(new[] { "run", "--workspace-root", "hello" }, plan.Arguments);
    }

    // ---- exec ----

    [Fact]
    public void Exec_Command_And_Args_Verbatim()
    {
        var plan = Pnpm.Exec(FakeTool(), s => s.SetCommand("tsc").AddArg("--noEmit").AddArgs("-p", "tsconfig.json").AddFilter("app"));
        Assert.Equal(new[] { "exec", "--filter", "app", "--fail-if-no-match", "tsc", "--noEmit", "-p", "tsconfig.json" }, plan.Arguments);
    }

    [Fact]
    public void Exec_Flags()
    {
        var plan = Pnpm.Exec(FakeTool(), s => s.SetCommand("x").SetShellMode().SetParallel().SetWorkspaceConcurrency(2).SetStream().SetRecursive());
        Assert.Contains("--shell-mode", plan.Arguments);
        Assert.Contains("--parallel", plan.Arguments);
        Assert.Contains("--stream", plan.Arguments);
        Assert.Contains("--recursive", plan.Arguments);
        Assert.Equal("x", plan.Arguments[^1]);
    }

    [Fact]
    public void Exec_Validation()
    {
        Assert.Throws<InvalidOperationException>(() => Pnpm.Exec(FakeTool(), s => { }));
        Assert.Throws<InvalidOperationException>(() => Pnpm.Exec(FakeTool(), s => s.SetCommand("x").SetWorkspaceConcurrency(-1)));
    }

    // ---- dlx ----

    [Theory]
    [InlineData("cowsay@1.6.0")]
    [InlineData("@scope/tool@2.0.1")]
    [InlineData("tool@1.0.0-rc.1")]
    [InlineData("tool@1.0.0+build.5")]
    public void Dlx_Accepts_Exact_Versions(string spec)
    {
        var plan = Pnpm.Dlx(FakeTool(), s => s.SetPackage(spec).AddArg("hi"));
        Assert.Equal(new[] { "dlx", spec, "hi" }, plan.Arguments);
    }

    [Theory]
    [InlineData("cowsay")]
    [InlineData("cowsay@latest")]
    [InlineData("cowsay@^1.6.0")]
    [InlineData("cowsay@~1.6.0")]
    [InlineData("cowsay@1")]
    [InlineData("cowsay@1.6")]
    [InlineData("cowsay@1.x")]
    [InlineData("cowsay@>=1.0.0")]
    [InlineData("cowsay@")]
    [InlineData("@scope/tool")]
    [InlineData("@scope/tool@next")]
    [InlineData("github:user/repo")]
    [InlineData("npm:cowsay@1.6.0")]
    [InlineData("")]
    public void Dlx_Refuses_Unpinned(string spec) =>
        Assert.Throws<InvalidOperationException>(() => Pnpm.Dlx(FakeTool(), s => s.SetPackage(spec)));

    [Fact]
    public void Dlx_Unpinned_Allowed_When_Overridden()
    {
        var plan = Pnpm.Dlx(FakeTool(), s => s.SetPackage("cowsay@latest").SetAllowUnpinned());
        Assert.Equal(new[] { "dlx", "cowsay@latest" }, plan.Arguments);
    }

    [Fact]
    public void Dlx_Package_Required() =>
        Assert.Throws<InvalidOperationException>(() => Pnpm.Dlx(FakeTool(), s => { }));

    [Fact]
    public void Dlx_Command_Uses_Package_Flag_And_AllowBuild()
    {
        var plan = Pnpm.Dlx(FakeTool(), s => s.SetPackage("typescript@5.6.3").SetCommand("tsc").AddArg("--version").AddAllowBuild("esbuild"));
        Assert.Equal(new[] { "dlx", "--allow-build", "esbuild", "--package", "typescript@5.6.3", "tsc", "--version" }, plan.Arguments);
    }

    [Fact]
    public void Dlx_IsPinned_Public_Helper()
    {
        Assert.True(PnpmDlxSettings.IsPinned("a@1.2.3"));
        Assert.False(PnpmDlxSettings.IsPinned(null));
        Assert.False(PnpmDlxSettings.IsPinned("a@"));
    }

    // ---- fetch ----

    [Fact]
    public void Fetch_Bare() => Assert.Equal(new[] { "fetch" }, Pnpm.Fetch(FakeTool()).Arguments);

    [Fact]
    public void Fetch_Flags()
    {
        var plan = Pnpm.Fetch(FakeTool(), s => s.SetProduction().SetIgnorePnpmfile().SetOffline().SetStoreDir("/s"));
        Assert.Equal(new[] { "fetch", "--prod", "--ignore-pnpmfile", "--offline", "--store-dir", "/s" }, plan.Arguments);
        Assert.Contains("--dev", Pnpm.Fetch(FakeTool(), s => s.SetDevelopment()).Arguments);
        Assert.Contains("--prefer-offline", Pnpm.Fetch(FakeTool(), s => s.SetPreferOffline()).Arguments);
    }

    [Fact]
    public void Fetch_Conflicts()
    {
        Assert.Throws<InvalidOperationException>(() => Pnpm.Fetch(FakeTool(), s => s.SetProduction().SetDevelopment()));
        Assert.Throws<InvalidOperationException>(() => Pnpm.Fetch(FakeTool(), s => s.SetOffline().SetPreferOffline()));
    }

    // ---- audit ----

    [Fact]
    public void Audit_Flags()
    {
        var plan = Pnpm.Audit(FakeTool(), s => s.SetJson().SetAuditLevel("high").SetProduction().SetIgnoreRegistryErrors().SetIgnoreUnfixable()
            .AddIgnoredAdvisory("GHSA-aaaa-bbbb-cccc"));
        Assert.Equal("audit", plan.Arguments[0]);
        AssertInOrder(plan.Arguments, "--audit-level", "high");
        Assert.Contains("--json", plan.Arguments);
        Assert.Contains("--prod", plan.Arguments);
        Assert.Contains("--ignore-registry-errors", plan.Arguments);
        Assert.Contains("--ignore-unfixable", plan.Arguments);
        AssertInOrder(plan.Arguments, "--ignore", "GHSA-aaaa-bbbb-cccc");
    }

    [Fact]
    public void Audit_Dev_And_Validation()
    {
        Assert.Contains("--dev", Pnpm.Audit(FakeTool(), s => s.SetDevelopment()).Arguments);
        Assert.Throws<InvalidOperationException>(() => Pnpm.Audit(FakeTool(), s => s.SetProduction().SetDevelopment()));
        Assert.Throws<InvalidOperationException>(() => Pnpm.Audit(FakeTool(), s => s.SetAuditLevel("severe")));
    }

    [Theory]
    [InlineData("info")] [InlineData("low")] [InlineData("moderate")] [InlineData("high")] [InlineData("critical")]
    public void Audit_Levels(string level) =>
        Assert.Equal(level, Pnpm.Audit(FakeTool(), new PnpmAuditSettings { AuditLevel = level }).Arguments.Last());

    [Fact]
    public void Audit_Never_Emits_Fix() =>
        Assert.DoesNotContain("--fix", Pnpm.Audit(FakeTool(), s => s.SetJson()).Arguments);

    // ---- store ----

    [Fact]
    public void Store_Path_And_Prune()
    {
        Assert.Equal(new[] { "store", "path" }, Pnpm.StorePath(FakeTool()).Arguments);
        Assert.Equal(new[] { "store", "prune" }, Pnpm.StorePrune(FakeTool()).Arguments);
        Assert.Equal(new[] { "store", "path", "--store-dir", "/x" }, Pnpm.StorePath(FakeTool(), s => s.SetStoreDir("/x")).Arguments);
        Assert.Equal(new[] { "store", "prune" }, Pnpm.StorePrune(FakeTool(), new PnpmStorePruneSettings()).Arguments);
        Assert.Equal(new[] { "store", "path" }, Pnpm.StorePath(FakeTool(), new PnpmStorePathSettings()).Arguments);
    }

    // ---- pack ----

    [Fact]
    public void Pack_Flags()
    {
        var plan = Pnpm.Pack(FakeTool(), s => s.SetPackDestination("out").SetJson().SetDryRun().SetGzipLevel(9));
        Assert.Equal(new[] { "pack", "--pack-destination", "out", "--pack-gzip-level", "9", "--json", "--dry-run" }, plan.Arguments);
        Assert.Equal(new[] { "pack", "--out", "%s-%v.tgz" }, Pnpm.Pack(FakeTool(), s => s.SetOut("%s-%v.tgz")).Arguments);
    }

    [Fact]
    public void Pack_Validation()
    {
        Assert.Throws<InvalidOperationException>(() => Pnpm.Pack(FakeTool(), s => s.SetPackDestination("a").SetOut("b")));
        Assert.Throws<InvalidOperationException>(() => Pnpm.Pack(FakeTool(), s => s.SetGzipLevel(10)));
        Assert.Throws<InvalidOperationException>(() => Pnpm.Pack(FakeTool(), s => s.SetGzipLevel(-1)));
    }

    // ---- publish ----

    [Fact]
    public void Publish_Default_Has_NoGitChecks()
    {
        var plan = Pnpm.Publish(FakeTool());
        Assert.Equal(new[] { "publish", "--no-git-checks" }, plan.Arguments);
    }

    [Fact]
    public void Publish_Flags()
    {
        var plan = Pnpm.Publish(FakeTool(), s => s.SetTag("next").SetAccess("public").SetDryRun().SetProvenance().SetIgnoreScripts().SetJson().SetPackage("pkg.tgz"));
        AssertInOrder(plan.Arguments, "publish", "--no-git-checks", "--tag", "next", "--access", "public", "--dry-run");
        Assert.Contains("--provenance", plan.Arguments);
        Assert.Contains("--ignore-scripts", plan.Arguments);
        Assert.Contains("--json", plan.Arguments);
        Assert.Equal("pkg.tgz", plan.Arguments[^1]);
    }

    [Fact]
    public void Publish_Can_Enable_Git_Checks()
    {
        var plan = Pnpm.Publish(FakeTool(), s => s.SetNoGitChecks(false));
        Assert.DoesNotContain("--no-git-checks", plan.Arguments);
    }

    [Fact]
    public void Publish_Validation()
    {
        Assert.Throws<InvalidOperationException>(() => Pnpm.Publish(FakeTool(), s => s.SetAccess("private")));
        Assert.Throws<InvalidOperationException>(() => Pnpm.Publish(FakeTool(), s => s.SetTag(" ")));
        Assert.Contains("--access", Pnpm.Publish(FakeTool(), s => s.SetAccess("restricted")).Arguments);
    }

    [Fact]
    public void Publish_Recursive_Filter()
    {
        var plan = Pnpm.Publish(FakeTool(), s => s.SetRecursive().AddFilter("@scope/lib"));
        Assert.Contains("--recursive", plan.Arguments);
        AssertInOrder(plan.Arguments, "--filter", "@scope/lib");
    }

    [Fact]
    public void Publish_Capability_Is_SideEffectful_Unless_DryRun()
    {
        Assert.Equal(CapabilityTier.SideEffectful, Pnpm.Publish(FakeTool()).RequiredCapability);
        Assert.Equal(CapabilityTier.Safe, Pnpm.Publish(FakeTool(), s => s.SetDryRun()).RequiredCapability);
        Assert.Equal(CapabilityTier.Safe, Pnpm.Install(FakeTool()).RequiredCapability);
    }

    // ---- deploy ----

    [Fact]
    public void Deploy_Is_Frozen_By_Default_And_Dir_Last()
    {
        var plan = Pnpm.Deploy(FakeTool(), s => s.AddFilter("@app/web").SetDirectory("out/web").SetProduction().SetOffline().SetIgnoreScripts().SetLegacy());
        Assert.Equal("deploy", plan.Arguments[0]);
        Assert.Equal("--frozen-lockfile", plan.Arguments[1]);
        Assert.Equal("out/web", plan.Arguments[^1]);
        AssertInOrder(plan.Arguments, "--filter", "@app/web");
        foreach (var f in new[] { "--prod", "--offline", "--ignore-scripts", "--legacy" }) Assert.Contains(f, plan.Arguments);
    }

    [Fact]
    public void Deploy_Flags_And_Validation()
    {
        Assert.Contains("--no-frozen-lockfile", Pnpm.Deploy(FakeTool(), s => s.SetDirectory("d").SetFrozenLockfile(false)).Arguments);
        Assert.Contains("--dev", Pnpm.Deploy(FakeTool(), s => s.SetDirectory("d").SetDevelopment()).Arguments);
        Assert.Contains("--prefer-offline", Pnpm.Deploy(FakeTool(), s => s.SetDirectory("d").SetPreferOffline()).Arguments);
        Assert.Throws<InvalidOperationException>(() => Pnpm.Deploy(FakeTool(), s => { }));
        Assert.Throws<InvalidOperationException>(() => Pnpm.Deploy(FakeTool(), s => s.SetDirectory("d").SetProduction().SetDevelopment()));
        Assert.Throws<InvalidOperationException>(() => Pnpm.Deploy(FakeTool(), s => s.SetDirectory("d").SetOffline().SetPreferOffline()));
        Assert.Equal("d", Pnpm.Deploy(FakeTool(), new PnpmDeploySettings { Directory = "d" }).Arguments[^1]);
    }

    // ---- SetVersion ----

    [Fact]
    public void SetVersion_Is_Idempotent_Flags()
    {
        var plan = Pnpm.SetVersion(FakeTool(), "1.0.7");
        Assert.Equal(new[] { "version", "1.0.7", "--no-git-tag-version", "--allow-same-version", "--no-git-checks" }, plan.Arguments);
    }

    [Theory]
    [InlineData("1.0.7-rc.1")]
    [InlineData("2.0.0+sha.abc")]
    public void SetVersion_Accepts_Prerelease_And_Build(string v) => Assert.Equal(v, Pnpm.SetVersion(FakeTool(), v).Arguments[1]);

    [Theory]
    [InlineData("patch")]
    [InlineData("from-git")]
    [InlineData("1.0")]
    [InlineData("v1.0.0")]
    public void SetVersion_Rejects_Non_Exact(string v) =>
        Assert.Throws<InvalidOperationException>(() => Pnpm.SetVersion(FakeTool(), v));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void SetVersion_Rejects_Blank(string v) =>
        Assert.Throws<ArgumentException>(() => Pnpm.SetVersion(FakeTool(), v));

    [Fact]
    public void SetVersion_ObjectInit_And_Fluent()
    {
        Assert.Equal("3.0.0", Pnpm.SetVersion(FakeTool(), new PnpmSetVersionSettings().SetVersion("3.0.0")).Arguments[1]);
        Assert.Throws<InvalidOperationException>(() => Pnpm.SetVersion(FakeTool(), new PnpmSetVersionSettings()));
        Assert.Throws<InvalidOperationException>(() => Pnpm.SetVersion(FakeTool(), new PnpmSetVersionSettings { Version = "patch" }));
    }

    // ---- Raw / common ----

    [Fact]
    public void Raw_Passes_Through()
    {
        var plan = Pnpm.Raw(FakeTool(), "why", "left-pad");
        Assert.Equal(new[] { "why", "left-pad" }, plan.Arguments);
        Assert.Throws<ArgumentException>(() => Pnpm.Raw(FakeTool()));
    }

    [Fact]
    public void Null_Tool_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Pnpm.Install(null!));
        Assert.Throws<ArgumentNullException>(() => Pnpm.Install(null!, new PnpmInstallSettings()));
        Assert.Throws<ArgumentNullException>(() => Pnpm.Install(FakeTool(), (PnpmInstallSettings)null!));
    }

    [Fact]
    public void WorkingDirectory_Wins_Over_Tool_Directory()
    {
        var tool = new Tool(AbsolutePath.Create("/fake/pnpm"), "/tool-cwd");
        Assert.Equal("/tool-cwd", Pnpm.Install(tool).WorkingDirectory);
        Assert.Equal("/wd", Pnpm.Install(tool, s => s.SetWorkingDirectory("/wd")).WorkingDirectory);
    }

    [Fact]
    public void StoreDir_And_Reporter()
    {
        var plan = Pnpm.Install(FakeTool(), s => s.SetStoreDir("/cache/store").SetReporter("append-only"));
        AssertInOrder(plan.Arguments, "--store-dir", "/cache/store", "--reporter", "append-only");
        Assert.Throws<InvalidOperationException>(() => Pnpm.Install(FakeTool(), s => s.SetReporter("fancy")));
        foreach (var r in new[] { "default", "ndjson", "silent" }) Assert.Contains(r, Pnpm.Install(FakeTool(), s => s.SetReporter(r)).Arguments);
    }

    [Fact]
    public void Corepack_Prefixes_Pnpm()
    {
        var plan = Pnpm.Install(new Tool(AbsolutePath.Create("/fake/corepack")), s => s.SetUseCorepack());
        Assert.Equal("/fake/corepack", plan.Executable);
        Assert.Equal(new[] { "pnpm", "install", "--frozen-lockfile" }, plan.Arguments);
    }
}
