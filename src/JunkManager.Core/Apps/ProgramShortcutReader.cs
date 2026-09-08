using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace JunkManager.Core.Apps;

internal sealed record ProgramShortcut(string Path, string Target);

internal static class ProgramShortcutReader
{
    internal static List<ProgramShortcut> Read(IReadOnlyList<string> roots, List<SkippedItem> skipped, CancellationToken ct)
    {
        var result = new List<ProgramShortcut>();
        var pending = new Stack<string>(roots.Where(Directory.Exists));
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { continue; }
                foreach (var child in Directory.GetDirectories(directory)) { pending.Push(child); }
                foreach (var link in Directory.GetFiles(directory, "*.lnk"))
                {
                    if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) { continue; }
                    var target = ReadTarget(link);
                    if (!string.IsNullOrWhiteSpace(target)) { result.Add(new(link, target)); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
            { skipped.Add(new(directory, "ярлыки не прочитаны: " + ex.Message)); }
        }
        return result;
    }

    internal static string ReadTarget(string path)
    {
        var type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))
            ?? throw new InvalidOperationException("класс ShellLink недоступен");
        var link = Activator.CreateInstance(type) ?? throw new InvalidOperationException("ShellLink не создан");
        try
        {
            // Load, never Resolve: reading a shortcut must not repair MSI or launch anything.
            ((IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(32768);
            ((IShellLink)link).GetPath(target, target.Capacity, IntPtr.Zero, 4);
            return target.ToString();
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
    }
}
