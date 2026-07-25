using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SystemNicknameViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-nickname-view-model-tests-{Guid.NewGuid():N}");

    [Fact]
    public void EnablingNamesPersistsAndNotifiesOpenOverlays()
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "system-nick-names.json"),
            "{\"map\":{\"Sol\":\"Birthplace of Humanity\"}}");
        var path = Path.Combine(temporaryDirectory, "ui.json");
        var viewModel = new SystemNicknameViewModel(
            SystemNicknameCatalog.Load(temporaryDirectory),
            new SystemNicknameSettingsStore(path));
        var changed = 0;
        viewModel.NamesChanged += (_, _) => changed++;

        viewModel.Enabled = true;

        Assert.Equal("Birthplace of Humanity", viewModel.Resolve("sol"));
        Assert.True(new SystemNicknameSettingsStore(path).LoadEnabled());
        Assert.Equal(1, changed);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
