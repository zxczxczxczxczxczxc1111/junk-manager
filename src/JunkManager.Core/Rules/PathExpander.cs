using System.Text.RegularExpressions;

namespace JunkManager.Core.Rules;

/// <summary>
/// Turns a rule's path pattern into the concrete paths that exist right now.
/// Two properties matter more than convenience: an undefined variable is a loud
/// refusal rather than a literal, and a wildcard never walks into a reparse
/// point.
/// </summary>
public static partial class PathExpander
{
    [GeneratedRegex(@"%([A-Za-z_][A-Za-z0-9_()]*)%")]
    private static partial Regex EnvVarPattern();

    /// <summary>
    /// Existing paths matching the pattern. Nothing that does not exist is
    /// returned: a scanner handed a path that is not there would have to treat an
    /// exception as "empty", and those two answers must stay distinguishable.
    /// </summary>
    /// <remarks>
    /// This is an iterator, so the refusal for an undefined variable surfaces on
    /// the first MoveNext rather than at the call. Callers materialise the result
    /// anyway; the alternative, splitting into a non-iterator wrapper, buys a
    /// nicer stack trace and nothing else.
    /// </remarks>
    public static IEnumerable<string> Expand(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            yield break;
        }

        var resolved = ExpandEnvironment(pattern.Trim());
        if (!Path.IsPathFullyQualified(resolved) || resolved.StartsWith(@"\\", StringComparison.Ordinal))
            throw new RuleFormatException("правило должно указывать абсолютный локальный путь");
        var segments = resolved.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.None);

        if (segments.Length == 0)
        {
            yield break;
        }

        // The first segment is the volume ("C:"), and it keeps its separator:
        // "C:" alone means "the current directory on C:", which is a different
        // place entirely.
        var current = new List<string> { segments[0] + Path.DirectorySeparatorChar };

        for (var i = 1; i < segments.Length; i++)
        {
            var segment = segments[i];

            if (segment.Length == 0)
            {
                continue;
            }

            var lastSegment = i == segments.Length - 1;
            var next = new List<string>();

            foreach (var baseDir in current)
            {
                if (IsReparsePoint(baseDir)) continue;
                if (!segment.Contains('*', StringComparison.Ordinal)
                    && !segment.Contains('?', StringComparison.Ordinal))
                {
                    var literal = Path.Combine(baseDir, segment);
                    if (!IsReparsePoint(literal)) next.Add(literal);
                    continue;
                }

                foreach (var match in MatchOneLevel(baseDir, segment, lastSegment))
                {
                    next.Add(match);
                }
            }

            current = next;
        }

        foreach (var path in current)
        {
            if (IsReparsePoint(path))
            {
                // Even as the final answer a link is refused: deleting through one
                // takes out whatever it points at, which the rule never named.
                continue;
            }

            if (Directory.Exists(path) || File.Exists(path))
            {
                yield return path;
            }
        }
    }

    /// <summary>
    /// Matches exactly one level. A wildcard in a rule means "any name here", and
    /// never "anywhere below here": a recursive glob is a different operation and
    /// must not be reachable by accident.
    /// </summary>
    private static List<string> MatchOneLevel(string baseDir, string segment, bool lastSegment)
    {
        var result = new List<string>();

        try
        {
            // Directories always: an intermediate level can only be a directory.
            // Files as well when this is the last segment, because "*.log" at the
            // end of a rule obviously means files.
            var matches = lastSegment
                ? Directory.EnumerateFileSystemEntries(baseDir, segment, SearchOption.TopDirectoryOnly)
                : Directory.EnumerateDirectories(baseDir, segment, SearchOption.TopDirectoryOnly);

            foreach (var match in matches)
            {
                // Refused at the earliest possible step. Once the walk is inside a
                // reparse point, everything below it looks perfectly legitimate.
                if (IsReparsePoint(match))
                {
                    continue;
                }

                result.Add(match);
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Expected: a rule is written for machines in general, not this one.
        }

        return result;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if (info.Exists)
            {
                return (info.Attributes & FileAttributes.ReparsePoint) != 0;
            }

            var file = new FileInfo(path);
            return file.Exists && (file.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            // Cannot tell, so assume the dangerous answer.
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    internal static string ExpandEnvironment(string pattern) =>
        EnvVarPattern().Replace(pattern, m =>
        {
            var name = m.Groups[1].Value;
            var value = Environment.GetEnvironmentVariable(name);

            if (string.IsNullOrEmpty(value))
            {
                throw new RuleFormatException(
                    $"переменная окружения {name} не определена, правило не может быть раскрыто");
            }

            return value;
        });
}
