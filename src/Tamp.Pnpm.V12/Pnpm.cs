namespace Tamp.Pnpm.V12;

/// <summary>Top-level facade for <c>pnpm</c> verbs (pnpm 12).</summary>
/// <remarks>
/// <para>Resolve the tool via <c>[FromPath("pnpm")]</c>:</para>
/// <code>
/// [FromPath("pnpm")] readonly Tool PnpmTool = null!;
/// </code>
/// <para>
/// Every installing verb states its lockfile mode explicitly, and every plan pins the registry/credential environment.
/// See the README for the variables verified against pnpm 12.16.1.
/// </para>
/// </remarks>
public static class Pnpm
{
    /// <summary><c>pnpm install</c>, always with an explicit <c>--frozen-lockfile</c> / <c>--no-frozen-lockfile</c>.</summary>
    public static CommandPlan Install(Tool tool, Action<PnpmInstallSettings>? configure = null) => Build(tool, configure);

    /// <summary><c>pnpm add &lt;package...&gt;</c>.</summary>
    public static CommandPlan Add(Tool tool, Action<PnpmAddSettings> configure) => Build(tool, configure);

    /// <summary><c>pnpm remove &lt;package...&gt;</c>.</summary>
    public static CommandPlan Remove(Tool tool, Action<PnpmRemoveSettings> configure) => Build(tool, configure);

    /// <summary><c>pnpm run &lt;script&gt;</c>.</summary>
    public static CommandPlan Run(Tool tool, Action<PnpmRunSettings> configure) => Build(tool, configure);

    /// <summary><c>pnpm exec &lt;command&gt;</c>.</summary>
    public static CommandPlan Exec(Tool tool, Action<PnpmExecSettings> configure) => Build(tool, configure);

    /// <summary><c>pnpm dlx &lt;name@exact-version&gt;</c>. Refuses unpinned specs unless <see cref="PnpmDlxSettings.AllowUnpinned"/>.</summary>
    public static CommandPlan Dlx(Tool tool, Action<PnpmDlxSettings> configure) => Build(tool, configure);

    /// <summary><c>pnpm fetch</c> — fill the store from the lockfile alone.</summary>
    public static CommandPlan Fetch(Tool tool, Action<PnpmFetchSettings>? configure = null) => Build(tool, configure);

    /// <summary><c>pnpm audit</c>.</summary>
    public static CommandPlan Audit(Tool tool, Action<PnpmAuditSettings>? configure = null) => Build(tool, configure);

    /// <summary><c>pnpm store path</c>.</summary>
    public static CommandPlan StorePath(Tool tool, Action<PnpmStorePathSettings>? configure = null) => Build(tool, configure);

    /// <summary><c>pnpm store prune</c>.</summary>
    public static CommandPlan StorePrune(Tool tool, Action<PnpmStorePruneSettings>? configure = null) => Build(tool, configure);

    /// <summary><c>pnpm pack</c>.</summary>
    public static CommandPlan Pack(Tool tool, Action<PnpmPackSettings>? configure = null) => Build(tool, configure);

    /// <summary><c>pnpm publish</c> (<c>--no-git-checks</c> by default; OTP via the environment).</summary>
    public static CommandPlan Publish(Tool tool, Action<PnpmPublishSettings>? configure = null) => Build(tool, configure);

    /// <summary><c>pnpm deploy &lt;dir&gt;</c>.</summary>
    public static CommandPlan Deploy(Tool tool, Action<PnpmDeploySettings> configure) => Build(tool, configure);

    /// <summary>
    /// Idempotent <c>pnpm version &lt;ver&gt; --no-git-tag-version --allow-same-version --no-git-checks</c>. Safe to
    /// repeat with the same version (plain <c>pnpm version</c> exits 1 on a no-op).
    /// </summary>
    public static CommandPlan SetVersion(Tool tool, string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version must be non-empty.", nameof(version));
        return Plan(tool, new PnpmSetVersionSettings { Version = version });
    }

    /// <summary>Configurable form of <see cref="SetVersion(Tool, string)"/> (working directory, registry, …).</summary>
    public static CommandPlan SetVersion(Tool tool, Action<PnpmSetVersionSettings> configure) => Build(tool, configure);

    /// <summary>Raw escape hatch: the arguments are passed through unvalidated. The registry/credential environment still applies.</summary>
    public static CommandPlan Raw(Tool tool, params string[] arguments)
    {
        if (arguments is null || arguments.Length == 0)
            throw new ArgumentException("Raw requires at least one argument.", nameof(arguments));
        var s = new PnpmRawSettings();
        s.AddArgs(arguments);
        return Plan(tool, s);
    }

    // Object-init overloads
    public static CommandPlan Install(Tool tool, PnpmInstallSettings settings) => Plan(tool, settings);
    public static CommandPlan Add(Tool tool, PnpmAddSettings settings) => Plan(tool, settings);
    public static CommandPlan Remove(Tool tool, PnpmRemoveSettings settings) => Plan(tool, settings);
    public static CommandPlan Run(Tool tool, PnpmRunSettings settings) => Plan(tool, settings);
    public static CommandPlan Exec(Tool tool, PnpmExecSettings settings) => Plan(tool, settings);
    public static CommandPlan Dlx(Tool tool, PnpmDlxSettings settings) => Plan(tool, settings);
    public static CommandPlan Fetch(Tool tool, PnpmFetchSettings settings) => Plan(tool, settings);
    public static CommandPlan Audit(Tool tool, PnpmAuditSettings settings) => Plan(tool, settings);
    public static CommandPlan StorePath(Tool tool, PnpmStorePathSettings settings) => Plan(tool, settings);
    public static CommandPlan StorePrune(Tool tool, PnpmStorePruneSettings settings) => Plan(tool, settings);
    public static CommandPlan Pack(Tool tool, PnpmPackSettings settings) => Plan(tool, settings);
    public static CommandPlan Publish(Tool tool, PnpmPublishSettings settings) => Plan(tool, settings);
    public static CommandPlan Deploy(Tool tool, PnpmDeploySettings settings) => Plan(tool, settings);
    public static CommandPlan SetVersion(Tool tool, PnpmSetVersionSettings settings) => Plan(tool, settings);

    private static CommandPlan Build<T>(Tool tool, Action<T>? configure) where T : PnpmSettingsBase, new()
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        var s = new T();
        configure?.Invoke(s);
        return s.ToCommandPlan(tool);
    }

    private static CommandPlan Plan<T>(Tool tool, T settings) where T : PnpmSettingsBase
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan(tool);
    }
}
