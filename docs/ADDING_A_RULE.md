# Adding a rule

## 1. Pick an id and describe the rule

Take the next free id: `NL` for real-time rules, `NLD` for dead code, `NLC` for clones. Ids are never reused.

Create `rules/<ID>.yaml`:

```yaml
id: NL0002
title: Short imperative title          # must match the DiagnosticDescriptor title
language: csharp
type: realtime                         # realtime | cli
category: Style
defaultSeverity: suggestion            # none | silent | suggestion | warning | error
description: >-
  What the rule reports and why, written by you (see CLEAN_ROOM.md).
options:
  - key: nl0002.some_option            # <id lowercase>.<name>
    type: integer                      # integer | boolean | string | enum (with values: [...])
    default: 3
    minimum: 1
    title: Label shown in settings screens
    description: One sentence.
```

`RuleFilesTests` fails if the file and the analyzer disagree.

## 2. Write the analyzer

In `src/dotnet/Neolinter.Analyzers/<RuleName>/`:

- one `DiagnosticAnalyzer` with a public `DiagnosticId` constant and a `DiagnosticDescriptor` whose help link
  points to the YAML file;
- register it in the `Analyzers` array of `tests/dotnet/Neolinter.Tests/RuleFilesTests.cs`;
- add a line to `AnalyzerReleases.Unshipped.md`;
- call `ConfigureGeneratedCodeAnalysis(None)` and `EnableConcurrentExecution()`;
- read options with `AnalyzerConfigOptionsProvider`, falling back to the default on invalid values;
- only use Roslyn 4.8 APIs.

## 3. Write the code fix (when there is a safe one)

In `src/dotnet/Neolinter.CodeFixes/<RuleName>/`. Share detection logic with the analyzer through an internal
class instead of duplicating it. Provide a `FixAllProvider`. Keep the user's trivia and avoid elastic trivia.
If a case cannot be fixed without changing behaviour, do not report it.

## 4. Test

In `tests/dotnet/Neolinter.Tests/<RuleName>/`, with `Microsoft.CodeAnalysis.Testing`:

- positive cases with the expected fixed code;
- negative cases (no diagnostic);
- comments and indentation preserved;
- each option, read from an `.editorconfig`;
- older language versions when the fix uses recent syntax;
- fix all.

## 5. Document

Add the rule to the table in `README.md` and examples to `samples/Playground` (`Reported*` / `NotReported*`).
Settings screens pick it up from `/rules` automatically.
