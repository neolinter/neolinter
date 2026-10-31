# Neolinter for VS Code

Settings screen for the [Neolinter](https://github.com/neolinter/neolinter) rules.

Run **Neolinter: Open Settings** from the command palette to:

- enable or disable each rule and choose its severity (`none`, `silent`, `suggestion`, `warning`, `error`);
- edit the rule options;
- add the `Neolinter` NuGet package to a project when it is missing.

Everything is stored in the workspace `.editorconfig` (`dotnet_diagnostic.<id>.severity` and `<id>.*` keys,
section `[*.cs]`). The rest of the file, comments included, is left untouched.

The rules themselves run inside the C# language server through the NuGet package; this extension only edits
their configuration.
