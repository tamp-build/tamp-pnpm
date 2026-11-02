using System.Text.RegularExpressions;

namespace Tamp.Pnpm.V12;

/// <summary>
/// Common knobs shared by every <c>pnpm</c> verb's settings class.
/// </summary>
/// <remarks>
/// <para>
/// <b>Environment hardening.</b> Every plan sets <c>PNPM_CONFIG_REGISTRY</c> explicitly (to
/// <see cref="Registry"/>, or to an empty string, which pnpm 12 treats as unset), so an inherited value can't
/// redirect a build to another registry. Credentials travel as <c>PNPM_CONFIG_//host/:_authToken</c> and
/// <c>PNPM_CONFIG_OTP</c>, never argv. The README lists every variable that was checked against pnpm 12.16.1.
/// </para>
/// </remarks>
public abstract class PnpmSettingsBase
{
    private static readonly string[] Reporters = { "default", "append-only", "ndjson", "silent" };

    /// <summary>Working directory of the spawned <c>pnpm</c> process. Typically the workspace root.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Per-invocation environment variables on top of the inherited environment. The variables the wrapper controls
    /// (see the README table) are applied afterwards and win.
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Registry base URL (http or https). Passed as <c>PNPM_CONFIG_REGISTRY</c>. When null the variable is set to an
    /// empty string, so the registry comes from <c>.npmrc</c> / <c>pnpm-workspace.yaml</c> / the default.
    /// </summary>
    public string? Registry { get; set; }

    /// <summary>
    /// Registry auth token (a bearer token). Passed through the environment as
    /// <c>PNPM_CONFIG_//&lt;host&gt;/:_authToken</c>, never on the command line, and registered for redaction.
    /// </summary>
    public Secret? AuthToken { get; set; }

    /// <summary>
    /// The registry the <see cref="AuthToken"/> belongs to, when it isn't <see cref="Registry"/> (for instance a scoped
    /// registry configured in <c>.npmrc</c>). Defaults to <see cref="Registry"/>, then <c>https://registry.npmjs.org/</c>.
    /// </summary>
    public string? AuthTokenRegistry { get; set; }

    /// <summary>
    /// Allow <see cref="AuthToken"/> for a plain <c>http://</c> registry on a non-loopback host. Off by default: the
    /// token would travel unencrypted.
    /// </summary>
    public bool AllowInsecureAuth { get; set; }

    /// <summary><c>--store-dir</c>: where the content-addressable store lives.</summary>
    public string? StoreDir { get; set; }

    /// <summary><c>--reporter</c>: <c>default</c>, <c>append-only</c>, <c>ndjson</c> or <c>silent</c>.</summary>
    public string? Reporter { get; set; }

    /// <summary>
    /// Run through Corepack: set the tool to <c>corepack</c> and the wrapper prepends <c>pnpm</c> to the argument list
    /// (<c>corepack pnpm install …</c>), so the version in the <c>packageManager</c> field is used.
    /// </summary>
    public bool UseCorepack { get; set; }

    /// <summary>Arguments added after the typed flags and before positional arguments, for flags this wrapper doesn't model. One argv token each.</summary>
    public List<string> ExtraArguments { get; } = new();

    /// <summary>Subclasses build the per-verb argument list. The verb token(s) come first.</summary>
    protected abstract IEnumerable<string> BuildArguments();

    /// <summary>Extra environment entries a verb owns (e.g. <c>PNPM_CONFIG_OTP</c> for publish).</summary>
    protected virtual void ApplyVerbEnvironment(Dictionary<string, string> env) { }

    /// <summary>Secrets beyond <see cref="AuthToken"/>.</summary>
    protected virtual IEnumerable<Secret> CollectSecrets() => Array.Empty<Secret>();

    /// <summary>The capability tier of the verb. External mutation (publish) is <see cref="CapabilityTier.SideEffectful"/>.</summary>
    protected virtual CapabilityTier Capability => CapabilityTier.Safe;

    /// <summary>Arguments shared by every verb, appended after the verb's own flags.</summary>
    protected IEnumerable<string> CommonArguments()
    {
        if (!string.IsNullOrWhiteSpace(StoreDir)) { yield return "--store-dir"; yield return StoreDir!; }
        if (Reporter is not null)
        {
            if (Array.IndexOf(Reporters, Reporter) < 0)
                throw new InvalidOperationException($"Reporter '{Reporter}' is not a pnpm 12 reporter ({string.Join(", ", Reporters)}).");
            yield return "--reporter"; yield return Reporter;
        }
    }

    internal CommandPlan ToCommandPlan(Tool tool)
    {
        if (tool is null) throw new ArgumentNullException(nameof(tool));

        var args = new List<string>();
        if (UseCorepack) args.Add("pnpm");
        args.AddRange(BuildArguments());

        var env = new Dictionary<string, string>(EnvironmentVariables, StringComparer.Ordinal)
        {
            ["PNPM_CONFIG_REGISTRY"] = ValidatedRegistry(Registry) ?? "",
        };
        if (AuthToken is { } token)
        {
            var target = ValidatedRegistry(AuthTokenRegistry) ?? ValidatedRegistry(Registry) ?? "https://registry.npmjs.org/";
            var uri = new Uri(target);
            if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback && !AllowInsecureAuth)
                throw new InvalidOperationException(
                    $"Refusing to send an auth token to the plain-http registry {uri.Authority}. Use https, or set AllowInsecureAuth.");
            env[AuthKey(uri)] = token.Reveal();
        }
        ApplyVerbEnvironment(env);

