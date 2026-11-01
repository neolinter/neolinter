using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace Neolinter.VisualStudio;

[VisualStudioContribution]
internal sealed class OpenSettingsCommand : Command
{
    public override CommandConfiguration CommandConfiguration => new("%Neolinter.OpenSettings.DisplayName%")
    {
        Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu],
        Icon = new(ImageMoniker.KnownValues.Settings, IconSettings.IconAndText),
    };

    public override Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken) =>
        Extensibility.Shell().ShowToolWindowAsync<SettingsToolWindow>(activate: true, cancellationToken);
}
