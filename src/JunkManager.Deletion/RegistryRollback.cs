using System.Security;
using System.Text.Json;
using JunkManager.Safety;
using Microsoft.Win32;

namespace JunkManager.Deletion;

public enum RegistryRestoreStatus { Refused, Restored, AlreadyPresent, Conflict, Cancelled }

public sealed record RollbackResult(bool Ok, string FilePath, string? Reason)
{
    public RegistryRestoreStatus Status { get; init; }
}

/// <summary>Restores the exact deleted entry. A neighbour is not collateral.</summary>
public static class RegistryRollback
{
    public static Task<RollbackResult> ImportAsync(string backupFile, RegistryView view, CancellationToken ct) =>
        Task.Run(() => Restore(backupFile, view, ct), CancellationToken.None);

    private static RollbackResult Restore(string file, RegistryView view, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return new(false, file, "восстановление отменено") { Status = RegistryRestoreStatus.Cancelled };
        if (!RegistryBackupEntry.TryRead(file, out var entry, out var reason))
            return new(false, file, "импорт не начинался: " + reason);
        var saved = entry.Snapshot;
        if (view != RegistryView.Default && view != saved.View)
            return new(false, file, "представление реестра не совпадает с резервной копией");
        try
        {
            if (!ValidSnapshot(saved, out reason)) return new(false, file, reason);
            var current = saved.ReadCurrent();
            if (current is not null)
                return saved.Matches(current)
                    ? new(true, file, "запись уже восстановлена") { Status = RegistryRestoreStatus.AlreadyPresent }
                    : new(false, file, "конфликт: по этому адресу уже существуют другие данные; они сохранены")
                        { Status = RegistryRestoreStatus.Conflict };

            // Decode everything before writing; halfway through a subtree is a bad time for surprises.
            var decoded = saved.Values.Select(value => (Value: value, Data: value.Decode())).ToArray();
            ct.ThrowIfCancellationRequested();
            using var root = RegistryKey.OpenBaseKey(saved.Hive, saved.View);
            if (saved.WholeKey)
            {
                foreach (var relative in saved.Keys.OrderBy(key => key.Length))
                {
                    ct.ThrowIfCancellationRequested();
                    using var key = root.CreateSubKey(Combine(saved.SubKey, relative), writable: true);
                }
            }
            foreach (var (value, data) in decoded)
            {
                ct.ThrowIfCancellationRequested();
                using var key = root.CreateSubKey(Combine(saved.SubKey, value.RelativeKey), writable: true);
                if (key.GetValueNames().Contains(value.Name, StringComparer.OrdinalIgnoreCase))
                    return new(false, file, "конфликт: значение появилось во время восстановления; оно сохранено")
                        { Status = RegistryRestoreStatus.Conflict };
                key.SetValue(value.Name, data, value.Kind);
            }
            return saved.Matches(saved.ReadCurrent())
                ? new(true, file, "выбранная запись восстановлена") { Status = RegistryRestoreStatus.Restored }
                : new(false, file, "результат восстановления не совпал с сохранённой записью; нужна повторная проверка");
        }
        catch (OperationCanceledException)
        {
            return new(false, file, "восстановление остановлено; часть данных могла быть восстановлена")
                { Status = RegistryRestoreStatus.Cancelled };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or ArgumentException or InvalidOperationException or JsonException or NotSupportedException)
        {
            return new(false, file, "восстановление не завершено: " + ex.Message);
        }
    }

    private static bool ValidSnapshot(RegistryEntrySnapshot saved, out string? reason)
    {
        var allowed = saved.WholeKey
            ? RegistryGuard.TryVerifyKey(saved.Hive, saved.SubKey, saved.View, out _, out reason)
            : RegistryGuard.TryVerifyValue(saved.Hive, saved.SubKey, saved.ValueName, saved.View, out _, out reason);
        if (!allowed) return false;
        if (saved.Keys is null || saved.Values is null || saved.Keys.Count + saved.Values.Count > 10000
            || saved.Keys.Any(key => !ValidRelative(key))
            || saved.Values.Any(value => value is null || value.Name is null || value.Name.Any(char.IsControl)
                || !ValidRelative(value.RelativeKey) || !Enum.IsDefined(value.Kind)))
        { reason = "неверные адреса или типы в сохранённой записи"; return false; }
        if (saved.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != saved.Keys.Count
            || saved.Values.Select(value => value.RelativeKey + "\0" + value.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != saved.Values.Count)
        { reason = "в резервной копии повторяются адреса"; return false; }
        if (!saved.WholeKey && (saved.Keys.Count != 0 || saved.Values.Count != 1
            || saved.Values[0].RelativeKey.Length != 0
            || !saved.Values[0].Name.Equals(saved.ValueName, StringComparison.OrdinalIgnoreCase))
            || saved.WholeKey && (!saved.Keys.Contains(string.Empty)
                || saved.Values.Any(value => !saved.Keys.Contains(value.RelativeKey, StringComparer.OrdinalIgnoreCase))))
        { reason = "состав резервной копии не соответствует выбранной записи"; return false; }
        reason = null;
        return true;
    }

    private static bool ValidRelative(string? relative) => relative is not null
        && !relative.Any(char.IsControl) && !relative.Contains('/', StringComparison.Ordinal)
        && (relative.Length == 0 || relative.Split('\\').All(segment => segment.Length > 0 && segment is not "." and not ".."));

    private static string Combine(string key, string relative) => relative.Length == 0 ? key : key + "\\" + relative;

    public static bool ValueRestored(RegistryHive hive, string subKey, string valueName, RegistryView view, string expected)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var key = root.OpenSubKey(subKey, writable: false);
        return key is not null && string.Equals(key.GetValue(valueName, null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string, expected, StringComparison.Ordinal);
    }
}
