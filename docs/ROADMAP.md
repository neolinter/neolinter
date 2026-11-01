# Roadmap

This file is the entry point for picking up work. Each work package (WP) below is self-contained: a new
session (human or AI) can start one from this file alone. Start a session with, for example:

> Read docs/ROADMAP.md and the "Rules for every session" section, then do WP-03.

Update this file in the same pull request when a WP is finished (move it to "Done", record decisions).

## Rules for every session

- **Read first**: `README.md`, `docs/architecture.md`, `docs/CLEAN_ROOM.md`, `docs/ADDING_A_RULE.md`.
- **Clean room**: no reverse engineering, no code, rule ids, messages or descriptions copied or paraphrased from
  other analyzers (ReSharper/JetBrains, Sonar, etc.). No third-party brand in names. Write texts yourself.
- **Frozen contracts** (changing them is a breaking change, ask first): rule ids (`NLR*`, `NLC*`),
  `neolinter.json` format, CLI exit codes `0` ok / `1` findings / `2` error, `.editorconfig` keys
  (`dotnet_diagnostic.<ID>.severity`, `<id>.<option>`).
- **Shell**: prefix every shell command with `rtk` (e.g. `rtk dotnet test Neolinter.slnx`). Use `rtk bypass <cmd>`
  only when the condensed output is unusable.
- **Git**: one branch and one pull request per WP, branched from `main`, named with gitflow prefixes
  (`feature/wp-NN-short-name`, `bugfix/...`). Commits are GPG-signed (this repository's
  local config points `gpg.program` to `C:/Program Files (x86)/GnuPG/bin/gpg.exe`; never bypass signing).
  **Never add `Co-Authored-By` lines, "Generated with Claude Code" or any AI attribution** to commits or PR
  descriptions. Never push to `main` directly, never force-push without asking.
- **Quality bar**: warnings are errors; `rtk dotnet test Neolinter.slnx` and `rtk npm test` (in `src/ide/vscode`)
  must pass; `rtk dotnet build samples/Playground` must show the expected diagnostics. Add `Reported*` /
  `NotReported*` examples to the playground for every rule change.
- **Analyzer constraints**: analyzers and code fixes target Roslyn **4.8** APIs only (netstandard2.0). Code fixes
  must not create elastic trivia (build tokens with explicit trivia or parse them) so hosts keep the user's layout.
- **Ask, don't guess**: when a WP lists an open decision, ask the maintainer before implementing it.
- **Language**: code, messages and docs in English; discussion with the maintainer in French.

## Status

