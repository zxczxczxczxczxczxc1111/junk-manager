namespace JunkManager.Core.Apps;

public enum UninstallExitStatus { Unknown, Success, RebootRequired, RebootInitiated, AlreadyAbsent, Cancelled, Busy, Failed }

public sealed record UninstallProgress(string ProgramId, string Executable, int ProcessId, TimeSpan Elapsed, bool CheckingRegistration);

public sealed record UninstallOptions
{
    public TimeSpan ObservationWindow { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan MaximumObservation { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan ConfirmationWindow { get; init; } = TimeSpan.FromSeconds(30);
    public Func<UninstallProgress, CancellationToken, Task<bool>>? ContinueWaitingAsync { get; init; }
}

public sealed record UninstallRequest(InstalledProgram Program, UninstallCommand? Command = null, bool AllowWindow = false);
public sealed record UninstallQueueResult(IReadOnlyList<UninstallResult> Results, bool Cancelled);
