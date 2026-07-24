using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class GlobalInputBindingRouterTests
{
    [Fact]
    public void ResolvesConfiguredChordAfterCanonicalization()
    {
        var settings = GlobalInputSettings.Default with
        {
            Bindings = GlobalInputSettings.Default.Bindings
                .ToDictionary(entry => entry.Key, entry => entry.Value),
        };
        var bindings = settings.Bindings.ToDictionary();
        bindings[GlobalInputAction.CopyNextBoxel] = "ctrl alt x";
        var router = new GlobalInputBindingRouter(settings with
        {
            Bindings = bindings,
        });

        Assert.True(router.TryResolve("ALT CTRL X", out var action));
        Assert.Equal(GlobalInputAction.CopyNextBoxel, action);
    }

    [Fact]
    public void FirstCatalogActionWinsDuplicateChord()
    {
        var bindings = GlobalInputSettings.Default.Bindings.ToDictionary();
        bindings[GlobalInputAction.ToggleAllVisibility] = "ALT X";
        bindings[GlobalInputAction.CopyNextBoxel] = "ALT X";
        var router = new GlobalInputBindingRouter(
            GlobalInputSettings.Default with { Bindings = bindings });

        Assert.True(router.TryResolve("ALT X", out var action));
        Assert.Equal(GlobalInputAction.ToggleAllVisibility, action);
    }

    [Fact]
    public void ResolvesNamedKeysWithoutCaseSensitivity()
    {
        var bindings = GlobalInputSettings.Default.Bindings.ToDictionary();
        bindings[GlobalInputAction.ToggleAllVisibility] = "ctrl oemcomma";
        var router = new GlobalInputBindingRouter(
            GlobalInputSettings.Default with { Bindings = bindings });

        Assert.True(
            router.TryResolve("CTRL Oemcomma", out var action));
        Assert.Equal(GlobalInputAction.ToggleAllVisibility, action);
    }
}
