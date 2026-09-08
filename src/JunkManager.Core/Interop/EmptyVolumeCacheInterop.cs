using System.Runtime.InteropServices;
using JunkManager.Core.Sources.VolumeCache;

namespace JunkManager.Core.Interop;

/// <summary>
/// The legacy disk cleanup handler interface. Method order below IS the vtable
/// order and must not be rearranged: it was confirmed by calling through it on
/// all 32 handlers of a live machine on 05.09.2026, and a reordering compiles
/// cleanly while calling the wrong function pointer.
/// </summary>
[ComImport]
[Guid("8FCE5227-04DA-11d1-A004-00805F8ABE06")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEmptyVolumeCache
{
    [PreserveSig]
    int Initialize(nint hkRegKey, [MarshalAs(UnmanagedType.LPWStr)] string volume,
        out nint displayName, out nint description, ref int flags);

    [PreserveSig]
    int GetSpaceUsed(out ulong spaceUsed,
        [MarshalAs(UnmanagedType.Interface)] IEmptyVolumeCacheCallBack callback);

    [PreserveSig]
    int Purge(ulong spaceToFree,
        [MarshalAs(UnmanagedType.Interface)] IEmptyVolumeCacheCallBack callback);

    [PreserveSig]
    int ShowProperties(nint hwnd);

    [PreserveSig]
    int Deactivate(out int flags);
}

/// <summary>
/// Verified: only 10 of 32 handlers on a Windows 11 26100 machine expose this.
/// The other 22 answer E_NOINTERFACE, so the fallback to
/// <see cref="IEmptyVolumeCache"/> is the normal path, not the exotic one.
/// </summary>
[ComImport]
[Guid("02B7E3BA-4DB3-11D2-B2D9-00C04F8EEC8C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEmptyVolumeCache2
{
    [PreserveSig]
    int Initialize(nint hkRegKey, [MarshalAs(UnmanagedType.LPWStr)] string volume,
        out nint displayName, out nint description, ref int flags);

    [PreserveSig]
    int GetSpaceUsed(out ulong spaceUsed,
        [MarshalAs(UnmanagedType.Interface)] IEmptyVolumeCacheCallBack callback);

    [PreserveSig]
    int Purge(ulong spaceToFree,
        [MarshalAs(UnmanagedType.Interface)] IEmptyVolumeCacheCallBack callback);

    [PreserveSig]
    int ShowProperties(nint hwnd);

    [PreserveSig]
    int Deactivate(out int flags);

    /// <remarks>
    /// The name is not ours to choose. CA1711 asks for "Initialize2" instead,
    /// and taking that advice would make the declaration stop matching the
    /// documented interface, which is the only way a reader can check that the
    /// vtable order above is right.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming", "CA1711:Identifiers should not have incorrect suffix",
        Justification =
            "InitializeEx is the name Windows gives this vtable slot. A declaration " +
            "that renames a COM method hides the only thing worth verifying here: " +
            "that the managed declaration matches the documented interface slot for slot.")]
    [PreserveSig]
    int InitializeEx(nint hkRegKey,
        [MarshalAs(UnmanagedType.LPWStr)] string volume,
        [MarshalAs(UnmanagedType.LPWStr)] string keyName,
        out nint displayName, out nint description, out nint buttonText, ref int flags);
}

[ComImport]
[Guid("6E793361-73C6-11D0-8469-00AA00442901")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEmptyVolumeCacheCallBack
{
    [PreserveSig]
    int ScanProgress(ulong spaceUsed, int flags,
        [MarshalAs(UnmanagedType.LPWStr)] string? status);

    [PreserveSig]
    int PurgeProgress(ulong spaceFreed, ulong spaceToFree, int flags,
        [MarshalAs(UnmanagedType.LPWStr)] string? status);
}

/// <summary>
/// Passing null for this is not an option. Verified 05.09.2026: BranchCache
/// dereferences the callback without a null check and takes the process down
/// with an AccessViolationException, which is not catchable. Cancellation also
/// lives here, because E_ABORT from the callback is the only documented way to
/// stop a handler that is already scanning.
/// </summary>
public sealed class VolumeCacheProgress(CancellationToken ct, IProgress<string>? report)
    : IEmptyVolumeCacheCallBack
{
    private const int EAbort = unchecked((int)0x80004004);

    public int ScanProgress(ulong spaceUsed, int flags, string? status)
    {
        report?.Report(status ?? string.Empty);
        return ct.IsCancellationRequested ? EAbort : 0;
    }

    public int PurgeProgress(ulong spaceFreed, ulong spaceToFree, int flags, string? status)
    {
        report?.Report(status ?? string.Empty);
        return ct.IsCancellationRequested ? EAbort : 0;
    }
}

/// <param name="Failure">
/// Null on success. Non-null means the handler was skipped with a reason, which
/// is a normal outcome: Microsoft marks the interface legacy, and a handler that
/// answers E_NOTIMPL or fails to create is a Tuesday, not an incident.
/// </param>
public sealed record VolumeCacheProbe(string KeyName, long SpaceUsedBytes, int Flags, string? Failure);

/// <remarks>
/// DllImport rather than LibraryImport throughout, same trade as everywhere else
/// in this solution: the generator emits unsafe code and demands
/// AllowUnsafeBlocks across the whole project. DefaultDllImportSearchPaths with
/// System32 is mandatory (CA5392): without it the loader may pick up an ole32 or
/// advapi32 sitting next to the executable.
/// </remarks>
public static class EmptyVolumeCacheInterop
{
    private const uint ClsctxServer = 0x5; // INPROC_SERVER | LOCAL_SERVER
    private const int SFalse = 1;
    private const int KeyRead = 0x20019;

    // Verified return values from the live sweep: EVCF_DONTSHOWIFZERO is the one
    // that actually matters, the rest are settings-button plumbing we do not use.
    internal const int EvcfDontShowIfZero = 0x0010;

    private static readonly Guid IidEvc = new("8FCE5227-04DA-11d1-A004-00805F8ABE06");
    private static readonly Guid IidEvc2 = new("02B7E3BA-4DB3-11D2-B2D9-00C04F8EEC8C");
    private static readonly nint HkeyLocalMachine = unchecked((nint)(int)0x80000002);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CoCreateInstance(
        in Guid clsid, nint outer, uint context, in Guid iid, out nint result);

    [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExW",
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RegOpenKeyEx(
        nint key, string subKey, int options, int rights, out nint result);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RegCloseKey(nint key);

    /// <summary>
    /// Asks one handler how much it can free. Read only: Purge lives in
    /// JunkManager.Deletion and nowhere else.
    /// </summary>
    public static VolumeCacheProbe Probe(VolumeCacheEntry entry, CancellationToken ct) =>
        Probe(entry, ct, null);

    public static VolumeCacheProbe Probe(
        VolumeCacheEntry entry, CancellationToken ct, IProgress<string>? report)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var subKey = $@"{VolumeCacheCatalog.RegistryPath}\{entry.KeyName}";

        if (RegOpenKeyEx(HkeyLocalMachine, subKey, 0, KeyRead, out var hkey) != 0)
        {
            return new VolumeCacheProbe(entry.KeyName, 0, 0, "ключ обработчика не открывается");
        }

        try
        {
            return ProbeWithKey(entry, hkey, ct, report);
        }
        catch (COMException ex)
        {
            // A handler failing is expected. Taking the scan down with it is not.
            return new VolumeCacheProbe(entry.KeyName, 0, 0, $"обработчик отказал: {ex.Message}");
        }
        finally
        {
            // The close code is deliberately discarded, and CA1806 is answered
            // by the discard rather than by a suppression: a failure to close a
            // read-only handle during unwinding is not actionable, and throwing
            // out of a finally block would replace a useful probe result with a
            // useless exception.
            _ = RegCloseKey(hkey);
        }
    }

    private static VolumeCacheProbe ProbeWithKey(
        VolumeCacheEntry entry, nint hkey, CancellationToken ct, IProgress<string>? report)
    {
        var hr = CoCreateInstance(entry.Clsid, nint.Zero, ClsctxServer, IidEvc2, out var ptr);
        var hasEvc2 = hr == 0;

        if (!hasEvc2)
        {
            hr = CoCreateInstance(entry.Clsid, nint.Zero, ClsctxServer, IidEvc, out ptr);
        }

        if (hr != 0)
        {
            return new VolumeCacheProbe(
                entry.KeyName, 0, 0, $"объект не создаётся: 0x{hr:X8}");
        }

        try
        {
            var callback = new VolumeCacheProgress(ct, report);
            var flags = 0;
            int hrInit;
            ulong used;
            int hrUsed;

            if (hasEvc2)
            {
                var handler = (IEmptyVolumeCache2)Marshal.GetTypedObjectForIUnknown(
                    ptr, typeof(IEmptyVolumeCache2));
                hrInit = handler.InitializeEx(
                    hkey, VolumeCacheCatalog.SystemVolume, entry.KeyName, out _, out _, out _, ref flags);

                if (hrInit != 0)
                {
                    return Refused(entry, flags, hrInit);
                }

                hrUsed = handler.GetSpaceUsed(out used, callback);
            }
            else
            {
                var handler = (IEmptyVolumeCache)Marshal.GetTypedObjectForIUnknown(
                    ptr, typeof(IEmptyVolumeCache));
                hrInit = handler.Initialize(hkey, VolumeCacheCatalog.SystemVolume, out _, out _, ref flags);

                if (hrInit != 0)
                {
                    return Refused(entry, flags, hrInit);
                }

                hrUsed = handler.GetSpaceUsed(out used, callback);
            }

            if (hrUsed != 0)
            {
                return new VolumeCacheProbe(
                    entry.KeyName, 0, flags, $"подсчёт не удался: 0x{hrUsed:X8}");
            }

            // The interface reports an unsigned 64-bit count. Anything above
            // long.MaxValue is nonsense from a broken handler, not a real disk.
            var bytes = used > long.MaxValue ? 0L : (long)used;
            return new VolumeCacheProbe(entry.KeyName, bytes, flags, null);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    private static VolumeCacheProbe Refused(VolumeCacheEntry entry, int flags, int hrInit) =>
        new(entry.KeyName, 0, flags,
            hrInit == SFalse
                ? "обработчик сообщил, что удалять нечего"
                : $"инициализация не удалась: 0x{hrInit:X8}");
}
