# Tamp.Pnpm.V12

> Tamp wrapper for the **[pnpm](https://pnpm.io) 12** package manager, for build machines rather than people. Typed `install`, `add`, `remove`, `run`, `exec`, `dlx`, `fetch`, `audit`, `store`, `pack`, `publish`, `deploy` and an idempotent version stamp, with:
>
> - the lockfile mode always stated explicitly, never left to the `CI` variable;
> - registry credentials passed as redacted secrets through the environment, never `argv`;
> - the environment variables pnpm 12 actually honours pinned on every command;
> - `dlx` refusing floating tool versions;
> - installs refused while `package.json` keeps settings in its `pnpm` field, which pnpm 12 ignores (the SPFx generator writes its security overrides there).

| Package | Status |
|---|---|
| `Tamp.Pnpm.V12` | 0.2.0 |

```bash
dotnet add package Tamp.Pnpm.V12
```

Multi-targets net8 / net9 / net10. Verified against pnpm **12.16.1**. The package is named for the pnpm major, like `Tamp.Npm.V10` and `Tamp.Yarn.V4`: a pnpm 13 wrapper will be a new package, so a build pinned to 12 keeps working.

## A CI pipeline

```csharp
using Tamp;
using Tamp.Pnpm.V12;

class Build : TampBuild
{
    [FromPath("pnpm")] readonly Tool PnpmTool = null!;

    [Secret("Registry token", EnvironmentVariable = "FEED_TOKEN")]
    readonly Secret FeedToken = null!;

    const string Registry = "https://pkgs.contoso.test/npm/";

    Target Install => _ => _.Executes(() => Pnpm.Install(PnpmTool, s => s
        .SetRegistry(Registry)
        .SetAuthToken(FeedToken)        // PNPM_CONFIG_//pkgs.contoso.test/npm/:_authToken, redacted in logs
        .SetIgnoreScripts()
        .SetPreferOffline()));          // --frozen-lockfile is the default and always explicit

    Target Compile => _ => _.DependsOn(nameof(Install)).Executes(() => Pnpm.Run(PnpmTool, s => s
        .SetScript("build")
        .SetRecursive()
        .SetStream()
        .SetWorkspaceConcurrency(4)));

    Target Web => _ => _.DependsOn(nameof(Install)).Executes(() => Pnpm.Run(PnpmTool, s => s
        .AddFilter("@contoso/web...")   // the package and what it depends on
        .SetScript("test")
        .AddScriptArg("--ci")));        // goes to the script, after its name, without a "--"

    Target Audit => _ => _.Executes(() => Pnpm.Audit(PnpmTool, s => s
        .SetProduction().SetAuditLevel("high").SetJson()));

    Target Stamp => _ => _.Executes(() => Pnpm.SetVersion(PnpmTool, "1.4.2"));   // safe to repeat
}
```

Docker layer or offline CI: `Pnpm.Fetch` fills the store from the lockfile alone, then `Pnpm.Install(s => s.SetOffline())` links it.

## Verbs

| Facade | Command line |
|---|---|
| `Pnpm.Install` | `pnpm install --frozen-lockfile\|--no-frozen-lockfile [--prefer-offline] [--offline] [--ignore-scripts] [--prod\|--dev] [--lockfile-only] [--filter …] [--recursive]` |
| `Pnpm.Add` | `pnpm add [--save-dev\|--save-optional] [--save-exact] [--ignore-scripts] [--filter …] [--recursive] [--workspace-root] <package…>` |
| `Pnpm.Remove` | `pnpm remove [--save-dev\|--save-optional\|--save-prod] [--filter …] [--recursive] [--workspace-root] <package…>` |
| `Pnpm.Run` | `pnpm run [--if-present] [--parallel] [--workspace-concurrency n] [--stream] [--filter …] [--recursive] [--workspace-root] <script> [args…]` |
| `Pnpm.Exec` | `pnpm exec [--shell-mode] [--parallel] [--workspace-concurrency n] [--stream] [--filter …] [--recursive] <command> [args…]` |
| `Pnpm.Dlx` | `pnpm dlx [--allow-build x] <name@x.y.z> [args…]`, or `pnpm dlx --package <name@x.y.z> <bin> [args…]` |
| `Pnpm.Fetch` | `pnpm fetch [--prod\|--dev] [--ignore-pnpmfile] [--offline\|--prefer-offline]` |
| `Pnpm.Audit` | `pnpm audit [--audit-level l] [--json] [--prod\|--dev] [--ignore-registry-errors] [--ignore-unfixable] [--ignore GHSA…]` |
| `Pnpm.StorePath` / `StorePrune` | `pnpm store path` / `pnpm store prune` |
| `Pnpm.Pack` | `pnpm pack [--pack-destination d \| --out template] [--pack-gzip-level n] [--json] [--dry-run]` |
| `Pnpm.Publish` | `pnpm publish --no-git-checks [--tag t] [--access public\|restricted] [--dry-run] [--provenance] [--ignore-scripts] [--json] [--filter …] [--recursive] [package]` |
| `Pnpm.Deploy` | `pnpm deploy --frozen-lockfile\|--no-frozen-lockfile [--prod\|--dev] [--offline\|--prefer-offline] [--ignore-scripts] [--legacy] [--filter …] <dir>` |
| `Pnpm.SetVersion` | `pnpm version <x.y.z> --no-git-tag-version --allow-same-version --no-git-checks` |
| `Pnpm.Raw` | `pnpm <args…>`, unvalidated |

Every settings type also has `SetWorkingDirectory`, `SetEnvironmentVariable`, `SetRegistry`, `SetAuthToken`, `SetAuthTokenRegistry`, `SetStoreDir`, `SetReporter`, `SetUseCorepack`, `SetAllowIgnoredPnpmField` and `AddExtraArgument` (flags this wrapper doesn't model, placed before the positional arguments), and there are object-init overloads for each verb. Every flag the wrapper emits was checked against `pnpm help <verb>` for 12.16.1. The one exception is `fetch --offline` / `--prefer-offline`: pnpm accepts them (an unknown flag is a usage error, exit 2) but `pnpm help fetch` doesn't list them.

Validation is eager: settings stranded in the `package.json` `pnpm` field (see [below](#the-packagejson-pnpm-field-and-spfx)), mutually exclusive flags (`--prod` with `--dev`, `--offline` with `--prefer-offline`, `--lockfile-only` with `--frozen-lockfile`), a missing script or package, a bad `--audit-level`, `--access` or reporter, and blank filters all throw `InvalidOperationException` when the plan is built.

### Choices worth knowing

- **Frozen lockfile.** pnpm 12 turns `--frozen-lockfile` on when `CI` is set and a lockfile exists, and off otherwise (checked with a drifted manifest: `CI=true` fails, `CI` unset updates the lockfile). The wrapper always emits one of the two flags, and an explicit flag beats the `PNPM_CONFIG_FROZEN_LOCKFILE` variable, `CI`, and `.npmrc` (all checked). `CI` itself is left alone.
- **The `package.json` `pnpm` field is ignored** by pnpm 11 and 12, with a warning and exit 0. `install`, `add`, `remove`, `deploy` and `fetch` refuse to plan while it holds settings `pnpm-workspace.yaml` doesn't also carry. See [below](#the-packagejson-pnpm-field-and-spfx).
- **`--filter` that matches nothing exits 0** in pnpm 12 (and 10), so a renamed package silently skips a step. `FailIfNoMatch` is on by default and emits `--fail-if-no-match`.
- **Script arguments.** `pnpm run build -- --foo` runs the script with `["--", "--foo"]`: pnpm forwards everything after the script name verbatim, in 10 and in 12. So pnpm's own flags go *before* the script name, and script arguments follow it with no separator. An argument such as `--filter` after the script name reaches the script, not pnpm.
- **`dlx`** accepts `name@1.2.3`, `@scope/name@1.2.3`, with pre-release and build suffixes. It refuses a bare name, `@latest`, ranges, partial versions and non-registry specs unless `AllowUnpinned` is set.
- **`SetVersion`.** On a package already at the target, `pnpm version 1.0.7 --no-git-tag-version` exits 1 (`ERR_PNPM_VERSION_NOT_CHANGED`); `--allow-same-version` makes the no-op exit 0. Inside a git repository with uncommitted changes it also fails (`ERR_PNPM_UNCLEAN_WORKING_TREE`) unless `--no-git-checks` is passed, so all three flags are always emitted. Only exact semver is accepted; `patch`, `from-git` and the like aren't idempotent.
- **`Publish`** defaults `--no-git-checks` (CI checks out a detached HEAD) and is stamped `CapabilityTier.SideEffectful` unless `--dry-run`.
- **`audit`** exits non-zero when advisories at or above the level exist. `PnpmAuditReport.Parse(json)` reads `metadata.vulnerabilities` and the advisory count from `--json` output. It reads only those fields; the clean report in its tests was captured live, the one with findings was written by hand. `audit --fix` is not wrapped: it rewrites the workspace.
- **`deploy`**'s flags were taken from `pnpm help deploy`; it was not run live (see [Not verified](#not-verified)).

## What the wrapper pins in the environment

pnpm 12 reads `PNPM_CONFIG_<KEY>` and ignores most `npm_config_*` variables, which is the opposite of pnpm 10. Everything below was reproduced against pnpm 12.16.1, using a loopback registry that records the requests it receives, and a drifted lockfile.

| Variable | What pnpm 12 does with it | Wrapper |
|---|---|---|
| `PNPM_CONFIG_REGISTRY` | Honoured. Beats a project `.npmrc` `registry=`; loses to `--registry`. An **empty** value is ignored, so `.npmrc` still decides. | Set on **every** plan, to `Registry` or to empty, so an inherited value can't redirect the build. Wins over `SetEnvironmentVariable`. |
| `npm_config_registry`, `NPM_CONFIG_REGISTRY` | **Ignored** (the request went to registry.npmjs.org). pnpm 10.34.9 honours `npm_config_registry`. | Not touched: it has no effect. |
| `PNPM_CONFIG_//host/path/:_authToken`, `npm_config_//host/path/:_authToken`, `NPM_CONFIG_//…` | All three are sent as `Authorization: Bearer …`. The key must match the registry host and path. | `AuthToken` sets the `PNPM_CONFIG_` form, keyed from `AuthTokenRegistry`, else `Registry`, else `registry.npmjs.org`. The value is registered as a `Secret`. Refused for plain-http non-loopback registries. |
| `NPM_TOKEN` | **No effect** on its own. A project `.npmrc` with `_authToken=${NPM_TOKEN}` is ignored too (`Ignored project-level auth setting … environment variables are not expanded in repository-controlled registry credentials`). | Not used. This is why the token goes in a `PNPM_CONFIG_//…` key rather than a `NPM_TOKEN` + `.npmrc` pair. |
| `PNPM_CONFIG_OTP` | Sent as the `npm-otp` header. An empty value sends nothing. `npm_config_otp` and `NPM_CONFIG_OTP` do nothing. | `Publish` sets it to `Otp` or to empty (so an inherited OTP isn't sent). Never `--otp` in argv. |
| `CI` | Turns `--frozen-lockfile` on by default when a lockfile exists. `CI=false` and unset behave the same. | Left as inherited. The lockfile mode is an explicit flag, so the result no longer depends on it. |
| `PNPM_CONFIG_FROZEN_LOCKFILE` | Honoured, but an explicit `--frozen-lockfile` / `--no-frozen-lockfile` beats it. `npm_config_frozen_lockfile` is ignored. | Not touched: the flag wins. |
| `PNPM_HOME` | Moves the store (`$PNPM_HOME/store/v11`). | Not touched. Use `StoreDir` (`--store-dir`) to pin the store; `PNPM_CONFIG_STORE_DIR` is honoured, `npm_config_store_dir` is not. |
| `PNPM_CONFIG_OFFLINE` and other `PNPM_CONFIG_*` | Honoured. | Not touched. `Offline` / `PreferOffline` are flags. |

`SetEnvironmentVariable` adds variables on top of the inherited environment (a proxy, say). The variables in the table that the wrapper controls are applied afterwards and win. `Raw` gets the same registry and credential handling and no verb flags.

### Differences from pnpm 10 that matter here

These were checked by running pnpm 10.34.9 next to 12.16.1:

- Configuration from the environment moved from `npm_config_*` to `PNPM_CONFIG_*` (registry, above). Code that set `npm_config_registry` for pnpm 10 silently does nothing on 12.
- `pnpm version` is now pnpm's own command; in 10 it ran npm's (`npm error Version not changed`).
- The store directory version went from `store/v10` to `store/v11`, so a cached store from pnpm 10 isn't reused.
- Unchanged: a `--filter` matching nothing exits 0, and `run` forwards a literal `--` to the script.

## The `package.json` `pnpm` field and SPFx

pnpm 11 and 12 no longer read the `pnpm` field of `package.json`. They print one line, `[WARN] The "pnpm" field in package.json is no longer read by pnpm. The following keys were ignored: "pnpm.overrides" …`, and **exit 0**, so the install runs without those settings. The settings now live in `pnpm-workspace.yaml`.

The SharePoint Framework generator (checked in 1.24.0-rc.0, 1.24.0 and 1.24.1) still writes its dependency overrides to `pnpm.overrides` for a pnpm solution, for every component type, and writes them again each time it runs. They're security pins (currently `"qs": ">=6.15.2"` and `"uuid": ">=11.1.1"`), so a pnpm 12 build of an SPFx project would quietly resolve the vulnerable versions.

Checked by overriding a transitive dependency:

| Where the override is | pnpm 9.15.9 | 10.34.9 | 11.28.9 | 12.16.1 |
|---|---|---|---|---|
| `package.json` → `pnpm.overrides` (what SPFx writes) | applied | applied | ignored, exit 0 | ignored, exit 0 |
| `pnpm-workspace.yaml` → `overrides` only | ignored | – | – | applied |
| Both | applied | applied | – | applied |

With a lockfile from pnpm ≤10 that has the overrides, a frozen install on 12 fails (`ERR_PNPM_LOCKFILE_CONFIG_MISMATCH`), but a first install, `--no-frozen-lockfile` and `--lockfile-only` all exit 0 without them.

**What the wrapper does.** Before planning `install`, `add`, `remove`, `deploy` or `fetch`, it reads `package.json` at the workspace root (the nearest directory up from the working directory with `pnpm-workspace.yaml`, else with `package.json`) and refuses when its `pnpm` field holds settings `pnpm-workspace.yaml` lacks:

- `overrides`: each entry must be in `pnpm-workspace.yaml` `overrides` with the same value. A diverged value is refused too (say, a newer SPFx generator bumped one copy).
- `onlyBuiltDependencies`, `onlyBuiltDependenciesFile`, `neverBuiltDependencies`, `ignoredBuiltDependencies`: pnpm 11 replaced these with `allowBuilds` and ignores the old names in `pnpm-workspace.yaml` too, so `allowBuilds` must be present.
- Anything else: a key of the same name must be present.

The error lists each problem and prints the `overrides` block to add. `SetAllowIgnoredPnpmField()` skips the check. Other verbs don't check (pnpm 12 already refuses to auto-install from `run` / `exec` in this state). An unreadable `package.json` or `pnpm-workspace.yaml` is left for pnpm to report.

**The fix for a project shared across pnpm versions** is to keep both copies. pnpm 9 and 10 read `package.json`, pnpm 12 reads `pnpm-workspace.yaml`, and all of them write `lockfileVersion: '9.0'`: a lockfile from either side passes a frozen install on the other.

```yaml
# pnpm-workspace.yaml (pnpm 9 needs the packages entry)
packages:
  - .
overrides:
  qs: ">=6.15.2"
  uuid: ">=11.1.1"
```

Set `"packageManager": "pnpm@12.16.1"` in `package.json` so developer machines and CI run the same pnpm.

## Tool resolution

```csharp
[FromPath("pnpm")] readonly Tool PnpmTool = null!;
```

`[FromPath]` finds `pnpm.cmd` on Windows. If `pnpm` isn't installed, `corepack enable` provides it from the `packageManager` field of `package.json` (for instance `"packageManager": "pnpm@12.16.1"`), which is the better way to pin the version on a build machine. Two routes:

- `corepack enable` once on the machine; then `[FromPath("pnpm")]` finds the shim, and Corepack picks the version from `packageManager`.
- Or leave the shim out and run through Corepack: resolve `[FromPath("corepack")] Tool Corepack` and call `Pnpm.Install(Corepack, s => s.SetUseCorepack())`. The wrapper prepends `pnpm`, giving `corepack pnpm install --frozen-lockfile`.

## Testing

```bash
dotnet test                              # unit tests, plus the live tests when pnpm 12 is on PATH
dotnet test --filter Category=Live       # live tests only
```

The `Live` category runs real pnpm against a two-package workspace (`@fixture/app` → `@fixture/lib` through `workspace:*`, so no registry is needed) and a loopback registry that records headers. Without pnpm 12 on PATH those tests skip. They cover: frozen install with `CI` unset, `true` and `false`; a drifted lockfile failing under every `CI` value; `--filter` runs and verbatim script arguments; the no-match failure; `exec`; `pack`; `fetch` then an offline install; `store path`; the idempotent version stamp in a dirty git tree; the token and OTP reaching a registry through the environment; and pnpm 12 ignoring a `pnpm.overrides` override that the same override in `pnpm-workspace.yaml` applies.

## Not verified

- **`deploy`**: flags are from `pnpm help deploy` only. pnpm 10+ also needs `inject-workspace-packages` (or `--legacy`) for it to work in a workspace.
- **`publish`**: only the credential path was exercised, against a loopback registry that answers 404. No real publish was made.
- **`audit`** was run against the real registry for a clean project only. Findings JSON in the tests is hand-written.
- **`add`, `remove`, `dlx`** were not run: they need a registry, and `dlx` downloads and executes code. Their argv is covered by unit tests and the flags by `pnpm help`.
- **Windows and macOS**: the environment-variable names containing `/` and `:` are passed through .NET's process environment. Linux was run locally; the other two are covered by CI.

## Releasing

Releases follow the Tamp dogfood pattern: bump `<Version>` in `Directory.Build.props`, tag `v<X.Y.Z>`, and the release workflow runs `dotnet tamp Ci` then `dotnet tamp Push`.

## License

MIT.
