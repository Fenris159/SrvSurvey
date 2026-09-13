namespace SrvSurvey.Desktop.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AvaloniaHeadlessTestCollection
{
    private AvaloniaHeadlessTestCollection() { }

    public const string Name = "Avalonia headless interaction";
}
