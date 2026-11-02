namespace Tamp.Pnpm.V12;

/// <summary>Shared validation for flags that are mutually exclusive in pnpm.</summary>
internal static class Exclusive
{
    public static void Check(bool a, bool b, string message)
    {
        if (a && b) throw new InvalidOperationException(message);
    }
}

/// <summary>
/// Settings for <c>pnpm install</c>. The lockfile mode is always emitted explicitly (<c>--frozen-lockfile</c> or
/// <c>--no-frozen-lockfile</c>), because pnpm 12 turns frozen on by itself when <c>CI</c> is set and the result
/// would otherwise depend on the machine's environment.
/// </summary>
public sealed class PnpmInstallSettings : PnpmWorkspaceSettingsBase
{
    /// <summary>
    /// <c>true</c> = <c>--frozen-lockfile</c>, <c>false</c> = <c>--no-frozen-lockfile</c>. Null (the default) means
    /// frozen, or not frozen when <see cref="LockfileOnly"/> is set (a lockfile-only run exists to write the lockfile).
    /// </summary>
    public bool? FrozenLockfile { get; set; }

    /// <summary><c>--prefer-offline</c>.</summary>
    public bool PreferOffline { get; set; }

    /// <summary><c>--offline</c>: fail on a cache miss.</summary>
    public bool Offline { get; set; }

    /// <summary><c>--ignore-scripts</c>.</summary>
    public bool IgnoreScripts { get; set; }

    /// <summary><c>--prod</c>: skip devDependencies.</summary>
    public bool Production { get; set; }

    /// <summary><c>--dev</c>: install only devDependencies.</summary>
    public bool Development { get; set; }

    /// <summary><c>--lockfile-only</c>.</summary>
    public bool LockfileOnly { get; set; }

    public PnpmInstallSettings SetFrozenLockfile(bool v = true) { FrozenLockfile = v; return this; }
    public PnpmInstallSettings SetNoFrozenLockfile() { FrozenLockfile = false; return this; }
    public PnpmInstallSettings SetPreferOffline(bool v = true) { PreferOffline = v; return this; }
    public PnpmInstallSettings SetOffline(bool v = true) { Offline = v; return this; }
    public PnpmInstallSettings SetIgnoreScripts(bool v = true) { IgnoreScripts = v; return this; }
    public PnpmInstallSettings SetProduction(bool v = true) { Production = v; return this; }
    public PnpmInstallSettings SetDevelopment(bool v = true) { Development = v; return this; }
    public PnpmInstallSettings SetLockfileOnly(bool v = true) { LockfileOnly = v; return this; }

    protected override IEnumerable<string> BuildArguments()
    {
        Exclusive.Check(Production, Development, "pnpm install: Production (--prod) and Development (--dev) are mutually exclusive.");
        Exclusive.Check(Offline, PreferOffline, "pnpm install: Offline (--offline) and PreferOffline (--prefer-offline) are mutually exclusive.");
        if (LockfileOnly && FrozenLockfile == true)
            throw new InvalidOperationException("pnpm install: LockfileOnly writes the lockfile, which --frozen-lockfile forbids. Leave FrozenLockfile unset or false.");

        yield return "install";
        yield return (FrozenLockfile ?? !LockfileOnly) ? "--frozen-lockfile" : "--no-frozen-lockfile";
        if (PreferOffline) yield return "--prefer-offline";
        if (Offline) yield return "--offline";
        if (IgnoreScripts) yield return "--ignore-scripts";
        if (Production) yield return "--prod";
        if (Development) yield return "--dev";
        if (LockfileOnly) yield return "--lockfile-only";
        foreach (var a in WorkspaceArguments()) yield return a;
        foreach (var a in CommonArguments()) yield return a;
        foreach (var a in ExtraArguments) yield return a;
    }
}

/// <summary>Settings for <c>pnpm add &lt;package...&gt;</c>.</summary>
public sealed class PnpmAddSettings : PnpmWorkspaceSettingsBase
{
    /// <summary>Packages to add (with optional version/tag specs). At least one is required.</summary>
    public List<string> Packages { get; } = new();

    /// <summary><c>--save-dev</c> (<c>-D</c>).</summary>
    public bool SaveDev { get; set; }

