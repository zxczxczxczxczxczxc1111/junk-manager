using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using JunkManager.Core.Apps;
using JunkManager.Safety;
using Microsoft.Win32.SafeHandles;

namespace JunkManager.Deletion;

public static class WinGetUserWorker
{
    private const string Switch = "--winget-user-worker";
    private const string Prefix = "JunkManager-WinGet-";
    internal static bool NeedsWorker(ProgramScope scope) => RequiresLimitedUser(scope)
        || !string.Equals(Path.GetDirectoryName(Environment.ProcessPath),
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase);

    private static bool RequiresLimitedUser(ProgramScope scope)
    {
        if (scope is not (ProgramScope.User or ProgramScope.User32) || !Elevation.IsElevated) return false;
        using var identity = WindowsIdentity.GetCurrent();
        if (!GetTokenInformation(identity.AccessToken, 18, out var elevationType, sizeof(int), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        // A natural administrator with UAC disabled has no limited token, and WinGet permits that context.
        return elevationType == 2;
    }
    public static bool IsRequested(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Count > 0 && args[0] == Switch;
    }

    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        // The worker never elevates itself. Invitations are not administrator costumes.
        if (args.Count != 3 || args[0] != Switch
            || !Guid.TryParseExact(args[1], "N", out _) || !uint.TryParse(args[2], out var parent)) return 2;
        try
        {
            using var pipe = new NamedPipeClientStream(".", Prefix + args[1], PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(30_000).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var actualParent) || actualParent != parent) return 2;
            var program = await ReadAsync<InstalledProgram>(pipe, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            if (RequiresLimitedUser(program.Scope)
                || (program.UserSid is not null && program.UserSid != Elevation.CurrentSid)
                || !ProgramInventory.CanReadCurrentUser(out _)
                || !WinGetUninstallRequest.TryCreate(program, out var request, out _)) return 2;
            var result = await WinGetUninstaller.RunAsync(program, request!).ConfigureAwait(false);
            await WriteAsync(pipe, result).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException
            or OperationCanceledException or JsonException or InvalidOperationException or Win32Exception or System.Security.SecurityException)
        {
            Trace.TraceError(ex.ToString());
            return 3;
        }
    }

    internal static async Task<UninstallProcessExit> StartAsync(InstalledProgram program)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User ?? throw new InvalidOperationException("нет SID текущего пользователя");
            // CurrentUserOnly also compares elevation levels, which would lock out our own limited worker.
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
            var nonce = Guid.NewGuid().ToString("N");
            using var pipe = NamedPipeServerStreamAcl.Create(Prefix + nonce, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
            var exe = WorkerPath();
            using var worker = RequiresLimitedUser(program.Scope)
                ? StartLimited(identity, nonce, exe) : StartCurrent(nonce, exe);
            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await pipe.WaitForConnectionAsync(connectTimeout.Token).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var client) || client != worker.Id)
                throw new InvalidOperationException("подключился другой процесс; запрос удаления не передан");
            await WriteAsync(pipe, program).ConfigureAwait(false);
            // No observation cancellation is passed here: the user installer still owns its transaction.
            // The authenticated reply confirms the API transaction. The runner separately probes the registration.
            return await ReadAsync<UninstallProcessExit>(pipe, Timeout.InfiniteTimeSpan).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException
            or OperationCanceledException or JsonException or InvalidOperationException or Win32Exception or System.Security.SecurityException)
        {
            return new(-1, "WinGet: не удалось выполнить удаление с правами пользователя: " + ex.Message, true);
        }
    }

    private static string WorkerPath()
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("путь приложения недоступен");
        var directory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var exe = string.Equals(Path.GetDirectoryName(current), directory, StringComparison.OrdinalIgnoreCase)
            ? current : Path.Combine(directory, "JunkManager.WinGetHost.exe");
        if (!Path.IsPathFullyQualified(exe) || !File.Exists(exe)
            || exe.Contains('"', StringComparison.Ordinal) || exe.Any(char.IsControl))
            throw new InvalidOperationException("внутренний помощник WinGet недоступен; скачай приложение заново");
        if (!string.Equals(current, exe, StringComparison.OrdinalIgnoreCase))
        {
            // The single-file host consumes these libraries; its extracted child is less omnivorous.
            foreach (var source in Directory.EnumerateFiles(Path.Combine(directory, "WinGetRuntime")))
            {
                var name = Path.GetFileName(source);
                var destination = Path.Combine(directory, name);
                var replace = name.EndsWith(".deps.json", StringComparison.Ordinal);
                if (replace || !File.Exists(destination))
                {
                    var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.Copy(source, staging, false);
                        try { File.Move(staging, destination, replace); }
                        catch (IOException) when (!replace && File.Exists(destination))
                        { /* Another worker completed the same immutable copy. Never expose a half-written DLL. */ }
                    }
                    finally { File.Delete(staging); }
                }
            }
        }
        return exe;
    }

    private static Process StartCurrent(string nonce, string exe)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(Switch);
        info.ArgumentList.Add(nonce);
        info.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Process.Start(info) ?? throw new InvalidOperationException("помощник WinGet не запустился");
    }

    private static Process StartLimited(WindowsIdentity identity, string nonce, string exe)
    {
        var window = GetShellWindow();
        if (window == nint.Zero || GetWindowThreadProcessId(window, out var shellId) == 0)
            throw new InvalidOperationException("открой приложение в обычном сеансе Windows: рабочий стол недоступен");
        using var shell = OpenProcess(0x1080, false, shellId);
        // WindowsPrincipal duplicates primary tokens to inspect group membership; QUERY alone is insufficient.
        if (shell.IsInvalid || !OpenProcessToken(shell, 10, out var shellToken))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        using (shellToken)
        using (var owner = new WindowsIdentity(shellToken.DangerousGetHandle()))
        {
            if (owner.User != identity.User || new WindowsPrincipal(owner).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("рабочий стол принадлежит другому пользователю или запущен с повышенными правами");
        }
        var command = ('"' + exe + "\" " + Switch + " " + nonce + " "
            + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + '\0').ToCharArray();
        // Windows documents inheriting the shell's token through the parent attribute. No token surgery.
        nuint bytes = 0;
        _ = InitializeProcThreadAttributeList(nint.Zero, 1, 0, ref bytes);
        if (bytes == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var attributes = Marshal.AllocHGlobal(checked((nint)bytes));
        var initialized = false;
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes)) throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;
            var parent = shell.DangerousGetHandle();
            if (!UpdateProcThreadAttribute(attributes, 0, 0x00020000, ref parent, (nuint)nint.Size, nint.Zero, nint.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var startup = new StartupInfoEx { Info = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>() }, Attributes = attributes };
            if (!CreateProcessW(exe, command, nint.Zero, nint.Zero, false, 0x08080000, nint.Zero,
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try { return Process.GetProcessById((int)info.ProcessId); }
            finally { CloseHandle(info.Thread); CloseHandle(info.Process); }
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(attributes);
        }
    }

    private static async Task WriteAsync<T>(Stream pipe, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 65_536) throw new InvalidOperationException("слишком большой запрос WinGet");
        await pipe.WriteAsync(BitConverter.GetBytes(bytes.Length)).ConfigureAwait(false);
        await pipe.WriteAsync(bytes).ConfigureAwait(false);
        await pipe.FlushAsync().ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(Stream pipe, TimeSpan timeout)
    {
        using var limit = new CancellationTokenSource(timeout);
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header, limit.Token).ConfigureAwait(false);
        var length = BitConverter.ToInt32(header);
        if (length is <= 0 or > 65_536) throw new InvalidOperationException("неверная длина ответа WinGet");
        var bytes = new byte[length];
        await pipe.ReadExactlyAsync(bytes, limit.Token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new JsonException("пустое сообщение WinGet");
    }

    // Native layout is dull until one pointer turns the desktop into modern art.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        internal int Size;
        internal nint Reserved, Desktop, Title;
        internal uint X, Y, Width, Height, XChars, YChars, Fill, Flags;
        internal ushort Show, ReservedSize;
        internal nint ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo { internal nint Process, Thread; internal uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx { internal StartupInfo Info; internal nint Attributes; }

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, out int value, int size, out int returned);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application,
        [In, Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] command,
        nint processAttributes, nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint flags, nint environment, string directory, ref StartupInfoEx startup, out ProcessInfo info);
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetShellWindow();
    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint id);
    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, ref nint value, nuint size, nint previous, nint returned);
    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
