using Microsoft.VisualStudio.Extensibility;

namespace Neolinter.VisualStudio;

[VisualStudioContribution]
internal sealed class ExtensionEntrypoint : Extension
{
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "Neolinter.VisualStudio.7d4e6f0a-3c52-4b1e-9a0d-5f6b8c2e1a47",
            version: ExtensionAssemblyVersion,
            publisherName: "Neolinter",
            displayName: "Neolinter",
            description: "Settings screen for Neolinter rules, stored in the solution .editorconfig.")
        {
            Icon = "icon.png",
            License = "LICENSE",
            MoreInfo = "https://github.com/neolinter/neolinter",
            Tags = ["analyzer", "linter", "csharp", "editorconfig"],
        },
    };
}