    /// <summary><c>--save-optional</c> (<c>-O</c>).</summary>
    public bool SaveOptional { get; set; }

    /// <summary><c>--save-exact</c> (<c>-E</c>): pin instead of a <c>^</c> range.</summary>
    public bool SaveExact { get; set; }

    /// <summary><c>--ignore-scripts</c>.</summary>
    public bool IgnoreScripts { get; set; }

    public PnpmAddSettings AddPackage(string package) { Packages.Add(package); return this; }
    public PnpmAddSettings AddPackages(params string[] packages) { Packages.AddRange(packages); return this; }
    public PnpmAddSettings SetSaveDev(bool v = true) { SaveDev = v; return this; }
    public PnpmAddSettings SetSaveOptional(bool v = true) { SaveOptional = v; return this; }
    public PnpmAddSettings SetSaveExact(bool v = true) { SaveExact = v; return this; }
    public PnpmAddSettings SetIgnoreScripts(bool v = true) { IgnoreScripts = v; return this; }

    protected override IEnumerable<string> BuildArguments()
    {
        if (Packages.Count == 0 || Packages.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("pnpm add requires at least one non-blank package.");
        Exclusive.Check(SaveDev, SaveOptional, "pnpm add: SaveDev (-D) and SaveOptional (-O) are mutually exclusive.");

        yield return "add";
        if (SaveDev) yield return "--save-dev";
        if (SaveOptional) yield return "--save-optional";
        if (SaveExact) yield return "--save-exact";
        if (IgnoreScripts) yield return "--ignore-scripts";
        foreach (var a in WorkspaceArguments()) yield return a;
        foreach (var a in CommonArguments()) yield return a;
        foreach (var a in ExtraArguments) yield return a;
        foreach (var p in Packages) yield return p;
    }
}

/// <summary>Settings for <c>pnpm remove &lt;package...&gt;</c>.</summary>
public sealed class PnpmRemoveSettings : PnpmWorkspaceSettingsBase
{
    /// <summary>Packages to remove. At least one is required.</summary>
    public List<string> Packages { get; } = new();

    /// <summary><c>--save-dev</c> (<c>-D</c>): remove only from devDependencies.</summary>
    public bool SaveDev { get; set; }

    /// <summary><c>--save-optional</c> (<c>-O</c>): remove only from optionalDependencies.</summary>
    public bool SaveOptional { get; set; }

    /// <summary><c>--save-prod</c> (<c>-P</c>): remove only from dependencies.</summary>
    public bool SaveProd { get; set; }

    public PnpmRemoveSettings AddPackage(string package) { Packages.Add(package); return this; }
    public PnpmRemoveSettings AddPackages(params string[] packages) { Packages.AddRange(packages); return this; }
    public PnpmRemoveSettings SetSaveDev(bool v = true) { SaveDev = v; return this; }
    public PnpmRemoveSettings SetSaveOptional(bool v = true) { SaveOptional = v; return this; }
    public PnpmRemoveSettings SetSaveProd(bool v = true) { SaveProd = v; return this; }

    protected override IEnumerable<string> BuildArguments()
    {
        if (Packages.Count == 0 || Packages.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("pnpm remove requires at least one non-blank package.");
        if ((SaveDev ? 1 : 0) + (SaveOptional ? 1 : 0) + (SaveProd ? 1 : 0) > 1)
            throw new InvalidOperationException("pnpm remove: SaveDev, SaveOptional and SaveProd are mutually exclusive.");

        yield return "remove";
        if (SaveDev) yield return "--save-dev";
        if (SaveOptional) yield return "--save-optional";
        if (SaveProd) yield return "--save-prod";
        foreach (var a in WorkspaceArguments()) yield return a;
        foreach (var a in CommonArguments()) yield return a;
        foreach (var a in ExtraArguments) yield return a;
        foreach (var p in Packages) yield return p;
    }
}

/// <summary>
/// Settings for <c>pnpm run &lt;script&gt; [args]</c>. In pnpm 12 everything after the script name is forwarded to the
/// script verbatim, <b>including a literal <c>--</c></b>, so pnpm's own flags are emitted before the script name and
/// script arguments follow it with no separator.
/// </summary>
public sealed class PnpmRunSettings : PnpmWorkspaceSettingsBase
{
    /// <summary>The script name from <c>package.json</c>. Required.</summary>
    public string? Script { get; set; }

