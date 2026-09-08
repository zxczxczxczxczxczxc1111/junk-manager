namespace JunkManager.Core;

/// <summary>
/// Exactly two tiers. The question a tier answers is narrow on purpose: will the
/// application bring this back on its own, without me? A cache is never Risk,
/// however large it is, because the app refills it by itself and nobody notices.
/// </summary>
/// <remarks>
/// Three tiers existed in an earlier draft (Safe / Refill / Review). They were
/// cut on 05.09.2026: a label that needs explaining is a bad label, and the
/// dataviz palette validator failed the three-step ramp anyway (delta E 12.2
/// between neighbours; two steps give 31.4).
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification =
        "Zero is left undefined deliberately. With a None = 0 member, or with Safe = 0, " +
        "an uninitialised RiskTier reads as a legitimate value, and in a tool that deletes " +
        "files the value it would read as is 'safe to remove'. Leaving zero undefined turns " +
        "that mistake into a switch falling through to its default arm, which is loud.")]
public enum RiskTier
{
    /// <summary>Comes back by itself. You will not notice it went.</summary>
    Safe = 1,

    /// <summary>Does not come back by itself, and you will notice.</summary>
    Risk = 2,
}