| WP | Title | Depends on | Status |
|---|---|---|---|
| WP-00 | Merge the initial skeleton (PR #1) | — | in review |
| WP-01 | Repository hygiene | WP-00 | done (branch protection pending) |
| WP-02 | Release and publishing | WP-00 | done (workflow not run on GitHub yet; maintainer setup pending) |
| WP-03 | CLI: dead code (NLC0002) | WP-00 | done |
| WP-04 | CLI: duplicated code (NLC0003) | WP-00 | done |
| WP-05 | CLI: output and CI options | WP-03 or WP-04 | done |
| WP-06 | VS Code: "Solution analysis" tab | WP-03 or WP-04 | done (F5 run not verified) |
| WP-07 | Shared `.editorconfig` fixtures | WP-00 | done |
| WP-08 | Visual Studio 2022+ settings extension | WP-07 | done (window opened in VS 2026, writing not verified) |
| WP-09 | Rider settings plugin | WP-07 | todo |
| WP-10 | Zed: feasibility and integration | WP-00 | todo |
| WP-11 | NLR0001 v2 | WP-00 | done |
| WP-12 | New real-time rules | WP-00 | todo |
| WP-13 | Second language | WP-02 | todo |
| WP-14 | Localized messages | WP-00 | decision needed |

Packages that only depend on WP-00 can run in parallel in separate sessions, except that WP-03 and WP-04 both
touch `Neolinter.Cli` and `Neolinter.Cli.Core`: run them one after the other or coordinate the shared files
(`NeolinterCommand.cs`).

## Done

- **0.1 skeleton** (PR #1): monorepo, NLR0001 analyzer + code fix + fix all, VS Code settings extension,
  CLI skeleton (solution loading, `neolinter.json`, SARIF export), CI, playground, docs.
- **WP-03 dead code**: `neolinter analyze dead-code` (NLC0002), exclusions documented in
  `docs/architecture.md`, one test per exclusion, playground `neolinter.json`. Decisions: members of every
  accessibility are reported, private included; `excludePaths`/`testProjects` globs use
  `Microsoft.Extensions.FileSystemGlobbing`; an unused type is reported once instead of member by member;
  "packable library" is read from the MSBuild property `IsPackable`; progress messages go to stderr so stdout
  carries only SARIF. Also fixed the open SonarCloud issues (webview message origin, `npm ci --ignore-scripts`,
  backtracking-prone `.editorconfig` property regex).
- **WP-05 output and CI options**: `--fail-on <note|warning|error>`, `--format sarif|text`,
  `--baseline <sarif>`, GitHub code scanning upload example in the README. Decisions: `--fail-on` defaults to
  `warning` (same behaviour as before for current rules); baseline matching uses SARIF
  `partialFingerprints["neolinter/v1"]` = SHA-256 of rule id, relative path, normalized start-line text and
  occurrence index (no line number, no message); baselined findings are dropped from the report and the exit
  code; text layout is `path(line,column): level RULEID: message` with indented `related:` lines.
- **WP-01 repository hygiene**: `.gitattributes` (`* text=auto eol=lf`; the index was already LF, so
  renormalizing changes nothing), Dependabot (weekly, minor/patch grouped, for NuGet, npm and GitHub Actions),
  `SECURITY.md`, `CODE_OF_CONDUCT.md`, issue templates (bug, rule proposal), PR template with the clean-room
  reminder. Decisions: security and conduct reports go through GitHub private vulnerability reporting (no
  e-mail published). The 2 moderate `npm audit` findings (`minimatch` via `@vscode/vsce`, no fix upstream) are
  accepted: dev dependency only, not shipped; recorded in `SECURITY.md`. **Maintainer to do in GitHub
  settings**: enable private vulnerability reporting, and protect `main` as listed in
  `docs/CONTRIBUTING.md` ("Branch protection").
- **Rule ids renamed**: one global numbering, third letter for where the rule runs: `NL0001` → `NLR0001`,
  `NLD0001` → `NLC0002`, `NLC0001` → `NLC0003`; next ids `NLR0004`, `NLC0005`… Breaking change of the frozen ids,
  accepted by the maintainer before any release. Migration note in `docs/architecture.md` ("Rule ids").
  Mentions of the old ids below are updated to the new ones.
- **WP-04 duplicated code**: `neolinter analyze duplicates` (NLC0003), algorithm and exclusions in
  `docs/architecture.md`, tests in `DuplicatesTests`, playground `DuplicateExamples.cs` (one expected group), the
  VS Code "Solution analysis" tab now offers it. Decisions (maintainer-approved): copies are compared across projects
  and inside one file; a minimum token count is applied in addition to the statement count. **New optional key
  `duplicates.minTokens`** (default 50, 0 disables it; additive, backward compatible change of the frozen
  `neolinter.json`; documented in `docs/architecture.md`, `rules/NLC0003.yaml` and the README). Own choices:
  analysis on syntax only (no restore needed), all literals share one placeholder and all identifiers another,
  windows are chained into maximal ranges, copies overlapping inside one block keep the first, and a group lying
  entirely inside copies of a larger group is dropped. Not covered: edited or reordered copies (type-3 clones) and
  top-level statements.
- **WP-06 VS Code "Solution analysis" tab**: tab enabled; `dead-code` only (`duplicates` was hidden until WP-04, now enabled);
  results grouped by rule with links; optional Problems panel. Decisions (maintainer-approved): settings
  `neolinter.cli.path` and `neolinter.cli.projectPath`; detection order setting path → local tool manifest →
  global tool → project fallback; "Install…" offers global or local (creates the manifest if missing) and says the
  package is not on nuget.org yet; "Show in Problems panel" off by default, stored per workspace. Exit code `1` is
  findings, not an error. Pure logic in `src/sarif.ts` and `src/cli.ts` with `node:test` tests. Not verified: the
  F5 run in a real VS Code window (the CLI output on `samples/Playground` was checked against the parser).
- **WP-07 shared `.editorconfig` fixtures**: `tests/fixtures/editorconfig/<case>/` with `input.editorconfig`,
  optional `read.json` (`keys`, `expected`) and optional `change.json` (`key`, `value`) + `expected.editorconfig`;
  format documented in `docs/architecture.md`. The VS Code test discovers the directories (adding a case needs no
  code change). Decisions: a read case and a write case are told apart by which JSON file exists; a key that is not
  found is absent from `expected`; fixtures are `-text` in `.gitattributes` so CRLF cases stay byte exact; glob
  recognition stays in VS Code's own test; "remove key" is not expressed because the editor cannot remove keys yet
  (add a `change.json` form when it can).
- **WP-08 Visual Studio settings extension**: `src/ide/visualstudio` (`Neolinter.EditorConfig` netstandard2.0 library
  passing the WP-07 fixtures, rules embedded from `/rules`, NuGet detection; `Neolinter.VisualStudio` extension; new
  test project `Neolinter.EditorConfig.Tests` in `Neolinter.slnx`; `visualstudio` CI job on `windows-latest` that
  uploads the VSIX). Decisions: VisualStudio.Extensibility over the classic VSSDK (maintainer-approved after the
  comparison in `docs/architecture.md`); the window sits in the document well, opened from Extensions > Neolinter
  Settings; the "Solution analysis" tab is a disabled button; the VSIX uses the VS Code icon. **Remaining**: the
  VSIX builds and the window opens (F5, VS Community 2026 18.11: tab strip, rule card, severity and option controls
  displayed); not verified: writing to a real solution's `.editorconfig` from the window, the dark/light theme
  colors (text is bound to `VsBrushes.WindowTextKey`, border is a fixed gray), other VS versions; minimum Visual Studio is 17.14 (SDK version), not 17.8, so lowering it means
  trying an older SDK; adding the package from the window is not offered (banner shows the command); the project
  files are not part of `Neolinter.slnx` (Windows only); the "Solution analysis" runner is not implemented.
- **WP-11 NLR0001 v2**: the four items confirmed by the maintainer. (1) `else if` cascades whose final `else` is a
  single exit; (2) `yield break` is an exit (found in an `else`, and inserted at the end of iterator bodies);
  (3) `nlr0001.simplify_logical_operators` (boolean, default `false`); (4) `nlr0001.guard_braces` (`always` default,
  `never`; no `auto`, because v1 always writes braces). **New optional `.editorconfig` keys** (additive, backward
  compatible with the frozen key contract; documented in the README, `rules/NLR0001.yaml` and `docs/architecture.md`).
  Decisions (maintainer-approved): items 1 and 2 are **on by default** (suggestion level), so cases that were silent
  before are now reported (the tests `Iterator_NoDiagnostic` became `Iterator_LeavesWithYieldBreak`, the playground
  `NotReportedIterator` became `ReportedIteratorLeavesWithYieldBreak`); cascade rewrite is
  `if (!c1 && !c2) EXIT` then `if (c1) B1 else B2`, only when every condition is side-effect free (no call, no
  user-defined operator, no declared variable). Own choices: a cascade with a branch that is only an exit, or comments
  between its branches, is not reported; De Morgan only on the built-in `&&`/`||` with `bool` operands (nullable
  and user-defined operators are never rewritten); `never` keeps braces that carry comments; the options only affect
  the code fix, not the diagnostics.
- **WP-02 release and publishing**: `.github/workflows/release.yml` (tags `v*` or a manual dry run), version
  resolution in `.github/scripts/release-version.sh`, `docs/RELEASING.md` (manual steps, versions, bad release).
  Decisions (maintainer): nuget.org organization `neolinter`, nuget.org trusted publishing (OIDC, environment `release`, no NuGet secret; policy to create), VS Code Marketplace trusted publishing too (`vsce publish --oidc`, no `VSCE_PAT`; PATs are announced as retired 2026-12-01), VS Marketplace
  publisher `neolinter`, pre-release naming `0.1.0-preview.1`. Own choices: version comes from the tag through
  `-p:Version=` (no new dependency; `Directory.Build.props` keeps `0.1.0` as the local default); `vsce package <version>
  --no-update-package-json` sets the VS Code version; a pre-release `X.Y.Z-label.N` is `X.Y.(Z*1000+N)` for the VS Code
  Marketplace (`--pre-release`) and the Visual Studio manifest, so the first stable after previews of a line must raise
  the minor; publish only
  runs for a tag reachable from `main`; the Visual Studio `.vsix` is only attached to the GitHub release; NuGet icon is the
  VS Code `icon.png`, README is the repository README. **Remaining**: the maintainer creates the publisher and its trusted publishing policy, `release` environment,
  nuget.org policy (first-push bootstrap unverified), prefix reservation and tag protection, then runs the dry run and cuts `v0.1.0-preview.1`; the workflow itself was
  never run on GitHub (steps were run locally); the Visual Studio Marketplace publication (listed in
  `docs/RELEASING.md`).

---

## WP-00 — Merge the initial skeleton

- **Goal**: PR #1 (`init/skeleton` → `main`) green and merged.
- **Done when**: CI passes on GitHub (first run on Linux), maintainer review done, merged.
- **Notes**: if CI fails, typical suspects are path separators, line endings and tool versions (TypeScript 7,
  Node 22 on CI vs Node 26 locally).

## WP-01 — Repository hygiene — done

- **Goal**: make the repository comfortable for contributors.
- **Scope**:
  - `.gitattributes` with `* text=auto eol=lf` (removes the LF/CRLF warnings on Windows), then renormalize.
  - Dependabot for NuGet, npm and GitHub Actions.
  - Review the 2 moderate `npm audit` findings in `@vscode/vsce` (dev dependency only, not shipped).
  - `SECURITY.md`, `CODE_OF_CONDUCT.md`, issue templates (bug, rule proposal), PR template that reminds the
    clean-room policy.
  - Branch protection on `main` (required CI, signed commits) — done by the maintainer in GitHub settings.
- **Done when**: files merged, `git add --renormalize .` produces no diff afterwards.

## WP-02 — Release and publishing — done

See "Done" above. Original scope:

- **Goal**: publish `Neolinter` (analyzers) and `Neolinter.Cli` (tool) on nuget.org and the VS Code extension on
  the Marketplace, from tags.
- **Scope**:
  - Release workflow triggered by tags `v*`: build, test, pack with the tag version, push to nuget.org, publish
    the vsix, create a GitHub release with the artifacts.
  - Version from the tag (replace the fixed `<Version>` in `Directory.Build.props`; avoid new dependencies if a
    `-p:Version=` from the workflow is enough).
  - Package icons and metadata.
- **Open decisions (ask)**: nuget.org account/organization and API key secret name; VS Marketplace publisher
  `neolinter` creation and PAT; pre-release naming (`0.1.0-preview.1`?).
- **Done when**: a test tag publishes all three artifacts (or a dry run proves it).

## WP-03 — CLI: dead code (NLC0002) — done

- **Goal**: implement `neolinter analyze dead-code` (rule `NLC0002`, see `rules/NLC0002.yaml`).
- **Design** (from `docs/architecture.md`): load with `SolutionLoader`; for each public/internal method,
  property, field, event and type in source, `SymbolFinder.FindReferencesAsync`; report members with no
  reference outside their own declaration.
- **Exclusions** (all from `neolinter.json` `deadCode`, already parsed by `NeolinterConfig`):
  `excludeAttributes`, `entryPointBaseTypes`, `excludePaths` (globs), `includePublicApi` (public members of
  packable libraries count as used when false), `countTestReferences` + `testProjects`.
  Built-in exclusions to define and document: `Main`/top-level entry points, overrides and interface
  implementations, members used by `nameof`, generated code, `[ModuleInitializer]`, serialization constructors.
- **Output**: SARIF via `SarifWriter` (`--output` file or stdout), exit code `1` when findings exist.
- **Tests**: in `tests/dotnet/Neolinter.Cli.Tests`, build small multi-project solutions in a temp directory
  (see `CommandTests.WriteProject`); one test per exclusion; performance smoke test on a medium solution.
- **Playground**: `samples/Playground/DeadCodeExamples.cs` already has one expected finding (`NeverCalled`).
- **Open decisions (ask)**: report private members too (the compiler already warns for some)? glob library vs
  small in-house matcher (prefer `Microsoft.Extensions.FileSystemGlobbing`, already in the .NET ecosystem).
- **Done when**: command reports the playground finding, all exclusions tested, docs updated.

## WP-04 — CLI: duplicated code (NLC0003) — done

See "Done" above. Original scope:

- **Goal**: implement `neolinter analyze duplicates` (rule `NLC0003`, see `rules/NLC0003.yaml`).
- **Design**: a `CSharpSyntaxWalker` collects statement lists of every block; each statement is normalized
  (identifiers and literals replaced by placeholders, trivia dropped) and hashed; a sliding window of
  `duplicates.minStatements` (default 8) hashes consecutive statements; collisions are grouped, then
  overlapping windows merged into maximal ranges. One SARIF result per group, other copies as
  `relatedLocations`.
- **Exclusions**: `excludePaths`, `excludeGenerated` (`*.g.cs`, `*.designer.cs`, `[GeneratedCode]`).
- **Tests**: exact clones, renamed-identifier clones, below-threshold sequences, overlapping windows, generated
  files ignored, determinism of output order.
- **Playground**: add a `DuplicateExamples.cs` with one expected group.
- **Open decisions (ask)**: compare across projects only or also inside one file? minimum token count in
  addition to statement count to skip trivial statements?
- **Done when**: command reports the playground group, tests above pass, docs updated.

## WP-05 — CLI: output and CI options — done

See "Done" above.

## WP-06 — VS Code: "Solution analysis" tab — done

See "Done" above. Original scope:

- **Goal**: enable the disabled tab in `src/ide/vscode`.
- **Scope**: detect the `neolinter` tool (global or local tool manifest), offer to install it, run
  `neolinter analyze <analysis> --output <temp>.sarif` in a task, show results grouped by rule with links to the
  locations, and optionally push them to the Problems panel (`DiagnosticCollection`, source `neolinter`).
- **Tests**: unit tests for SARIF parsing and result mapping (`node:test`).
- **Done when**: works on the playground from F5.

## WP-07 — Shared `.editorconfig` fixtures — done

See "Done" above. Original scope:

- **Goal**: one set of test cases every IDE implementation must pass.
- **Scope**: move the cases of `src/ide/vscode/src/test/editorconfig.test.ts` to
  `tests/fixtures/editorconfig/<case>/{input.editorconfig, change.json, expected.editorconfig}` (and read
  cases with expected values), make the VS Code tests load them, document the format in
  `docs/architecture.md`.
- **Done when**: VS Code tests run from the fixtures; adding a fixture needs no code change.

## WP-08 — Visual Studio 2022+ settings extension — done

See "Done" above. Original scope:

- **Goal**: native settings screen in Visual Studio (2022 17.8 and later, including newer versions).
- **Scope**: `src/ide/visualstudio`; choose between the classic VSSDK (in-process, WPF tool window) and
  VisualStudio.Extensibility (out-of-process, Remote UI) — **ask** after a short comparison; `.editorconfig`
  editing in a `netstandard2.0` library `Neolinter.EditorConfig` that passes the WP-07 fixtures; rules embedded
  from `/rules`; NuGet detection; disabled "Solution analysis" tab.
- **CI**: needs a `windows-latest` job; VSIX as artifact.
- **Note**: Visual Studio already shows analyzer severities in its own `.editorconfig` editor; the value we add is
  the rule options and a single place for all Neolinter settings.

## WP-09 — Rider settings plugin

- **Goal**: native settings page in Rider.
- **Scope**: `src/ide/rider`, Kotlin with the IntelliJ Platform Gradle plugin, settings page under
  Tools → Neolinter, `.editorconfig` editing passing the WP-07 fixtures, rules embedded from `/rules`.
- **Check first**: that Rider loads the analyzers and code fixes of the `Neolinter` package (expected, to verify
  on the playground) and document the result.
- **CI**: JDK + Gradle job; plugin zip as artifact.

## WP-10 — Zed: feasibility and integration

- **Goal**: decide what "settings in Zed" can be.
- **Scope**: check the current Zed extension API (Rust → WASM). As of 0.1 it offers no custom UI panel, so the
  likely outcome is documentation (Roslyn language server setup so NLR0001 works in Zed, `.editorconfig`
  snippets) and possibly slash commands. Write the findings in `docs/architecture.md` and **ask** before building
  anything.

## WP-11 — NLR0001 v2 — done

See "Done" above. Original scope: `else if` cascades, De Morgan option, braces option, `yield break`.

## WP-12 — New real-time rules

- **Process**: follow `docs/ADDING_A_RULE.md`; one rule per pull request; next id `NLR0004`.
- **Candidate ideas** (general practices, descriptions to be written from scratch, **maintainer picks**):
  merge nested `if` statements without `else`; remove `else` after a branch that always exits; simplify boolean
  comparisons (`x == true`); use the null-coalescing assignment `??=`; avoid empty `catch` blocks.
- **Each rule**: analyzer, code fix with fix all when safe, YAML, tests, playground examples, README table.

## WP-13 — Second language

- **Goal**: pick the next language and its real-time host.
- **Principle** (see `docs/architecture.md`): plug into the language's own analysis host when it is pluggable
  (e.g. an ESLint plugin for JavaScript/TypeScript); write a Neolinter language server only where none exists.
- **Scope of the first session**: comparison note for 2–3 candidate languages (host, packaging, configuration
  file, how settings screens would write it), then **ask** the maintainer to choose.
- **Impact to plan**: `rules/*.yaml` keep the `language` field; settings screens need one configuration writer
  per host; the CLI may need per-language analyzers.

## WP-14 — Localized messages

- **Decision needed**: keep English only, or add French (Roslyn supports localized descriptors through
  resources). Ask the maintainer before starting.

## Known pitfalls

- VS Code shows no Neolinter diagnostic when the **C#** extension (`ms-dotnettools.csharp`) is disabled; the
  Roslyn language server is what loads the analyzers.
- After changing an analyzer, rebuild `samples/Playground` and restart the C# language server.
- `Microsoft.CodeAnalysis.Testing` pulls a vulnerable `System.Security.Cryptography.Pkcs`; the test project pins a
  patched version. Keep it when updating.
- `MSBuildWorkspace` loads `.slnx` without `Microsoft.Build.Locator`; keep it that way unless a host needs it.
