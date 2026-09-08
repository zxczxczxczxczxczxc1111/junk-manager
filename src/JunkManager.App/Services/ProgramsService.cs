using System.IO;
using JunkManager.Core;
using JunkManager.Core.Apps;
using JunkManager.Core.Registry;
using JunkManager.Deletion;

namespace JunkManager.App.Services;

internal sealed class ProgramsService : IProgramsService
{
    private readonly ISettingsService _settings;
    private readonly IProgramInventory _inventory;
    private readonly IRegistryCleanupService _registryCleanup;

    public ProgramsService(ISettingsService settings, IProgramInventory? inventory = null, IRegistryCleanupService? registryCleanup = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _inventory = inventory ?? new ProgramInventory();
        _registryCleanup = registryCleanup ?? new RegistryCleanupService(settings);
    }

    public string CleanupNote => (_settings.Current.Mode == DeleteMode.RecycleBin
        ? "Файловые остатки отправятся в корзину." : "Файловые остатки удалятся безвозвратно.")
        + (_settings.Current.BackupRegistryBeforeCleanup
            ? " Выбранные записи реестра будут сохранены для точечного восстановления."
            : " Экспорт записей реестра выключен.")
        + " Копия удаляемой программы не создаётся.";

    public Task<ProgramInventorySnapshot> ReadAsync(CancellationToken ct) => _inventory.ReadAsync(ct);
    public Task<ProgramSizeEstimate> MeasureSizeAsync(InstalledProgram program, IReadOnlyList<InstalledProgram> owners, CancellationToken ct) =>
        ProgramSizeReader.ReadAsync(program, owners, ct);
    public Task<string> PrepareLeftoversAsync(CancellationToken ct) => _registryCleanup.ZastrahovatAsync(ct);

