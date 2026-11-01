# Neolinter for VS Code

Settings screen for the [Neolinter](https://github.com/neolinter/neolinter) rules.

Run **Neolinter: Open Settings** from the command palette to:

- enable or disable each rule and choose its severity (`none`, `silent`, `suggestion`, `warning`, `error`);
- edit the rule options;
- add the `Neolinter` NuGet package to a project when it is missing.

The **Solution analysis** tab runs the `neolinter` command line (`dead-code` for now) on the workspace folder,
lists the findings grouped by rule with links to the code, and can copy them to the Problems panel (source
`neolinter`). The command line is searched in this order: setting `neolinter.cli.path`, the local tool manifest,
the global tool, then setting `neolinter.cli.projectPath` (a `Neolinter.Cli.csproj` run with `dotnet run --project`).
If none is found, **Install…** offers a global or local `dotnet tool install Neolinter.Cli`; the package is not on
nuget.org until the first release, so use one of the two settings until then. Restore the solution first
(`dotnet restore`).

Everything is stored in the workspace `.editorconfig` (`dotnet_diagnostic.<id>.severity` and `<id>.*` keys,
section `[*.cs]`). The rest of the file, comments included, is left untouched.

The rules themselves run inside the C# language server through the NuGet package; this extension only edits
their configuration.
