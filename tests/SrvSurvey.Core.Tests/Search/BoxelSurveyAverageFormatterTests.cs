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
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("1 in 10", BoxelSurveyAverageFormatter.Format(1, 10));
            Assert.Equal("1 in 3.3", BoxelSurveyAverageFormatter.Format(3, 10));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void BodiesPerSystemUsesTwoDecimalsWhenMoreCommonThanOnePerSystem()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("2.5", BoxelSurveyAverageFormatter.Format(25, 10));
            Assert.Equal("1.25", BoxelSurveyAverageFormatter.Format(15, 12));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void HonorsCustomMinimumAndCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
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
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
