using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop;

[assembly: AvaloniaTestApplication(
    typeof(SrvSurvey.Desktop.Tests.Infrastructure.AvaloniaHeadlessTestApplication))]

namespace SrvSurvey.Desktop.Tests.Infrastructure;

public static class AvaloniaHeadlessTestApplication
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            });
    }
}
