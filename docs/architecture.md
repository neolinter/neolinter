# Architecture

## Principles

1. **No engine of our own.** Real-time rules plug into the analysis host each language already has. For C#
   that is Roslyn: analyzers and code fixes in a NuGet package, loaded by the compiler, Visual Studio, Rider
   and the Roslyn language server used by VS Code and other LSP editors.
2. **No permanent process.** Anything too expensive to run while typing (whole-solution analyses) is a
   command line run on demand, locally or in CI.
3. **Standard configuration.** Real-time rules are configured in `.editorconfig`; the command line in
   `neolinter.json`. Settings screens only edit these files.
4. **Standard output.** The command line writes SARIF 2.1.0, readable by GitHub code scanning, Visual Studio
   and the VS Code SARIF viewer.
5. **One description per rule.** `/rules/<id>.yaml` is the single, language-neutral description of a rule
   (title, description, default severity, options). Settings screens embed these files at build time; tests
   check them against the analyzers.

## Repository layout

```
rules/                       one YAML file per rule (id, title, description, severity, options, language, type)
src/dotnet/
  Neolinter.Analyzers        DiagnosticAnalyzer classes (netstandard2.0, Roslyn 4.8)
  Neolinter.CodeFixes        CodeFixProvider classes (separate assembly: they need Roslyn Workspaces)
  Neolinter.Package          packs both into analyzers/dotnet/cs of the "Neolinter" NuGet package
  Neolinter.Cli.Core         solution loading (MSBuildWorkspace), analyses, result model, SARIF export, neolinter.json
  Neolinter.Cli              "neolinter" .NET tool (System.CommandLine)
src/ide/
  vscode                     VS Code settings extension (TypeScript, webview)
  visualstudio/
    Neolinter.EditorConfig   .editorconfig editing, embedded rules, NuGet detection (netstandard2.0)
    Neolinter.VisualStudio   Visual Studio extension (VisualStudio.Extensibility, Windows only, builds the .vsix)
tests/dotnet/
  Neolinter.Tests            analyzer and code fix tests (Microsoft.CodeAnalysis.Testing), rule file checks
  Neolinter.Cli.Tests        SARIF, configuration, analysis and command line tests
  Neolinter.EditorConfig.Tests  the shared .editorconfig fixtures, rule embedding and NuGet detection
```

The analyzers target Roslyn 4.8 so they load in Visual Studio 2022 17.8+ and the .NET 8 SDK and later.
Raising that version drops older hosts.

## Real-time rules

A rule is an analyzer plus, when possible, a code fix with a `FixAllProvider` (document, project, solution).
Detection logic that the code fix also needs lives in the analyzer assembly as an internal class (see
`InvertIfAnalysis`). Code fixes keep the user's formatting: they reuse existing trivia and never create
elastic trivia, which would let the host reformat surrounding code.

Rule options are `.editorconfig` keys named `<id lowercase>.<option>`, e.g. `nl0001.min_statements`, read through
`AnalyzerConfigOptionsProvider`. Invalid values fall back to the default.

## Settings screens

One native screen per IDE, in this order: VS Code (done), Visual Studio (done, "Solution analysis" disabled), Rider,
Zed. Each one:

- lists the rules embedded from `/rules` at build time;
- reads and writes `dotnet_diagnostic.<id>.severity` and `<id>.*` in the workspace `.editorconfig`, changing only
  the affected line (it updates the effective line, else adds to the last `[*.cs]` section, else appends one);
- offers to add the `Neolinter` package when no project references it;
- has a "Solution analysis" tab that runs the command line (VS Code: enabled, see below; other IDEs: disabled
  until implemented).

VS Code "Solution analysis" tab: finds the CLI in this order: setting `neolinter.cli.path`, local tool manifest
(`dotnet tool run neolinter`), global tool, setting `neolinter.cli.projectPath` (`dotnet run --project <csproj> --`,
for use before the package is published). It runs `neolinter analyze <analysis> --output <temp>.sarif` as a VS Code
task in the first workspace folder, treats exit codes `0` and `1` as success and anything else as a failure, parses
the SARIF (`src/sarif.ts`) and lists findings grouped by rule with links to the locations. Only `dead-code` is
offered. "Show in Problems panel" (off by default, remembered per workspace) copies the findings to a
`DiagnosticCollection` with source `neolinter`. The webview sends only an index into the last results, never a path.

Visual Studio (`src/ide/visualstudio`): built with VisualStudio.Extensibility (out of process, Remote UI), chosen
over the classic VSSDK by the maintainer. Trade-offs that drove it:

