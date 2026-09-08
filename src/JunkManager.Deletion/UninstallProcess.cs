using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using JunkManager.Core.Apps;
using Microsoft.Win32.SafeHandles;

namespace JunkManager.Deletion;

/// <summary>Owns pipes and process handles until the installer and observed children finish.</summary>
public sealed record UninstallProcessTreeEntry(int Id, int ParentId);
public sealed record UninstallProcessTreeSnapshot(IReadOnlyList<UninstallProcessTreeEntry> Entries, string? Error = null);
public interface IUninstallProcessTreeReader { UninstallProcessTreeSnapshot Read(); }
public sealed class WindowsUninstallProcessTreeReader : IUninstallProcessTreeReader
{
    public UninstallProcessTreeSnapshot Read() => UninstallProcess.ReadSystemTree();
}

internal sealed record UninstallProcessExit(int ExitCode, string Error, bool TreeVerified);

internal sealed class UninstallProcess : IDisposable
{
    private readonly Process _root;
    private readonly Dictionary<int, Process> _tree = [];
    private readonly CancellationTokenSource _pipes = new();
    private readonly Task<string> _stdout;
    private readonly Task<string> _stderr;
    private string? _trackingError;
    private string? _inputError;
    private readonly IUninstallProcessTreeReader _reader;

    private UninstallProcess(Process root, IUninstallProcessTreeReader reader)
    {
        _root = root;
        _reader = reader;
        Id = root.Id;
        _tree.Add(Id, root);
        _stdout = DrainAsync(root.StandardOutput, _pipes.Token);
        _stderr = DrainAsync(root.StandardError, _pipes.Token);
        Completion = ObserveAsync();
    }

    public int Id { get; }
    public Task<UninstallProcessExit> Completion { get; }

    public static async Task<UninstallProcess> StartAsync(UninstallCommand command, IUninstallProcessTreeReader reader)
    {
        var psi = new ProcessStartInfo(command.Executable)
        {
            UseShellExecute = false, CreateNoWindow = command.Quiet,
            RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = command.StandardInput is not null,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        if (command.StandardInput is not null) { psi.StandardInputEncoding = new UTF8Encoding(false); }
        foreach (var argument in command.Arguments) { psi.ArgumentList.Add(argument); }
        var root = Process.Start(psi) ?? throw new InvalidOperationException("деинсталлятор не запустился");
        var operation = new UninstallProcess(root, reader);
        if (command.StandardInput is not null)
        {
            try { await root.StandardInput.WriteAsync(command.StandardInput).ConfigureAwait(false); }
            catch (IOException ex) { operation._inputError = "ввод деинсталлятора закрыт: " + ex.Message; }
            finally { root.StandardInput.Close(); }
        }
        return operation;
    }

    private async Task<UninstallProcessExit> ObserveAsync()
    {
        try
        {
            // A bootstrapper's exit is not its children's resignation letter.
            while (true)
            {
                DiscoverChildren();
                if (_tree.Values.All(p => p.HasExited))
                {
                    await Task.Delay(150).ConfigureAwait(false);
                    DiscoverChildren();
                    if (_tree.Values.All(p => p.HasExited)) { break; }
                }
                await Task.Delay(200).ConfigureAwait(false);
            }

            var code = _root.ExitCode;
            _pipes.CancelAfter(TimeSpan.FromSeconds(2));
            var output = await _stdout.ConfigureAwait(false);
            return new(code, string.Join("; ", new[] { await _stderr.ConfigureAwait(false), code != 0 ? output : null, _trackingError, _inputError }
                .Where(s => !string.IsNullOrWhiteSpace(s))), _trackingError is null);
        }
        finally
        {
            await _pipes.CancelAsync().ConfigureAwait(false);
            Dispose();
        }
    }

    public void Dispose()
    {
        // Only ObserveAsync owns this lifetime; UI cancellation never disposes a living operation.
        foreach (var process in _tree.Values) { process.Dispose(); }
        _pipes.Dispose();
    }

    private void DiscoverChildren()
    {
        var snapshot = _reader.Read();
        if (snapshot.Error is not null)
        {
            _trackingError = snapshot.Error;
            return;
        }
        var added = true;
        while (added)
        {
            added = false;
            foreach (var entry in snapshot.Entries)
            {
                if (_tree.ContainsKey(entry.Id) || !_tree.TryGetValue(entry.ParentId, out var owner)) { continue; }
                Process? child = null;
                try
                {
                    child = Process.GetProcessById(entry.Id);
                    var started = child.StartTime.ToUniversalTime();
                    if (!UninstallRunner.CanAssociateChild(owner.StartTime.ToUniversalTime(),
                        owner.HasExited ? owner.ExitTime.ToUniversalTime() : null, started)) { continue; }
                    _tree.Add(entry.Id, child);
                    child = null;
                    added = true;
                }
                catch (ArgumentException) { /* The child already left; the registry still gets the final vote. */ }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                { _trackingError = "дочерний процесс не прочитан: " + ex.Message; }
                finally { child?.Dispose(); }
            }
        }
    }

    internal static UninstallProcessTreeSnapshot ReadSystemTree()
    {
        using var snapshot = Native.CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid)
        {
            return new([], "не удалось прочитать дерево процессов: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }
        var entry = new Native.ProcessEntry { Size = (uint)Marshal.SizeOf<Native.ProcessEntry>(), Executable = string.Empty };
        var entries = new List<UninstallProcessTreeEntry>();
        if (Native.Process32First(snapshot, ref entry))
        {
            do
            {
                if (entry.ProcessId <= int.MaxValue && entry.ParentProcessId <= int.MaxValue)
                { entries.Add(new((int)entry.ProcessId, (int)entry.ParentProcessId)); }
            }
            while (Native.Process32Next(snapshot, ref entry));
        }
        var error = Marshal.GetLastWin32Error();
        return error == 18 ? new(entries) : new(entries, "перечисление процессов не завершено: " + new Win32Exception(error).Message);
    }

    private static async Task<string> DrainAsync(StreamReader stream, CancellationToken ct)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            int count;
            while ((count = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
                // Keep draining after the log cap, or the pipe becomes a very expensive cork.
                if (text.Length < 65536) { text.Append(buffer, 0, Math.Min(count, 65536 - text.Length)); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return text.ToString(); }
        return text.ToString();
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ProcessEntry
        {
            public uint Size;
            public uint Usage;
            public uint ProcessId;
            public UIntPtr DefaultHeapId;
            public uint ModuleId;
            public uint Threads;
            public uint ParentProcessId;
            public int BasePriority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
    }
}
