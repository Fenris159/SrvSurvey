namespace SrvSurvey.Desktop.ViewModels;

/// <summary>
/// Applies each journal monitor tick to registered projections in registration order. Idle polls reach only
/// idle projections; every other tick reaches only full projections.
/// </summary>
/// <typeparam name="TTick">Values earlier projections hand to later ones; created fresh for every tick.</typeparam>
internal sealed class JournalProjectionPipeline<TTick>
    where TTick : new()
{
    private readonly List<Func<JournalProjectionContext, TTick, Task>> idleProjections = [];
    private readonly List<Func<JournalProjectionContext, TTick, Task>> fullProjections = [];

    public JournalProjectionPipeline<TTick> Idle(Action<JournalProjectionContext, TTick> projection) =>
        Register(idleProjections, projection);

    public JournalProjectionPipeline<TTick> IdleAsync(Func<JournalProjectionContext, TTick, Task> projection) =>
        Register(idleProjections, projection);

    public JournalProjectionPipeline<TTick> Full(Action<JournalProjectionContext, TTick> projection) =>
        Register(fullProjections, projection);

    public JournalProjectionPipeline<TTick> FullAsync(Func<JournalProjectionContext, TTick, Task> projection) =>
        Register(fullProjections, projection);

    /// <summary>
    /// Completes each projection before starting the next. The first failure ends the tick and propagates
    /// unchanged, so later projections do not observe a partially applied update.
    /// </summary>
    public async Task ApplyAsync(JournalProjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tick = new TTick();
        foreach (
            Func<JournalProjectionContext, TTick, Task> projection in context.IsIdle ? idleProjections : fullProjections
        )
        {
            await projection(context, tick);
        }
    }

    private JournalProjectionPipeline<TTick> Register(
        List<Func<JournalProjectionContext, TTick, Task>> projections,
        Action<JournalProjectionContext, TTick> projection
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        projections.Add(
            (context, tick) =>
            {
                projection(context, tick);
                return Task.CompletedTask;
            }
        );
        return this;
    }

    private JournalProjectionPipeline<TTick> Register(
        List<Func<JournalProjectionContext, TTick, Task>> projections,
        Func<JournalProjectionContext, TTick, Task> projection
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        projections.Add(projection);
        return this;
    }
}
