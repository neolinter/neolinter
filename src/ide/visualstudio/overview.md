# Neolinter for Visual Studio

Settings screen for the [Neolinter](https://github.com/neolinter/neolinter) rules.

Open **Extensions > Neolinter Settings** to:

- enable or disable each rule and choose its severity (`none`, `silent`, `suggestion`, `warning`, `error`);
- edit the rule options;
- get a banner with the install command when the `Neolinter` NuGet package is missing.

Everything is stored in the solution `.editorconfig` (`dotnet_diagnostic.<id>.severity` and `<id>.*` keys, section
`[*.cs]`). The rest of the file, comments included, is left untouched.

The rules themselves run inside Visual Studio through the `Neolinter` NuGet package; this extension only edits their
configuration. Add the package to your projects first:

```xml
<PackageReference Include="Neolinter" Version="0.1.0-preview.1" PrivateAssets="all" />
```

Requires Visual Studio 2022 17.14 or Visual Studio 2026. This is an early preview.

Source code, documentation and issues: <https://github.com/neolinter/neolinter>.