    /// <summary>Arguments forwarded to the script (verbatim, one argv token each).</summary>
    public List<string> ScriptArgs { get; } = new();

    /// <summary><c>--if-present</c>: don't fail when a script is missing.</summary>
    public bool IfPresent { get; set; }

    /// <summary><c>--parallel</c>: run in every selected project concurrently, ignoring topological order.</summary>
    public bool Parallel { get; set; }

    /// <summary><c>--workspace-concurrency &lt;n&gt;</c>.</summary>
    public int? WorkspaceConcurrency { get; set; }

    /// <summary><c>--stream</c>: print script output as it arrives, prefixed by project.</summary>
    public bool Stream { get; set; }

    public PnpmRunSettings SetScript(string script) { Script = script; return this; }
    public PnpmRunSettings AddScriptArg(string arg) { ScriptArgs.Add(arg); return this; }
    public PnpmRunSettings AddScriptArgs(params string[] args) { ScriptArgs.AddRange(args); return this; }
    public PnpmRunSettings SetIfPresent(bool v = true) { IfPresent = v; return this; }
    public PnpmRunSettings SetParallel(bool v = true) { Parallel = v; return this; }
    public PnpmRunSettings SetWorkspaceConcurrency(int? n) { WorkspaceConcurrency = n; return this; }
    public PnpmRunSettings SetStream(bool v = true) { Stream = v; return this; }

    protected override IEnumerable<string> BuildArguments()
    {
        if (string.IsNullOrWhiteSpace(Script))
            throw new InvalidOperationException("Script is required for pnpm run (set via SetScript).");
        if (WorkspaceConcurrency is < 1)
            throw new InvalidOperationException("WorkspaceConcurrency must be at least 1.");

        yield return "run";
        if (IfPresent) yield return "--if-present";
        if (Parallel) yield return "--parallel";
        if (WorkspaceConcurrency is { } n) { yield return "--workspace-concurrency"; yield return n.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        if (Stream) yield return "--stream";
        foreach (var a in WorkspaceArguments()) yield return a;
        foreach (var a in CommonArguments()) yield return a;
        foreach (var a in ExtraArguments) yield return a;
        yield return Script!;
        foreach (var a in ScriptArgs) yield return a;
    }
}

/// <summary>
/// Settings for <c>pnpm exec &lt;command&gt; [args]</c> — run a command with the project's <c>node_modules/.bin</c> on
/// PATH. Everything after the command is passed to it verbatim.
/// </summary>
public sealed class PnpmExecSettings : PnpmWorkspaceSettingsBase
{
    /// <summary>The command to run (e.g. <c>tsc</c>). Required.</summary>
    public string? Command { get; set; }

    /// <summary>Arguments for the command (one argv token each).</summary>
    public List<string> Args { get; } = new();

    /// <summary><c>--parallel</c>.</summary>
    public bool Parallel { get; set; }

    /// <summary><c>--workspace-concurrency &lt;n&gt;</c>.</summary>
    public int? WorkspaceConcurrency { get; set; }

    /// <summary><c>--stream</c>.</summary>
    public bool Stream { get; set; }

    /// <summary><c>--shell-mode</c> (<c>-c</c>): run the command inside a shell.</summary>
    public bool ShellMode { get; set; }

    public PnpmExecSettings SetCommand(string command) { Command = command; return this; }
    public PnpmExecSettings AddArg(string arg) { Args.Add(arg); return this; }
    public PnpmExecSettings AddArgs(params string[] args) { Args.AddRange(args); return this; }
    public PnpmExecSettings SetParallel(bool v = true) { Parallel = v; return this; }
    public PnpmExecSettings SetWorkspaceConcurrency(int? n) { WorkspaceConcurrency = n; return this; }
    public PnpmExecSettings SetStream(bool v = true) { Stream = v; return this; }
    public PnpmExecSettings SetShellMode(bool v = true) { ShellMode = v; return this; }

