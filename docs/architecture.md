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
tests/dotnet/
  Neolinter.Tests            analyzer and code fix tests (Microsoft.CodeAnalysis.Testing), rule file checks
  Neolinter.Cli.Tests        SARIF, configuration, analysis and command line tests
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

One native screen per IDE, in this order: VS Code (done), Visual Studio 2022+, Rider, Zed. Each one:

- lists the rules embedded from `/rules` at build time;
- reads and writes `dotnet_diagnostic.<id>.severity` and `<id>.*` in the workspace `.editorconfig`, changing only
  the affected line (it updates the effective line, else adds to the last `[*.cs]` section, else appends one);
- offers to add the `Neolinter` package when no project references it;
- has a "Solution analysis" tab, disabled until the command line analyses exist.

Each IDE re-implements the small `.editorconfig` editor in its own language. To keep them consistent, new
implementations must pass the same cases as `src/ide/vscode/src/test/editorconfig.test.ts`; moving these
cases to shared fixture files is planned with the second IDE.

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

#### duplicates (planned)

A `CSharpSyntaxWalker` normalizes statement sequences (identifiers and literals abstracted),
hashes them with a sliding window of `minStatements`, and groups collisions. Each group becomes one result
whose `relatedLocations` are the other copies.

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
