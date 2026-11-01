using Microsoft.Playwright;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Desktop.Platform;
using static Microsoft.Playwright.Assertions;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed partial class JourneyReportBundleExporterTests
{
    /// <summary>Exercises both return visits and original-image navigation from a relocated report with networking disabled.</summary>
    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    [Trait("Category", "Browser")]
    public async Task RelocatedJourneyWorksOfflineWithReturnVisitsAndScreenshotNavigation(int viewportWidth)
    {
        if (Environment.GetEnvironmentVariable("SRVSURVEY_RUN_BROWSER_TESTS") != "1")
        {
            Assert.Skip("Set SRVSURVEY_RUN_BROWSER_TESTS=1 after installing the pinned Playwright Chromium runtime.");
        }
        JourneyHtmlExport report = CreateReport();
        string exported = await JourneyReportBundleExporter.SaveAsync(report, Path.Combine(directory, "exported"));
        string relocated = Path.Combine(directory, "relocated");
        Directory.Move(exported, relocated);
        File.Delete(report.Images[0].SourcePath);
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using IBrowserContext context = await browser.NewContextAsync(
            new()
            {
                Offline = true,
                ViewportSize = new() { Width = viewportWidth, Height = 900 },
            }
        );
        var externalRequests = new List<string>();
        await context.RouteAsync(
            url =>
                url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
            route =>
            {
                externalRequests.Add(route.Request.Url);
                return route.AbortAsync();
            }
        );
        IPage page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(new Uri(Path.Combine(relocated, "index.html")).AbsoluteUri);
        await Expect(page.Locator("#map-galactic")).ToBeVisibleAsync();
        await Expect(page.Locator("#map-detail")).ToBeHiddenAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Route detail", Exact = true }).ClickAsync();
        await Expect(page.Locator("#map-detail")).ToBeVisibleAsync();
        await Expect(page.Locator("#map-galactic")).ToBeHiddenAsync();

        await page.Locator("#system-search").FillAsync("No matching system");
        await Expect(page.Locator("#visit-1")).ToBeHiddenAsync();
        await page.Locator("#map-detail .route-stop-link").Last.ClickAsync();
        await Expect(page.Locator("#map-visit-picker")).ToBeVisibleAsync();
        await Expect(page.Locator(".map-visit-option")).ToHaveCountAsync(2);
        await Expect(page.Locator(".map-visit-date"))
            .ToHaveTextAsync(["29 Oct 2026 12:00 UTC", "29 Oct 2026 14:00 UTC"]);
        await page.Locator(".map-visit-option").First.ClickAsync();
        await Expect(page.Locator("#map-visit-picker")).ToBeHiddenAsync();
        await Expect(page.Locator("#system-search")).ToHaveValueAsync("");
        await Expect(page.Locator("#visit-1")).ToBeVisibleAsync();
        await Expect(page.Locator("#visit-1 details")).ToHaveAttributeAsync("open", "");
        await Expect(page.Locator("#visit-1")).ToBeFocusedAsync();

        await page.Locator("#map-detail .route-stop-link").Last.ClickAsync();
        await Expect(page.Locator("#map-visit-picker")).ToBeVisibleAsync();
        await page.Locator(".map-visit-option").Nth(1).ClickAsync();
        await Expect(page.Locator("#map-visit-picker")).ToBeHiddenAsync();
        await Expect(page.Locator("#visit-3")).ToBeVisibleAsync();
        await Expect(page.Locator("#visit-3 details")).ToHaveAttributeAsync("open", "");
        await Expect(page.Locator("#visit-3")).ToBeFocusedAsync();

        ILocator tile = page.Locator("#visit-3 .screenshot-tile").First;
        ILocator preview = tile.Locator("img");
        await preview.ScrollIntoViewIfNeededAsync();
        await Expect(preview).ToHaveJSPropertyAsync("naturalWidth", 640);
        await tile.ClickAsync();
        await Expect(page.Locator("#image-viewer")).ToBeVisibleAsync();
        await Expect(page.Locator("#image-full")).ToHaveJSPropertyAsync("naturalWidth", 1600);
        await Expect(page.Locator("#image-position")).ToHaveTextAsync("1 of 2");
        await page.Keyboard.PressAsync("ArrowRight");
        await Expect(page.Locator("#image-position")).ToHaveTextAsync("2 of 2");
        await Expect(page.Locator("#image-full")).ToHaveAttributeAsync("src", "images/42/0002.png");
        await page.WaitForFunctionAsync(
            """
            () => {
                const image = document.querySelector('#image-full');
                return image?.getAttribute('src') === 'images/42/0002.png'
                    && image.complete && image.naturalWidth === 1600;
            }
            """,
            options: new() { Timeout = 5000 }
        );
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#image-viewer")).ToBeHiddenAsync();
        await Expect(tile).ToBeFocusedAsync();
        Assert.Empty(errors);
        Assert.Empty(externalRequests);
    }
}
