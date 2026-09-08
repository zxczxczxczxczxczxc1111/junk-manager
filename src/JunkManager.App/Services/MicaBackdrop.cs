using System.Runtime.InteropServices;

namespace JunkManager.App.Services;

/// <summary>
/// Asks the desktop window manager for the Mica backdrop, and shrugs when it
/// says no.
/// </summary>
/// <remarks>
/// <para>
/// DllImport rather than LibraryImport, for the same reason as
/// <c>JunkManager.Safety.Native.PathResolver</c>: the source generator needs
/// AllowUnsafeBlocks across the whole project, and one signature is not worth
/// turning pointer arithmetic back on. SYSLIB1054 will keep suggesting
/// otherwise, and the suggestion is declined knowingly.
/// </para>
/// <para>
/// Windows 10 has neither attribute and returns a failure HRESULT for both. The
/// window already carries a solid <c>VoidBrush</c> background, so the failure
/// path is the design and not a fallback bolted on afterwards.
/// </para>
/// </remarks>
internal static class MicaBackdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtMainWindow = 2;   // Mica

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    /// <summary>
    /// Returns true when the backdrop was actually applied. The caller changes
    /// nothing on false: saying so is for the report, not for a second attempt.
    /// </summary>
    public static bool TryApply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        // Dark mode first. The order matters: setting the backdrop on a window
        // the manager still considers light paints a light Mica sheet under a
        // dark application, and the seam shows along the title bar.
        var tyomnaya = 1;
        var kodTemy = DwmSetWindowAttribute(
            hwnd, DwmwaUseImmersiveDarkMode, ref tyomnaya, sizeof(int));

        var vid = DwmsbtMainWindow;
        var kodFona = DwmSetWindowAttribute(
            hwnd, DwmwaSystemBackdropType, ref vid, sizeof(int));

        return kodTemy == 0 && kodFona == 0;
    }
}
