# Roadmap

This file is the entry point for picking up work. Each work package (WP) below is self-contained: a new
session (human or AI) can start one from this file alone. Start a session with, for example:

> Read docs/ROADMAP.md and the "Rules for every session" section, then do WP-03.

Update this file in the same pull request when a WP is finished (move it to "Done", record decisions).

## Rules for every session

- **Read first**: `README.md`, `docs/architecture.md`, `docs/CLEAN_ROOM.md`, `docs/ADDING_A_RULE.md`.
- **Clean room**: no reverse engineering, no code, rule ids, messages or descriptions copied or paraphrased from
  other analyzers (ReSharper/JetBrains, Sonar, etc.). No third-party brand in names. Write texts yourself.
- **Frozen contracts** (changing them is a breaking change, ask first): rule ids (`NL*`, `NLD*`, `NLC*`),
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
| WP-02 | Release and publishing | WP-00 | todo |
| WP-03 | CLI: dead code (NLD0001) | WP-00 | done |
| WP-04 | CLI: duplicated code (NLC0001) | WP-00 | todo |
| WP-05 | CLI: output and CI options | WP-03 or WP-04 | done |
| WP-06 | VS Code: "Solution analysis" tab | WP-03 or WP-04 | todo |
| WP-07 | Shared `.editorconfig` fixtures | WP-00 | todo |
| WP-08 | Visual Studio 2022+ settings extension | WP-07 | todo |
| WP-09 | Rider settings plugin | WP-07 | todo |
| WP-10 | Zed: feasibility and integration | WP-00 | todo |
| WP-11 | NL0001 v2 | WP-00 | todo |
| WP-12 | New real-time rules | WP-00 | todo |
| WP-13 | Second language | WP-02 | todo |
| WP-14 | Localized messages | WP-00 | decision needed |

Packages that only depend on WP-00 can run in parallel in separate sessions, except that WP-03 and WP-04 both
touch `Neolinter.Cli` and `Neolinter.Cli.Core`: run them one after the other or coordinate the shared files
(`NeolinterCommand.cs`).

## Done

- **0.1 skeleton** (PR #1): monorepo, NL0001 analyzer + code fix + fix all, VS Code settings extension,
  CLI skeleton (solution loading, `neolinter.json`, SARIF export), CI, playground, docs.
- **WP-03 dead code**: `neolinter analyze dead-code` (NLD0001), exclusions documented in
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

## WP-02 — Release and publishing

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

## WP-03 — CLI: dead code (NLD0001) — done

- **Goal**: implement `neolinter analyze dead-code` (rule `NLD0001`, see `rules/NLD0001.yaml`).
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

## WP-04 — CLI: duplicated code (NLC0001)

- **Goal**: implement `neolinter analyze duplicates` (rule `NLC0001`, see `rules/NLC0001.yaml`).
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

## WP-06 — VS Code: "Solution analysis" tab

- **Goal**: enable the disabled tab in `src/ide/vscode`.
- **Scope**: detect the `neolinter` tool (global or local tool manifest), offer to install it, run
  `neolinter analyze <analysis> --output <temp>.sarif` in a task, show results grouped by rule with links to the
  locations, and optionally push them to the Problems panel (`DiagnosticCollection`, source `neolinter`).
- **Tests**: unit tests for SARIF parsing and result mapping (`node:test`).
- **Done when**: works on the playground from F5.

## WP-07 — Shared `.editorconfig` fixtures

- **Goal**: one set of test cases every IDE implementation must pass.
- **Scope**: move the cases of `src/ide/vscode/src/test/editorconfig.test.ts` to
  `tests/fixtures/editorconfig/<case>/{input.editorconfig, change.json, expected.editorconfig}` (and read
  cases with expected values), make the VS Code tests load them, document the format in
  `docs/architecture.md`.
- **Done when**: VS Code tests run from the fixtures; adding a fixture needs no code change.

## WP-08 — Visual Studio 2022+ settings extension

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
  likely outcome is documentation (Roslyn language server setup so NL0001 works in Zed, `.editorconfig`
  snippets) and possibly slash commands. Write the findings in `docs/architecture.md` and **ask** before building
  anything.

## WP-11 — NL0001 v2

- **Scope**, each item to confirm with the maintainer first:
  - `else if` cascades whose final `else` only exits;
  - optional De Morgan simplification (`!(a && b)` → `!a || !b`) behind an option such as
    `nl0001.simplify_logical_operators`;
  - optional braces style for the inserted exit (`nl0001.guard_braces = always|never`);
  - `yield break` in iterators.
- **Constraints**: new options go to `rules/NL0001.yaml`; every change keeps behaviour (add negative tests).

## WP-12 — New real-time rules

- **Process**: follow `docs/ADDING_A_RULE.md`; one rule per pull request; next id `NL0002`.
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
