using JunkManager.Core;
using JunkManager.Core.Apps;
using JunkManager.Deletion;

namespace JunkManager.App.Services;

internal sealed record ProgramLeftoverCandidate(InstalledProgram Program, UninstallResult Removal, Leftover Item);
internal sealed record ProgramsRemovalReport(UninstallQueueResult Queue,
    IReadOnlyList<ProgramLeftoverCandidate> Leftovers, IReadOnlyList<SkippedItem> Skipped);

internal interface IProgramsService
{
    string CleanupNote { get; }
    Task<string> PrepareLeftoversAsync(CancellationToken ct) => Task.FromResult(string.Empty);
    Task<ProgramInventorySnapshot> ReadAsync(CancellationToken ct);
    Task<ProgramSizeEstimate> MeasureSizeAsync(InstalledProgram program, IReadOnlyList<InstalledProgram> owners, CancellationToken ct) =>
        Task.FromResult(new ProgramSizeEstimate(program.EstimatedSizeBytes, false, "Установщик не сообщил размер"));
    Task<ProgramsRemovalReport> RemoveAsync(IReadOnlyList<InstalledProgram> programs, UninstallOptions options,
        IProgress<UninstallProgress>? progress, CancellationToken ct);
    Task<IReadOnlyList<DeleteOutcome>> CleanLeftoversAsync(IReadOnlyList<ProgramLeftoverCandidate> items,
        IProgress<string>? progress, CancellationToken ct);
}
