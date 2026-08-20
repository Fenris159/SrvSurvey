namespace SrvSurvey.Desktop.Platform.Overlay;

internal readonly record struct OverlayId
{
    internal OverlayId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    internal string Value { get; }

    public override string ToString() => Value;
}