        var secrets = new List<Secret>();
        if (AuthToken is not null) secrets.Add(AuthToken);
        secrets.AddRange(CollectSecrets());

        return new CommandPlan
        {
            Executable = tool.Executable.Value,
            Arguments = args,
            Environment = env,
            WorkingDirectory = WorkingDirectory ?? tool.WorkingDirectory,
            Secrets = secrets,
            RequiredCapability = Capability,
        };
    }

    /// <summary>The nerf-darted config key pnpm matches credentials against: <c>//host[:port]/path/</c>.</summary>
    internal static string AuthKey(Uri registry)
    {
        var path = registry.AbsolutePath;
        if (!path.EndsWith('/')) path += "/";
        return $"PNPM_CONFIG_//{registry.Authority}{path}:_authToken";
    }

    private static string? ValidatedRegistry(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"Registry '{url}' must be an absolute http(s) URL.");
        return u.ToString();
    }

    /// <summary>Appends <paramref name="values"/> as repeated <c>--flag value</c> pairs, rejecting blanks.</summary>
    internal static IEnumerable<string> Repeated(string flag, IEnumerable<string> values)
    {
        foreach (var v in values)
        {
            if (string.IsNullOrWhiteSpace(v)) throw new InvalidOperationException($"A value for {flag} is blank.");
            yield return flag; yield return v;
        }
    }

    internal static readonly Regex ExactSemVer =
        new(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
}

/// <summary>Fluent setters for the common knobs.</summary>
public static class PnpmSettingsBaseExtensions
{
    public static T SetWorkingDirectory<T>(this T s, string? cwd) where T : PnpmSettingsBase { s.WorkingDirectory = cwd; return s; }
    public static T SetEnvironmentVariable<T>(this T s, string name, string value) where T : PnpmSettingsBase { s.EnvironmentVariables[name] = value; return s; }
    public static T SetRegistry<T>(this T s, string? registry) where T : PnpmSettingsBase { s.Registry = registry; return s; }
    public static T SetAuthToken<T>(this T s, Secret? token) where T : PnpmSettingsBase { s.AuthToken = token; return s; }
    public static T SetAuthTokenRegistry<T>(this T s, string? registry) where T : PnpmSettingsBase { s.AuthTokenRegistry = registry; return s; }
    public static T SetAllowInsecureAuth<T>(this T s, bool v = true) where T : PnpmSettingsBase { s.AllowInsecureAuth = v; return s; }
    public static T SetStoreDir<T>(this T s, string? dir) where T : PnpmSettingsBase { s.StoreDir = dir; return s; }
    public static T SetReporter<T>(this T s, string? reporter) where T : PnpmSettingsBase { s.Reporter = reporter; return s; }
    public static T SetUseCorepack<T>(this T s, bool v = true) where T : PnpmSettingsBase { s.UseCorepack = v; return s; }
    public static T AddExtraArgument<T>(this T s, string arg) where T : PnpmSettingsBase { s.ExtraArguments.Add(arg); return s; }
}

/// <summary>
/// Workspace selection shared by the verbs that can fan out over a pnpm workspace:
/// <c>--filter</c>, <c>--recursive</c>, <c>--workspace-root</c>.
/// </summary>
public abstract class PnpmWorkspaceSettingsBase : PnpmSettingsBase
{
    /// <summary><c>--filter</c> selectors (repeatable): <c>@scope/*</c>, <c>./path</c>, <c>name...</c>, <c>!excluded</c>.</summary>
    public List<string> Filters { get; } = new();

    /// <summary><c>-r</c>/<c>--recursive</c>: run for every workspace project.</summary>
    public bool Recursive { get; set; }

    /// <summary><c>-w</c>/<c>--workspace-root</c>: target the workspace root project.</summary>
    public bool WorkspaceRoot { get; set; }

    /// <summary>
    /// <c>--fail-if-no-match</c>, on by default whenever <see cref="Filters"/> is set. pnpm 12 otherwise exits 0
    /// when a <c>--filter</c> matches nothing, which turns a renamed package into a silently skipped step.
    /// </summary>
    public bool FailIfNoMatch { get; set; } = true;

    protected IEnumerable<string> WorkspaceArguments()
    {
        foreach (var a in Repeated("--filter", Filters)) yield return a;
        if (Filters.Count > 0 && FailIfNoMatch) yield return "--fail-if-no-match";
        if (Recursive) yield return "--recursive";
        if (WorkspaceRoot) yield return "--workspace-root";
    }
}

/// <summary>Fluent setters for workspace selection.</summary>
public static class PnpmWorkspaceSettingsExtensions
{
    public static T AddFilter<T>(this T s, string filter) where T : PnpmWorkspaceSettingsBase { s.Filters.Add(filter); return s; }
    public static T SetRecursive<T>(this T s, bool v = true) where T : PnpmWorkspaceSettingsBase { s.Recursive = v; return s; }
    public static T SetWorkspaceRoot<T>(this T s, bool v = true) where T : PnpmWorkspaceSettingsBase { s.WorkspaceRoot = v; return s; }
    public static T SetFailIfNoMatch<T>(this T s, bool v = true) where T : PnpmWorkspaceSettingsBase { s.FailIfNoMatch = v; return s; }
}
