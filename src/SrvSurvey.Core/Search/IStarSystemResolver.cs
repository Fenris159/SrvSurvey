namespace SrvSurvey.Core.Search;

public interface IStarSystemResolver
{
    Task<IReadOnlyList<StarSystemReference>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}
