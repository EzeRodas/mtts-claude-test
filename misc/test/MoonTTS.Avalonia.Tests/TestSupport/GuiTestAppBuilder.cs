using Avalonia;
using Avalonia.Headless;
using MoonTTS.Avalonia;
using MoonTTS.Avalonia.Tests.TestSupport;

[assembly: AvaloniaTestApplication(typeof(GuiTestAppBuilder))]
[assembly: AssemblyFixture(typeof(GuiIsolationFixture))]

namespace MoonTTS.Avalonia.Tests.TestSupport;

/* File Structure:
        - BuildAvaloniaApp() : AppBuilder
*/

/* Builds the real App on Avalonia's headless platform: a dummy windowing system with one 1920x1280 screen,
   no display server and no renderer, so views and window services run exactly as on a desktop */
public static class GuiTestAppBuilder
{
    /* Entry point used by [AvaloniaFact] tests (each test gets its own App instance and dispatcher) */
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