    public async Task<ProgramsRemovalReport> RemoveAsync(IReadOnlyList<InstalledProgram> programs, UninstallOptions options,
        IProgress<UninstallProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(programs);
        ArgumentNullException.ThrowIfNull(options);
        var results = new List<UninstallResult>();
        var leftovers = new List<ProgramLeftoverCandidate>();
        var skipped = new List<SkippedItem>();
        using var log = JsonlOperationLog.CreateForRun();
        var runner = new UninstallRunner(log, _inventory);
        foreach (var program in programs)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var current = await _inventory.ReadAsync(ct).ConfigureAwait(false);
                var fresh = current.Programs.SingleOrDefault(item => item.Id.Equals(program.Id, StringComparison.OrdinalIgnoreCase));
                UninstallResult result;
                if (fresh is null)
                {
                    var presence = await _inventory.ProbeAsync(program, ct).ConfigureAwait(false);
                    result = new(program.Id, presence.Presence == ProgramPresence.Absent
                        ? UninstallOutcome.AlreadyAbsent : UninstallOutcome.Refused, null, 0,
                        presence.Presence == ProgramPresence.Absent ? "программа уже отсутствует" : "регистрация не подтверждена; обновите список")
                        { RemovalConfirmed = presence.Presence == ProgramPresence.Absent };
                    await log.RecordAsync(new(program.DisplayName, result.RemovalConfirmed ? DeleteStatus.Skipped : DeleteStatus.Failed,
                        0, result.Reason), CancellationToken.None).ConfigureAwait(false);
                }
                else if (!SameRegistration(program, fresh))
                {
                    result = new(program.Id, UninstallOutcome.Refused, null, 0, "регистрация программы изменилась; обновите список");
                    await log.RecordAsync(new(program.DisplayName, DeleteStatus.Skipped, 0, result.Reason), CancellationToken.None).ConfigureAwait(false);
                }
                else if (fresh.Installer == InstallerKind.Msix)
                    result = await runner.RunMsixAsync(fresh, options, progress, ct).ConfigureAwait(false);
                else if (UninstallCommandBuilder.TryBuild(fresh, out var command, out var reason))
                    result = await runner.RunAsync(fresh, command, options, true, progress, ct).ConfigureAwait(false);
                else
                {
                    result = new(program.Id, UninstallOutcome.Refused, null, 0, reason);
                    await log.RecordAsync(new(program.DisplayName, DeleteStatus.Skipped, 0, reason), CancellationToken.None).ConfigureAwait(false);
                }
                results.Add(result);
                if (result.RemovalConfirmed && _settings.Current.LeftoverSearch && !ct.IsCancellationRequested)
                {
                    var found = await LeftoverFinder.FindAfterRemovalAsync(program, result, _inventory, ct).ConfigureAwait(false);
                    leftovers.AddRange(found.Found.Select(item => new ProgramLeftoverCandidate(program, result, item)));
                    skipped.AddRange(found.Skipped);
                }
                if (result.QueueCancelled || result.Outcome is UninstallOutcome.StillRunning or UninstallOutcome.TimedOut
                    or UninstallOutcome.Cancelled or UninstallOutcome.Busy) break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                skipped.Add(new(program.DisplayName, ex.Message));
                if (!results.Any(item => item.ProgramId == program.Id))
                    results.Add(new(program.Id, UninstallOutcome.Failed, null, 0, ex.Message));
                await log.RecordAsync(new(program.DisplayName, DeleteStatus.Failed, 0, ex.Message), CancellationToken.None).ConfigureAwait(false);
            }
        }
        return new(new(results, ct.IsCancellationRequested || results.Any(item => item.QueueCancelled)
            || results.Count < programs.Count), leftovers, skipped);
    }

    public async Task<IReadOnlyList<DeleteOutcome>> CleanLeftoversAsync(IReadOnlyList<ProgramLeftoverCandidate> items,
        IProgress<string>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(items);
        var outcomes = new List<DeleteOutcome>();
        if (items.Any(item => item.Item.CanDelete && item.Item.RegistryFinding is not null))
            await PrepareLeftoversAsync(ct).ConfigureAwait(false);
        var settings = _settings.Current;
        using var log = JsonlOperationLog.CreateForRun();
        var files = new FileDeleter(log);
        var registry = new RegistryCleanupRunner(new RegistryExecutor(log,
            settings.BackupRegistryBeforeCleanup ? new RegistryBackup() : null));
        foreach (var candidate in items)
        {
            if (ct.IsCancellationRequested) break;
            progress?.Report(candidate.Item.Path);
            try
            {
                if (candidate.Item.CanDelete && candidate.Item.Kind is LeftoverKind.File or LeftoverKind.Directory or LeftoverKind.Shortcut)
                {
                    outcomes.Add(await files.DeleteProgramLeftoverAsync(candidate.Program, candidate.Item, settings.Mode,
                        _inventory, ct).ConfigureAwait(false));
                    continue;
                }
                var search = await LeftoverFinder.FindAfterRemovalAsync(candidate.Program, candidate.Removal, _inventory, ct).ConfigureAwait(false);
                var fresh = search.Found.FirstOrDefault(item => item.CanDelete && SameReference(candidate.Item.RegistryFinding, item.RegistryFinding));
                if (candidate.Item.CanDelete && fresh?.RegistryFinding is { } finding)
                {
                    var result = await registry.RunAsync([finding], null, ct).ConfigureAwait(false);
                    outcomes.AddRange(result.Outcomes);
                }
                else
                {
                    var refusal = new DeleteOutcome(candidate.Item.Path, DeleteStatus.Skipped, 0,
                        "принадлежность или неизменность остатка не подтверждена; объект сохранён");
                    outcomes.Add(refusal);
                    await log.RecordAsync(refusal, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                var failure = new DeleteOutcome(candidate.Item.Path, DeleteStatus.Failed, 0, ex.Message);
                outcomes.Add(failure);
                await log.RecordAsync(failure, CancellationToken.None).ConfigureAwait(false);
            }
        }
        return outcomes;
    }

    private static bool SameRegistration(InstalledProgram before, InstalledProgram after) =>
        before.Scope == after.Scope && before.Installer == after.Installer
        && before.PackageFullName == after.PackageFullName && before.UninstallString == after.UninstallString
        && before.QuietUninstallString == after.QuietUninstallString && before.InstallLocation == after.InstallLocation
        && before.Version == after.Version;

    private static bool SameReference(RegistryFinding? before, RegistryFinding? after) => before is not null && after is not null
        && before.Hive == after.Hive && before.View == after.View && before.Kind == after.Kind
        && before.SubKey.Equals(after.SubKey, StringComparison.OrdinalIgnoreCase)
        && before.ValueName.Equals(after.ValueName, StringComparison.OrdinalIgnoreCase)
        && before.RawValue.Equals(after.RawValue, StringComparison.Ordinal)
        && before.MissingTarget.Equals(after.MissingTarget, StringComparison.OrdinalIgnoreCase)
        && before.Snapshot?.Matches(after.Snapshot) == true;
}
