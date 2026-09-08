namespace JunkManager.Safety;

/// <summary>
/// A pass into deletion. The constructor is internal on purpose: no code outside
/// this assembly can produce one, so "did you actually check this path?" stops
/// being a question a reviewer has to ask and becomes something the compiler
/// answers. A method that takes a <see cref="VerifiedPath"/> cannot be handed a
/// raw string by accident.
/// </summary>
public readonly struct VerifiedPath : IEquatable<VerifiedPath>
{
    internal VerifiedPath(string canonical, string resolved)
    {
        Value = canonical;
        Resolved = resolved;
    }

    /// <summary>Canonical form of the path the rule asked for.</summary>
    public string Value { get; }

    /// <summary>
    /// Where it actually points after links and junctions were followed. Equal to
    /// <see cref="Value"/> until stage 4 teaches the guard to resolve reparse
    /// points; the two are separate fields from the start because the difference
    /// between them is the whole junction-swap defence.
    /// </summary>
    public string Resolved { get; }

    public bool Equals(VerifiedPath other) =>
        string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Resolved, other.Resolved, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is VerifiedPath p && Equals(p);

    public override int GetHashCode() =>
        Value is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value ?? string.Empty;

    public static bool operator ==(VerifiedPath left, VerifiedPath right) => left.Equals(right);

    public static bool operator !=(VerifiedPath left, VerifiedPath right) => !left.Equals(right);
}
