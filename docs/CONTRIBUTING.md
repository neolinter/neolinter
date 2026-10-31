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

## Pull requests

- One topic per pull request, with tests.
- `dotnet test` and `npm test` must pass; warnings are errors.
- New rules follow [ADDING_A_RULE](ADDING_A_RULE.md).
- Rule ids, `neolinter.json` and exit codes are public contracts (see [architecture](architecture.md)): changing
  them needs a discussion first.
