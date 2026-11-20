# Changelog

All notable changes to `Tamp.Pnpm.V12` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0] - 2026-11-20 - .NET 11 in, .NET 8 and .NET 9 out

### Changed

- **Multi-targets `net10.0;net11.0`** instead of `net8.0;net9.0;net10.0` ([ADR 0015](https://github.com/tamp-build/tamp/blob/main/docs/adr/0015-target-framework-strategy.md)). .NET 11 shipped 2026-11-10 and .NET 8 and .NET 9 reached end of support the same day; the rule is to add a TFM the day a release ships and drop one the day Microsoft ends its support. `global.json` pins SDK 11.0.100.

  **If you target `net8.0` or `net9.0`, stay on the previous version** — this one carries no assets your project can resolve.

- **`Tamp.*` pinned to 1.21.0**, which carries a SARIF fix worth knowing about if this project's output is read as evidence: before 1.21.0, a SARIF result whose scanner omitted `level` was read as `none` — the least severe value — instead of the declared `warning`, on every runtime before net11 ([tamp#166](https://github.com/tamp-build/tamp/issues/166)).

- **Publishing moved to nuget.org trusted publishing (OIDC).** The `tamp_nuget_api_key` org secret expired 2026-11-01 along with every other pre-2026-08-17 nuget.org key. The publish job now mints a short-lived key from its own OIDC token, so there is no long-lived credential to rotate or leak.

## [0.2.0] — 2026-11-03

### Added

- `install`, `add`, `remove`, `deploy` and `fetch` refuse to plan while the root `package.json` keeps settings in its `pnpm` field that `pnpm-workspace.yaml` doesn't also carry. pnpm 11 and 12 ignore that field with one warning and exit 0, and the SharePoint Framework generator (1.24.x) writes its security overrides (`qs`, `uuid`) there, so a pnpm 12 build silently dropped them. `overrides` must match entry for entry, the old build lists need `allowBuilds`, and other keys need a key of the same name. The error prints the `overrides` block to add. `SetAllowIgnoredPnpmField()` opts out. Closes #4.
- Dependency on `YamlDotNet` 18.1.0, to read `pnpm-workspace.yaml`.

## [0.1.0] — 2026-11-02

### Added

- Initial release, verified against pnpm **12.16.1**. Closes #1.
- `Pnpm` facade with fluent and object-init forms: `Install`, `Add`, `Remove`, `Run`, `Exec`, `Dlx`, `Fetch`, `Audit`, `StorePath`, `StorePrune`, `Pack`, `Publish`, `Deploy`, `SetVersion` and `Raw`.
- `Install` (and `Deploy`) always emit `--frozen-lockfile` or `--no-frozen-lockfile`, because pnpm 12 turns frozen on by itself when `CI` is set. Frozen is the default; `LockfileOnly` defaults to not frozen and refuses an explicit frozen request.
- Workspace selection (`--filter` repeatable, `--recursive`, `--workspace-root`) with `--fail-if-no-match` on by default whenever a filter is given: pnpm 12 exits 0 when a filter matches nothing.
- `Run` puts pnpm's flags before the script name and the script's arguments after it with no `--`, since pnpm 12 forwards everything after the script verbatim, including a literal `--`.
- `Dlx` requires an exact `name@x.y.z`; `AllowUnpinned` is the explicit override.
- `SetVersion`: idempotent `pnpm version <ver> --no-git-tag-version --allow-same-version --no-git-checks`; exact semver only.
- Credentials through the environment, never argv: `AuthToken` as `PNPM_CONFIG_//<host>/:_authToken` (key derived from `Registry` / `AuthTokenRegistry`), publish `Otp` as `PNPM_CONFIG_OTP`. Both are registered for redaction. A token is refused for a plain-http, non-loopback registry unless `AllowInsecureAuth`.
- `PNPM_CONFIG_REGISTRY` is set on every plan (to `Registry`, or blank), so an inherited value can't redirect a build.
- `Publish` defaults to `--no-git-checks` and stamps `CapabilityTier.SideEffectful` unless `--dry-run`.
- `PnpmAuditReport`: a minimal reader for `pnpm audit --json` (severity counts, advisory count).
- `UseCorepack`: run as `corepack pnpm …`.
- A `Live`-category test suite that runs real pnpm 12 against a two-package workspace fixture and a loopback registry.
