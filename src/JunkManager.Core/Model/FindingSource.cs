namespace JunkManager.Core;

/// <summary>
/// Which mechanism produced a finding. Carried on every finding so the seeded
/// acceptance run can check not merely that a planted item was found, but that
/// it was found by the mechanism that was supposed to find it.
/// </summary>
/// <remarks>
/// Without this, a dead Windows cleanup handler stays invisible: the pattern
/// scan quietly covers for it, the count adds up, and the hole survives.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification =
        "Zero is left undefined deliberately. A None = 0 member would mean 'we do not know " +
        "which mechanism produced this finding', and the seeded acceptance run compares " +
        "sources: an unknown source that compares equal to nothing is better caught at the " +
        "point it is created than carried silently into the verdict.")]
public enum FindingSource
{
    /// <summary>A rule from the JSON rule files matched a literal path.</summary>
    Rule = 1,

    /// <summary>An IEmptyVolumeCache2 handler registered by Windows itself.</summary>
    VolumeCache = 2,

    /// <summary>A documented platform utility: DISM, pnputil.</summary>
    PlatformTool = 3,

    /// <summary>A pattern search that did not know the path in advance.</summary>
    PatternScan = 4,

    /// <summary>Reclaimable space inside a file, found without deleting it.</summary>
    Vacuum = 5,

    /// <summary>A detector with no list behind it: orphans, leftovers, duplicates.</summary>
    Detector = 6,

    /// <summary>A registry entry pointing at a file that is not there.</summary>
    Registry = 7,

    /// <summary>An installed program, or a trace one left behind.</summary>
    Program = 8,
}
