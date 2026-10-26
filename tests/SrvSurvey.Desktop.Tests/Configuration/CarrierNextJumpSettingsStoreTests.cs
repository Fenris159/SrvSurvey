using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class CarrierNextJumpSettingsStoreTests
{
    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"CarrierNextJump\":true}", true)]
    [InlineData("{\"CarrierNextJump\":{\"ShowInformation\":\"false\"}}", true)]
    [InlineData("{\"CarrierNextJump\":{\"ShowInformation\":false}}", false)]
    public void MissingAndInvalidSettingsDefaultOnAndExplicitValuesPersist(string json, bool expected)
    {
        string directory = Path.Combine(Path.GetTempPath(), "carrier-jump-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ui.json");
        try
        {
            File.WriteAllText(path, json);
            var store = new CarrierNextJumpSettingsStore(path);
            Assert.Equal(expected, store.Load());
            store.Save(false);
            Assert.False(new CarrierNextJumpSettingsStore(path).Load());
            store.Save(true);
            Assert.True(store.Load());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
