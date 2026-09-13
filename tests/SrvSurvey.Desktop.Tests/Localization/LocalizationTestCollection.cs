namespace SrvSurvey.Desktop.Tests.Localization;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalizationTestCollection
{
    private LocalizationTestCollection() { }

    public const string Name = "Process-wide localization";
}
