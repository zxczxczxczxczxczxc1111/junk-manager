using System.Security;
using JunkManager.Core.Registry;
using JunkManager.Safety;
using Microsoft.Win32;

namespace JunkManager.Deletion;

/// <summary>
/// The only type in the solution allowed to remove anything from the registry.
/// Not a convention: <c>ArchitectureTests</c> reads the compiled assemblies and
/// fails the build if <c>DeleteValue</c>, <c>DeleteSubKey</c> or
/// <c>DeleteSubKeyTree</c> shows up in Core or Safety.
/// </summary>
/// <remarks>
/// Backup is optional. When requested, both the export and the exact saved entry
/// must reach disk before deletion. Current data is checked again after export.
/// </remarks>
public sealed class RegistryExecutor
{
    private readonly IOperationLog _zhurnal;
    private readonly IRegistryBackup? _bekap;

    internal async Task<DeleteOutcome> RecordAsync(DeleteOutcome outcome)
    {
        await _zhurnal.RecordAsync(outcome, CancellationToken.None).ConfigureAwait(false);
        return outcome;
    }

    public RegistryExecutor(IOperationLog log, IRegistryBackup? backup = null)
    {
        ArgumentNullException.ThrowIfNull(log);

        _zhurnal = log;
        _bekap = backup;
    }

    /// <summary>
    /// Removes one verified value. Never throws for a value it could not take:
    /// a vanished key and a denied permission are ordinary results here, and
    /// turning them into exceptions would mean the first one aborts the rest of
    /// the clean.
    /// </summary>
    public Task<DeleteOutcome> DeleteValueAsync(VerifiedRegistryValue value, CancellationToken ct = default) =>
        DeleteValueAsync(value, null, null, ct);

    public async Task<DeleteOutcome> DeleteValueAsync(
        VerifiedRegistryValue value, RegistryEntrySnapshot? expected, string? missingTarget, CancellationToken ct)
    {
        var itog = value.IsEmpty
            ? new DeleteOutcome(string.Empty, DeleteStatus.Skipped, 0, "пустой пропуск: удалять нечего")
            : await Vypolnit(
                    value.Address, value.Hive, value.SubKey, value.View, value.ValueName, false, expected, missingTarget,
                    () => UbratZnachenie(value.Hive, value.View, value.SubKey, value.ValueName), ct)
                .ConfigureAwait(false);

        // Журнал пишется всегда и с CancellationToken.None, ровно как в
        // FileDeleter: отменённая запись означала бы удалённую строку реестра,
        // о которой не осталось следа.
        await _zhurnal.RecordAsync(itog, CancellationToken.None).ConfigureAwait(false);

        return itog;
    }

    /// <summary>
    /// Removes one verified key with everything under it. The backup covers the
    /// key itself, so the whole subtree comes back on import.
    /// </summary>
    public Task<DeleteOutcome> DeleteKeyAsync(VerifiedRegistryKey key, CancellationToken ct = default) =>
        DeleteKeyAsync(key, null, null, ct);

    public async Task<DeleteOutcome> DeleteKeyAsync(
        VerifiedRegistryKey key, RegistryEntrySnapshot? expected, string? missingTarget, CancellationToken ct)
    {
        DeleteOutcome itog;

        if (key.IsEmpty)
        {
            itog = new DeleteOutcome(string.Empty, DeleteStatus.Skipped, 0, "пустой пропуск: удалять нечего");
        }
        else
        {
            var razdel = key.SubKey.LastIndexOf('\\');

            if (razdel <= 0)
            {
                itog = new DeleteOutcome(
                    key.Address, DeleteStatus.Skipped, 0, "у ключа нет родителя, удалять нечего");
            }
            else
            {
                var roditel = key.SubKey[..razdel];
                var imya = key.SubKey[(razdel + 1)..];

                itog = await Vypolnit(
                    key.Address, key.Hive, key.SubKey, key.View, string.Empty, true, expected, missingTarget,
                    () => UbratKlyuch(key.Hive, key.View, roditel, imya), ct).ConfigureAwait(false);
            }
        }

        await _zhurnal.RecordAsync(itog, CancellationToken.None).ConfigureAwait(false);

        return itog;
    }

    /// <param name="backupSubKey">
    /// What gets exported. For a value it is the key holding it, for a key it is
    /// the key itself: in both cases importing the file puts back exactly what
    /// left.
    /// </param>
    private async Task<DeleteOutcome> Vypolnit(
        string adres,
        RegistryHive uley,
        string backupSubKey,
        RegistryView vid,
        string valueName,
        bool wholeKey,
        RegistryEntrySnapshot? expected,
        string? missingTarget,
        Func<DeleteOutcome> deystvie,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return new DeleteOutcome(adres, DeleteStatus.Cancelled, 0, "отменено до бэкапа");
        }

