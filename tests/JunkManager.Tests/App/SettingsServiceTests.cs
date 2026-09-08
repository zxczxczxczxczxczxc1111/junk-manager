using FluentAssertions;
using JunkManager.App.Services;
using JunkManager.Deletion;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.App;

[Trait("Class", "Sandbox")]
public sealed class SettingsServiceTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _pesochnica;

    public SettingsServiceTests(SandboxFixture pesochnica) => _pesochnica = pesochnica;

    private string Fayl(string imya) =>
        Path.Combine(_pesochnica.Root, "settings-" + imya + ".json");

    [Fact]
    public async Task Otsutstvuyushchiy_fayl_daet_znacheniya_po_umolchaniyu()
    {
        var sluzhba = new SettingsService(Fayl("net"));

        var nastroyki = await sluzhba.LoadAsync(TestContext.Current.CancellationToken);

        nastroyki.ElevateOnStart.Should().BeTrue("спека называет повышение включённым по умолчанию");
        nastroyki.Mode.Should().Be(DeleteMode.Permanent, "через корзину медленнее и не всё туда влезает");
        nastroyki.BackupRegistryBeforeCleanup.Should().BeFalse();
        nastroyki.RestorePointBeforeRegistry.Should().BeFalse();
        nastroyki.DetectorsEnabled.Should().BeTrue();
        nastroyki.LeftoverSearch.Should().BeTrue();
        nastroyki.HistoryRetentionDays.Should().Be(90);
    }

    [Fact]
    public async Task Sohranyonnoe_chitaetsya_obratno()
    {
        var put = Fayl("krug");
        var sluzhba = new SettingsService(put);

        await sluzhba.SaveAsync(
            new AppSettings
            {
                ElevateOnStart = false,
                Mode = DeleteMode.RecycleBin,
                BackupRegistryBeforeCleanup = true,
                RestorePointBeforeRegistry = true,
                DetectorsEnabled = false,
                LeftoverSearch = false,
                HistoryRetentionDays = 0,
            },
            TestContext.Current.CancellationToken);

        var chitatel = new SettingsService(put);
        var nastroyki = await chitatel.LoadAsync(TestContext.Current.CancellationToken);

        nastroyki.ElevateOnStart.Should().BeFalse();
        nastroyki.Mode.Should().Be(DeleteMode.RecycleBin);
        nastroyki.BackupRegistryBeforeCleanup.Should().BeTrue();
        nastroyki.RestorePointBeforeRegistry.Should().BeTrue();
        nastroyki.DetectorsEnabled.Should().BeFalse();
        nastroyki.LeftoverSearch.Should().BeFalse();
        nastroyki.HistoryRetentionDays.Should().Be(0);
    }

    [Fact]
    public async Task Rezhim_udaleniya_hranitsya_slovom_a_ne_chislom()
    {
        // Число это порядок членов перечисления. Переставленные местами, они
        // молча превращают «в корзину» в «безвозвратно» у всех, кто уже
        // сохранил настройки.
        var put = Fayl("slovom");
        var sluzhba = new SettingsService(put);

        await sluzhba.SaveAsync(
            new AppSettings { Mode = DeleteMode.RecycleBin }, TestContext.Current.CancellationToken);

        var tekst = await File.ReadAllTextAsync(put, TestContext.Current.CancellationToken);

        tekst.Should().Contain("RecycleBin");
    }

    [Fact]
    public async Task Bityy_fayl_ne_teryaet_nastroyki_molcha()
    {
        // Тихо вернуть значения по умолчанию значит переключить режим удаления
        // с корзины на безвозвратное, никому об этом не сказав.
        var put = Fayl("bityy");
        await File.WriteAllTextAsync(put, "{ это не json", TestContext.Current.CancellationToken);

        var sluzhba = new SettingsService(put);
        var deystvie = async () => await sluzhba.LoadAsync(TestContext.Current.CancellationToken);

        await deystvie.Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task Zapis_atomarnaya()
    {
        // Питание пропало посреди записи. Половина файла настроек это тот же
        // битый файл, только полученный своими руками.
        var put = Fayl("atomarno");
        var sluzhba = new SettingsService(put);

        await sluzhba.SaveAsync(new AppSettings(), TestContext.Current.CancellationToken);

        Directory.GetFiles(_pesochnica.Root, "*.tmp").Should().BeEmpty(
            "временный файл обязан быть переименован, а не оставлен рядом");
        File.Exists(put).Should().BeTrue();
    }

    [Fact]
    public async Task Povtornaya_zapis_ne_spotykaetsya_o_svoy_vremennyy_fayl()
    {
        // File.Move без overwrite падает на втором сохранении, а второе
        // сохранение это ровно то, что человек делает чаще первого.
        var put = Fayl("dvazhdy");
        var sluzhba = new SettingsService(put);

        await sluzhba.SaveAsync(new AppSettings(), TestContext.Current.CancellationToken);
        await sluzhba.SaveAsync(
            new AppSettings { HistoryRetentionDays = 30 }, TestContext.Current.CancellationToken);

        var chitatel = new SettingsService(put);
        var nastroyki = await chitatel.LoadAsync(TestContext.Current.CancellationToken);

        nastroyki.HistoryRetentionDays.Should().Be(30);
    }

    [Fact]
    public async Task Ustarevshee_pole_razmera_ignoriruetsya_a_vybor_tochki_sohranyaetsya()
    {
        // A fossil setting must not keep hiding files from beyond the grave.
        var put = Fayl("legacy");
        await File.WriteAllTextAsync(put,
            """{ "MinSizeBytes": 9223372036854775807, "Mode": "RecycleBin", "RestorePointBeforeRegistry": true }""",
            TestContext.Current.CancellationToken);
        var sluzhba = new SettingsService(put);
        var loaded = await sluzhba.LoadAsync(TestContext.Current.CancellationToken);
        loaded.Mode.Should().Be(DeleteMode.RecycleBin);
        loaded.RestorePointBeforeRegistry.Should().BeTrue();
        loaded.BackupRegistryBeforeCleanup.Should().BeFalse();
        await sluzhba.SaveAsync(loaded, TestContext.Current.CancellationToken);
        var text = await File.ReadAllTextAsync(put, TestContext.Current.CancellationToken);
        text.Should().NotContain("MinSizeBytes");
    }

    [Fact]
    public async Task Srok_hraneniya_ne_byvaet_otricatelnym()
    {
        var sluzhba = new SettingsService(Fayl("srok"));

        var deystvie = async () => await sluzhba.SaveAsync(
            new AppSettings { HistoryRetentionDays = -5 }, TestContext.Current.CancellationToken);

        await deystvie.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Tekushchie_znacheniya_obnovlyayutsya_posle_zapisi()
    {
        // Current читают те, кто не держит своей копии: решение о повышении при
        // следующем запуске и служба очистки. Устаревшее значение здесь значит
        // сохранённую настройку, которая не действует.
        var sluzhba = new SettingsService(Fayl("tekushchie"));

        await sluzhba.SaveAsync(
            new AppSettings { Mode = DeleteMode.RecycleBin }, TestContext.Current.CancellationToken);

        sluzhba.Current.Mode.Should().Be(DeleteMode.RecycleBin);
    }
}
