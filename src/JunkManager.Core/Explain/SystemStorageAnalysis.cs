using System.Text;
using System.Text.Json;
using JunkManager.Core.Sources.Platform;
using JunkManager.Core.Rules;

namespace JunkManager.Core.Explain;

public static class SystemStorageAnalysis
{
    // These commands ask Windows for numbers. Neither gets a demolition verb.
    internal const string ShadowScript = "[Console]::OutputEncoding=[Text.Encoding]::ASCII; $ErrorActionPreference='Stop'; "
        + "$rows=@(Get-CimInstance -ClassName Win32_ShadowStorage | ForEach-Object { "
        + "[pscustomobject]@{ Path=$_.DiffVolume.DeviceID; Used=[long]$_.UsedSpace } }); "
        + "ConvertTo-Json -InputObject $rows -Compress";

    public static async Task<StorageAnalysis> AppendAsync(StorageAnalysis analysis, string windows,
        IProgress<string>? progress, CancellationToken ct,
        Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<ToolRun>>? run = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        run ??= ProcessRunner.RunAsync;
        var rows = analysis.Items.ToList();
        var issues = analysis.Issues.ToList();
        if (analysis.Cancelled || ct.IsCancellationRequested) return analysis with { Cancelled = true };
        try
        {
            progress?.Report("Windows: анализ хранилища компонентов");
            var result = await run(ProcessRunner.SystemTool("dism.exe"), DismComponentStore.AnalyzeArguments(),
                TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                var report = DismComponentStore.Parse(result.StandardOutput);
                if (report.ActualSizeBytes <= 0) throw new RuleFormatException("DISM не сообщил размер хранилища компонентов.");
                var path = Path.Combine(windows, "WinSxS");
                rows.RemoveAll(row => row.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
                rows.Add(new("Windows · DISM", path, report.ActualSizeBytes,
                    (report.CleanupRecommended ? "Windows рекомендует очистку. Проверь раздел «Файлы». " : "Windows не рекомендует очистку. ")
                    + "Размер по данным DISM. Весь этот объём не освобождается."));
            }
            else issues.Add(result.ExitCode == 740 ? "DISM: для точного размера WinSxS нужны права администратора."
                : $"DISM: анализ не завершён, код {result.ExitCode}.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { issues.Add("DISM: превышено время ожидания."); }
        catch (OperationCanceledException) { return Finish(true); }
        catch (Exception ex) when (ex is RuleFormatException or InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException)
        { issues.Add("DISM: " + ex.Message); }

        if (ct.IsCancellationRequested) return Finish(true);
        try
        {
            progress?.Report("Windows: место под теневые копии и восстановление");
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var result = await run(exe, ["-NoProfile", "-NonInteractive", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(ShadowScript))], TimeSpan.FromSeconds(45), ct).ConfigureAwait(false);
            if (result.ExitCode != 0) issues.Add($"Теневые копии: размер недоступен, код {result.ExitCode}. Могут потребоваться права администратора.");
            else rows.AddRange(ParseShadows(result.StandardOutput));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { issues.Add("Теневые копии: превышено время ожидания."); }
        catch (OperationCanceledException) { return Finish(true); }
        catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException)
        { issues.Add("Теневые копии: " + ex.Message); }
        return Finish(ct.IsCancellationRequested);

        StorageAnalysis Finish(bool cancelled) => new(rows.OrderByDescending(row => row.Bytes).ThenBy(row => row.Path, StringComparer.OrdinalIgnoreCase).ToArray(), issues, cancelled);
    }

    internal static IReadOnlyList<StorageItem> ParseShadows(string json)
    {
        using var document = JsonDocument.Parse(json);
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("Path", out var pathValue) || pathValue.ValueKind != JsonValueKind.String
                || !entry.TryGetProperty("Used", out var usedValue) || !usedValue.TryGetInt64(out var used))
                throw new FormatException("Windows вернула неполные сведения о хранилище копий.");
            var path = pathValue.GetString();
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParse(path.AsSpan(11).TrimEnd("\\}"), out _) || used < 0)
                throw new FormatException("Windows вернула неполные сведения о хранилище копий.");
            totals[path] = checked(totals.GetValueOrDefault(path) + used);
        }
        return totals.Select(pair => new StorageItem("Теневые копии и восстановление", pair.Key, pair.Value,
            "Занято копиями на этом томе. Это резервные данные, автоматически не удаляются.")).ToArray();
    }
}
