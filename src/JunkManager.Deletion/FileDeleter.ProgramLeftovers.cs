using JunkManager.Core.Apps;
using JunkManager.Safety;

namespace JunkManager.Deletion;

public sealed partial class FileDeleter
{
    public async Task<DeleteOutcome> DeleteProgramLeftoverAsync(InstalledProgram program, Leftover leftover,
        DeleteMode mode, IProgramInventory? inventory = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(leftover);
        if (!leftover.CanDelete || leftover.Kind is not (LeftoverKind.Directory or LeftoverKind.File or LeftoverKind.Shortcut))
        { return await RecordRefusalAsync(leftover.Path, "нет сильного файлового доказательства; реестр удаляется своим исполнителем").ConfigureAwait(false); }
        if (ct.IsCancellationRequested)
        { return await RecordAsync(new(leftover.Path, DeleteStatus.Cancelled, 0, "очистка остатков отменена")).ConfigureAwait(false); }
        try { _ = File.GetAttributes(leftover.Path); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { return await RecordRefusalAsync(leftover.Path, "остаток уже отсутствует").ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return await RecordRefusalAsync(leftover.Path, "остаток не проверен: " + ex.Message).ConfigureAwait(false); }

        var source = inventory ?? new ProgramInventory();
        ProgramPresenceResult presence;
        ProgramInventorySnapshot snapshot;
        try
        {
            presence = await source.ProbeAsync(program, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            snapshot = await source.ReadAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { return await RecordAsync(new(leftover.Path, DeleteStatus.Cancelled, 0, "проверка остатков отменена")).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return await RecordRefusalAsync(leftover.Path, "список владельцев не проверен: " + ex.Message).ConfigureAwait(false); }
        if (presence.Presence != ProgramPresence.Absent || !snapshot.IsComplete || UninstallRunner.IsRunning(program.Id))
        { return await RecordRefusalAsync(leftover.Path, "удаление программы или полнота списка владельцев не подтверждены").ConfigureAwait(false); }
        var current = LeftoverFinder.Evaluate(program, snapshot.OwnershipPrograms, leftover.Kind,
            leftover.Path, leftover.TargetPath, true);
        if (!current.CanDelete || !current.Snapshot.SequenceEqual(leftover.Snapshot))
        { return await RecordRefusalAsync(leftover.Path, "доказательства или содержимое остатка изменились: " + current.Basis).ConfigureAwait(false); }
        if (leftover.Kind == LeftoverKind.Shortcut)
        {
            LeftoverSearch confirmation;
            try
            {
                confirmation = await LeftoverFinder.FindAfterRemovalAsync(program,
                    new(program.Id, UninstallOutcome.Removed, 0, 0, null) { RemovalConfirmed = true }, source, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            { return await RecordAsync(new(leftover.Path, DeleteStatus.Cancelled, 0, "проверка ярлыка отменена")).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { return await RecordRefusalAsync(leftover.Path, "ярлык не проверен: " + ex.Message).ConfigureAwait(false); }
            if (!confirmation.Found.Any(f => f.Kind == LeftoverKind.Shortcut && f.CanDelete
                && f.Path.Equals(leftover.Path, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.TargetPath, leftover.TargetPath, StringComparison.OrdinalIgnoreCase)))
            { return await RecordRefusalAsync(leftover.Path, "ярлык больше не указывает на удалённую программу").ConfigureAwait(false); }
        }
        var root = program.InstallLocation ?? Path.GetDirectoryName(program.ExecutablePath);
        if (root is null)
        { return await RecordRefusalAsync(leftover.Path, "нет точного каталога установки").ConfigureAwait(false); }
        if (ct.IsCancellationRequested)
        { return await RecordAsync(new(leftover.Path, DeleteStatus.Cancelled, 0, "очистка остатков отменена")).ConfigureAwait(false); }
        if (mode == DeleteMode.RecycleBin)
        {
            DeleteOutcome outcome;
            try
            {
                outcome = RecycleBinDeleter.DeleteProgramLeftover(leftover.Path, root,
                    leftover.Kind == LeftoverKind.Shortcut, current.Snapshot, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { outcome = new(leftover.Path, DeleteStatus.Failed, 0, "перемещение в корзину не выполнено: " + ex.Message); }
            return await RecordAsync(outcome).ConfigureAwait(false);
        }
        if (mode != DeleteMode.Permanent) { throw new ArgumentOutOfRangeException(nameof(mode)); }

        long bytes = 0;
        var deleted = 0;
        string? refusal = null;
        foreach (var item in current.Snapshot.OrderByDescending(f => f.Path.Length))
        {
            if (ct.IsCancellationRequested)
            {
                await RecordAsync(new(item.Path, DeleteStatus.Cancelled, 0, "очистка остатков отменена")).ConfigureAwait(false);
                return new(leftover.Path, DeleteStatus.Cancelled, bytes, "очистка остатков отменена частично");
            }
            var outcome = DeleteProgramEntry(item, root, leftover.Kind == LeftoverKind.Shortcut, ct);
            await _zhurnal.RecordAsync(outcome, CancellationToken.None).ConfigureAwait(false);
            if (outcome.Status == DeleteStatus.Deleted) { bytes += outcome.BytesFreed; deleted++; }
            else { refusal ??= outcome.Reason; }
            if (outcome.Status == DeleteStatus.Cancelled)
            { return new(leftover.Path, DeleteStatus.Cancelled, bytes, "очистка остатков отменена частично"); }
        }
        // The journal already owns every byte. The returned summary is not another disk operation.
        return new(leftover.Path, deleted == current.Snapshot.Count ? DeleteStatus.Deleted : DeleteStatus.Failed,
            bytes, refusal);
    }

    private static DeleteOutcome DeleteProgramEntry(ProgramFileStamp item, string root, bool shortcut, CancellationToken ct)
    {
        try
        {
            if (!ProgramLeftoverGuard.TryVerify(item.Path, root, shortcut, out _, out var reason))
            { return new(item.Path, DeleteStatus.Skipped, 0, reason); }
            if (ct.IsCancellationRequested)
            { return new(item.Path, DeleteStatus.Cancelled, 0, "очистка остатков отменена"); }
            if (item.IsDirectory)
            {
                // Nonrecursive deletion refuses anything that arrived after the preview.
                Directory.Delete(item.Path, recursive: false);
                return new(item.Path, DeleteStatus.Deleted, 0);
            }
            var info = new FileInfo(item.Path);
            if (info.Length != item.Length || info.LastWriteTimeUtc != item.LastWriteUtc)
            { return new(item.Path, DeleteStatus.Skipped, 0, "файл изменился после просмотра"); }
            return UdalitFayl(item.Path, ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { return new(item.Path, DeleteStatus.Skipped, 0, "остаток уже отсутствует"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return new(item.Path, DeleteStatus.Failed, 0, ex.Message); }
    }
}