| | VisualStudio.Extensibility | Classic VSSDK |
|---|---|---|
| Supported VS | the SDK version sets the floor: SDK 17.14 gives 17.14 and later (2022 and 2026). Reaching 17.8 to 17.13 would need an older SDK (not tried) | 2022 17.8 and later, 2026 |
| Maturity | newer API, still growing; no free-form WPF, only Remote UI controls | very mature, large documentation, full WPF |
| Effort | small: attributes, no `.vsct` or pkgdef, view models are plain data contracts | more boilerplate (package, `.vsct`, `.pkgdef`, WPF control) |
| Isolation | out of process: a bug cannot freeze or crash Visual Studio | in process |
| Packaging | the build package writes `extension.vsixmanifest` and the `.vsix` | `.vsixmanifest` and package registration to maintain |
| Testability | logic lives in `Neolinter.EditorConfig` (plain xUnit, runs on Linux too); the UI itself needs a Visual Studio instance | same split, UI also needs an instance |
| CI | `windows-latest`, `dotnet build` produces the `.vsix` | `windows-latest` with MSBuild and the Visual Studio workload |

The project is Windows only and is not part of `Neolinter.slnx`; the `visualstudio` CI job builds it. The
`Neolinter.EditorConfig` library (netstandard2.0) holds the `.editorconfig` editor (a port of the VS Code one,
passing the same fixtures), the rule catalog (`/rules/*.yaml` embedded at build time and read with YamlDotNet) and
the NuGet detection (`PackageReference Include="Neolinter"` in project files and `Directory.Build.props` next to the
solution). The window is opened from **Extensions > Neolinter Settings**, edits the `.editorconfig` next to the
solution file, and shows the "Solution analysis" tab disabled. The tab strip is two buttons, the second
disabled, because a Remote UI tab control could not be checked without running the window. Booleans and enums are drop-downs; integers are validated against
`minimum` before anything is written. Visual Studio's own `.editorconfig` editor already shows analyzer severities;
the value here is the rule options and one place for every Neolinter setting. The window follows the open solution (checked every 3 seconds, there is no solution event we could rely on) and
reloads when the `.editorconfig` is edited elsewhere (file watcher; its own saves are ignored); a Reload button
forces it. Not offered yet: adding the package
from the window (the banner shows the `dotnet add package` command; the package is not on nuget.org yet).

### Shared `.editorconfig` fixtures

Each IDE re-implements the small `.editorconfig` editor in its own language. To keep them consistent, every
implementation must pass the cases in `tests/fixtures/editorconfig/<case>/`, one directory per case, plain files
only (UTF-8, no TypeScript-specific content). A test runner lists the directories, so adding a case needs no code
change. Files in a case:

- `input.editorconfig` (required): the file before the operation, byte for byte.
- `read.json` (optional): a read case, `{"keys": ["..."], "expected": {"key": "value"}}`. Reading `keys` from the input
  must give exactly `expected`; a key that is not found is absent from `expected`. Key matching is case-insensitive, and
  values come back without inline comments.
- `change.json` (optional): a write case, `{"key": "...", "value": "..."}`. Setting `key` to `value` in the input must
  give exactly the bytes of `expected.editorconfig` (required with `change.json`).

A directory may hold both a read and a write case. Files are compared byte for byte, so line endings matter
(`tests/fixtures/** -text` in `.gitattributes`). Glob recognition (`appliesToCSharp`) and anything that cannot be
written as a file stay in each implementation's own tests. Removing a key is not supported by the editor yet.

## Command line contracts

These are frozen from 0.1 on; changing them is a breaking change.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Analysis ran, nothing reported |
| 1 | Analysis ran, at least one finding at or above `--fail-on` (default `warning`) that is not in the `--baseline` |
| 2 | Error: bad arguments, invalid configuration, load failure (including an unrestored project), analysis not implemented |

### Output options

- `--format sarif` (default) or `text`; `--output` writes to a file; progress and the summary go to stderr.
- `--fail-on <note|warning|error>` only moves the exit code threshold; every non-baselined finding is still
  reported.
- Every SARIF result carries `partialFingerprints["neolinter/v1"]`: SHA-256 of rule id, path relative to the
  solution directory (`/` separators), whitespace-normalized text of the start line and the occurrence index of
  identical findings in that file. Line numbers and messages are not part of it, so edits above a finding do not
  make it new. `--baseline` ignores any finding whose fingerprint is in the given SARIF log.

### Rule ids

- `NL0001`…: real-time rules.
- `NLD0001`…: dead code (`neolinter analyze dead-code`). `NLD0001` = unused member.
- `NLC0001`…: duplicated code (`neolinter analyze duplicates`). `NLC0001` = duplicated statements.

### neolinter.json

Looked up next to the solution, or passed with `--config`. Every property is optional; unknown properties
are rejected so typos do not go unnoticed. Comments and trailing commas are allowed.

```jsonc
{
  "version": 1,
  "deadCode": {
    // Members or types with one of these attributes (full names) are never reported:
    // serialization, dependency injection, reflection markers...
    "excludeAttributes": [],
    // Types deriving from or implementing these (full names) are used by convention: controllers, handlers...
    "entryPointBaseTypes": [],
    // Globs relative to the solution directory.
    "excludePaths": [],
    // false: public members of packable libraries count as used by external callers.
    "includePublicApi": false,
    // false: a member referenced only from test projects is reported.
    "countTestReferences": true,
    "testProjects": ["**/*.Tests.csproj", "**/*.Test.csproj"]
  },
  "duplicates": {
    // Smallest number of consecutive statements reported (at least 2).
    "minStatements": 8,
    // Smallest number of tokens of a reported sequence (0 turns the check off). Skips runs of trivial
    // statements such as eight one-word calls.
    "minTokens": 50,
    // Globs relative to the solution directory.
    "excludePaths": [],
    // Ignore *.g.cs, *.designer.cs and [GeneratedCode] members.
    "excludeGenerated": true
  }
}
```

