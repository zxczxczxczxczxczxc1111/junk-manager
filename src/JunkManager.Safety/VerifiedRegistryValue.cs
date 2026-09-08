using Microsoft.Win32;

namespace JunkManager.Safety;

/// <summary>
/// A pass into deleting one registry VALUE. The constructor is internal for
/// exactly the reason <see cref="VerifiedPath"/> gives: nothing outside this
/// assembly can build one, so "was this actually checked" stops being a
/// question for a reviewer and becomes one the compiler answers.
/// </summary>
public readonly struct VerifiedRegistryValue : IEquatable<VerifiedRegistryValue>
{
    internal VerifiedRegistryValue(RegistryHive hive, string subKey, string valueName, RegistryView view)
    {
        Hive = hive;
        SubKey = subKey;
        ValueName = valueName;
        View = view;
    }

    public RegistryHive Hive { get; }

    /// <summary>Normalized branch: no leading, trailing or doubled separators.</summary>
    public string SubKey { get; }

    /// <summary>
    /// Never empty in a pass that was actually issued. The default value has no
    /// name and is refused by the guard before a pass exists.
    /// </summary>
    public string ValueName { get; }

    public RegistryView View { get; }

    public string Address => RegistryAddress.Format(Hive, SubKey, ValueName);

    /// <summary>
    /// True for default(VerifiedRegistryValue). A struct can always be produced
    /// uninitialised, so the holder of the delete call checks this rather than
    /// trusting that a pass exists because the type says so.
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(SubKey) || ValueName is null;

    public bool Equals(VerifiedRegistryValue other) =>
        Hive == other.Hive
        && View == other.View
        && string.Equals(SubKey, other.SubKey, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ValueName, other.ValueName, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is VerifiedRegistryValue v && Equals(v);

    public override int GetHashCode() => HashCode.Combine(
        Hive,
        View,
        SubKey is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(SubKey),
        ValueName is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(ValueName));

    public override string ToString() => Address;

    public static bool operator ==(VerifiedRegistryValue left, VerifiedRegistryValue right) =>
        left.Equals(right);

    public static bool operator !=(VerifiedRegistryValue left, VerifiedRegistryValue right) =>
        !left.Equals(right);
}

/// <summary>
/// A pass into deleting a whole KEY, subtree and all. Separate from
/// <see cref="VerifiedRegistryValue"/> and not a flag on it: the guard applies
/// one extra rule here, and a boolean would let a caller ask for the wrong
/// operation with the right pass.
/// </summary>
public readonly struct VerifiedRegistryKey : IEquatable<VerifiedRegistryKey>
{
    internal VerifiedRegistryKey(RegistryHive hive, string subKey, RegistryView view)
    {
        Hive = hive;
        SubKey = subKey;
        View = view;
    }

    public RegistryHive Hive { get; }

    public string SubKey { get; }

    public RegistryView View { get; }

    public string Address => RegistryAddress.Format(Hive, SubKey, null);

    public bool IsEmpty => string.IsNullOrEmpty(SubKey);

    public bool Equals(VerifiedRegistryKey other) =>
        Hive == other.Hive
        && View == other.View
        && string.Equals(SubKey, other.SubKey, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is VerifiedRegistryKey k && Equals(k);

    public override int GetHashCode() => HashCode.Combine(
        Hive, View, SubKey is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(SubKey));

    public override string ToString() => Address;

    public static bool operator ==(VerifiedRegistryKey left, VerifiedRegistryKey right) =>
        left.Equals(right);

    public static bool operator !=(VerifiedRegistryKey left, VerifiedRegistryKey right) =>
        !left.Equals(right);
}
