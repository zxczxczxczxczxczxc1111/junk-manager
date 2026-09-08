namespace JunkManager.Core;

/// <summary>
/// How far a scan has got. Lives in Core rather than in the application because
/// the CLI reports the same thing, and two shapes for one fact drift apart.
/// </summary>
/// <param name="Total">
/// Zero means the total is not known yet. Not "nothing to do": the difference
/// matters, because a bar drawn from a zero total sits at the far left and
/// reads as a stuck job.
/// </param>
public sealed record ScanProgress(string Path, int Done, int Total)
{
    /// <summary>
    /// Доля от нуля до единицы, либо null, когда общее число неизвестно.
    /// Null это честный ответ, а не отсутствие ответа: полоса рисуется
    /// неопределённой, и это ровно то, что происходит.
    /// </summary>
    public double? Share => Total > 0
        ? Math.Clamp((double)Done / Total, 0, 1)
        : null;
}
