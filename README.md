# Neolinter

A lightweight, open-source code analyzer: real-time rules with quick fixes while you type, plus on-demand
solution-wide analyses (dead code, duplicated code). It starts with C#; more languages will follow.

Neolinter does not ship its own engine or background process. Real-time rules are Roslyn analyzers loaded by
the C# tooling you already use, configuration lives in `.editorconfig`, and the expensive analyses run from a
command line only when you ask for them.

> Status: early development (0.1). One real-time rule (NLR0001); the command line analyses are not implemented yet.

## Real-time rules vs. command line

| | Real-time rules | Command line (`neolinter analyze`) |
|---|---|---|
| What | Local, per-file rules with code fixes | Solution-wide analyses: dead code, duplicated code |
| Where it runs | Inside the C# compiler and language server, as you type and on `dotnet build` | On demand, locally or in CI |
| Delivered as | NuGet package `Neolinter` | .NET tool `Neolinter.Cli` |
| Configured in | `.editorconfig` | `neolinter.json` |
| Output | Editor diagnostics, build warnings | SARIF 2.1.0 |
| Rule ids | `NLR0001`… | `NLC0002` (dead code), `NLC0003` (clones)… |

## Installation

### Real-time rules

Reference the package in each project, or once for all projects in `Directory.Build.props`:

```xml
<ItemGroup>
  <PackageReference Include="Neolinter" Version="0.1.0-preview.1" PrivateAssets="all" />
</ItemGroup>
```

Then configure the rules in `.editorconfig`:

```ini
[*.cs]
# none | silent | suggestion | warning | error
dotnet_diagnostic.NLR0001.severity = warning
nlr0001.min_statements = 3
```

The rules then show up wherever Roslyn analyzers are loaded:

| Host | Diagnostics | Code fixes |
|---|---|---|
| Visual Studio 2022 and later | yes | yes |
| VS Code (C# extension / C# Dev Kit) | yes | yes |
| JetBrains Rider | yes | yes |
| Editors using the Roslyn language server or OmniSharp (Neovim, Helix, Zed, Emacs…) | yes | yes, as LSP code actions |
| `dotnet build` / CI | yes | — |

The package, `Neolinter` on nuget.org, is published by the release workflow (see [Releasing](docs/RELEASING.md)).
Until the first `v*` tag exists, build it with `dotnet pack` (see [CONTRIBUTING](docs/CONTRIBUTING.md)) and use a
local package source. Pre-releases are versioned like `0.1.0-preview.1`: pick one explicitly.

### Settings screen

Each IDE gets a native settings screen that edits the same `.editorconfig` keys. Available today: VS Code
(`src/ide/vscode`, command **Neolinter: Open Settings**, with a "Solution analysis" tab that runs the command
line, see below), and Visual Studio 2022 17.14 / 2026 (`src/ide/visualstudio`, **Extensions > Neolinter Settings**,
"Solution analysis" tab disabled for now). Planned: Rider, then Zed.

The VS Code extension (`neolinter.neolinter` on the Marketplace) is published by the same release workflow, and every
release also attaches both `.vsix` files to its
[GitHub release](https://github.com/neolinter/neolinter/releases) (VS Code: `code --install-extension <file>`;
Visual Studio: double-click the file). The Visual Studio extension is also published on the Visual Studio Marketplace by the release workflow, from the first release that includes that step.

### Command line

```bash
dotnet tool install --global Neolinter.Cli
neolinter analyze dead-code MySolution.slnx --output dead-code.sarif
neolinter analyze duplicates MySolution.slnx
```

Exit codes: `0` nothing to report, `1` findings reported, `2` error. Restore the solution first (`dotnet restore`;
`duplicates` only reads source files, `dead-code` needs the framework references).

`duplicates` reports sequences of at least `duplicates.minStatements` statements (default 8) and
`duplicates.minTokens` tokens (default 50) that appear more than once in the solution, in one file or across
projects, whatever the identifier names and literal values. One result per group; the other copies are its
related locations. Both keys, `excludePaths` and `excludeGenerated` live in the `duplicates` section of
`neolinter.json` (see [Architecture](docs/architecture.md)).

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
| [NLR0001](rules/NLR0001.yaml) | Invert 'if' to reduce nesting | real-time, code fix | suggestion |
| [NLC0002](rules/NLC0002.yaml) | Unused member | command line | warning |
| [NLC0003](rules/NLC0003.yaml) | Duplicated code | command line | warning |

## Documentation

- [Roadmap](docs/ROADMAP.md): work packages and rules for picking them up
- [Releasing](docs/RELEASING.md): tags, versions, secrets, unlisting a bad release
- [Architecture](docs/architecture.md), including the `neolinter.json` format
- [Contributing](docs/CONTRIBUTING.md)
- [Adding a rule](docs/ADDING_A_RULE.md)
- [Clean-room policy](docs/CLEAN_ROOM.md)

## License

[MIT](LICENSE). See also [NOTICE](NOTICE).
