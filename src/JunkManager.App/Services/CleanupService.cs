using JunkManager.Core;
using JunkManager.Deletion;

namespace JunkManager.App.Services;

/// <summary>
/// One journal file per run, opened when the run starts and closed when it ends.
/// </summary>
/// <remarks>
/// The journal is opened here rather than once for the process, because the
/// journal file name carries the start time of the run and the history screen
/// reads one file as one cleanup. A process-wide journal would merge a week of
/// runs into a single entry.
/// </remarks>
internal sealed class CleanupService : ICleanupService
{
    public DeleteMode Mode { get; set; } = DeleteMode.Permanent;

    public async Task<CleanupReport> RunAsync(
        IReadOnlyList<Finding> findings,
        IProgress<CleanupProgress>? progress,
        CancellationToken ct)
    {
        using var zhurnal = JsonlOperationLog.CreateForRun();
        var runner = new CleanupRunner(new FileDeleter(zhurnal));

        return await runner.RunAsync(findings, Mode, progress, ct).ConfigureAwait(false);
    }
}
