using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using JunkManager.Core.Apps;
using JunkManager.Safety;

namespace JunkManager.Deletion;

public sealed class UninstallRunner
{
    private static readonly ConcurrentDictionary<string, Task<UninstallProcessExit>> Active = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOperationLog _log;
    private readonly IProgramInventory _inventory;
    private readonly Action? _testGuard;
    private readonly IUninstallProcessTreeReader _processTree;

    public UninstallRunner(IOperationLog log, Action? fuse = null)
        : this(log, new ProgramInventory()) => _testGuard = fuse;

    public UninstallRunner(IOperationLog log, IProgramInventory inventory, IUninstallProcessTreeReader? processTree = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(inventory);
        _log = log;
        _inventory = inventory;
        _processTree = processTree ?? new WindowsUninstallProcessTreeReader();
    }

    public static bool IsRunning(string programId) => Active.TryGetValue(programId, out var task)
        && (!task.IsCompletedSuccessfully || !task.Result.TreeVerified);

    public static bool CanAssociateChild(DateTime parentStartedUtc, DateTime? parentExitedUtc, DateTime childStartedUtc) =>
        childStartedUtc >= parentStartedUtc && (parentExitedUtc is null || childStartedUtc <= parentExitedUtc.Value);

    public static UninstallExitStatus ClassifyExitCode(int code, InstallerKind installer) =>
        code == 0 ? UninstallExitStatus.Success : installer != InstallerKind.Msi ? UninstallExitStatus.Failed : code switch
        {
            1602 => UninstallExitStatus.Cancelled,
            1605 => UninstallExitStatus.AlreadyAbsent,
            1618 => UninstallExitStatus.Busy,
            1641 => UninstallExitStatus.RebootInitiated,
            3010 => UninstallExitStatus.RebootRequired,
            _ => UninstallExitStatus.Failed,
        };