    protected override IEnumerable<string> BuildArguments()
    {
        if (string.IsNullOrWhiteSpace(Command))
            throw new InvalidOperationException("Command is required for pnpm exec (set via SetCommand).");
        if (WorkspaceConcurrency is < 1)
            throw new InvalidOperationException("WorkspaceConcurrency must be at least 1.");

        yield return "exec";
        if (ShellMode) yield return "--shell-mode";
        if (Parallel) yield return "--parallel";
        if (WorkspaceConcurrency is { } n) { yield return "--workspace-concurrency"; yield return n.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        if (Stream) yield return "--stream";
        foreach (var a in WorkspaceArguments()) yield return a;
        foreach (var a in CommonArguments()) yield return a;
        foreach (var a in ExtraArguments) yield return a;
        yield return Command!;
        foreach (var a in Args) yield return a;
    }
}

/// <summary>
/// Settings for <c>pnpm dlx</c>. The package must be an exact <c>name@x.y.z</c> (or <c>@scope/name@x.y.z</c>): a build
/// machine shouldn't fetch whatever <c>latest</c> is today. <see cref="AllowUnpinned"/> overrides, deliberately.
/// </summary>
public sealed class PnpmDlxSettings : PnpmSettingsBase
{
    /// <summary>The package, pinned: <c>cowsay@1.6.0</c>. Required.</summary>
    public string? Package { get; set; }

    /// <summary>
    /// The binary to run when it differs from the package name. Emits <c>--package &lt;spec&gt; &lt;command&gt;</c>.
    /// When null, the package spec itself is the command.
    /// </summary>
    public string? Command { get; set; }

    /// <summary>Arguments for the command (one argv token each).</summary>
    public List<string> Args { get; } = new();

    /// <summary>Permit a spec without an exact version (<c>pkg</c>, <c>pkg@latest</c>, <c>pkg@^1</c>). Off by default.</summary>
    public bool AllowUnpinned { get; set; }

    /// <summary><c>--allow-build</c> package names whose lifecycle scripts may run during the dlx install (repeatable).</summary>
    public List<string> AllowBuild { get; } = new();

    public PnpmDlxSettings SetPackage(string package) { Package = package; return this; }
    public PnpmDlxSettings SetCommand(string command) { Command = command; return this; }
    public PnpmDlxSettings AddArg(string arg) { Args.Add(arg); return this; }
    public PnpmDlxSettings AddArgs(params string[] args) { Args.AddRange(args); return this; }
    public PnpmDlxSettings SetAllowUnpinned(bool v = true) { AllowUnpinned = v; return this; }
    public PnpmDlxSettings AddAllowBuild(string package) { AllowBuild.Add(package); return this; }

    /// <summary>True when <paramref name="spec"/> ends in <c>@&lt;exact semver&gt;</c> after the package name.</summary>
    public static bool IsPinned(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return false;
        var at = spec.LastIndexOf('@');
        if (at <= 0) return false; // no version, or only a scope marker
        var name = spec[..at];
        if (name.Contains(':') || (name.Contains('/') && !name.StartsWith('@'))) return false;
        return ExactSemVer.IsMatch(spec[(at + 1)..]);
    }

    protected override IEnumerable<string> BuildArguments()
    {
        if (string.IsNullOrWhiteSpace(Package))
            throw new InvalidOperationException("Package is required for pnpm dlx (set via SetPackage).");
        if (!AllowUnpinned && !IsPinned(Package))
            throw new InvalidOperationException(
                $"pnpm dlx refuses '{Package}': pin an exact version (name@1.2.3), or set AllowUnpinned to accept a floating tool.");

        yield return "dlx";
        foreach (var a in Repeated("--allow-build", AllowBuild)) yield return a;
        foreach (var a in CommonArguments()) yield return a;
        foreach (var a in ExtraArguments) yield return a;
        if (Command is null)
        {
            yield return Package!;
        }
        else
        {
            yield return "--package"; yield return Package!;
            yield return Command;
        }
        foreach (var a in Args) yield return a;
    }
}

/// <summary>
/// Settings for <c>pnpm fetch</c> — populate the store from the lockfile only, ignoring <c>package.json</c>. Cache the
/// result in a Docker layer or CI cache, then run an offline <see cref="PnpmInstallSettings"/>.
/// </summary>
public sealed class PnpmFetchSettings : PnpmSettingsBase
{
    /// <summary><c>--prod</c>.</summary>
    public bool Production { get; set; }

