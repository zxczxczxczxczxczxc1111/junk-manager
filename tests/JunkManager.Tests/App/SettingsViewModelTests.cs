using FluentAssertions;
using JunkManager.App.Services;
using JunkManager.App.ViewModels;
using JunkManager.Deletion;
using Xunit;

namespace JunkManager.Tests.App;

/// <summary>
/// The settings screen, without a disk. The gate that matters here is not the
/// round trip: it is that a saved setting reaches the thing it names.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class SettingsViewModelTests
{
    /// <summary>Настройки в памяти. Умеет отказать при чтении и при записи.</summary>
    private sealed class Zaglushka(AppSettings? nachalnye = null) : ISettingsService
    {
        public Exception? OtkazChteniya { get; set; }

        public Exception? OtkazZapisi { get; set; }

        public int SkolkoRazPisali { get; private set; }

        public string FilePath => "C:\\net-takogo\\settings.json";

        public AppSettings Current { get; private set; } = nachalnye ?? new AppSettings();

        public Task<AppSettings> LoadAsync(CancellationToken ct) =>
            OtkazChteniya is { } beda ? Task.FromException<AppSettings>(beda) : Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken ct)
        {
            if (OtkazZapisi is { } beda)
            {
                return Task.FromException(beda);
            }

            SkolkoRazPisali++;
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private static async Task<SettingsViewModel> Zagruzit(
        ISettingsService sluzhba, bool elevated = true, Action<AppSettings>? primenit = null)
    {
        var model = new SettingsViewModel(sluzhba, primenit);
        await model.ZagruzitAsync(elevated, TestContext.Current.CancellationToken);
        return model;
    }

    [Fact]
    public async Task Rezervirovanie_sohranyaetsya_srazu_i_ne_vklyuchaet_druguyu_opciyu()
    {
        // The checkbox does not get to postpone its only job until next launch.
        var sluzhba = new Zaglushka();
        var model = await Zagruzit(sluzhba);
        sluzhba.SkolkoRazPisali.Should().Be(0, "загрузка не должна перезаписывать выбор");
        model.BackupRegistryBeforeCleanup = true;
        await model.PendingSave;
        sluzhba.Current.BackupRegistryBeforeCleanup.Should().BeTrue();
        sluzhba.Current.RestorePointBeforeRegistry.Should().BeFalse();
        model.Sohraneno.Should().BeTrue("синхронная запись тоже должна подтверждаться на экране");
        model.RestorePointBeforeRegistry = true;
        await model.PendingSave;
        sluzhba.Current.BackupRegistryBeforeCleanup.Should().BeTrue();
        sluzhba.Current.RestorePointBeforeRegistry.Should().BeTrue();
        model.BackupRegistryBeforeCleanup = false;
        await model.PendingSave;
        sluzhba.Current.BackupRegistryBeforeCleanup.Should().BeFalse();
        sluzhba.Current.RestorePointBeforeRegistry.Should().BeTrue();
    }

    [Fact]
    public async Task Otkaz_mgnovennoy_zapisi_viden_i_ne_menyaet_deystvuyushchiy_vybor()
    {
        var sluzhba = new Zaglushka { OtkazZapisi = new IOException("файл занят") };
        var model = await Zagruzit(sluzhba);
        model.BackupRegistryBeforeCleanup = true;
        await model.PendingSave;
        sluzhba.Current.BackupRegistryBeforeCleanup.Should().BeFalse();
        model.State.Phase.Should().Be(ScreenPhase.Error);
        model.State.ErrorTitle.Should().Contain("не сохранились");
    }

    [Fact]
    public async Task Znacheniya_iz_fayla_stanovyatsya_vyborom_na_ekrane()
    {
        var model = await Zagruzit(new Zaglushka(new AppSettings
        {
            ElevateOnStart = false,
            Mode = DeleteMode.RecycleBin,
            BackupRegistryBeforeCleanup = true,
            RestorePointBeforeRegistry = true,
            DetectorsEnabled = false,
            DeepScan = true,
            LeftoverSearch = false,
            HistoryRetentionDays = 30,
        }));

        model.State.Phase.Should().Be(ScreenPhase.Ready);
        model.ElevateOnStart.Should().BeFalse();
        model.Rezhim!.Znachenie.Should().Be(DeleteMode.RecycleBin);
        model.BackupRegistryBeforeCleanup.Should().BeTrue();
        model.RestorePointBeforeRegistry.Should().BeTrue();
        model.DetectorsEnabled.Should().BeFalse();
        model.DeepScan.Should().BeTrue();
        model.LeftoverSearch.Should().BeFalse();
        model.Hranenie!.Znachenie.Should().Be(30);
    }

    [Fact]
    public async Task Neznakomyy_srok_hraneniya_pokazyvaetsya_kak_est()
    {
        var model = await Zagruzit(new Zaglushka(new AppSettings { HistoryRetentionDays = 7 }));

        model.Hranenie!.Znachenie.Should().Be(7);
        model.Hranenie.Podpis.Should().Be("7 дней");
    }

    [Fact]
    public async Task Sohranenie_pishet_vse_sem_znacheniy()
    {
        var sluzhba = new Zaglushka();
        var model = await Zagruzit(sluzhba);

        model.ElevateOnStart = false;
        model.Rezhim = model.RezhimVarianty.Single(v => v.Znachenie == DeleteMode.RecycleBin);
        model.RestorePointBeforeRegistry = true;
        model.BackupRegistryBeforeCleanup = true;
        model.DetectorsEnabled = false;
        model.DeepScan = true;
        model.LeftoverSearch = false;
        model.Hranenie = model.HranenieVarianty.Single(v => v.Znachenie == 0);

        await model.SohranitCommand.ExecuteAsync(null);

        sluzhba.Current.ElevateOnStart.Should().BeFalse();
        sluzhba.Current.Mode.Should().Be(DeleteMode.RecycleBin);
        sluzhba.Current.RestorePointBeforeRegistry.Should().BeTrue();
        sluzhba.Current.BackupRegistryBeforeCleanup.Should().BeTrue();
        sluzhba.Current.DetectorsEnabled.Should().BeFalse();
        sluzhba.Current.DeepScan.Should().BeTrue();
        sluzhba.Current.LeftoverSearch.Should().BeFalse();
        sluzhba.Current.HistoryRetentionDays.Should().Be(0);
    }

    [Fact]
    public async Task Sohranennoe_primenyaetsya_srazu_a_ne_posle_perezapuska()
    {
        // Ворота против «построено, но не подключено». Настройка, которая
        // ложится в файл и не доезжает до службы очистки, это переключатель,
        // который ничего не переключает до следующего запуска.
        AppSettings? primenennye = null;
        var model = await Zagruzit(new Zaglushka(), primenit: n => primenennye = n);

        model.Rezhim = model.RezhimVarianty.Single(v => v.Znachenie == DeleteMode.RecycleBin);
        await model.SohranitCommand.ExecuteAsync(null);

        primenennye.Should().NotBeNull();
        primenennye!.Mode.Should().Be(DeleteMode.RecycleBin);
    }

    [Fact]
    public async Task Neudachnaya_zapis_nichego_ne_primenyaet()
    {
        // Иначе служба очистки берёт режим, которого нет в файле, и следующий
        // запуск молча возвращает прежний.
        var primenili = 0;
        var sluzhba = new Zaglushka { OtkazZapisi = new IOException("файл занят") };
        var model = await Zagruzit(sluzhba, primenit: _ => primenili++);

        await model.SohranitCommand.ExecuteAsync(null);

        primenili.Should().Be(0);
        model.State.Phase.Should().Be(ScreenPhase.Error);
        model.State.ErrorTitle.Should().Contain("не сохранились");
    }

    [Fact]
    public async Task Bez_povysheniya_stoit_plashka()
    {
        var model = await Zagruzit(new Zaglushka(), elevated: false);

        model.Elevated.Should().BeFalse();
        model.State.HasRestriction.Should().BeTrue();
        model.State.RestrictionText.Should().Contain("администратор");
    }

    [Fact]
    public async Task S_povysheniem_plashki_net()
    {
        var model = await Zagruzit(new Zaglushka(), elevated: true);

        model.State.HasRestriction.Should().BeFalse();
    }

    [Fact]
    public async Task Bityy_fayl_daet_oshibku_a_ne_tihie_umolchaniya()
    {
        var sluzhba = new Zaglushka(new AppSettings { Mode = DeleteMode.RecycleBin })
        {
            OtkazChteniya = new System.Text.Json.JsonException("неожиданный символ на позиции 12"),
        };

        var model = await Zagruzit(sluzhba);

        model.State.Phase.Should().Be(ScreenPhase.Error);
        model.State.ErrorBody.Should().Contain("12");
        model.State.ErrorBody.Should().Contain("не изменились");
    }

    [Fact]
    public async Task Povtornaya_zagruzka_ne_mnozhit_varianty()
    {
        var sluzhba = new Zaglushka(new AppSettings { HistoryRetentionDays = 7 });
        var model = new SettingsViewModel(sluzhba, null);

        await model.ZagruzitAsync(true, TestContext.Current.CancellationToken);
        var bylo = model.HranenieVarianty.Count;
        await model.ZagruzitAsync(true, TestContext.Current.CancellationToken);

        model.HranenieVarianty.Should().HaveCount(bylo);
    }
}
