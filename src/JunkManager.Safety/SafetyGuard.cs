using System.Diagnostics.CodeAnalysis;

namespace JunkManager.Safety;

/// <summary>
/// The only door into deletion. Everything it refuses is refused with a reason a
/// human can read, and nothing it refuses ever produces an exception: rules come
/// from a file a human edits, and one typo must not take a whole scan down.
/// </summary>
public static class SafetyGuard
{
    // Path.GetInvalidPathChars() on .NET Core returns only NUL, so it cannot be
    // used to catch these. Wildcards are in the list on purpose: a rule file
    // stores literal paths, and a "*" in one means the rule was written wrong.
    private static readonly System.Buffers.SearchValues<char> Nedopustimye =
        System.Buffers.SearchValues.Create("<>|\"*?");

    /// <summary>
    /// Turns a raw string from a rule into a pass, or explains why it will not.
    /// </summary>
    /// <param name="path">Empty on refusal, always. A caller that ignores the
    /// return value still cannot get a usable pass out of a refused path.</param>
    public static bool TryVerify(
        string raw,
        out VerifiedPath path,
        [NotNullWhen(false)] out string? reason)
    {
        path = default;

        if (string.IsNullOrWhiteSpace(raw))
        {
            reason = "путь пустой";
            return false;
        }

        var trimmed = raw.Trim();

        // The extended-length prefix is stripped before anything else:
        // "\\?\C:\Windows" and "C:\Windows" are the same directory, and a guard
        // that compares them as different strings is a guard with a bypass.
        if (trimmed.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            trimmed = trimmed[4..];
        }

        if (trimmed.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            reason = "сетевые пути не обрабатываются";
            return false;
        }

        if (trimmed.AsSpan().IndexOfAny(Nedopustimye) >= 0 || trimmed.Any(char.IsControl))
        {
            reason = "в пути есть символы, недопустимые в имени файла";
            return false;
        }

        // Fully-qualified is checked BEFORE canonicalisation, not after.
        // Path.GetFullPath resolves a relative path against the current
        // directory, so asking afterwards always answers "yes" and the check
        // would be theatre.
        if (!Path.IsPathFullyQualified(trimmed))
        {
            reason = "путь не абсолютный";
            return false;
        }

        string canonical;
        try
        {
            canonical = Canonicalize(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = $"путь не разбирается: {ex.Message}";
            return false;
        }

        if (IsDeniedExact(canonical, out var exact))
        {
            reason = $"это корень тома {exact}, он не удаляется целиком никогда";
            return false;
        }

        if (IsDenied(canonical, out var deniedBy) && !IsExplicitlyAllowed(canonical, deniedBy))
        {
            reason = $"путь находится в запрещённом корне {deniedBy}";
            return false;
        }

        path = new VerifiedPath(canonical, canonical);
        reason = null;
        return true;
    }

    /// <summary>
    /// The check that runs immediately before deletion. Re-running the string
    /// check would prove nothing: the same string yields the same answer. What
    /// can change between scan and delete is what the path POINTS AT, so this
    /// opens a handle, asks the kernel where it landed, and re-runs every rule
    /// against that resolved path.
    /// </summary>
    public static bool TryVerifyForDeletion(
        string raw,
        out VerifiedPath path,
        [NotNullWhen(false)] out string? reason)
    {
        path = default;

        if (!TryVerify(raw, out var declared, out reason))
        {
            return false;
        }

        if (!PathResolver.TryResolveFinalPath(declared.Value, out var resolved, out var error))
        {
            reason = $"путь не открывается: {error}";
            return false;
        }

        string canonicalResolved;
        try
        {
            canonicalResolved = Canonicalize(resolved);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = $"разрешённый путь не разбирается: {ex.Message}";
            return false;
        }

        if (!canonicalResolved.Equals(declared.Value, StringComparison.OrdinalIgnoreCase))
        {
            // A link is not automatically an attack, but we never delete THROUGH
            // one: the rule asked for A and we are standing on B.
            reason = $"путь оказался ссылкой: {declared.Value} ведёт в {canonicalResolved}";
            return false;
        }

        // Belt and braces: run the full rule set against the resolved path too,
        // so a future change that makes resolution lenient cannot open a hole.
        if (!TryVerify(canonicalResolved, out _, out reason))
        {
            return false;
        }

        path = new VerifiedPath(declared.Value, canonicalResolved);
        reason = null;
        return true;
    }

    /// <summary>
    /// A pass for one entry that is supposed to live inside an already verified
    /// directory. Everything <see cref="TryVerifyForDeletion"/> checks, plus
    /// containment.
    /// </summary>
    /// <remarks>
    /// Containment is checked here and not left to the caller because the list
    /// of entries is built by one component and consumed by another. A target
    /// that escaped its root, whether through a bug in selection or through a
    /// path that resolved elsewhere, is somebody else's file, and the component
    /// holding the delete call is the last place that can still say no.
    /// </remarks>
    public static bool TryVerifyUnder(
        VerifiedPath root,
        string candidate,
        out VerifiedPath path,
        [NotNullWhen(false)] out string? reason)
    {
        if (!TryVerifyForDeletion(candidate, out path, out reason))
        {
            return false;
        }

        if (!IsAtOrUnder(path.Value, root.Value))
        {
            path = default;
            reason = $"цель вне корня находки: {candidate} не внутри {root.Value}";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is <paramref name="root"/> or lies
    /// inside it. Takes passes rather than strings so the containment question
    /// can only be asked about paths that were already canonicalised: asking it
    /// about two raw strings is how "C:\Temp" and "C:\temp\" end up looking like
    /// different places.
    /// </summary>
    public static bool Contains(VerifiedPath root, VerifiedPath candidate) =>
        IsAtOrUnder(candidate.Value, root.Value);

    /// <summary>
    /// One string per directory, whatever spelling the rule used. Expects input
    /// already trimmed and already stripped of the extended-length prefix.
    /// </summary>
    internal static string Canonicalize(string trimmed)
    {
        var full = Path.GetFullPath(trimmed);

        // Drop the trailing separator, except on a bare volume root where "C:\"
        // is the entire path and trimming it changes the meaning to "the current
        // directory on C:".
        if (full.Length > 3 && full.EndsWith(Path.DirectorySeparatorChar))
        {
            full = full.TrimEnd(Path.DirectorySeparatorChar);
        }

        return full;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> IS <paramref name="root"/> or lies
    /// under it. Deliberately not StartsWith: the string "C:\Windows" is a prefix
    /// of the string "C:\WindowsApps", but WindowsApps is not inside Windows, and
    /// a guard built on prefixes refuses honest work while feeling strict.
    /// </summary>
    internal static bool IsAtOrUnder(string candidate, string root)
    {
        var r = root.Length > 3 ? root.TrimEnd(Path.DirectorySeparatorChar) : root;

        if (candidate.Equals(r.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // The separator is what turns a prefix into containment.
        var withSeparator = r.EndsWith(Path.DirectorySeparatorChar)
            ? r
            : r + Path.DirectorySeparatorChar;

        return candidate.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeniedExact(string canonical, [NotNullWhen(true)] out string? root)
    {
        foreach (var candidate in ForbiddenRoots.DeniedExact)
        {
            if (canonical.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                || canonical.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                root = candidate;
                return true;
            }
        }

        root = null;
        return false;
    }

    private static bool IsDenied(string canonical, [NotNullWhen(true)] out string? deniedBy)
    {
        // Longest first, so the reason names the most specific denial that
        // caught the path instead of whichever one happens to sit earliest.
        foreach (var root in ForbiddenRoots.DeniedTrees.OrderByDescending(r => r.Length))
        {
            if (IsAtOrUnder(canonical, root))
            {
                deniedBy = root;
                return true;
            }
        }

        deniedBy = null;
        return false;
    }

    private static bool IsExplicitlyAllowed(string canonical, string deniedBy)
    {
        var denialDepth = deniedBy.TrimEnd(Path.DirectorySeparatorChar).Length;

        foreach (var allowed in ForbiddenRoots.Allowed)
        {
            if (!IsAtOrUnder(canonical, allowed))
            {
                continue;
            }

            // The carve-out only wins when it is deeper than the denial it
            // overrides. Otherwise an exception at the same level as a denied
            // root would unlock the whole root.
            if (allowed.TrimEnd(Path.DirectorySeparatorChar).Length > denialDepth)
            {
                return true;
            }
        }

        return false;
    }
}