    /// <summary><c>--dev</c>.</summary>
    public bool Development { get; set; }

    /// <summary><c>--ignore-pnpmfile</c>.</summary>
    public bool IgnorePnpmfile { get; set; }

    /// <summary>
    /// <c>--offline</c>. pnpm 12.16.1 accepts it on <c>fetch</c> although <c>pnpm help fetch</c> doesn't list it; an
    /// unknown flag is a usage error (exit 2), so acceptance was checked directly.
    /// </summary>
    public bool Offline { get; set; }

    /// <summary><c>--prefer-offline</c> (accepted, see <see cref="Offline"/>).</summary>
    public bool PreferOffline { get; set; }

    public PnpmFetchSettings SetProduction(bool v = true) { Production = v; return this; }
    public PnpmFetchSettings SetDevelopment(bool v = true) { Development = v; return this; }
    public PnpmFetchSettings SetIgnorePnpmfile(bool v = true) { IgnorePnpmfile = v; return this; }
    public PnpmFetchSettings SetOffline(bool v = true) { Offline = v; return this; }
    public PnpmFetchSettings SetPreferOffline(bool v = true) { PreferOffline = v; return this; }

    protected override IEnumerable<string> BuildArguments()
    {
        Exclusive.Check(Production, Development, "pnpm fetch: Production (--prod) and Development (--dev) are mutually exclusive.");
        Exclusive.Check(Offline, PreferOffline, "pnpm fetch: Offline and PreferOffline are mutually exclusive.");
        yield return "fetch";
        if (Production) yield return "--prod";
        if (Development) yield return "--dev";
        if (IgnorePnpmfile) yield return "--ignore-pnpmfile";
        if (Offline) yield return "--offline";
        if (PreferOffline) yield return "--prefer-offline";
        foreach (var a in CommonArguments()) yield return a;
        foreach (var a in ExtraArguments) yield return a;
    }
}

/// <summary>Settings for <c>pnpm audit</c>. Exits non-zero when advisories at or above the level are found.</summary>
public sealed class PnpmAuditSettings : PnpmSettingsBase
{
    private static readonly string[] Levels = { "info", "low", "moderate", "high", "critical" };

    /// <summary><c>--audit-level</c>: info, low, moderate, high or critical.</summary>
    public string? AuditLevel { get; set; }

    /// <summary><c>--json</c>. Parse the output with <see cref="PnpmAuditReport"/>.</summary>
    public bool Json { get; set; }

    /// <summary><c>--prod</c>: only audit dependencies and optionalDependencies.</summary>
    public bool Production { get; set; }

    /// <summary><c>--dev</c>: only audit devDependencies.</summary>
    public bool Development { get; set; }

    /// <summary><c>--ignore-registry-errors</c>: exit 0 when the registry answers with an error (an unreachable audit endpoint).</summary>
    public bool IgnoreRegistryErrors { get; set; }

    /// <summary><c>--ignore-unfixable</c>.</summary>
    public bool IgnoreUnfixable { get; set; }

    /// <summary><c>--ignore &lt;GHSA&gt;</c> (repeatable): advisories to accept.</summary>
    public List<string> IgnoredAdvisories { get; } = new();

    public PnpmAuditSettings SetAuditLevel(string? level) { AuditLevel = level; return this; }
    public PnpmAuditSettings SetJson(bool v = true) { Json = v; return this; }
    public PnpmAuditSettings SetProduction(bool v = true) { Production = v; return this; }
    public PnpmAuditSettings SetDevelopment(bool v = true) { Development = v; return this; }
    public PnpmAuditSettings SetIgnoreRegistryErrors(bool v = true) { IgnoreRegistryErrors = v; return this; }
    public PnpmAuditSettings SetIgnoreUnfixable(bool v = true) { IgnoreUnfixable = v; return this; }
    public PnpmAuditSettings AddIgnoredAdvisory(string ghsa) { IgnoredAdvisories.Add(ghsa); return this; }

