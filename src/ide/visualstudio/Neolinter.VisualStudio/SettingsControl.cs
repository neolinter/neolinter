using Microsoft.VisualStudio.Extensibility.UI;

namespace Neolinter.VisualStudio;

internal sealed class SettingsControl(SettingsViewModel viewModel) : RemoteUserControl(viewModel)
{
}