### Analyses

The solution must be restored: a project without framework references stops the analysis with exit code 2.

#### dead-code (NLD0001)

`DeadCodeAnalyzer` looks at every type, method, constructor, operator, property, field and event declared in
source, whatever its accessibility (the compiler only warns about some private members). Each one goes through
`SymbolFinder.FindReferencesAsync` over the solution loaded with `MSBuildWorkspace`; it is reported when no
reference remains outside its own declaration (a recursive call does not count). References inside XML
documentation (`cref`) do not count; `nameof` does.

A type is in use when it is referenced or when one of its members is used. An unused type gets one result;
its members are not reported one by one.

Never reported, and keeping their containing types in use (the runtime, a framework or external code uses them):

- the entry point (`Main` or top-level statements);
- `[ModuleInitializer]` methods;
- serialization constructors `(SerializationInfo, StreamingContext)`;
- code in generated files (`*.g.cs`, `*.g.i.cs`, `*.designer.cs`, `*.generated.cs`, files starting with an
  `<auto-generated>` comment) or marked `[GeneratedCode]`;
- members with an attribute of `excludeAttributes` (full name, with or without the `Attribute` suffix);
- files matching `excludePaths` (globs relative to the solution directory, `Microsoft.Extensions.FileSystemGlobbing`);
- files outside the solution directory (projects only loaded through project references);
- when `includePublicApi` is false, members visible outside their assembly (`public`, `protected`) in packable
  libraries: library projects whose MSBuild property `IsPackable` is `true` (read with `dotnet msbuild -getProperty`).

Never reported, without keeping their types in use (they are only called through another member):

- overrides and interface implementations (implicit or explicit);
- parameterless constructors (serializers, `new()` constraints, or a private one that blocks instantiation);
- accessors, finalizers and static constructors.

Types deriving from or implementing one of `entryPointBaseTypes` (full name without type arguments, e.g.
`MyApp.IHandler` for `IHandler<T>`) are skipped with all their members. Projects matching `testProjects` are not
reported; references from them count unless `countTestReferences` is false.

#### duplicates (NLC0001)

`DuplicatesAnalyzer` works on syntax trees only (no binding, so it does not need a restored solution):

1. A `CSharpSyntaxWalker` collects the statement list of every block and switch section. Files outside the
   solution directory, files matching `excludePaths` and, when `excludeGenerated` is true, generated files
   (same rules as dead-code: `*.g.cs`, `*.g.i.cs`, `*.designer.cs`, `*.generated.cs`, `<auto-generated>` header)
   and members marked `[GeneratedCode]` (matched by attribute name) are skipped. A file linked into several
   projects counts once.
2. Each statement is reduced to the sequence of its token kinds: trivia is dropped, every identifier looks the same
   and every literal looks the same (keywords, operators and punctuation are kept), then hashed. Renamed variables
   and changed constants therefore still match; a changed operator, type keyword or statement order does not.
3. A sliding window of `minStatements` consecutive statements is hashed per list. Windows with fewer than
   `minTokens` tokens are dropped (the second criterion, to skip trivial sequences). Copies are compared across
   projects and inside a single file.
4. Windows with the same hash form a group. Windows that shift together by one statement are chained into one
   maximal range, so a 12-statement clone gives one result, not five. Copies of a group overlapping in the same
   block (very repetitive code) keep only the first of each overlap, and a group whose copies all lie inside the
   copies of a larger group is dropped.

One result per group at `warning`: the location is the first copy (path, then line), the other copies are
`relatedLocations`. Results are ordered by location, so the output does not depend on project or file order.
Limits: no detection of reordered or partly edited statements (type-3 clones), and top-level statements are not
looked at.

## Other languages

C# is the first language, not the only one. What is already language-neutral:

- rule files (`language` field), rule ids (one global numbering for `NL`, `NLD`, `NLC`), `neolinter.json`
  sections per analysis, SARIF output and exit codes;
- settings screens, which are organized per IDE, not per language.

What is C#-specific and will need a per-language counterpart:

- the real-time host. Each new language should first plug into the analysis host of its ecosystem (for
  example an ESLint plugin for JavaScript/TypeScript) and fall back to a Neolinter language server only where
  no pluggable host exists. This decision is made per language.
- the configuration keys. `.editorconfig` with `dotnet_diagnostic.*` is how Roslyn reads severities; other
  hosts have their own configuration files, so the settings screens will need one configuration writer per host.
- the command line analyses, which use Roslyn today. Duplicate detection could become language-neutral with a
  generic parser; dead-code detection stays per language because it needs symbol resolution.