    protected override IEnumerable<string> BuildArguments()
    {
        Exclusive.Check(Production, Development, "pnpm audit: Production (--prod) and Development (--dev) are mutually exclusive.");
        if (AuditLevel is not null && Array.IndexOf(Levels, AuditLevel) < 0)
            throw new InvalidOperationException($"AuditLevel '{AuditLevel}' must be one of {string.Join(", ", Levels)}.");

        yield return "audit";
        if (AuditLevel is not null) { yield return "--audit-level"; yield return AuditLevel; }
        if (Json) yield return "--json";
        if (Production) yield return "--prod";
        if (Development) yield return "--dev";
        if (IgnoreRegistryErrors) yield return "--ignore-registry-errors";
        if (IgnoreUnfixable) yield return "--ignore-unfixable";
        foreach (var a in Repeated("--ignore", IgnoredAdvisories)) yield return a;
        foreach (var a in CommonArguments()) yield return a;
        foreach (var a in ExtraArguments) yield return a;
    }
}

/// <summary>Settings for <c>pnpm store path</c> — print the active store directory.</summary>
public sealed class PnpmStorePathSettings : PnpmSettingsBase
{
    protected override IEnumerable<string> BuildArguments()
    {
        yield return "store";
        yield return "path";
        foreach (var a in CommonArguments()) yield return a;
        foreach (var a in ExtraArguments) yield return a;
    }
}

/// <summary>Settings for <c>pnpm store prune</c> — remove unreferenced packages from the store.</summary>
public sealed class PnpmStorePruneSettings : PnpmSettingsBase
{
    protected override IEnumerable<string> BuildArguments()
    {
        yield return "store";
        yield return "prune";
        foreach (var a in CommonArguments()) yield return a;
        foreach (var a in ExtraArguments) yield return a;
    }
}

/// <summary>Settings for <c>pnpm pack</c> — create a tarball.</summary>
public sealed class PnpmPackSettings : PnpmSettingsBase
{
    /// <summary><c>--pack-destination</c>: directory for the tarball.</summary>
    public string? PackDestination { get; set; }

    /// <summary><c>--out</c>: output path template (<c>%s</c> = name, <c>%v</c> = version). Excludes <see cref="PackDestination"/>.</summary>
    public string? Out { get; set; }

    /// <summary><c>--json</c>.</summary>
    public bool Json { get; set; }

    /// <summary><c>--dry-run</c>.</summary>
    public bool DryRun { get; set; }

    /// <summary><c>--pack-gzip-level</c> (0–9).</summary>
    public int? GzipLevel { get; set; }

    public PnpmPackSettings SetPackDestination(string? dir) { PackDestination = dir; return this; }
    public PnpmPackSettings SetOut(string? template) { Out = template; return this; }
    public PnpmPackSettings SetJson(bool v = true) { Json = v; return this; }
    public PnpmPackSettings SetDryRun(bool v = true) { DryRun = v; return this; }
    public PnpmPackSettings SetGzipLevel(int? level) { GzipLevel = level; return this; }

    protected override IEnumerable<string> BuildArguments()
    {
        if (PackDestination is not null && Out is not null)
            throw new InvalidOperationException("pnpm pack: PackDestination and Out are alternatives; set one.");
        if (GzipLevel is < 0 or > 9) throw new InvalidOperationException("GzipLevel must be 0-9.");

        yield return "pack";
        if (!string.IsNullOrWhiteSpace(PackDestination)) { yield return "--pack-destination"; yield return PackDestination!; }
        if (!string.IsNullOrWhiteSpace(Out)) { yield return "--out"; yield return Out!; }
        if (GzipLevel is { } g) { yield return "--pack-gzip-level"; yield return g.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        if (Json) yield return "--json";
        if (DryRun) yield return "--dry-run";
        foreach (var a in CommonArguments()) yield return a;
        foreach (var a in ExtraArguments) yield return a;
    }
}

/// <summary>
/// Settings for <c>pnpm publish</c>. <c>--no-git-checks</c> is on by default because CI checks out a detached HEAD.
/// The one-time password is a <see cref="Secret"/> sent as <c>PNPM_CONFIG_OTP</c>, not <c>--otp</c>.
/// </summary>
public sealed class PnpmPublishSettings : PnpmWorkspaceSettingsBase
{
    private static readonly string[] AccessLevels = { "public", "restricted" };

    /// <summary>Tarball or directory to publish. Null = the current directory.</summary>
    public string? Package { get; set; }

    /// <summary><c>--tag</c>: dist-tag (default <c>latest</c>).</summary>
    public string? Tag { get; set; }

