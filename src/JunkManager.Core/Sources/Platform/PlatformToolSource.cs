using System.Text.Json;
using System.Text.Json.Serialization;
using JunkManager.Core.Rules;

namespace JunkManager.Core.Sources.Platform;

public sealed record PlatformToolTexts(
    [property: JsonPropertyName("dismName")] string DismName,
    [property: JsonPropertyName("dismConsequence")] string DismConsequence,
    [property: JsonPropertyName("driverName")] string DriverName,
    [property: JsonPropertyName("driverConsequence")] string DriverConsequence);

/// <summary>
/// Two documented utilities, one source. Analysis only: both tools are asked
/// what they could free, and neither is asked to free it. The removal side lives
/// in JunkManager.Deletion behind the fuse.
/// </summary>
public sealed class PlatformToolSource
{
    public Task<ScanResult> ScanAsync(IProgress<string>? progress, CancellationToken ct) =>
        ScanAsync(null, progress, ct);

    private const string ComponentStoreId = FindingPath.PlatformToolScheme + "dism/component-store";
    private static readonly TimeSpan Terpenie = TimeSpan.FromMinutes(10);

    // CA1869: a JsonSerializerOptions built inside the method is rebuilt on every
    // call and drags its whole reflection cache with it. Same shape as RuleLoader.
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1822:Mark members as static",
        Justification =
            "Форма источника одна на все пять: экземпляр плюс ScanAsync(rulesDirectory, progress, ct). " +
            "Состояния у этого источника действительно нет, но статический метод развалил бы " +
            "единый вид: место сборки находок звало бы четыре источника через экземпляр, а этот " +
            "по имени класса. Экономия тут ноль, а расхождение видно в каждом вызове.")]
    public async Task<ScanResult> ScanAsync(
        string? rulesDirectory, IProgress<string>? progress, CancellationToken ct)
    {
        var texts = rulesDirectory is null ? BuiltInCatalog.LoadPlatformToolTexts() : LoadTexts(rulesDirectory);
        var findings = new List<Finding>();
        var skipped = new List<SkippedItem>();

        if (ct.IsCancellationRequested)
        {
            return new ScanResult(findings, skipped, Cancelled: true);
        }

        progress?.Report(texts.DismName);
        await ScanComponentStoreAsync(texts, findings, skipped, ct).ConfigureAwait(false);

        if (ct.IsCancellationRequested)
        {
            return new ScanResult(findings, skipped, Cancelled: true);
        }

        progress?.Report(texts.DriverName);
        await ScanDriversAsync(texts, findings, skipped, ct).ConfigureAwait(false);

        return new ScanResult(findings, skipped, ct.IsCancellationRequested);
    }

    internal static PlatformToolTexts LoadTexts(string rulesDirectory)
    {
        var file = Path.Combine(rulesDirectory, "platform-tools.json");

        if (!File.Exists(file))
        {
            throw new RuleFormatException($"файл описаний утилит не найден: {file}");
        }

        return ParseTexts(File.ReadAllText(file));
    }

    internal static PlatformToolTexts ParseTexts(string text)
    {
        PlatformToolTexts? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PlatformToolTexts>(text, Options);
        }
        catch (JsonException ex)
        {
            // A malformed catalog deserves a diagnosis, not interpretive silence.
            throw new RuleFormatException($"platform-tools.json не разбирается: {ex.Message}", ex);
        }

        if (parsed is null
            || string.IsNullOrWhiteSpace(parsed.DismConsequence)
            || string.IsNullOrWhiteSpace(parsed.DriverConsequence))
        {
            throw new RuleFormatException(
                "platform-tools.json: пустое consequence. Находка без объяснения не показывается");
        }

        return parsed;
    }

    private static async Task ScanComponentStoreAsync(
        PlatformToolTexts texts, List<Finding> findings, List<SkippedItem> skipped, CancellationToken ct)
    {
        ToolRun run;
        try
        {
            run = await ProcessRunner
                .RunAsync(ProcessRunner.SystemTool("dism.exe"), DismComponentStore.AnalyzeArguments(), Terpenie, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            skipped.Add(new SkippedItem(ComponentStoreId, $"DISM не запустился: {ex.Message}"));
            return;
        }

        if (run.ExitCode != 0)
        {
            // 740 is "needs elevation" and is the common case without admin.
            // Naming it beats a bare number in a list nobody can act on.
            var hint = run.ExitCode == 740
                ? "нужны права администратора"
                : $"код выхода {run.ExitCode}";
            skipped.Add(new SkippedItem(ComponentStoreId, $"склад компонентов не проверен: {hint}"));
            return;
        }

        ComponentStoreReport report;
        try
        {
            report = DismComponentStore.Parse(run.StandardOutput);
        }
        catch (RuleFormatException ex)
        {
            skipped.Add(new SkippedItem(ComponentStoreId, ex.Message));
            return;
        }

        if (!report.CleanupRecommended || report.BackupsBytes <= 0)
        {
            return;
        }

        findings.Add(new Finding(
            Name: texts.DismName,
            Path: ComponentStoreId,
            SizeBytes: report.BackupsBytes,
            Tier: RiskTier.Risk,
            Consequence: texts.DismConsequence,
            Source: FindingSource.PlatformTool));
    }

    private static async Task ScanDriversAsync(
        PlatformToolTexts texts, List<Finding> findings, List<SkippedItem> skipped, CancellationToken ct)
    {
        var run = await ProcessRunner
            .RunAsync(ProcessRunner.SystemTool("pnputil.exe"), PnpDriverStore.EnumerateArguments(), Terpenie, ct)
            .ConfigureAwait(false);

        if (run.ExitCode != 0)
        {
            skipped.Add(new SkippedItem(
                FindingPath.PlatformToolScheme + "pnputil",
                $"список драйверов не получен: код выхода {run.ExitCode}"));
            return;
        }

        foreach (var package in PnpDriverStore.SelectRemovable(PnpDriverStore.Parse(run.StandardOutput)))
        {
            var id = FindingPath.PlatformToolScheme + "pnputil/" + package.PublishedName;

            if (!PnpDriverStore.TryMeasure(package, out var bytes, out var reason))
            {
                skipped.Add(new SkippedItem(id, reason!));
                continue;
            }

            findings.Add(new Finding(
                Name: $"{texts.DriverName}: {package.OriginalName} {package.Version}",
                Path: id,
                SizeBytes: bytes,
                Tier: RiskTier.Risk,
                Consequence: texts.DriverConsequence,
                Source: FindingSource.PlatformTool));
        }
    }
}
