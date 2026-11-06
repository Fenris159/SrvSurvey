using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed partial class RavenColonialClientTests
{
    /// <summary>The public project response preserves preparation layout keys and both numeric and string building counts.</summary>
    [Fact]
    public async Task ReadsLoadingPreparedBuildingCountsFromProjectResponse()
    {
        RavenColonialClient client = Create(
            new StubHandler(_ =>
                Json("""{"buildId":"loading","buildType":"fc_loading","prepBuilds":{"alastor?":1,"apollo":"2"}}""")
            )
        );
        ColonizationProject? project = await client.GetProjectAsync("loading");
        Assert.NotNull(project);
        Assert.NotNull(project.PreparedBuilds);
        Assert.Equal(2, project.PreparedBuilds.Count);
        Assert.Equal(1, project.PreparedBuilds["alastor?"]);
        Assert.Equal(2, project.PreparedBuilds["apollo"]);
    }
}
