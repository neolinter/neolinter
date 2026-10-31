# Contributing

Thanks for helping. Please read the [clean-room policy](CLEAN_ROOM.md) first: contributions that copy or
paraphrase another analyzer's code, rules or texts cannot be accepted.

## Prerequisites

- .NET SDK 10.0.301 or a later 10.0 feature band (see `global.json`)
- Node.js 22 or later (VS Code extension only)

## Build and test

```bash
dotnet build Neolinter.slnx
dotnet test Neolinter.slnx
dotnet pack Neolinter.slnx -c Release -o artifacts
```

`artifacts/` then contains `Neolinter.<version>.nupkg` (analyzers) and `Neolinter.Cli.<version>.nupkg` (tool).
To try the analyzers in another project, add `artifacts` as a package source in its `nuget.config`.

VS Code extension:

```bash
cd src/ide/vscode
npm ci
npm test
npm run package
```

To debug the extension, open the repository in VS Code and press F5 ("Run Neolinter extension").

## Trying the rules live

`samples/Playground` is a small project that loads the analyzers and code fixes straight from their build
output, with its own `.editorconfig`. It is not part of `Neolinter.slnx`.

- **In an IDE**: open `samples/Playground` (or `Playground.slnx`) in VS Code, Visual Studio or Rider and build
  once. Methods named `Reported*` show the diagnostic and its fix; `NotReported*` must stay silent. After
  changing an analyzer, rebuild the playground and restart the C# language server to load the new version.
- **On the command line**: `dotnet build samples/Playground` lists the diagnostics as warnings.
- **Settings screen**: F5 from the repository opens the playground in the extension host; "Neolinter: Open
  Settings" then edits `samples/Playground/.editorconfig`.
- **CLI**: `dotnet run --project src/dotnet/Neolinter.Cli -- analyze dead-code samples/Playground`.

When you add a rule, add `Reported*` and `NotReported*` examples to the playground too. CI builds it.

## Pull requests

- One topic per pull request, with tests.
- `dotnet test` and `npm test` must pass; warnings are errors.
- New rules follow [ADDING_A_RULE](ADDING_A_RULE.md).
- Rule ids, `neolinter.json` and exit codes are public contracts (see [architecture](architecture.md)): changing
  them needs a discussion first.
