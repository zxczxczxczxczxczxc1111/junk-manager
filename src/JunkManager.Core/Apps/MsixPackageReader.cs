using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace JunkManager.Core.Apps;

public sealed record MsixPackage(string FullName, string DisplayName, string Publisher, string? RootFolder)
{
    public string Version { get; init; } = string.Empty;
    public bool ProtectionKnown { get; init; }
    public bool NonRemovable { get; init; }
    public bool IsFramework { get; init; }
    public bool IsResourcePackage { get; init; }
    public bool IsBundle { get; init; }
    public string SignatureKind { get; init; } = string.Empty;

    public bool CanUninstall([NotNullWhen(false)] out string? reason)
    {
        reason = !ProtectionKnown ? "защита пакета не определена" :
            NonRemovable ? "пакет помечен NonRemovable" :
            IsBundle ? "контейнер установки; приложение показано отдельной строкой" :
            IsFramework || IsResourcePackage ? "общая зависимость или пакет ресурсов" :
            SignatureKind.Equals("System", StringComparison.OrdinalIgnoreCase)
                || FullName.StartsWith("Microsoft.WindowsStore_", StringComparison.OrdinalIgnoreCase)
                || FullName.StartsWith("Microsoft.DesktopAppInstaller_", StringComparison.OrdinalIgnoreCase)
                ? "защищённый системный пакет" : null;
        return reason is null;
    }
}

public sealed record MsixPackageSnapshot(IReadOnlyList<MsixPackage> Packages, IReadOnlyList<SkippedItem> Skipped);

public static class MsixPackageReader
{
    private const string Script = """
        # Package metadata is evidence; registry folklore has retired.
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        try {
            $packages = @(Get-AppxPackage -PackageTypeFilter Main,Framework,Resource,Bundle,Optional -ErrorAction Stop | ForEach-Object {
                [pscustomobject]@{
                    FullName = $_.PackageFullName; DisplayName = $_.Name; Publisher = $_.Publisher
                    RootFolder = $_.InstallLocation; Version = [string]$_.Version
                    ProtectionKnown = ($null -ne $_.PSObject.Properties['NonRemovable'])
                    NonRemovable = [bool]$_.NonRemovable; IsFramework = [bool]$_.IsFramework
                    IsResourcePackage = [bool]$_.IsResourcePackage; IsBundle = [bool]$_.IsBundle
                    SignatureKind = [string]$_.SignatureKind
                }
            })
            ConvertTo-Json -InputObject $packages -Compress -Depth 3
            exit 0
        } catch {
            [Console]::Error.WriteLine($_.Exception.ToString())
            exit 1
        }
        """;

    public static string PowerShellExecutable => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    public static IReadOnlyList<MsixPackage> Read() => ReadDetailedAsync().GetAwaiter().GetResult().Packages;

    public static async Task<MsixPackageSnapshot> ReadDetailedAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!ProgramInventory.CanReadCurrentUser(out var reason))
        {
            return new([], [new("MSIX текущего пользователя", reason)]);
        }

        try
        {
            var psi = new ProcessStartInfo(PowerShellExecutable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)) }) { psi.ArgumentList.Add(arg); }
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("PowerShell не запустился");
            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                // This process only reads metadata; no installer transaction lives here.
                if (!process.HasExited) { process.Kill(entireProcessTree: true); }
                throw;
            }

            var output = await stdout.ConfigureAwait(false);
            var error = await stderr.ConfigureAwait(false);
            return process.ExitCode == 0 ? Parse(output) :
                new([], [new("MSIX текущего пользователя", $"ошибка {process.ExitCode}: {error}")]);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new([], [new("MSIX текущего пользователя", "опрос пакетов превысил 45 секунд")]);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new([], [new("MSIX текущего пользователя", ex.Message)]);
        }
    }

    public static MsixPackageSnapshot Parse(string json)
    {
        try
        {
            var packages = JsonSerializer.Deserialize<List<MsixPackage>>(json);
            if (packages is null || packages.Any(p => string.IsNullOrWhiteSpace(p.FullName)
                    || string.IsNullOrWhiteSpace(p.DisplayName)))
            {
                return new([], [new("MSIX", "ответ пакетов не содержит обязательных данных")]);
            }

            return new(packages, []);
        }
        catch (JsonException ex)
        {
            return new([], [new("MSIX", "ответ пакетов не разобран: " + ex.Message)]);
        }
    }
}
