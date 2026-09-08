using JunkManager.Core.Apps;
using JunkManager.Core.Rules;
using JunkManager.Core.Sources.Detect;
using JunkManager.Core.Sources.Pattern;
using JunkManager.Core.Sources.Platform;
using JunkManager.Core.Sources.VolumeCache;

namespace JunkManager.Core.Scanning;

/// <param name="Pravila">
/// The JSON rule files. On by default because it is the only source with a
/// written answer for every path it proposes.
/// </param>
/// <param name="SkladWindows">
/// Windows' own cleanup handlers. Read-only here: this asks each handler how
/// much it holds and never runs a purge.
/// </param>
/// <param name="PlatformennyeUtility">
/// DISM and pnputil. Off by default: both are slow, both need administrator,
/// and a scan that silently takes four minutes is a scan people stop running.
/// </param>
/// <param name="PoiskPoObraztsu">
/// Mask search. Off by default because a mask cannot know what a file is for,
/// which is why everything it produces is Risk.
/// </param>
/// <param name="Obnaruzhiteli">
/// Detectors with no list behind them: Electron caches, abandoned folders.
/// </param>
/// <param name="Programmy">
/// Traces of programs that are no longer installed.
/// </param>
/// <param name="KorniObnaruzhiteley">
/// Where the detectors look. Null means the two profile roots, which is what
/// the product uses; the tests point them at a sandbox instead.
/// </param>
/// <param name="ObrazcyOverride">
/// Pattern-scan settings. Null means <see cref="PatternScanOptions.Default"/>.
/// </param>
/// <param name="DneyBezIzmeneniy">
/// How long a folder has to sit untouched before the abandoned-folder detector
/// will name it.
/// </param>
public sealed record ScanPlan(
    bool Pravila = true,
    bool SkladWindows = true,
    bool PlatformennyeUtility = false,
    bool PoiskPoObraztsu = false,
    bool Obnaruzhiteli = true,
    bool Programmy = true,
    IReadOnlyList<string>? KorniObnaruzhiteley = null,
    PatternScanOptions? ObrazcyOverride = null,
    int DneyBezIzmeneniy = 180)
{
    /// <summary>
    /// Everything off. Exists so a test can turn on exactly one source and
    /// attribute what comes back, and so the "disabled means silent" gate has
    /// something to assert against.
    /// </summary>
    public static ScanPlan Nichego { get; } = new(
        Pravila: false,
        SkladWindows: false,
        PlatformennyeUtility: false,
        PoiskPoObraztsu: false,
        Obnaruzhiteli: false,
        Programmy: false);

    /// <summary>
    /// Everything the product can reach without administrator rights and
    /// without a wait a person would call a hang.
    /// </summary>
    public static ScanPlan PoUmolchaniyu { get; } = new();

    /// <summary>The two roots the detectors walk when the caller names none.</summary>
    public static IReadOnlyList<string> KorniProfilya { get; } =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    ];
}

/// <summary>
/// One call that asks every enabled source and merges the answers.
/// </summary>
/// <remarks>
/// <para>
/// Written 05.09.2026 after the seeded acceptance found 13 baits out of 18. All
/// five misses were sources that exist, compile, and have green unit tests, and
/// that nothing called. A unit test proves a mechanism works; only a pass like
/// this proves it is wired in. "Built" and "connected" are different words for a
/// reason.
/// </para>
/// <para>
/// Merging goes through <see cref="SliyanieIstochnikov"/> rather than list
/// concatenation. The sources overlap on purpose: a rule takes a browser cache
/// by path, the Electron detector recognises the same directory by shape, and
/// the mask search finds a file inside it. Added up, 5 GB on disk is shown as
/// 15 GB, and a person decides by that number.
/// </para>
/// <para>
/// A source that throws is recorded and skipped, never allowed to end the pass.
/// A single broken source taking the other five with it turns an incomplete list
/// into an empty one, and those look identical to the person reading the screen.
/// </para>
/// </remarks>
public static class PolnyyProhod
{
    /// <summary>
    /// Where the per-source configuration lives: volume-caches.json,
    /// platform-tools.json, registry-branches.json. A subdirectory rather than
    /// the rules root because these are not rules: nothing here proposes a path
    /// to delete, they only supply the wording for what a source found. Named
    /// once, because passing the wrong level answers "файл описаний не найден"
    /// and looks exactly like a source that found nothing.
    /// </summary>
    public const string KatalogIstochnikov = "sources";

