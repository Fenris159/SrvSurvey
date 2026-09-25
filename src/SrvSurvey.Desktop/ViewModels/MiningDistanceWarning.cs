namespace SrvSurvey.Desktop.ViewModels;

public static class MiningDistanceWarning
{
    public static string For(double radius) =>
        radius switch
        {
            <= 100 => "",
            < 200 => "Over 100 ly: more systems may make this search noticeably slower.",
            < 300 => "200+ ly: expect a longer search and more data requests.",
            < 400 => "300+ ly: this search may take several minutes in a dense region.",
            < 500 => "400+ ly: the broad search may be slow or reach a provider request limit.",
            _ => "500 ly: this is the widest search. Allow extra time or narrow the filters if it fails.",
        };
}
