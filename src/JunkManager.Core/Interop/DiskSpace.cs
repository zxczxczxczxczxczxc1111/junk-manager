using System.Runtime.InteropServices;

namespace JunkManager.Core.Interop;

/// <summary>
/// GetDiskFreeSpaceEx rather than DriveInfo. They disagree when quotas are on:
/// DriveInfo.AvailableFreeSpace reports what the current user may still write,
/// and the explainer needs what the volume actually holds.
/// </summary>
/// <remarks>
/// DllImport rather than LibraryImport, and the reason is written down in
/// JunkManager.Safety/Native/PathResolver.cs: the source generator emits unsafe
/// code for any signature carrying a string and demands AllowUnsafeBlocks on the
/// whole project. A solution that deletes files does not buy that to save
/// hand-writing one signature. DefaultDllImportSearchPaths is not decoration
/// either: without it the loader may pick up a kernel32 sitting next to the
/// executable (CA5392).
/// </remarks>
public static class DiskSpace
{
    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true,
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(
        string directoryName, out ulong freeBytesAvailable,
        out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes);

    public static bool TryGetVolume(string root, out long totalBytes, out long freeBytes)
    {
        totalBytes = 0;
        freeBytes = 0;

        if (!GetDiskFreeSpaceEx(root, out _, out var total, out var free))
        {
            return false;
        }

        totalBytes = total > long.MaxValue ? long.MaxValue : (long)total;
        freeBytes = free > long.MaxValue ? long.MaxValue : (long)free;
        return true;
    }
}
