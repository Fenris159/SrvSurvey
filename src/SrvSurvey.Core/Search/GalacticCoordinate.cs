namespace SrvSurvey.Core.Search;

public readonly record struct GalacticCoordinate
{
    public GalacticCoordinate(double x, double y, double z)
    {
        if (!double.IsFinite(x)
            || !double.IsFinite(y)
            || !double.IsFinite(z))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                "Galactic coordinates must be finite numbers.");
        }

        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }

    public double Y { get; }

    public double Z { get; }

    public double DistanceTo(GalacticCoordinate other)
    {
        return Math.Sqrt(
            Math.Pow(X - other.X, 2)
                + Math.Pow(Y - other.Y, 2)
                + Math.Pow(Z - other.Z, 2));
    }

    public override string ToString()
    {
        return $"[ {X}, {Y}, {Z} ]";
    }
}

public sealed record StarSystemReference(
    string Name,
    long SystemAddress,
    GalacticCoordinate Position);