    public static Task<ScanResult> ScanAsync(
        ScanPlan plan,
        IProgress<string>? progress,
        CancellationToken ct) => ScanCoreAsync(plan, null, progress, ct);

    /// <summary>Explicit fixture seam; the product uses the embedded overload.</summary>
    public static Task<ScanResult> ScanAsync(
        ScanPlan plan,
        string rulesDirectory,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesDirectory);
        return ScanCoreAsync(plan, rulesDirectory, progress, ct);
    }

    private static async Task<ScanResult> ScanCoreAsync(
        ScanPlan plan,
        string? rulesDirectory,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var itog = ScanResult.Empty;
        var otmeneno = false;

        async Task Dobavit(string imya, Func<Task<ScanResult>> istochnik)
        {
            if (ct.IsCancellationRequested)
            {
                otmeneno = true;
                return;
            }

            progress?.Report(imya);

            ScanResult chast;

            try
            {
                chast = await istochnik().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                otmeneno = true;
                return;
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception
                or InvalidOperationException
                or RuleFormatException)
            {
                // Named in Skipped rather than swallowed: "источник отказал" and
                // "источник ничего не нашёл" need opposite fixes, and the person
                // cannot tell them apart from an empty list.
                itog = itog with
                {
                    Skipped = [.. itog.Skipped, new SkippedItem(imya, $"источник отказал: {ex.Message}")],
                };
                return;
            }

            otmeneno |= chast.Cancelled;
            progress?.Report("Собираем результаты: " + imya);
            itog = SliyanieIstochnikov.Slit(itog, chast.Findings, chast.Skipped, ct);
        }

        if (plan.Pravila)
        {
            await Dobavit("правила", () =>
                new FileScanner().ScanAsync(rulesDirectory is null
                    ? BuiltInCatalog.LoadRules() : RuleLoader.Load(rulesDirectory), progress, ct))
                .ConfigureAwait(false);
        }

        if (plan.SkladWindows)
        {
            await Dobavit("склад Windows", () =>
                new VolumeCacheSource().ScanAsync(
                    rulesDirectory is null ? null : Path.Combine(rulesDirectory, KatalogIstochnikov), progress, ct))
                .ConfigureAwait(false);
        }

        if (plan.PlatformennyeUtility)
        {
            await Dobavit("платформенные утилиты", () =>
                new PlatformToolSource().ScanAsync(
                    rulesDirectory is null ? null : Path.Combine(rulesDirectory, KatalogIstochnikov), progress, ct))
                .ConfigureAwait(false);
        }

        if (plan.Obnaruzhiteli)
        {
            var korni = plan.KorniObnaruzhiteley ?? ScanPlan.KorniProfilya;

            await Dobavit("кэши Electron", () =>
                new ElectronCacheDetector().ScanAsync(korni, ct)).ConfigureAwait(false);

            await Dobavit("брошенные каталоги", () =>
                new AbandonedFolderDetector().ScanAsync(korni, plan.DneyBezIzmeneniy, ct))
                .ConfigureAwait(false);
        }

        if (plan.PoiskPoObraztsu)
        {
            var nastroyki = plan.ObrazcyOverride ?? PatternScanOptions.Default();

            await Dobavit("поиск по образцу", () =>
                new PatternScanner().ScanAsync(nastroyki, progress, ct)).ConfigureAwait(false);
        }

        if (plan.Programmy)
        {
            await Dobavit("следы программ", () => Task.Run(() =>
            {
                var siroty = LeftoverFinder.FindOrphans(
                    InstalledProgramReader.Read(), MsixPackageReader.Read(), idleDays: 90);

                return Task.FromResult(new ScanResult(
                    [], [.. siroty.Skipped, .. siroty.Found.Select(item =>
                        new SkippedItem(item.Path, "принадлежность удалённой программе не подтверждена; " + item.Basis))]));
            }, ct)).ConfigureAwait(false);
        }

        return otmeneno || ct.IsCancellationRequested ? itog with { Cancelled = true } : itog;
    }
}
