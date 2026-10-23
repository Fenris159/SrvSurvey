namespace SrvSurvey.Desktop.ViewModels;

public sealed partial record BiologySurveyViewModel
{
    /// <summary>Reuses only equivalent immutable row data while allowing all header and mode changes through.</summary>
    internal BiologySurveyViewModel RetainUnchangedRows(BiologySurveyViewModel? previous)
    {
        return this with
        {
            Bodies = OverlayListRetention.Keep(previous?.Bodies, Bodies, SameBody),
            Organisms = OverlayListRetention.Keep(previous?.Organisms, Organisms),
            GeologicalSignals = OverlayListRetention.Keep(previous?.GeologicalSignals, GeologicalSignals),
        };
    }

    private static bool SameBody(BiologyBodyRowViewModel left, BiologyBodyRowViewModel right) =>
        BodyValues(left) == BodyValues(right) && left.RewardBands.SequenceEqual(right.RewardBands);

    private static (
        int Id,
        string Name,
        string Subtype,
        int Analyzed,
        int Signals,
        long Known,
        long Minimum,
        long Maximum,
        bool Predicted,
        bool Unknown,
        bool Destination,
        bool Current,
        bool Canonn,
        double BucketOne,
        double BucketTwo,
        double BucketThree
    ) BodyValues(BiologyBodyRowViewModel body) =>
        (
            body.BodyId,
            body.Name,
            body.BodySubtype,
            body.AnalyzedSignalCount,
            body.SignalCount,
            body.KnownReward,
            body.MinimumReward,
            body.MaximumReward,
            body.HasPredictedReward,
            body.HasUnknownReward,
            body.IsDestination,
            body.IsCurrentBody,
            body.HasCanonnSignals,
            body.RewardBucketOneMillions,
            body.RewardBucketTwoMillions,
            body.RewardBucketThreeMillions
        );
}
