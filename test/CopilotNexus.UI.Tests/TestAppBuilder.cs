[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(CopilotNexus.UI.Tests.TestAppBuilder))]

namespace CopilotNexus.UI.Tests;

using Avalonia;
using Avalonia.Headless;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
