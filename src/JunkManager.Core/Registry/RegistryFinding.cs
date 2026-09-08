using JunkManager.Safety;
using Microsoft.Win32;

namespace JunkManager.Core.Registry;

/// <summary>
/// One registry entry pointing at a file that is not there.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately NOT convertible to <see cref="Finding"/>. Such a conversion
/// produces a finding whose Path is the missing file, and that finding can be
/// handed to FileDeleter: the registry cleaner becomes a disk cleaner through
/// one honest-looking call. The acceptance judge builds the conversion for
/// itself, on the spot, with this warning written next to it.
/// </para>
/// <para>
/// No size and no tier either. Section 9 of the spec: freed megabytes are never
/// shown for the registry because there are none, and the tier answers "will the
/// system bring this back on its own", a question with no meaning for a dead
/// pointer.
/// </para>
/// </remarks>
/// <param name="ValueName">Empty for <see cref="RegistryEntryKind.Key"/>.</param>
/// <param name="RawValue">
/// What the value actually said, before expansion. Kept so the journal records
/// what was removed and not our reading of it.
/// </param>
public sealed record RegistryFinding(
    RegistryHive Hive,
    string SubKey,
    string ValueName,
    RegistryView View,
    RegistryEntryKind Kind,
    string RawValue,
    string MissingTarget,
    string Name,
    string Consequence,
    string RuleId)
{
    public RegistryEntrySnapshot? Snapshot { get; init; }

    public bool ManualSelectionOnly { get; init; }

    public string Category { get; init; } = "Основные ссылки";

    public string Address => RegistryAddress.Format(
        Hive, SubKey, Kind == RegistryEntryKind.Value ? ValueName : null);
}
