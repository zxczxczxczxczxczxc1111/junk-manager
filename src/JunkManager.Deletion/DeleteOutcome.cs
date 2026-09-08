using System.Diagnostics.CodeAnalysis;

namespace JunkManager.Deletion;

/// <summary>
/// What actually happened to one path. Four states rather than three: a refusal
/// and a cancellation look the same in a summary line but mean opposite things
/// to the person reading it, and collapsing them would make the journal lie
/// about who stopped the work.
/// </summary>
[SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification =
        "Zero is left undefined so an unassigned outcome cannot read as 'Deleted' or as any " +
        "other real state. Same reasoning as RiskTier and DeleteMode.")]
public enum DeleteStatus
{
    /// <summary>Gone. BytesFreed says how much came back.</summary>
    Deleted = 1,

    /// <summary>Deliberately not taken. Reason says why, and it is always filled.</summary>
    Skipped = 2,

    /// <summary>Tried and could not. Distinct from Skipped: nobody decided this.</summary>
    Failed = 3,

    /// <summary>Stopped by the person, not by the product.</summary>
    Cancelled = 4,
}

/// <summary>
/// The record of one deletion attempt, and the only thing the caller gets back.
/// </summary>
/// <param name="Path">
/// The path as the guard canonicalised it, never as the caller spelled it.
/// </param>
/// <param name="BytesFreed">
/// Counted from the files actually removed, not from what the scan promised.
/// A scan figure is a forecast; this one is a measurement.
/// </param>
/// <param name="Reason">
/// Filled for every state except <see cref="DeleteStatus.Deleted"/>. A skip
/// without a reason is indistinguishable from a bug.
/// </param>
/// <param name="HoldingProcess">
/// The process holding the file, once the Restart Manager can name it. Null
/// while it cannot: an invented name is worse than an admitted gap.
/// </param>
public sealed record DeleteOutcome(
    string Path,
    DeleteStatus Status,
    long BytesFreed,
    string? Reason = null,
    string? HoldingProcess = null);
