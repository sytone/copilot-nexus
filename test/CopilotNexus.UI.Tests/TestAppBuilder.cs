[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(CopilotFamily.UI.Tests.TestAppBuilder))]

namespace CopilotFamily.UI.Tests;

using Avalonia;
using Avalonia.Headless;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