        try
        {
            var snapshot = RegistryEntrySnapshot.Capture(uley, backupSubKey, valueName, vid, wholeKey);
            if (snapshot is null)
                return new DeleteOutcome(adres, DeleteStatus.Skipped, 0, "значение или ключ исчезло до удаления");
            if (expected is not null && !expected.Matches(snapshot))
                return new DeleteOutcome(adres, DeleteStatus.Skipped, 0, "запись изменилась после сканирования");

            RegistryBackupEntry? saved = null;
            string? exportFile = null;
            if (_bekap is not null)
            {
                var backup = await _bekap.ExportAsync(uley, backupSubKey, vid, ct).ConfigureAwait(false);
                if (!backup.Ok)
                    return new DeleteOutcome(adres, DeleteStatus.Skipped, 0,
                        $"удаления не было: бэкап не удался, {backup.Reason ?? "причина не названа"}");
                exportFile = backup.FilePath;
                saved = RegistryBackupEntry.Prepare(exportFile, snapshot);
            }
            if (ct.IsCancellationRequested)
                return new DeleteOutcome(adres, DeleteStatus.Cancelled, 0, "отменено до удаления");
            if (!snapshot.Matches(snapshot.ReadCurrent()))
                return new DeleteOutcome(adres, DeleteStatus.Skipped, 0, "запись изменилась перед удалением");
            if (missingTarget is not null && !RegistryScanner.IsTargetMissing(missingTarget, vid))
                return new DeleteOutcome(adres, DeleteStatus.Skipped, 0, "целевой файл появился или его отсутствие не подтверждено");
            // Ключ здесь НЕ открывается. Для ключа удаление идёт от родителя, и
            // открытый описатель на удаляемую ветку означал бы, что мы держим
            // ровно то, что просим убрать.
            var outcome = deystvie();
            if (outcome.Status == DeleteStatus.Deleted && saved is not null)
            {
                try { saved.MarkDeleted(exportFile!); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The deletion happened. A bookkeeping failure must not rewrite history.
                    return outcome with { Reason = "удалено, но подтверждение резервной копии не записалось: " + ex.Message };
                }
            }
            return outcome;
        }
        catch (SecurityException ex)
        {
            return new DeleteOutcome(adres, DeleteStatus.Failed, 0, $"нет прав: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new DeleteOutcome(adres, DeleteStatus.Failed, 0, $"отказано в доступе: {ex.Message}");
        }
        catch (IOException ex)
        {
            return new DeleteOutcome(adres, DeleteStatus.Failed, 0, $"реестр не отдал ключ: {ex.Message}");
        }
    }

    private static DeleteOutcome UbratZnachenie(
        RegistryHive uley, RegistryView vid, string vetka, string imya)
    {
        using var baza = RegistryKey.OpenBaseKey(uley, vid);
        using var klyuch = baza.OpenSubKey(vetka, writable: true);

        var adres = RegistryAddress.Format(uley, vetka, imya);

        if (klyuch is null)
        {
            return new DeleteOutcome(
                adres, DeleteStatus.Skipped, 0, "ветка исчезла между проверкой и удалением");
        }

        if (!klyuch.GetValueNames().Contains(imya, StringComparer.OrdinalIgnoreCase))
        {
            // Спрашивается список имён, а не GetValue: у значения бывает пустая
            // строка, и "нет значения" от "значение пустое" отличается только
            // так.
            return new DeleteOutcome(
                adres, DeleteStatus.Skipped, 0, "значение исчезло между проверкой и удалением");
        }

        klyuch.DeleteValue(imya, throwOnMissingValue: false);

        // Ноль освобождённых байт не заглушка. Раздел 9 спеки: освобождённые
        // мегабайты для реестра не показываются никогда, потому что их там нет.
        return new DeleteOutcome(adres, DeleteStatus.Deleted, 0);
    }

    private static DeleteOutcome UbratKlyuch(
        RegistryHive uley, RegistryView vid, string roditel, string imya)
    {
        using var baza = RegistryKey.OpenBaseKey(uley, vid);
        using var roditelskiy = baza.OpenSubKey(roditel, writable: true);

        var adres = RegistryAddress.Format(uley, roditel + "\\" + imya, null);

        if (roditelskiy is null)
        {
            return new DeleteOutcome(adres, DeleteStatus.Skipped, 0, "родительская ветка исчезла");
        }

        if (!roditelskiy.GetSubKeyNames().Contains(imya, StringComparer.OrdinalIgnoreCase))
        {
            return new DeleteOutcome(
                adres, DeleteStatus.Skipped, 0, "ключ исчез между проверкой и удалением");
        }

        roditelskiy.DeleteSubKeyTree(imya, throwOnMissingSubKey: false);

        return new DeleteOutcome(adres, DeleteStatus.Deleted, 0);
    }
}
