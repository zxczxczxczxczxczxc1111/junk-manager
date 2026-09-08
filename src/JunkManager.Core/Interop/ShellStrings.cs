using System.Runtime.InteropServices;

namespace JunkManager.Core.Interop;

/// <summary>
/// Registry values under VolumeCaches store the visible name as an indirect
/// string: "@%SystemRoot%\System32\dskquota.dll,-1234". The literal is useless
/// to a person, and the resource it points at is the only localized name the
/// handler has, because Initialize returns NULL for the name on every legacy
/// handler. Verified on all 22 of them, 05.09.2026.
/// </summary>
/// <remarks>
/// DllImport rather than LibraryImport on purpose, same trade as in
/// JunkManager.Safety/Native/PathResolver.cs: the generator emits unsafe code
/// for any signature carrying a string or an array, and turning
/// AllowUnsafeBlocks on across a solution that deletes files is not worth
/// saving one hand-written signature. DefaultDllImportSearchPaths is not
/// decoration either: without it the loader may pick up a shlwapi sitting next
/// to the executable (CA5392).
/// </remarks>
public static class ShellStrings
{
    [DllImport("shlwapi.dll", EntryPoint = "SHLoadIndirectString",
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SHLoadIndirectString(
        string source, [Out] char[] buffer, int cchOutBuf, nint reserved);

    /// <summary>
    /// Resolves "@file,-id" to the string it names. Anything that does not start
    /// with '@' is already a plain string and comes back unchanged.
    /// </summary>
    public static bool TryResolve(string? source, out string resolved)
    {
        resolved = string.Empty;

        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (!source.StartsWith('@'))
        {
            resolved = source;
            return true;
        }

        var buffer = new char[1024];
        var hr = SHLoadIndirectString(source, buffer, buffer.Length, nint.Zero);

        if (hr != 0)
        {
            return false;
        }

        var end = Array.IndexOf(buffer, '\0');
        resolved = new string(buffer, 0, end < 0 ? buffer.Length : end);
        return resolved.Length > 0;
    }
}
