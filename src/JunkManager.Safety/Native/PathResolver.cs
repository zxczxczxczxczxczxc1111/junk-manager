using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace JunkManager.Safety;

/// <summary>
/// Resolves a path the way the kernel sees it, by opening a handle and asking
/// where that handle actually landed. String inspection cannot do this: a
/// junction looks like an ordinary directory from the outside, and the string
/// that named it does not change when somebody swaps what it points at.
/// </summary>
/// <remarks>
/// These two declarations use DllImport rather than LibraryImport on purpose.
/// The source generator refuses to run unless the whole project is built with
/// AllowUnsafeBlocks, and this solution deletes files for a living: turning
/// pointer arithmetic back on across an assembly, to save hand-writing two
/// signatures that are called a few times per scan, is a bad trade. SYSLIB1054
/// will keep suggesting otherwise; the suggestion is declined knowingly.
/// </remarks>
public static class PathResolver
{
    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareAll = 0x00000007;      // read | write | delete
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;   // required to open a directory
    private const uint VolumeNameDos = 0x0;

    // DefaultDllImportSearchPaths is not decoration: without it the loader may
    // pick up a kernel32 sitting next to the executable. For a tool that runs
    // elevated and deletes files, that is a hijack, not a nuisance (CA5392).
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true,
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle handle, [Out] char[] buffer, uint bufferLength, uint flags);

    /// <summary>
    /// Where the path really leads. Opens WITHOUT FILE_FLAG_OPEN_REPARSE_POINT so
    /// the kernel follows any link for us, then asks the handle where it ended up.
    /// </summary>
    public static bool TryResolveFinalPath(string path, out string resolved, out string? error)
    {
        using var handle = OpenForVerification(path);

        if (handle.IsInvalid)
        {
            resolved = string.Empty;
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        return TryResolveFromHandle(handle, out resolved, out error);
    }

    internal static bool TryResolveFromHandle(SafeFileHandle handle, out string resolved, out string? error)
    {
        var buffer = new char[1024];
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, VolumeNameDos);

        if (length == 0)
        {
            resolved = string.Empty;
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        if (length > buffer.Length)
        {
            // Documented contract: on overflow the return value is the required
            // size INCLUDING the terminator, so retry once with the real size.
            buffer = new char[length];
            length = GetFinalPathNameByHandle(handle, buffer, length, VolumeNameDos);

            if (length == 0)
            {
                resolved = string.Empty;
                error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }
        }

        var raw = new string(buffer, 0, (int)length);

        // The API always answers in extended-length form. Strip it, or every
        // comparison against a rule path fails for a reason that has nothing to
        // do with where the path points.
        resolved = raw.StartsWith(@"\\?\", StringComparison.Ordinal) ? raw[4..] : raw;
        error = null;
        return true;
    }

    /// <summary>
    /// Opens a handle the caller keeps for the whole delete window. Sharing is
    /// wide open on purpose: this handle exists to pin the identity of the
    /// object, not to lock anyone else out of it.
    /// </summary>
    internal static SafeFileHandle OpenForVerification(string path) => CreateFile(
        path, FileReadAttributes, FileShareAll, nint.Zero,
        OpenExisting, BackupSemantics, nint.Zero);
}
