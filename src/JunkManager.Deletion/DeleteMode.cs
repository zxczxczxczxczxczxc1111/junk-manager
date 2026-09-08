using System.Diagnostics.CodeAnalysis;

namespace JunkManager.Deletion;

/// <summary>
/// How a path is to be removed.
/// </summary>
/// <remarks>
/// Both members do what they say. RecycleBin was deliberately absent until
/// IFileOperation was actually wired up: an enum member naming an unimplemented
/// behaviour is worse than a missing one, because a caller can select it and get
/// something else.
/// </remarks>
[SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification =
        "Zero is left undefined on purpose, as in RiskTier. A default(DeleteMode) that " +
        "silently means 'permanent' would turn a forgotten assignment into an irreversible " +
        "deletion. Undefined zero makes the switch fall through to its throwing arm instead.")]
public enum DeleteMode
{
    /// <summary>Gone for good. Never reaches the recycle bin.</summary>
    Permanent = 1,

    /// <summary>
    /// Into the recycle bin, so the person can change their mind. Costs disk
    /// space rather than freeing it until the bin is emptied, and that is the
    /// honest trade, not a hidden one.
    /// </summary>
    RecycleBin = 2,
}
