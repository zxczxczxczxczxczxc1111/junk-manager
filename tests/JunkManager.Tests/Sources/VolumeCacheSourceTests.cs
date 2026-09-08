using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Interop;
using JunkManager.Core.Sources.VolumeCache;
using Xunit;

namespace JunkManager.Tests.Sources;

[Trait("Class", "Sandbox")]
public sealed class VolumeCacheSourceTests
{
    private static string RulesDir => Path.Combine(AppContext.BaseDirectory, "rules", "sources");

    [Fact]
    public void FindingPath_shema_obrabotchika_ne_schitaetsya_putem_na_diske()
    {
        // The seeded acceptance run checks every finding path against SafetyGuard.
        // A handler has no path, so it gets a scheme, and the scheme has to be
        // recognisable or the acceptance run declares a forbidden-root hit.
        FindingPath.IsFileSystem(@"C:\Windows\Temp\thing").Should().BeTrue();
        FindingPath.IsFileSystem("volumecache:Update Cleanup").Should().BeFalse();
        FindingPath.IsFileSystem("platformtool:dism/component-store").Should().BeFalse();
        FindingPath.IsFileSystem(@"registry:HKCU\Software\X|Value").Should().BeFalse();
        FindingPath.IsFileSystem("msix:Claude_1.0.0.0_x64__abc").Should().BeFalse();
    }

    [Fact]
    public void Probe_prohodit_po_vsem_obrabotchikam_i_ne_ubivaet_process()
    {
        // This is the whole point of the task. Passing NULL for picb kills the
        // process on BranchCache with an AccessViolationException that no catch
        // can intercept, verified 05.09.2026.
        foreach (var entry in VolumeCacheCatalog.Enumerate())
        {
            var probe = EmptyVolumeCacheInterop.Probe(entry, CancellationToken.None);

            probe.KeyName.Should().Be(entry.KeyName);
            probe.SpaceUsedBytes.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void Probe_neizvestnyy_CLSID_daet_prichinu_a_ne_isklyuchenie()
    {
        var entry = new VolumeCacheEntry(
            "нет такого", new Guid("11111111-2222-3333-4444-555555555555"), "нет такого");

        var probe = EmptyVolumeCacheInterop.Probe(entry, CancellationToken.None);

        probe.Failure.Should().NotBeNullOrWhiteSpace();
        probe.SpaceUsedBytes.Should().Be(0);
    }

    [Fact]
    public async Task ScanAsync_ne_vozvrashchaet_DownloadsFolder_ni_pri_kakih_usloviyah()
    {
        var result = await new VolumeCacheSource().ScanAsync(RulesDir, null, CancellationToken.None);

        result.Findings.Should().NotContain(f => f.Path.Contains("DownloadsFolder", StringComparison.OrdinalIgnoreCase));
        result.Skipped.Should().NotContain(s => s.Path.Contains("DownloadsFolder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanAsync_kazhdaya_nahodka_neset_istochnik_i_consequence()
    {
        var result = await new VolumeCacheSource().ScanAsync(RulesDir, null, CancellationToken.None);

        result.Findings.Should().OnlyContain(f => f.Source == FindingSource.VolumeCache);
        result.Findings.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.Consequence));
        result.Findings.Should().OnlyContain(f => f.SizeBytes > 0,
            "обработчик с нулём байт показывать нечего");
    }

    [Fact]
    public async Task ScanAsync_neopisannyy_obrabotchik_uhodit_v_Skipped_a_ne_v_nahodki()
    {
        var result = await new VolumeCacheSource().ScanAsync(RulesDir, null, CancellationToken.None);
        var described = VolumeCacheDescriptions.Load(RulesDir);

        foreach (var finding in result.Findings)
        {
            var key = finding.Path[FindingPath.VolumeCacheScheme.Length..];
            described.Should().ContainKey(key,
                "показывать находку без описания запрещено");
        }
    }

    [Fact]
    public async Task ScanAsync_otmena_ostanavlivaet_i_pomechaet_rezultat()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await new VolumeCacheSource().ScanAsync(RulesDir, null, cts.Token);

        result.Cancelled.Should().BeTrue();
        result.Findings.Should().BeEmpty();
    }
}
