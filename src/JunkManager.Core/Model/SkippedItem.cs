namespace JunkManager.Core;

/// <summary>
/// Something the product looked at and deliberately did not take. A skip is a
/// result, not an error: the honest answer to a locked file is its holder's
/// name, not an exception and not silence.
/// </summary>
/// <param name="HoldingProcess">
/// Filled from the Restart Manager when the reason is a lock. Null when the
/// reason is something else, or when the holder could not be identified: an
/// invented name would be worse than an admitted gap.
/// </param>
public sealed record SkippedItem(string Path, string Reason, string? HoldingProcess = null)
{
    /// <summary>A known policy exclusion, not a failure to inspect the object.</summary>
    public bool IsExpectedExclusion { get; init; }
}
