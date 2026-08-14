using System.Globalization;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class BoxelSurveyAverageFormatterTests
{
    [Fact]
    public void NullOrZeroCountIsEmDash()
    {
        Assert.Equal(
            BoxelSurveyAverageFormatter.Placeholder,
            BoxelSurveyAverageFormatter.Format(null, 20));
        Assert.Equal(
            BoxelSurveyAverageFormatter.Placeholder,
            BoxelSurveyAverageFormatter.Format(0, 20));
        Assert.Equal("\u2014", BoxelSurveyAverageFormatter.Placeholder);
    }

    [Fact]
    public void HidesAverageUntilMinimumVisitedSystems()
    {
        Assert.Equal(
            BoxelSurveyAverageFormatter.Placeholder,
            BoxelSurveyAverageFormatter.Format(1, 9));
        Assert.Equal(
            "1 in 10",
            BoxelSurveyAverageFormatter.Format(1, 10));
    }

    [Fact]
    public void InverseFrequencyUsesOneDecimalWhenRarerThanOnePerSystem()
    {
        using var _ = new CultureScope("en-US");
        Assert.Equal("1 in 10", BoxelSurveyAverageFormatter.Format(1, 10));
        Assert.Equal("1 in 3.3", BoxelSurveyAverageFormatter.Format(3, 10));
    }

    [Fact]
    public void BodiesPerSystemUsesTwoDecimalsWhenMoreCommonThanOnePerSystem()
    {
        using var _ = new CultureScope("en-US");
        Assert.Equal("2.5", BoxelSurveyAverageFormatter.Format(25, 10));
        Assert.Equal("1.25", BoxelSurveyAverageFormatter.Format(15, 12));
    }

    [Fact]
    public void HonorsCustomMinimumAndCurrentCulture()
    {
        using var _ = new CultureScope("de-DE");
        Assert.Equal(
            BoxelSurveyAverageFormatter.Placeholder,
            BoxelSurveyAverageFormatter.Format(
                1,
                4,
                new BoxelSurveyAverageFormat(5)));
        Assert.Equal(
            "2,5",
            BoxelSurveyAverageFormatter.Format(
                25,
                10,
                new BoxelSurveyAverageFormat(5)));
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous;

        public CultureScope(string cultureName)
        {
            previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
