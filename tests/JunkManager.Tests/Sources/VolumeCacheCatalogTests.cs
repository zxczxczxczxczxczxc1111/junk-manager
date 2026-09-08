using FluentAssertions;
using JunkManager.Core.Rules;
using JunkManager.Core.Sources.VolumeCache;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Sources;

[Trait("Class", "Sandbox")]
public sealed class VolumeCacheCatalogTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _sandbox;

    public VolumeCacheCatalogTests(SandboxFixture sandbox) => _sandbox = sandbox;

    private static VolumeCacheEntry Entry(string key, string clsid) =>
        new(key, new Guid(clsid), key);

    // The generic handler CLSID, verified on DERMO 05.09.2026: twelve keys share
    // it, DownloadsFolder among them.
    private const string Obshchiy = "{C0E13E61-0CC6-11d1-BBB6-0060978B2AE6}";

    [Fact]
    public void Filter_DownloadsFolder_ne_popadaet_v_vydachu()
    {
        var raw = new[] { Entry("DownloadsFolder", Obshchiy) };

        VolumeCacheCatalog.Filter(raw).Should().BeEmpty(
            "папка Загрузки это файлы человека, а не мусор");
    }

    [Fact]
    public void Filter_ne_vybrasyvaet_sosedey_s_tem_zhe_CLSID()
    {
        // The trap this test exists for: blacklisting by CLSID instead of by key
        // name silently kills eleven working handlers, and the count still looks
        // plausible.
        var raw = new[]
        {
            Entry("DownloadsFolder", Obshchiy),
            Entry("System error memory dump files", Obshchiy),
            Entry("Windows Defender", Obshchiy),
            Entry("Update Cleanup", "{606B3777-3051-401F-974A-E66ACA82A3A3}"),
        };

        var kept = VolumeCacheCatalog.Filter(raw).Select(e => e.KeyName).ToList();

        kept.Should().BeEquivalentTo(
            ["System error memory dump files", "Windows Defender", "Update Cleanup"]);
    }

    [Theory]
    [InlineData("downloadsfolder")]
    [InlineData("DOWNLOADSFOLDER")]
    [InlineData(" DownloadsFolder ")]
    public void Filter_chernyy_spisok_ne_obhoditsya_regictrom_i_probelami(string key)
    {
        VolumeCacheCatalog.Filter([Entry(key, Obshchiy)]).Should().BeEmpty();
    }

    [Fact]
    public void Enumerate_na_zhivoy_mashine_daet_obrabotchiki_i_ni_odnogo_pustogo_CLSID()
    {
        var all = VolumeCacheCatalog.Enumerate();

        all.Should().NotBeEmpty("ключ VolumeCaches есть на любой поддерживаемой Windows");
        all.Should().OnlyContain(e => e.Clsid != Guid.Empty);
        all.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.KeyName));
        all.Select(e => e.KeyName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Load_opisanie_bez_consequence_ne_gruzitsya()
    {
        var dir = _sandbox.CreateDirectory("vc-no-consequence");
        File.WriteAllText(Path.Combine(dir, "volume-caches.json"), """
        { "handlers": [
          { "keyName": "Update Cleanup", "name": "Вытесненные обновления",
            "tier": "Risk", "consequence": "   " } ] }
        """);

        var act = () => VolumeCacheDescriptions.Load(dir);

        act.Should().Throw<RuleFormatException>().WithMessage("*consequence*");
    }

    [Fact]
    public void Load_opisanie_dlya_DownloadsFolder_zapreshcheno()
    {
        // Belt and braces: even if somebody describes it in the file with good
        // intentions, the file must refuse to load rather than quietly enable it.
        var dir = _sandbox.CreateDirectory("vc-downloads");
        File.WriteAllText(Path.Combine(dir, "volume-caches.json"), """
        { "handlers": [
          { "keyName": "DownloadsFolder", "name": "Загрузки",
            "tier": "Safe", "consequence": "скачанное пропадёт" } ] }
        """);

        var act = () => VolumeCacheDescriptions.Load(dir);

        act.Should().Throw<RuleFormatException>().WithMessage("*DownloadsFolder*");
    }

    [Fact]
    public void Load_nastoyashchiy_fayl_opisyvaet_vse_krupnye_obrabotchiki()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "rules", "sources");

        var described = VolumeCacheDescriptions.Load(dir);

        // The handlers that carry gigabytes on a real machine. A missing entry
        // here means the product finds the space and refuses to show it.
        described.Keys.Should().Contain(KrupnyeObrabotchiki);
    }

    /// <summary>
    /// Отдельным полем, а не литералом в аргументе: массив констант прямо в
    /// вызове это CA1861, а при TreatWarningsAsErrors это провал сборки.
    /// </summary>
    private static readonly string[] KrupnyeObrabotchiki =
    [
        "Update Cleanup",
        "Previous Installations",
        "Delivery Optimization Files",
        "Device Driver Packages",
        "Windows ESD installation files",
        "System error memory dump files",
        "Thumbnail Cache",
        "D3D Shader Cache",
        "Temporary Files",
        "Windows Error Reporting Files",
        "Old ChkDsk Files",
        "Windows Upgrade Log Files",
    ];
    [Fact]
    public void SystemVolume_eto_koren_kataloga_Windows_a_ne_bukva_C()
    {
        // Обработчики инициализируются НА ТОМ. Вбитая буква «C» на машине, где
        // Windows стоит на другом диске, измеряет один том, а очищает другой, и
        // обработчик отчитается успехом в обоих случаях: с его стороны запрос
        // был совершенно правильный.
        var okno = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        VolumeCacheCatalog.SystemVolume.Should().Be(Path.GetPathRoot(okno));
        VolumeCacheCatalog.SystemVolume.Should().EndWith(Path.DirectorySeparatorChar.ToString());
        Directory.Exists(VolumeCacheCatalog.SystemVolume).Should().BeTrue();
    }

}