    /// <summary><c>--access</c>: <c>public</c> or <c>restricted</c>.</summary>
    public string? Access { get; set; }

    /// <summary><c>--dry-run</c>: everything except the upload.</summary>
    public bool DryRun { get; set; }

    /// <summary><c>--no-git-checks</c>. Default true.</summary>
    public bool NoGitChecks { get; set; } = true;

    /// <summary><c>--provenance</c>.</summary>
    public bool Provenance { get; set; }

    /// <summary><c>--ignore-scripts</c>.</summary>
    public bool IgnoreScripts { get; set; }

    /// <summary><c>--json</c>.</summary>
    public bool Json { get; set; }

    /// <summary>One-time password for a 2FA registry, passed as <c>PNPM_CONFIG_OTP</c> and redacted.</summary>
    public Secret? Otp { get; set; }

    public PnpmPublishSettings SetPackage(string? package) { Package = package; return this; }
    public PnpmPublishSettings SetTag(string? tag) { Tag = tag; return this; }
    public PnpmPublishSettings SetAccess(string? access) { Access = access; return this; }
    public PnpmPublishSettings SetDryRun(bool v = true) { DryRun = v; return this; }
    public PnpmPublishSettings SetNoGitChecks(bool v = true) { NoGitChecks = v; return this; }
    public PnpmPublishSettings SetProvenance(bool v = true) { Provenance = v; return this; }
    public PnpmPublishSettings SetIgnoreScripts(bool v = true) { IgnoreScripts = v; return this; }
    public PnpmPublishSettings SetJson(bool v = true) { Json = v; return this; }
    public PnpmPublishSettings SetOtp(Secret? otp) { Otp = otp; return this; }

    protected override CapabilityTier Capability => DryRun ? CapabilityTier.Safe : CapabilityTier.SideEffectful;

    protected override void ApplyVerbEnvironment(Dictionary<string, string> env) =>
        env["PNPM_CONFIG_OTP"] = Otp?.Reveal() ?? "";

    protected override IEnumerable<Secret> CollectSecrets()
    {
        if (Otp is not null) yield return Otp;
    }

    protected override IEnumerable<string> BuildArguments()
    {
        if (Access is not null && Array.IndexOf(AccessLevels, Access) < 0)
            throw new InvalidOperationException($"Access '{Access}' must be 'public' or 'restricted'.");
        if (Tag is not null && string.IsNullOrWhiteSpace(Tag))
            throw new InvalidOperationException("Tag is blank.");

        yield return "publish";
        if (NoGitChecks) yield return "--no-git-checks";
        if (Tag is not null) { yield return "--tag"; yield return Tag; }
        if (Access is not null) { yield return "--access"; yield return Access; }
        if (DryRun) yield return "--dry-run";
        if (Provenance) yield return "--provenance";
        if (IgnoreScripts) yield return "--ignore-scripts";
        if (Json) yield return "--json";
        foreach (var a in WorkspaceArguments()) yield return a;
        foreach (var a in CommonArguments()) yield return a;
        foreach (var a in ExtraArguments) yield return a;
        if (!string.IsNullOrEmpty(Package)) yield return Package!;
    }
}

/// <summary>
/// Settings for <c>pnpm deploy &lt;dir&gt;</c> — copy a workspace project with its production dependencies into a
/// directory. Select the project with <see cref="PnpmWorkspaceSettingsBase.Filters"/>. The lockfile mode is explicit,
/// like <see cref="PnpmInstallSettings"/>.
/// </summary>
public sealed class PnpmDeploySettings : PnpmWorkspaceSettingsBase
{
    /// <summary>Target directory. Required.</summary>
    public string? Directory { get; set; }

    /// <summary><c>true</c> = <c>--frozen-lockfile</c> (the default when null), <c>false</c> = <c>--no-frozen-lockfile</c>.</summary>
    public bool? FrozenLockfile { get; set; }

    /// <summary><c>--prod</c>.</summary>
    public bool Production { get; set; }

    /// <summary><c>--dev</c>.</summary>
    public bool Development { get; set; }

    /// <summary><c>--offline</c>.</summary>
    public bool Offline { get; set; }

    /// <summary><c>--prefer-offline</c>.</summary>
    public bool PreferOffline { get; set; }

