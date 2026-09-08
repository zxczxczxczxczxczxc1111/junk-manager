namespace JunkManager.Core;

/// <summary>
/// One thing the product proposes to remove. The path is the identity: two
/// findings with the same path are the same finding, whichever mechanism found
/// them first.
/// </summary>
/// <param name="Name">What a person sees in the list.</param>
/// <param name="Path">Full canonical path. The identity of the finding.</param>
/// <param name="Consequence">
/// One plain sentence saying what disappears. Mandatory: the tier says whether
/// something is lost, this says what. A finding without it does not ship, and
/// the rule loader refuses to load a rule that lacks one.
/// </param>
/// <param name="Source">Which mechanism produced this. See <see cref="FindingSource"/>.</param>
/// <param name="RuleId">
/// Filled only when <paramref name="Source"/> is <see cref="FindingSource.Rule"/>.
/// Null everywhere else, because nothing else comes from a rule file.
/// </param>
/// <param name="LastUsedDays">
/// Days since the item was last touched, when that can be established. Null when
/// it cannot: an unknown age must not be displayed as zero.
/// </param>
/// <param name="Scope">
/// Whether agreeing to this finding takes <paramref name="Path"/> whole or only
/// the entries listed in <paramref name="Targets"/>.
/// </param>
/// <param name="Targets">
/// The exact paths deletion will take, filled only in
/// <see cref="DeleteScope.SelectedEntries"/>. Read it through
/// <see cref="DeletionTargets"/> and never directly: the raw property is
/// nullable and a null read as "nothing selected" is the shape of the bug this
/// whole mechanism exists to prevent.
/// </param>
public sealed record Finding(
    string Name,
    string Path,
    long SizeBytes,
    RiskTier Tier,
    string Consequence,
    FindingSource Source,
    string? RuleId = null,
    int? LastUsedDays = null,
    DeleteScope Scope = DeleteScope.Whole,
    IReadOnlyList<string>? Targets = null)
{
    public IReadOnlyList<string> RequiredStoppedProcesses { get; init; } = [];

    public bool IsSizeEstimate { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string, Scanning.CleanupFileSnapshot>? FileSnapshots { get; init; }

    /// <summary>
    /// Everything this finding removes, and the only honest answer to "what
    /// exactly goes". <see cref="SizeBytes"/> is the sum over this list, so a
    /// caller that deletes anything else is deleting bytes nobody was shown.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A per-entry finding was built without a list. Loud on purpose: an empty
    /// list would read as "delete nothing" and a null path list one layer down
    /// reads as "delete the root", and those are opposite mistakes.
    /// </exception>
    public IReadOnlyList<string> DeletionTargets => Scope switch
    {
        DeleteScope.Whole => [Path],
        DeleteScope.SelectedEntries => Targets
            ?? throw new InvalidOperationException(
                $"поэлементная находка без списка целей: {Path}"),
        _ => throw new InvalidOperationException(
            $"неизвестная область удаления {Scope} у находки {Path}"),
    };
}
