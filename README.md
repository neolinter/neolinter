# Neolinter

A lightweight, open-source code analyzer: real-time rules with quick fixes while you type, plus on-demand
solution-wide analyses (dead code, duplicated code). It starts with C#; more languages will follow.

Neolinter does not ship its own engine or background process. Real-time rules are Roslyn analyzers loaded by
the C# tooling you already use, configuration lives in `.editorconfig`, and the expensive analyses run from a
command line only when you ask for them.

> Status: early development (0.1). One real-time rule (NL0001); the command line analyses are not implemented yet.

## Real-time rules vs. command line

| | Real-time rules | Command line (`neolinter analyze`) |
|---|---|---|
| What | Local, per-file rules with code fixes | Solution-wide analyses: dead code, duplicated code |
| Where it runs | Inside the C# compiler and language server, as you type and on `dotnet build` | On demand, locally or in CI |
| Delivered as | NuGet package `Neolinter` | .NET tool `Neolinter.Cli` |
| Configured in | `.editorconfig` | `neolinter.json` |
| Output | Editor diagnostics, build warnings | SARIF 2.1.0 |
| Rule ids | `NL0001`, `NL0002`… | `NLD0001`… (dead code), `NLC0001`… (clones) |

## Installation

### Real-time rules

Reference the package in each project, or once for all projects in `Directory.Build.props`:

```xml
<ItemGroup>
  <PackageReference Include="Neolinter" Version="0.1.0" PrivateAssets="all" />
</ItemGroup>
```

Then configure the rules in `.editorconfig`:

```ini
[*.cs]
# none | silent | suggestion | warning | error
dotnet_diagnostic.NL0001.severity = warning
nl0001.min_statements = 3
```

The rules then show up wherever Roslyn analyzers are loaded:

| Host | Diagnostics | Code fixes |
|---|---|---|
| Visual Studio 2022 and later | yes | yes |
| VS Code (C# extension / C# Dev Kit) | yes | yes |
| JetBrains Rider | yes | yes |
| Editors using the Roslyn language server or OmniSharp (Neovim, Helix, Zed, Emacs…) | yes | yes, as LSP code actions |
| `dotnet build` / CI | yes | — |

The package is not published on nuget.org yet: build it with `dotnet pack` (see [CONTRIBUTING](docs/CONTRIBUTING.md))
and use a local package source.

### Settings screen

Each IDE gets a native settings screen that edits the same `.editorconfig` keys. Available today: VS Code
(`src/ide/vscode`, command **Neolinter: Open Settings**). Planned: Visual Studio, then Rider, then Zed.

### Command line

```bash
dotnet tool install --global Neolinter.Cli
neolinter analyze dead-code MySolution.slnx --output dead-code.sarif
neolinter analyze duplicates MySolution.slnx
```

Exit codes: `0` nothing to report, `1` findings reported, `2` error. Restore the solution first (`dotnet restore`).
`dead-code` is available; `duplicates` still stops with "not implemented" (exit code 2) after loading the
solution.

Options of every analysis:

| Option | Effect |
|---|---|
| `--output <file>` | Write the report to a file instead of the standard output. |
| `--format sarif\|text` | `sarif` (default) or one line per finding: `path(line,column): level RULEID: message`. |
| `--fail-on note\|warning\|error` | Exit code `1` only for findings at or above this level (default `warning`). Lower findings are still reported. |
| `--baseline <file.sarif>` | Skip findings already present in a previous SARIF log (matched by `partialFingerprints`), in the report and in the exit code. |

Before `--fail-on` existed, any finding gave exit code `1`; the default `warning` keeps that behaviour for every
current rule, which all report at `warning`.

### Continuous integration (GitHub code scanning)

```yaml
- run: dotnet restore
- run: dotnet tool install --global Neolinter.Cli
- run: neolinter analyze dead-code MySolution.slnx --output dead-code.sarif
  continue-on-error: true   # exit code 1 = findings, still upload them
- uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: dead-code.sarif
    category: neolinter-dead-code
```

To fail a pull request only on new findings, keep the SARIF of the main branch (artifact or cache) and pass it with
`--baseline main.sarif`.

## Rules

| Id | Title | Kind | Default |
|---|---|---|---|
| [NL0001](rules/NL0001.yaml) | Invert 'if' to reduce nesting | real-time, code fix | suggestion |
| [NLD0001](rules/NLD0001.yaml) | Unused member | command line | warning |
| [NLC0001](rules/NLC0001.yaml) | Duplicated code | command line (planned) | warning |

## Documentation

- [Roadmap](docs/ROADMAP.md): work packages and rules for picking them up
- [Architecture](docs/architecture.md), including the `neolinter.json` format
- [Contributing](docs/CONTRIBUTING.md)
- [Adding a rule](docs/ADDING_A_RULE.md)
- [Clean-room policy](docs/CLEAN_ROOM.md)

## License

[MIT](LICENSE). See also [NOTICE](NOTICE).