    /// <summary><c>--ignore-scripts</c>.</summary>
    public bool IgnoreScripts { get; set; }

    /// <summary><c>--legacy</c>: the pre-10 deploy implementation, which doesn't need <c>inject-workspace-packages</c>.</summary>
    public bool Legacy { get; set; }

    public PnpmDeploySettings SetDirectory(string dir) { Directory = dir; return this; }
    public PnpmDeploySettings SetFrozenLockfile(bool v = true) { FrozenLockfile = v; return this; }
    public PnpmDeploySettings SetProduction(bool v = true) { Production = v; return this; }
    public PnpmDeploySettings SetDevelopment(bool v = true) { Development = v; return this; }
    public PnpmDeploySettings SetOffline(bool v = true) { Offline = v; return this; }
    public PnpmDeploySettings SetPreferOffline(bool v = true) { PreferOffline = v; return this; }
    public PnpmDeploySettings SetIgnoreScripts(bool v = true) { IgnoreScripts = v; return this; }
    public PnpmDeploySettings SetLegacy(bool v = true) { Legacy = v; return this; }

    protected override IEnumerable<string> BuildArguments()
    {
        if (string.IsNullOrWhiteSpace(Directory))
            throw new InvalidOperationException("Directory is required for pnpm deploy (set via SetDirectory).");
        Exclusive.Check(Production, Development, "pnpm deploy: Production (--prod) and Development (--dev) are mutually exclusive.");
        Exclusive.Check(Offline, PreferOffline, "pnpm deploy: Offline and PreferOffline are mutually exclusive.");

        yield return "deploy";
        yield return (FrozenLockfile ?? true) ? "--frozen-lockfile" : "--no-frozen-lockfile";
        if (Production) yield return "--prod";
        if (Development) yield return "--dev";
        if (Offline) yield return "--offline";
        if (PreferOffline) yield return "--prefer-offline";
        if (IgnoreScripts) yield return "--ignore-scripts";
        if (Legacy) yield return "--legacy";
        foreach (var a in WorkspaceArguments()) yield return a;
        foreach (var a in CommonArguments()) yield return a;
        foreach (var a in ExtraArguments) yield return a;
        yield return Directory!;
    }
}

/// <summary>
/// Settings for the idempotent version stamp: <c>pnpm version &lt;ver&gt; --no-git-tag-version --allow-same-version
/// --no-git-checks</c>. Verified on pnpm 12.16.1: plain <c>pnpm version 1.0.7</c> on a package already at 1.0.7 exits 1
/// (<c>ERR_PNPM_VERSION_NOT_CHANGED</c>), <c>--allow-same-version</c> makes it exit 0, and in a dirty git working tree
/// <c>--no-git-checks</c> is also needed (<c>ERR_PNPM_UNCLEAN_WORKING_TREE</c> otherwise). The version must be an exact
/// semver; bump keywords (<c>patch</c>, …) are refused because they aren't idempotent.
/// </summary>
public sealed class PnpmSetVersionSettings : PnpmSettingsBase
{
    /// <summary>Target version, e.g. <c>1.0.7</c>. Required.</summary>
    public string? Version { get; set; }

    public PnpmSetVersionSettings SetVersion(string version) { Version = version; return this; }

    protected override IEnumerable<string> BuildArguments()
    {
        if (string.IsNullOrWhiteSpace(Version))
            throw new InvalidOperationException("Pnpm.SetVersion requires a non-empty Version.");
        if (!ExactSemVer.IsMatch(Version!))
            throw new InvalidOperationException($"Version '{Version}' is not an exact semver (x.y.z[-pre][+build]); bump keywords are not idempotent.");

        yield return "version";
        yield return Version!;
        yield return "--no-git-tag-version";
        yield return "--allow-same-version";
        yield return "--no-git-checks";
        foreach (var a in CommonArguments()) yield return a;
    }
}

/// <summary>Raw escape hatch for verbs and flags that don't have typed wrappers. Registry/auth environment still applies.</summary>
public sealed class PnpmRawSettings : PnpmSettingsBase
{
    private readonly List<string> _args = new();
    public void AddArgs(IEnumerable<string> args) => _args.AddRange(args);
    protected override IEnumerable<string> BuildArguments() => _args;
}
