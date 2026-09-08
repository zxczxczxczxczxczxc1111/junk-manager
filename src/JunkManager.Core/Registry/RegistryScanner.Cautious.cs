using JunkManager.Safety;
using Microsoft.Win32;

namespace JunkManager.Core.Registry;

public sealed partial class RegistryScanner
{
    private void ScanCautious(RegistryKey root, RegistryScanRule rule, string branch,
        List<RegistryFinding> findings, List<SkippedItem> skipped, CancellationToken ct)
    {
        if (rule.Mode == RegistryScanMode.SharedDlls)
        {
            foreach (var name in root.GetValueNames())
            {
                if (ct.IsCancellationRequested) return;
                var address = RegistryAddress.Format(rule.Hive, branch, name);
                if (!RegistryGuard.TryVerifyValue(rule.Hive, branch, name, rule.View, out _, out var refusal))
                { skipped.Add(new(address, refusal!) { IsExpectedExclusion = true }); continue; }
                if (root.GetValueKind(name) != RegistryValueKind.DWord || root.GetValue(name) is not int count)
                { skipped.Add(new(address, "счётчик общих компонентов имеет неизвестный тип")); continue; }
                if (count != 0)
                { skipped.Add(new(address, "компонент используется; счётчик ссылок не равен нулю") { IsExpectedExclusion = true }); continue; }
                if (!CautiousTarget(name, out var target, out refusal, out var excluded))
                { skipped.Add(new(address, refusal!) { IsExpectedExclusion = excluded }); continue; }
                if (!MissingForView(target, rule.View, out var unknown))
                {
                    if (unknown) skipped.Add(new(address, "существование DLL проверить не удалось"));
                    continue;
                }
                var snapshot = RegistryEntrySnapshot.Capture(rule.Hive, branch, name, rule.View, false);
                if (snapshot is null || snapshot.Values.Single().Decode() is not int current || current != 0) continue;
                findings.Add(new(rule.Hive, branch, name, rule.View, RegistryEntryKind.Value, "0", target,
                    rule.Name, rule.Consequence, rule.Id)
                    { Snapshot = snapshot, ManualSelectionOnly = true, Category = rule.Name });
            }
            return;
        }

        foreach (var childName in root.GetSubKeyNames())
        {
            if (ct.IsCancellationRequested) return;
            if (rule.Mode == RegistryScanMode.ComServers && !Guid.TryParseExact(childName, "B", out _)) continue;
            var leaves = rule.Mode == RegistryScanMode.ComServers
                ? ComLeaves : AssociationLeaves;
            foreach (var leaf in leaves)
            {
                var path = branch + "\\" + childName + "\\" + leaf;
                if (!RegistryGuard.TryVerifyValue(rule.Hive, path, string.Empty, rule.View, out _, out _)) continue;
                try
                {
                    using var key = root.OpenSubKey(childName + "\\" + leaf, writable: false);
                    if (key is null) continue;
                    var raw = key.GetValue(string.Empty, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if (!CautiousTarget(raw, out var target, out var refusal, out var excluded))
                    { skipped.Add(new(path, refusal!) { IsExpectedExclusion = excluded }); continue; }
                    if (key.GetValue("ServerExecutable", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string executable
                        && (!CautiousTarget(executable, out var declared, out _, out _) || !declared.Equals(target, StringComparison.OrdinalIgnoreCase)))
                    { skipped.Add(new(path, "ServerExecutable задаёт другую цель; запись сохраняется")); continue; }
                    Rassmotret(rule, path, string.Empty, RegistryEntryKind.Value, raw!, path, findings, skipped);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                { skipped.Add(new(path, "проверка ссылки недоступна: " + ex.Message)); }
            }
        }
    }

    private static readonly string[] ComLeaves = ["InprocServer32", "LocalServer32"];
    private static readonly string[] AssociationLeaves = [@"shell\open\command"];

    private bool MissingForView(string target, RegistryView view, out bool unknown)
    {
        var state = _proba(target);
        unknown = state == TargetState.Unknown;
        if (state != TargetState.Missing) return false;
        var twin = view == RegistryView.Registry32 ? Bitnost.Blizhnec32(target) : null;
        var twinState = twin is null ? TargetState.Missing : _proba(twin);
        unknown = twinState == TargetState.Unknown;
        return twinState == TargetState.Missing;
    }

    private static bool CautiousTarget(string? raw, out string target, out string? refusal, out bool excluded)
    {
        target = string.Empty;
        excluded = string.IsNullOrWhiteSpace(raw);
        if (string.IsNullOrWhiteSpace(raw) || !RegistryTargetExtractor.TryExtract(raw, out target, out refusal))
        { refusal = "нет однозначного абсолютного пути к файлу"; return false; }
        var extension = Path.GetExtension(target);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
        if (target.Length < 3 || !char.IsAsciiLetter(target[0]) || target[1] != ':' || target[2] != '\\'
            || !Path.IsPathFullyQualified(target)
            || !(extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            || target.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase))
        { excluded = true; refusal = "системная, сетевая или неподдерживаемая цель; запись сохраняется"; return false; }
        refusal = null;
        return true;
    }
}
