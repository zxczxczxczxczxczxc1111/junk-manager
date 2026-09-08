namespace JunkManager.Deletion;

/// <summary>
/// Where every outcome goes before the caller sees it. The journal is not an
/// undo: deletion is permanent, and nothing here brings a file back. It exists
/// so that the question "what did this thing remove yesterday" has an answer.
/// </summary>
public interface IOperationLog
{
    /// <summary>
    /// Records one outcome. Called for successes and refusals alike, and it must
    /// be durable by the time it returns: a journal that keeps entries in memory
    /// until the end loses exactly the run that most needed explaining.
    /// </summary>
    Task RecordAsync(DeleteOutcome outcome, CancellationToken ct = default);
}
