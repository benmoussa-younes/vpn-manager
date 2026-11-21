using Avalonia;
using Avalonia.Styling;
using Core.Mvp.Services;
using Core.Platform.Linux;
using Core.UI.Preferences;

namespace VpnManager.App.Tests;

public sealed class AppearanceTests
{
    [Fact]
    public async Task AThemeChoiceAppliesWithoutRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "vpn-appearance-" + Guid.NewGuid().ToString("N"));
        try
        {
            await HeadlessTestSession.RunAsync(() =>
            {
                var service = new AppearanceService(new JsonAppearanceStore(
                    Path.Combine(root, "preferences.json"),
                    new LinuxAtomicFileWriterFactory(),
                    new LinuxFileSystemAdapter()));

                service.ChooseTheme(ThemePreference.Dark);
                Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);

                service.ChooseTheme(ThemePreference.Light);
                Assert.Equal(ThemeVariant.Light, Application.Current.RequestedThemeVariant);

                service.ChooseTheme(ThemePreference.System);
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