    public Task<UninstallResult> RunAsync(InstalledProgram program, UninstallCommand command,
        TimeSpan timeout, bool razreshitOkno, CancellationToken ct) =>
        RunAsync(program, command, new UninstallOptions { ObservationWindow = timeout }, razreshitOkno, null, ct);

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "UninstallProcess transfers ownership to its ObserveAsync finally. UI cancellation must not dispose live process handles; its Completion fault is explicitly observed below.")]
    public async Task<UninstallResult> RunAsync(InstalledProgram program, UninstallCommand command,
        UninstallOptions options, bool allowWindow, IProgress<UninstallProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);
        _testGuard?.Invoke();
        Validate(options);
        if (ct.IsCancellationRequested) { return await Record(program, new(program.Id, UninstallOutcome.Cancelled, null, 0, "очередь отменена до запуска")).ConfigureAwait(false); }
        if ((!command.Quiet && !allowWindow) || (command.WinGet is null && !Path.IsPathFullyQualified(command.Executable)))
        {
            return await Record(program, new(program.Id, UninstallOutcome.Refused, null, 0,
                "для деинсталлятора нужно разрешение на окно и полный путь")).ConfigureAwait(false);
        }

        if (command.WinGet is not null && (!WinGetUninstallRequest.TryCreate(program, out var expected, out _)
                || expected != command.WinGet))
        {
            return await Record(program, new(program.Id, UninstallOutcome.Refused, null, 0,
                "WinGet: выбранная программа не совпадает с запросом удаления")).ConfigureAwait(false);
        }
        if ((program.Scope is ProgramScope.User or ProgramScope.User32 or ProgramScope.Msix)
            && (!ProgramInventory.CanReadCurrentUser(out _) || (program.UserSid is not null
                && !string.Equals(program.UserSid, Elevation.CurrentSid, StringComparison.Ordinal))))
        {
            return await Record(program, new(program.Id, UninstallOutcome.Refused, null, 0,
                "программа принадлежит другому пользователю; запустите удаление из его сеанса")).ConfigureAwait(false);
        }

        var reservation = new TaskCompletionSource<UninstallProcessExit>(TaskCreationOptions.RunContinuationsAsynchronously);
        while (!Active.TryAdd(program.Id, reservation.Task))
        {
            if (Active.TryGetValue(program.Id, out var existing) && IsRunning(program.Id))
            {
                return await Record(program, new(program.Id, UninstallOutcome.Refused, null, 0,
                    "деинсталлятор ещё работает или остановка его дочерних процессов не подтверждена; повторный запуск заблокирован")).ConfigureAwait(false);
            }
            if (existing is not null) { Active.TryRemove(new KeyValuePair<string, Task<UninstallProcessExit>>(program.Id, existing)); }
        }

        (int? Id, Task<UninstallProcessExit> Completion) operation;
        try
        {
            if (command.WinGet is { } request)
            { operation = (null, WinGetUserWorker.NeedsWorker(request.Scope)
                ? WinGetUserWorker.StartAsync(program) : WinGetUninstaller.RunAsync(program, request)); }
            else
            {
                var process = await UninstallProcess.StartAsync(command, _processTree).ConfigureAwait(false);
                operation = (process.Id, process.Completion);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            reservation.TrySetResult(new(-1, ex.Message, true));
            return await Record(program, new(program.Id, UninstallOutcome.Failed, null, 0,
                "деинсталлятор не запустился: " + ex.Message)).ConfigureAwait(false);
        }
        Active[program.Id] = operation.Completion;
        _ = operation.Completion.ContinueWith(static failed =>
        {
            // A detached observer still owns its exceptions; the UI cancellation is not an amnesty.
            Trace.TraceError(failed.Exception?.ToString());
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        var timer = Stopwatch.StartNew();
        var nextQuestion = options.ObservationWindow;
        var confirmationStarted = TimeSpan.MaxValue;
        int? exitCode = null;
        var classification = UninstallExitStatus.Unknown;
        string? reason = null;
        var outcome = UninstallOutcome.Unconfirmed;
        var treeVerified = false;

        while (true)
        {
            var update = new UninstallProgress(program.Id, command.Executable, operation.Id, timer.Elapsed, operation.Completion.IsCompleted);
            progress?.Report(update);
            if (ct.IsCancellationRequested)
            {
                outcome = operation.Completion.IsCompleted ? UninstallOutcome.Unconfirmed : UninstallOutcome.StillRunning;
                reason = "наблюдение и очередь отменены; процесс установщика не остановлен, удаление не подтверждено";
                break;
            }
            if (timer.Elapsed >= options.MaximumObservation)
            {
                outcome = operation.Completion.IsCompleted ? UninstallOutcome.Unconfirmed : UninstallOutcome.TimedOut;
                reason = "время наблюдения истекло; установщик не остановлен, удаление не подтверждено";
                break;
            }
            if (timer.Elapsed >= nextQuestion && !operation.Completion.IsCompleted)
            {
                using var questionTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                questionTimeout.CancelAfter(Remaining(options.MaximumObservation, timer.Elapsed));
                bool keepWaiting;
                try
                {
                    keepWaiting = options.ContinueWaitingAsync is not null
                        && await options.ContinueWaitingAsync(update, questionTimeout.Token)
                            .WaitAsync(questionTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (questionTimeout.IsCancellationRequested) { keepWaiting = false; }
                if (!keepWaiting)
                {
                    outcome = UninstallOutcome.StillRunning;
                    reason = "деинсталлятор ещё работает; наблюдение закончено, удаление не подтверждено";
                    break;
                }
                nextQuestion = options.MaximumObservation;
            }
            if (operation.Completion.IsCompleted)
            {
                try
                {
                    var exit = await operation.Completion.ConfigureAwait(false);
                    exitCode = exit.ExitCode;
                    classification = ClassifyExitCode(exit.ExitCode, program.Installer);
                    reason = string.IsNullOrWhiteSpace(exit.Error) ? null : exit.Error;
                    treeVerified = exit.TreeVerified;
                    if (!treeVerified)
                    {
                        reason = "дерево деинсталлятора проверено не полностью; удаление не подтверждено: " + exit.Error;
                        break;
                    }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
                {
                    reason = "наблюдение за процессом не завершено: " + ex.Message;
                    break;
                }
                if (classification is UninstallExitStatus.Failed or UninstallExitStatus.Cancelled or UninstallExitStatus.Busy)
                {
                    outcome = classification == UninstallExitStatus.Cancelled ? UninstallOutcome.Cancelled :
                        classification == UninstallExitStatus.Busy ? UninstallOutcome.Busy : UninstallOutcome.Failed;
                    reason = classification == UninstallExitStatus.Cancelled ? "отмена в окне деинсталлятора" :
                        classification == UninstallExitStatus.Busy ? "Windows Installer занят другой операцией" :
                        $"код деинсталлятора {exitCode}: {reason}";
                    break;
                }
                if (confirmationStarted == TimeSpan.MaxValue) { confirmationStarted = timer.Elapsed; }
                ProgramPresenceResult presence;
                using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                probeTimeout.CancelAfter(Remaining(options.MaximumObservation, timer.Elapsed));
                try { presence = await _inventory.ProbeAsync(program, probeTimeout.Token).WaitAsync(probeTimeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (probeTimeout.IsCancellationRequested)
                {
                    reason = "проверка регистрации отменена; удаление не подтверждено";
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    reason = "регистрация не проверена: " + ex.Message;
                    break;
                }
                if (presence.Presence == ProgramPresence.Absent)
                {
                    outcome = classification == UninstallExitStatus.AlreadyAbsent ? UninstallOutcome.AlreadyAbsent : UninstallOutcome.Removed;
                    reason = classification is UninstallExitStatus.RebootRequired or UninstallExitStatus.RebootInitiated
                        ? "регистрация удалена; требуется перезагрузка" : null;
                    break;
                }
                reason = presence.Presence == ProgramPresence.Present
                    ? "деинсталлятор завершился, но запись программы ещё присутствует" : presence.Reason;
                if (timer.Elapsed - confirmationStarted >= options.ConfirmationWindow) { break; }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None).ConfigureAwait(false);
        }

        return await Record(program, new(program.Id, outcome, exitCode, 0, reason)
        {
            RemovalConfirmed = outcome is UninstallOutcome.Removed or UninstallOutcome.AlreadyAbsent,
            RebootRequired = classification is UninstallExitStatus.RebootRequired or UninstallExitStatus.RebootInitiated,
            RebootInitiated = classification == UninstallExitStatus.RebootInitiated,
            QueueCancelled = ct.IsCancellationRequested, ProcessId = operation.Id,
            Executable = command.Executable, Elapsed = timer.Elapsed,
            // The API confirms its transaction; it does not lend us a fictitious process tree.
            ProcessTreeVerified = treeVerified && command.WinGet is null,
        }).ConfigureAwait(false);
    }

    public async Task<UninstallQueueResult> RunQueueAsync(IReadOnlyList<UninstallRequest> requests,
        UninstallOptions options, IProgress<UninstallProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var results = new List<UninstallResult>();
        foreach (var request in requests)
        {
            if (ct.IsCancellationRequested) { break; }
            var result = request.Program.Installer == InstallerKind.Msix
                ? await RunMsixAsync(request.Program, options, progress, ct).ConfigureAwait(false)
                : request.Command is not null
                    ? await RunAsync(request.Program, request.Command, options, request.AllowWindow, progress, ct).ConfigureAwait(false)
                    : await Record(request.Program, new(request.Program.Id, UninstallOutcome.Refused, null, 0, "команда удаления не подготовлена")).ConfigureAwait(false);
            results.Add(result);
            if (result.Outcome is UninstallOutcome.StillRunning or UninstallOutcome.TimedOut or UninstallOutcome.Cancelled or UninstallOutcome.Busy
                || result.QueueCancelled) { break; }
        }
        return new(results, ct.IsCancellationRequested || results.Any(r => r.QueueCancelled
            || r.Outcome is UninstallOutcome.StillRunning or UninstallOutcome.TimedOut or UninstallOutcome.Cancelled));
    }

    public Task<UninstallResult> RunMsixAsync(InstalledProgram program, UninstallOptions options,
        IProgress<UninstallProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.Installer != InstallerKind.Msix || string.IsNullOrWhiteSpace(program.PackageFullName))
        {
            return Record(program, new(program.Id, UninstallOutcome.Refused, null, 0, "нет точного имени MSIX пакета"));
        }
        var command = new UninstallCommand(MsixPackageReader.PowerShellExecutable,
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(MsixRemovalScript))], true)
        { StandardInput = JsonSerializer.Serialize(new { FullName = program.PackageFullName }) };
        return RunAsync(program, command, options, false, progress, ct);
    }

    private static void Validate(UninstallOptions options)
    {
        if (options.ObservationWindow <= TimeSpan.Zero || options.MaximumObservation <= TimeSpan.Zero
            || options.MaximumObservation > TimeSpan.FromMinutes(30) || options.ConfirmationWindow < TimeSpan.Zero)
        { throw new ArgumentOutOfRangeException(nameof(options), "наблюдение должно быть положительным и не более 30 минут"); }
    }

    private static TimeSpan Remaining(TimeSpan maximum, TimeSpan elapsed) =>
        maximum > elapsed ? maximum - elapsed : TimeSpan.Zero;

    private async Task<UninstallResult> Record(InstalledProgram program, UninstallResult result)
    {
        var status = result.Outcome switch
        {
            UninstallOutcome.Removed or UninstallOutcome.AlreadyAbsent => DeleteStatus.Deleted,
            UninstallOutcome.Refused => DeleteStatus.Skipped,
            UninstallOutcome.Cancelled => DeleteStatus.Cancelled,
            _ => DeleteStatus.Failed,
        };
        await _log.RecordAsync(new(program.InstallLocation ?? program.Id, status, result.BytesFreed,
            result.Reason), CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    private const string MsixRemovalScript = """
        # The package name is JSON data. Turning it into code would be an avoidable circus.
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        [Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        try {
            $request = [Console]::In.ReadToEnd() | ConvertFrom-Json -ErrorAction Stop
            if ([string]::IsNullOrWhiteSpace($request.FullName)) { throw 'Missing package identity' }
            $package = @(Get-AppxPackage -PackageTypeFilter Main,Framework,Resource,Bundle,Optional -ErrorAction Stop |
                Where-Object { $_.PackageFullName -ceq $request.FullName })
            if ($package.Count -eq 0) { exit 0 }
            if ($package.Count -ne 1) { throw 'Package identity is ambiguous' }
            $p = $package[0]
            if ($null -eq $p.PSObject.Properties['NonRemovable'] -or $p.NonRemovable -or $p.IsFramework -or $p.IsResourcePackage -or
                [string]$p.SignatureKind -eq 'System' -or $p.Name -in @('Microsoft.WindowsStore', 'Microsoft.DesktopAppInstaller')) {
                throw 'Protected system package or shared dependency'
            }
            Remove-AppxPackage -Package $p.PackageFullName -ErrorAction Stop
            exit 0
        } catch {
            [Console]::Error.WriteLine($_.Exception.ToString())
            exit 1
        }
        """;
}
