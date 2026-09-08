using FluentAssertions;
using JunkManager.App.Services;
using JunkManager.App.ViewModels;
using JunkManager.Core;
using JunkManager.Core.Registry;
using JunkManager.Tests.Infrastructure;
using Microsoft.Win32;
using Xunit;

namespace JunkManager.Tests.App;

/// <summary>
/// Экран реестра без реестра. Проверяется ровно одно: что показано, когда
/// найдено, когда не найдено, когда не читалось и когда остановили.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class RegistryViewModelTests : IDisposable
{
    private readonly List<RegistryViewModel> _sozdannye = [];

    /// <summary>
    /// Закрывает всё созданное набором. С задачи 6 у модели свой источник
    /// отмены, то есть она одноразовая, и анализатор требует закрытия
    /// (CA2000). Тот же приём, что в FilesViewModelTests: держать «using» на
    /// два десятка проверок дороже, чем один список.
    /// </summary>
    public void Dispose()
    {
        foreach (var model in _sozdannye)
        {
            model.Dispose();
        }
    }

    private RegistryViewModel Sobrat(IRegistryScanService reestr, bool elevated = true) =>
        Zapomnit(new RegistryViewModel(
            reestr, new OchistkaReestraZaglushka(), new BekapyZaglushka(), elevated));

    private RegistryViewModel SobratS(
        IRegistryScanService reestr, OchistkaReestraZaglushka ochistka) =>
        Zapomnit(new RegistryViewModel(
            reestr, ochistka, new BekapyZaglushka(), elevated: true));

    private RegistryViewModel SobratSBekapami(
        IRegistryScanService reestr, BekapyZaglushka bekapy) =>
        Zapomnit(new RegistryViewModel(
            reestr, new OchistkaReestraZaglushka(), bekapy, elevated: true));

    private RegistryViewModel Zapomnit(RegistryViewModel model)
    {
        _sozdannye.Add(model);
        return model;
    }

    private static ReestrZaglushka Odna(string imya = "Ushedshee") =>
        new(new RegistryScanResult(
            [ReestrObraztsy.Nahodka(RegistryHive.CurrentUser, imya)], [], false));

    [Fact]
    public async Task Nastoyashchie_zapisi_dohodyat_do_ekrana()
    {
        var model = Sobrat(new ReestrZaglushka(new RegistryScanResult(
            [ReestrObraztsy.Nahodka(RegistryHive.CurrentUser, "Ushedshee")], [], false)));

        await model.ProveritAsync(TestContext.Current.CancellationToken);

        model.State.Phase.Should().Be(ScreenPhase.Ready);
        model.Rows.Should().HaveCount(1);
        model.Rows[0].MissingTarget.Should().Be(@"C:\Program Files\Ushedshee\run.exe");
    }

    [Fact]
    public async Task Chistyy_reestr_pokazyvaet_polozhitelnyy_itog_i_povtornuyu_proverku()
    {
        // A clean result deserves an actual result, not a lecture from an empty rectangle.
        var model = Sobrat(new ReestrZaglushka(new RegistryScanResult([], [], false)));

        await model.ProveritAsync(TestContext.Current.CancellationToken);

        model.State.Phase.Should().Be(ScreenPhase.Empty);
        model.State.EmptyTitle.Should().Be("Всё в порядке");
        model.State.EmptyBody.Should().Contain("Очистка не требуется");
        model.State.EmptyAction.Should().Be("Проверить ещё раз");
        model.State.EmptyCommand.Should().BeSameAs(model.ProveritCommand);
    }

    [Fact]
    public async Task Expected_exclusions_do_not_turn_a_clean_scan_into_a_warning()
    {
        var model = Sobrat(new ReestrZaglushka(new RegistryScanResult([], [
            new SkippedItem("fixture", "нет файловой ссылки") { IsExpectedExclusion = true }] )));
        await model.ProveritAsync(TestContext.Current.CancellationToken);
        model.State.EmptyTitle.Should().Be("Всё в порядке");
        model.State.HasRestriction.Should().BeFalse();
        model.State.RestrictionDetails.Should().BeNull();
    }

    [Fact]
    public async Task Missing_rules_never_claim_the_registry_is_clean()
    {
        var model = Sobrat(new ReestrZaglushka(new RegistryScanResult([], []), vetok: 0));
        await model.ProveritAsync(TestContext.Current.CancellationToken);
        model.State.Phase.Should().Be(ScreenPhase.Error);
        model.State.RetryCommand.Should().BeSameAs(model.ProveritCommand);
    }

    [Fact]
    public async Task Actual_failures_have_a_compact_summary_and_separate_details()
    {
        var skips = Enumerable.Range(0, 337).Select(index => new SkippedItem($"HKLM\\fixture{index}", "нет доступа")).ToArray();
        var model = Sobrat(new ReestrZaglushka(new RegistryScanResult([], skips)));
        await model.ProveritAsync(TestContext.Current.CancellationToken);
        model.State.EmptyTitle.Should().NotBe("Всё в порядке");
        model.State.RestrictionText.Should().Contain("337").And.NotContain("HKLM");
        model.State.RestrictionText!.Length.Should().BeLessThan(160);
        model.State.RestrictionDetails.Should().Contain("HKLM\\fixture336");
        model.State.RestrictionDetailsOpen.Should().BeFalse();
        model.State.ToggleRestrictionDetailsCommand.Execute(null);
        model.State.RestrictionDetailsOpen.Should().BeTrue();
        model.State.EmptyCommand.Should().BeSameAs(model.ProveritCommand);
        model.Prinyat(new RegistryScanResult([], []));
        model.State.RestrictionDetails.Should().BeNull();
        model.State.RestrictionDetailsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Chistyy_reestr_nazyvaet_chislo_osmotrennyh_vetok()
    {
        // «Найдено 0» при нуле веток это сломанная выкладка, а не чистый
        // реестр, и отличить их человеку больше нечем.
        var model = Sobrat(new ReestrZaglushka(new RegistryScanResult([], [], false), vetok: 7));

        await model.ProveritAsync(TestContext.Current.CancellationToken);

        model.State.EmptyBody.Should().Contain("7");
    }

    [Fact]
    public async Task Propuski_nazyvayutsya_chislom_a_ne_proglatyvayutsya()
    {
        // Пропуск это «не проверено». Ветка, которую не прочитали, и ветка, в
        // которой чисто, дают на экране один и тот же ноль находок, и без
        // плашки их не различить.
        var model = Sobrat(new ReestrZaglushka(new RegistryScanResult(
            [],
            [new SkippedItem(@"HKLM\SOFTWARE\...\Run", "нет прав на чтение ветки: отказано")],
            false)));

        await model.ProveritAsync(TestContext.Current.CancellationToken);

        model.State.HasRestriction.Should().BeTrue();
        model.State.RestrictionText.Should().Contain("не проверен");
        model.State.EmptyTitle.Should().NotContain("Чисто",
            "чисто это доказанное отсутствие, а одну ветку доказать не удалось");
    }

    [Fact]
    public async Task Prervannaya_proverka_ne_nazyvaetsya_chistotoy()
    {
        var model = Sobrat(new ReestrZaglushka(new RegistryScanResult([], [], Cancelled: true)));

        await model.ProveritAsync(TestContext.Current.CancellationToken);

        model.State.Phase.Should().Be(ScreenPhase.Empty);
        model.State.EmptyTitle.Should().Contain("становлен");
    }

    [Fact]
    public async Task Bez_prav_nahodka_mashiny_pokazana_no_otmetit_ee_nelzya()
    {
        // Решение 3 из шапки плана. HKLM читается без прав, значит прятать
        // найденное нельзя. Но отметка означала бы кнопку, которая обещает
        // удалить и не может.
        var model = Sobrat(
            new ReestrZaglushka(new RegistryScanResult(
                [
                    ReestrObraztsy.Nahodka(RegistryHive.LocalMachine, "Obshchee"),
                    ReestrObraztsy.Nahodka(RegistryHive.CurrentUser, "Svoe"),
                ],
                [], false)),
            elevated: false);

        await model.ProveritAsync(TestContext.Current.CancellationToken);

        model.Rows.Should().HaveCount(2);
        model.Rows.Single(r => r.Hive == "HKLM").CanSelect.Should().BeFalse();
        model.Rows.Single(r => r.Hive == "HKCU").CanSelect.Should().BeTrue();
        model.State.RestrictionText.Should().Contain("администратор");
    }

    [Fact]
    public async Task Otmetka_na_stroke_bez_prav_ne_stavitsya_dazhe_esli_poprosit()
    {
        // Разметка спросит именно так: привязка флажка присваивает true. Отказ
        // обязан жить в модели, потому что исключение в привязке WPF либо
        // проглатывается, либо роняет окно.
        var model = Sobrat(
            new ReestrZaglushka(new RegistryScanResult(
                [ReestrObraztsy.Nahodka(RegistryHive.LocalMachine, "Obshchee")], [], false)),
            elevated: false);

        await model.ProveritAsync(TestContext.Current.CancellationToken);

        model.Rows[0].IsSelected = true;

        model.Rows[0].IsSelected.Should().BeFalse("прав на ветку машины нет");
    }

    [Fact]
    public async Task Vid_i_rod_zapisi_dohodyat_do_stroki()
    {
        // 32- и 64-битная проекции дают две строки с ОДИНАКОВЫМ путём, и без
        // пометки человек читает это как дубль. А «значение» против «ключ
        // целиком» это разница, ради которой список вообще читают.
        var model = Sobrat(new ReestrZaglushka(new RegistryScanResult(
            [ReestrObraztsy.Klyuch(RegistryHive.CurrentUser, "ushedshaya.exe")], [], false)));

        await model.ProveritAsync(TestContext.Current.CancellationToken);

        model.Rows[0].KindLabel.Should().Be("ключ целиком");
        model.Rows[0].ViewLabel.Should().Be("Registry64");
    }

    [Fact]
    public async Task Otkaz_v_dostupe_eto_sostoyanie_oshibki_a_ne_pustoty()
    {
        var model = Sobrat(new PadayushchiyReestr());

        await model.ProveritAsync(TestContext.Current.CancellationToken);

        model.State.Phase.Should().Be(ScreenPhase.Error);
    }

    [Fact]
    public async Task Ostanovlennaya_proverka_ne_ostaetsya_v_zagruzke()
    {
        // Экран, застрявший в загрузке, выглядит как работающий.
        var model = Sobrat(new OtmenennyyReestr());

        await model.ProveritAsync(TestContext.Current.CancellationToken);

        model.State.Phase.Should().Be(ScreenPhase.Empty);
        model.State.EmptyTitle.Should().Contain("становлен");
    }

    [Fact]
    public async Task Povtornaya_proverka_ne_udvaivaet_spisok()
    {
        var model = Sobrat(new ReestrZaglushka(new RegistryScanResult(
            [ReestrObraztsy.Nahodka(RegistryHive.CurrentUser, "Ushedshee")], [], false)));

        await model.ProveritAsync(TestContext.Current.CancellationToken);
        await model.ProveritAsync(TestContext.Current.CancellationToken);

        model.Rows.Should().HaveCount(1);
    }

    [Fact]
    public void Plashka_ogranicheniya_snimaetsya_kogda_prichiny_bolshe_net()
    {
        // Плашка, пережившая свою причину, висит над чистым списком до
        // перезапуска и врёт про непроверенные ветки. Тот же класс дефекта,
        // что ловила проверка вокруг заглушки, которой больше нет.
        var model = Sobrat(new ReestrZaglushka(new RegistryScanResult([], [], false)));

        model.Prinyat(new RegistryScanResult(
            [],
            [new SkippedItem(@"HKLM\SOFTWARE\...\Run", "нет прав на чтение ветки: отказано")],
            false));

        model.State.HasRestriction.Should().BeTrue();

        model.Prinyat(new RegistryScanResult(
            [ReestrObraztsy.Nahodka(RegistryHive.CurrentUser, "Ushedshee")], [], false));

        model.State.HasRestriction.Should().BeFalse("непрочитанных веток больше нет");
    }
    [Fact]
    public async Task Bez_otmetok_k_podtverzhdeniyu_ne_perehodim()
    {
        var model = Sobrat(Odna());

        await model.ProveritAsync(TestContext.Current.CancellationToken);

        model.CanGoToConfirm.Should().BeFalse("ничего не отмечено заранее");
        await model.KPodtverzhdeniyuAsync(TestContext.Current.CancellationToken);
        model.Step.Should().Be(FlowStep.Selection);
    }

    [Fact]
    public async Task Udalyaetsya_tolko_otmechennoe()
    {
        // Проверка ловит класс дефектов «удалили весь список вместо
        // выбранного». Он не виден на экране: список после удаления пуст в
        // обоих случаях.
        var ochistka = new OchistkaReestraZaglushka();
        var model = SobratS(
            new ReestrZaglushka(new RegistryScanResult(
                [
                    ReestrObraztsy.Nahodka(RegistryHive.CurrentUser, "Pervoe"),
                    ReestrObraztsy.Nahodka(RegistryHive.CurrentUser, "Vtoroe"),
                ],
                [], false)),
            ochistka);

        await model.ProveritAsync(TestContext.Current.CancellationToken);
        model.Rows[0].IsSelected = true;
        await model.KPodtverzhdeniyuAsync(TestContext.Current.CancellationToken);
        model.Confirmed = true;
        await model.UdalitAsync(TestContext.Current.CancellationToken);

        ochistka.Prinyatye.Should().HaveCount(1);
        ochistka.Prinyatye[0].ValueName.Should().Be("Pervoe");
    }

    [Fact]
    public async Task Knopka_udaleniya_ne_rabotaet_poka_spisok_ne_dolistan()
    {
        // Execute у команды НЕ спрашивает CanExecute: его спрашивает кнопка.
        // Любой другой вызов, от горячей клавиши до чужого кода, прошёл бы мимо
        // ворот прямо в удаление. Тот же довод записан на FilesViewModel.
        var ochistka = new OchistkaReestraZaglushka();
        var model = SobratS(Odna("Pervoe"), ochistka);

        await model.ProveritAsync(TestContext.Current.CancellationToken);
        model.Rows[0].IsSelected = true;
        await model.KPodtverzhdeniyuAsync(TestContext.Current.CancellationToken);

        await model.UdalitAsync(TestContext.Current.CancellationToken);

        ochistka.Prinyatye.Should().BeEmpty("список не долистан, согласия нет");
    }

    [Fact]
    public async Task Vozvrat_k_vyboru_snimaet_soglasie()
    {
        // Иначе человек, вернувшийся к выбору и добавивший десять строк,
        // попадает на подтверждение с уже активной кнопкой: ворота открылись за
        // прошлое согласие, данное на другой список.
        var model = Sobrat(Odna("Pervoe"));

        await model.ProveritAsync(TestContext.Current.CancellationToken);
        model.Rows[0].IsSelected = true;
        await model.KPodtverzhdeniyuAsync(TestContext.Current.CancellationToken);
        model.Confirmed = true;
        model.NazadKVyboruCommand.Execute(null);

        model.Confirmed.Should().BeFalse();
        model.Step.Should().Be(FlowStep.Selection);
    }

    [Fact]
    public async Task Strahovka_delaetsya_do_pokaza_podtverzhdeniya_a_ne_posle_nazhatiya()
    {
        // Порядок здесь и есть смысл: заметка, приехавшая вместе с отчётом,
        // сообщает об отсутствии страховки тому, кто уже нажал.
        var ochistka = new OchistkaReestraZaglushka();
        var model = SobratS(Odna("Pervoe"), ochistka);

        await model.ProveritAsync(TestContext.Current.CancellationToken);
        model.Rows[0].IsSelected = true;
        await model.KPodtverzhdeniyuAsync(TestContext.Current.CancellationToken);

        ochistka.Strahovok.Should().Be(1);
        ochistka.Prinyatye.Should().BeEmpty("удалять ещё нечего, спрашивали только про страховку");
        model.RestorePointNote.Should().NotBeNullOrWhiteSpace(
            "до удаления человек обязан знать, будет страховка или нет");
    }

    [Fact]
    public async Task Na_podtverzhdenii_pokazyvaetsya_otmechennoe_a_ne_ves_spisok()
    {
        // Согласие даётся на СПИСОК. Лишняя строка в нём это согласие не на то,
        // что произойдёт, и заметить подмену человеку нечем.
        var model = Sobrat(new ReestrZaglushka(new RegistryScanResult(
            [
                ReestrObraztsy.Nahodka(RegistryHive.CurrentUser, "Pervoe"),
                ReestrObraztsy.Nahodka(RegistryHive.CurrentUser, "Vtoroe"),
            ],
            [], false)));

        await model.ProveritAsync(TestContext.Current.CancellationToken);
        model.Rows[1].IsSelected = true;

        model.SelectedRows.Should().ContainSingle();
        model.SelectedRows[0].ValueName.Should().Be("Vtoroe");
        model.SelectedLabel.Should().Contain("1");
    }

    [Fact]
    public async Task Snyataya_i_vozvrashchennaya_otmetka_ne_udvaivaet_schet()
    {
        // Накопительный счётчик расходится с истиной на первой же строке,
        // которую сняли и поставили обратно, и расходится молча.
        var model = Sobrat(Odna("Pervoe"));

        await model.ProveritAsync(TestContext.Current.CancellationToken);
        model.Rows[0].IsSelected = true;
        model.Rows[0].IsSelected = false;
        model.Rows[0].IsSelected = true;

        model.SelectedCount.Should().Be(1);
        model.SelectedRows.Should().ContainSingle();
    }

    [Fact]
    public async Task Otmetit_vsyo_ne_trogaet_stroki_vetki_mashiny_bez_prav()
    {
        // Кнопка, отметившая то, чего не удалит, обещает и не делает.
        var model = Sobrat(
            new ReestrZaglushka(new RegistryScanResult(
                [
                    ReestrObraztsy.Nahodka(RegistryHive.LocalMachine, "Obshchee"),
                    ReestrObraztsy.Nahodka(RegistryHive.CurrentUser, "Svoe"),
                ],
                [], false)),
            elevated: false);

        await model.ProveritAsync(TestContext.Current.CancellationToken);
        model.OtmetitVsyoCommand.Execute(null);

        model.SelectedCount.Should().Be(1);
        model.Rows.Single(r => r.Hive == "HKLM").IsSelected.Should().BeFalse();
    }

    [Fact]
    public async Task Posle_ochistki_spisok_ne_predlagaet_udalit_udalennoe()
    {
        var model = SobratS(Odna("Pervoe"), new OchistkaReestraZaglushka());

        await model.ProveritAsync(TestContext.Current.CancellationToken);
        model.Rows[0].IsSelected = true;
        await model.KPodtverzhdeniyuAsync(TestContext.Current.CancellationToken);
        model.Confirmed = true;
        await model.UdalitAsync(TestContext.Current.CancellationToken);

        model.Step.Should().Be(FlowStep.Report);
        model.ReportSummary.Should().Contain("1");
        model.BackupDirectory.Should().Be(@"C:\bekapy");

        model.ZavershitCommand.Execute(null);

        model.Rows.Should().BeEmpty();
        model.Step.Should().Be(FlowStep.Selection);
        model.State.EmptyTitle.Should().Contain("Очистка завершена");
    }

    [Fact]
    public async Task Novaya_proverka_vozvrashchaet_potok_k_vyboru()
    {
        // Иначе повторная проверка оставляет человека на подтверждении списка,
        // которого больше нет.
        var model = Sobrat(Odna("Pervoe"));

        await model.ProveritAsync(TestContext.Current.CancellationToken);
        model.Rows[0].IsSelected = true;
        await model.KPodtverzhdeniyuAsync(TestContext.Current.CancellationToken);
        model.Confirmed = true;

        await model.ProveritAsync(TestContext.Current.CancellationToken);

        model.Step.Should().Be(FlowStep.Selection);
        model.Confirmed.Should().BeFalse();
        model.SelectedCount.Should().Be(0);
    }
    private static RegistryBackupFile Bekap(string imya, bool godnyy = true) => new(
        @"C:\bekapy\" + imya,
        imya,
        DateTimeOffset.UtcNow,
        4096,
        godnyy,
        godnyy ? null : "первая строка не заголовок экспорта");

    [Fact]
    public async Task Backup_list_starts_with_the_largest_file()
    {
        var model = SobratSBekapami(Odna(), new BekapyZaglushka(
            Bekap("small.reg") with { SizeBytes = 1 }, Bekap("large.reg") with { SizeBytes = 100 }));
        await model.OtkryitBekapyCommand.ExecuteAsync(null);
        model.Backups.Select(file => file.FileName).Should().Equal("large.reg", "small.reg");
    }

    [Fact]
    public async Task Pustoy_katalog_bekapov_nazyvaet_gde_poyavitsya_fayl()
    {
        // «Бэкапов нет» без адреса это тупик: человек не знает, где искать и
        // когда файл там появится.
        var model = SobratSBekapami(Odna(), new BekapyZaglushka());

        await model.OtkryitBekapyCommand.ExecuteAsync(null);

        model.BackupsOpen.Should().BeTrue();
        model.Backups.Should().BeEmpty();
        model.RollbackNote.Should().Contain(@"C:\bekapy");
    }

    [Fact]
    public async Task Spisok_bekapov_dohodit_do_ekrana()
    {
        var bekapy = new BekapyZaglushka(Bekap("svezhiy.reg"), Bekap("bityy.reg", godnyy: false));
        var model = SobratSBekapami(Odna(), bekapy);

        await model.OtkryitBekapyCommand.ExecuteAsync(null);

        model.Backups.Should().HaveCount(2);
        model.Backups.Single(b => b.FileName == "bityy.reg").Valid.Should().BeFalse();
        model.RollbackNote.Should().BeNull("откатов ещё не было");
    }

    [Fact]
    public async Task Negodnyy_bekap_do_sluzhby_ne_doezzhaet()
    {
        // Кнопка у негодного файла выключена, но Execute у команды не
        // спрашивает CanExecute. Импорт файла, который не является экспортом
        // реестра, хуже отсутствия отката: человек после него уверен, что
        // машину восстановили.
        var bekapy = new BekapyZaglushka();
        var model = SobratSBekapami(Odna(), bekapy);

        await model.VosstanovitCommand.ExecuteAsync(Bekap("bityy.reg", godnyy: false));

        bekapy.Vosstanovlennye.Should().BeEmpty();
        model.RollbackNote.Should().Contain("не проходит проверку");
    }

    [Fact]
    public async Task Udachnyy_otkat_chistit_spisok_nahodok()
    {
        // Список после импорта врёт: записи вернулись. Не почистить его значит
        // предложить удалить то, что только что вернули.
        var bekapy = new BekapyZaglushka();
        var model = SobratSBekapami(Odna("Pervoe"), bekapy);

        await model.ProveritAsync(TestContext.Current.CancellationToken);
        model.Rows.Should().ContainSingle();

        await model.VosstanovitCommand.ExecuteAsync(Bekap("svezhiy.reg"));

        bekapy.Vosstanovlennye.Should().ContainSingle();
        model.Rows.Should().BeEmpty();
        model.SelectedCount.Should().Be(0);
        model.Step.Should().Be(FlowStep.Selection);
        model.State.EmptyTitle.Should().Contain("восстановлена");
    }

    [Fact]
    public async Task Neudachnyy_otkat_spisok_ne_trogaet()
    {
        // Иначе несостоявшийся откат выглядит состоявшимся: список пуст, экран
        // говорит «восстановлена», а в реестре всё как было.
        var bekapy = new BekapyZaglushka
        {
            Otvet = new JunkManager.Deletion.RollbackResult(
                false, @"C:\bekapy\svezhiy.reg", "reg.exe вернул код 1"),
        };

        var model = SobratSBekapami(Odna("Pervoe"), bekapy);

        await model.ProveritAsync(TestContext.Current.CancellationToken);
        await model.VosstanovitCommand.ExecuteAsync(Bekap("svezhiy.reg"));

        model.Rows.Should().ContainSingle("откат не состоялся, список прежний");
        model.RollbackNote.Should().Contain("не состоялся");
        model.RollbackNote.Should().Contain("код 1");
    }

    [Fact]
    public async Task Zakrytie_paneli_ubiraet_i_zametku_otkata()
    {
        // Заметка, пережившая панель, всплывает в следующий раз поверх нового
        // списка и рассказывает про прошлый откат.
        var model = SobratSBekapami(Odna(), new BekapyZaglushka());

        await model.OtkryitBekapyCommand.ExecuteAsync(null);
        model.RollbackNote.Should().NotBeNull();

        model.ZakryitBekapyCommand.Execute(null);

        model.BackupsOpen.Should().BeFalse();
        model.RollbackNote.Should().BeNull();
    }
}
