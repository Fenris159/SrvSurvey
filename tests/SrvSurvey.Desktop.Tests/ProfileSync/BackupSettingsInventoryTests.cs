using System.Reflection;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ProfileSync;

namespace SrvSurvey.Desktop.Tests.ProfileSync;

public sealed class BackupSettingsInventoryTests
{
    [Fact]
    public void EverySettingsStoreHasAnExplicitBackupDecision()
    {
        Type[] stores = new[] { typeof(UiSettingsDocumentStore).Assembly, typeof(GroundTargetSettingsStore).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .Where(type =>
                type.Name.EndsWith("SettingsStore", StringComparison.Ordinal)
                || type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(field => field.FieldType == typeof(UiSettingsDocumentStore))
            )
            .ToArray();
        Type[] registered = ProfileSyncSettingsCatalog.Definitions.Select(definition => definition.StoreType).ToArray();

        Assert.Empty(stores.Except(registered).Select(type => type.FullName));
        Assert.Equal(registered.Length, registered.Distinct().Count());
        Assert.All(
            ProfileSyncSettingsCatalog.Definitions.Where(item => item.Scope == ProfileBackupScope.Excluded),
            item => Assert.NotEqual(ProfileBackupExclusion.None, item.Exclusion)
        );
    }
}
