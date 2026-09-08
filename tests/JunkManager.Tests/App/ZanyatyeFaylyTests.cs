using System.Collections.ObjectModel;
using System.Threading;
using FluentAssertions;
using JunkManager.App.Services;
using JunkManager.App.ViewModels;
using JunkManager.Core;
using JunkManager.Deletion;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.App;

/// <summary>
/// The way out of "file is in use".
/// </summary>
/// <remarks>
/// <para>
/// Ни одна проверка здесь не трогает настоящие процессы: служба подставная.
/// Настоящая закрывает и завершает ЧУЖИЕ программы человека, и набор, который
/// это делает по дороге, сам является аварией.
/// </para>
/// <para>
/// Порядок выходов с 06.09.2026 такой: первый продукт делает САМ (просит
/// держателей закрыться и повторяет удаление), а два оставшихся показывает
/// человеку только если сам не справился.
/// </para>
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class ZanyatyeFaylyTests : IDisposable
{
    private readonly List<FilesViewModel> _sozdannye = [];
    private readonly SynchronizationContext? _byvshiy;

    public ZanyatyeFaylyTests()
    {
        _byvshiy = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new PryamoyKontekst());
    }

    public void Dispose()
    {
        SynchronizationContext.SetSynchronizationContext(_byvshiy);

        foreach (var model in _sozdannye)
        {
            model.Dispose();
        }
    }

    private const string PutKesha = "C:\\кэш";

    private static Finding Nahodka(string imya) =>
        new(imya, $"C:\\{imya}", 1000, RiskTier.Safe, "пересоздастся", FindingSource.Rule,
            RuleId: "Тест");

    private FilesViewModel Sobrat(ZanyatayaOchistka ochistka, ZaglushkaZanyatyh sluzhba,
        params Finding[] nahodki)
    {
        var proshloe = new PosledniyProhod();
        var model = new FilesViewModel(proshloe, ochistka, sluzhba);
        _sozdannye.Add(model);
        proshloe.Polozhit(new ScanResult(nahodki, []));
        return model;
    }

    private static async Task Ochistit(FilesViewModel model)
    {
        model.Confirmed = true;
        await model.UdalitCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Prosba_zakrytsya_idyot_sama_bez_nazhatiya()
    {
        // Первый выход безболезненный: программу просят закрыться штатно, она
        // сохраняет своё и выходит сама. У такого шага нет второй стороны, и
        // спрашивать про него человека значит выкладывать три кнопки там, где
        // решения нет ни одного.
        var ochistka = new ZanyatayaOchistka { Derzhatel = "Spotify (PID 13248)" };
        var sluzhba = new ZaglushkaZanyatyh();
        var model = Sobrat(ochistka, sluzhba, Nahodka("кэш"));

        await Ochistit(model);

        sluzhba.Poprosheno.Should().ContainSingle(
            "продукт обязан попросить сам, без нажатия").Which.Should().Be(PutKesha);

        model.HasStuck.Should().BeTrue();
        model.Stuck[0].Holder.Should().Be("Spotify (PID 13248)");
        model.Stuck[0].ProsbaProshla.Should().BeTrue();
    }

    [Fact]
    public void Do_prosby_u_stroki_net_ni_odnoy_knopki()
    {
        // Мутация «DeystviyaVidny => !Resheno» пережила прогон 06.09.2026, и
        // это была настоящая дыра. Соседние проверки смотрят строку ПОСЛЕ
        // просьбы, а там постепенное раскрытие от трёх кнопок сразу отличить
        // нечем: признак просьбы уже поднят, и оба выражения дают одно и то
        // же. Состояние ДО просьбы не проверял никто, хотя переделка делалась
        // ровно ради него.
        //
        // Строка собирается напрямую, а не через экран, потому что на экране
        // продукт просит держателя сам и сразу: наблюдаемого промежутка «строка
        // есть, просьбы ещё не было» там не существует.
        var derzhitel = "Spotify (PID 13248)";

        var stroka = new ZanyatyyViewModel(
            Nahodka("кэш"),
            new DeleteOutcome(PutKesha, DeleteStatus.Failed, 0, "файл занят", derzhitel),
            new ZaglushkaZanyatyh(),
            (_, _) => Task.FromResult(
                new DeleteOutcome(PutKesha, DeleteStatus.Failed, 0, "файл занят", derzhitel)));

        stroka.ProsbaProshla.Should().BeFalse("продукт ещё не просил");
        stroka.Resheno.Should().BeFalse("строка ещё ничем не кончилась");
        stroka.DeystviyaVidny.Should().BeFalse(
            "два оставшихся выхода появляются только после неудавшейся просьбы");
    }

    [Fact]
    public async Task Zakrylas_i_fayl_ushyol_bez_edinogo_nazhatiya()
    {
        // Лучший исход целиком: человек нажал «Удалить» один раз, а держателя
        // закрыл и файл унёс продукт.
        var ochistka = new ZanyatayaOchistka
        {
            Derzhatel = "Spotify (PID 13248)",
            UdaetsyaSVtorogoRaza = true,
        };

        var sluzhba = new ZaglushkaZanyatyh();
        var model = Sobrat(ochistka, sluzhba, Nahodka("кэш"));

        await Ochistit(model);

        model.Stuck[0].Resheno.Should().BeTrue();
        model.Stuck[0].Note.Should().Contain("Файл удалён");
        model.Stuck[0].DeystviyaVidny.Should().BeFalse("решённой строке кнопки не нужны");
        model.HasStuckActions.Should().BeFalse(
            "предупреждать про принудительное завершение больше не о чем");
    }

    [Fact]
    public async Task Ne_zakrylas_i_togda_poyavlyayutsya_dva_vyhoda()
    {
        var ochistka = new ZanyatayaOchistka { Derzhatel = "Spotify (PID 13248)" };
        var sluzhba = new ZaglushkaZanyatyh
        {
            Otvet = new LockedFileActionResult(false, "у неё нет окна, которое можно попросить"),
        };

        var model = Sobrat(ochistka, sluzhba, Nahodka("кэш"));
        var bylo = ochistka.Vyzovov;

        await Ochistit(model);

        model.Stuck[0].Resheno.Should().BeFalse();
        model.Stuck[0].Note.Should().Contain("нет окна");
        model.Stuck[0].DeystviyaVidny.Should().BeTrue("остальные два выхода ещё не пробовали");
        model.HasStuckActions.Should().BeTrue();

        // Отказ обязан останавливать поток целиком. Продукт, который после
        // неуслышанной просьбы всё равно лезет удалять, кладёт в отчёт и в
        // журнал вторую строку отказа про тот же файл и по той же причине.
        ochistka.Vyzovov.Should().Be(
            bylo + 1, "проход был один, и повторного удаления после отказа быть не должно");
        model.Stuck[0].Note.Should().NotContain(
            "не вышло", "это слова про неудавшееся удаление, а удаления не было");
    }

    [Fact]
    public async Task Otkaz_bez_derzhatelya_v_spisok_ne_popadaet()
    {
        // Отбор идёт по НАЗВАННОМУ держателю, а не по статусу. Иначе в список с
        // кнопкой «закрыть программу» попал бы отказ предохранителя и находка
        // обработчика Windows, где закрывать нечего и кнопка была бы обманом.
        var ochistka = new ZanyatayaOchistka { Derzhatel = null };
        var sluzhba = new ZaglushkaZanyatyh();
        var model = Sobrat(ochistka, sluzhba, Nahodka("кэш"));

        await Ochistit(model);

        model.HasStuck.Should().BeFalse();
        model.Stuck.Should().BeEmpty();
        sluzhba.Poprosheno.Should().BeEmpty("закрывать некого, и просить некого");

        model.ReportRows.Should().ContainSingle(
            "строка без выхода живёт в общем списке итога, а не пропадает с экрана");
    }

    [Fact]
    public async Task Spisok_itoga_ne_povtoryaet_zanyatye_stroki()
    {
        // Занятые показаны выше отдельным блоком со своими действиями. Те же
        // пути строкой ниже читаются как два разных события про один файл.
        var ochistka = new ZanyatayaOchistka { Derzhatel = "Spotify (PID 13248)" };
        var model = Sobrat(ochistka, new ZaglushkaZanyatyh(), Nahodka("кэш"), Nahodka("логи"));

        await Ochistit(model);

        model.Stuck.Should().HaveCount(2);
        model.ReportRows.Should().BeEmpty("обе строки заняты, и обе уже показаны блоком выше");
    }

    [Fact]
    public async Task Prervannaya_ochistka_ne_zakryvaet_chuzhie_programmy()
    {
        // «Остановить» значит «не трогай мою машину». Закрывать после этого
        // чужие программы означало бы продолжить ровно то, что человек только
        // что велел прекратить.
        var ochistka = new ZanyatayaOchistka
        {
            Derzhatel = "Spotify (PID 13248)",
            Prervat = true,
        };

        var sluzhba = new ZaglushkaZanyatyh();
        var model = Sobrat(ochistka, sluzhba, Nahodka("кэш"));

        await Ochistit(model);

        sluzhba.Poprosheno.Should().BeEmpty("после остановки продукт сам ничего не закрывает");

        // Но выход при этом остаётся: кнопки на месте, решает человек.
        model.Stuck.Should().ContainSingle();
        model.Stuck[0].DeystviyaVidny.Should().BeTrue();
    }

    [Fact]
    public async Task Osvobodili_no_udalit_vsyo_ravno_ne_vyshlo_govoritsya_pryamo()
    {
        // Худший из молчаливых случаев: держателя убрали, файл остался, а
        // строка написала бы «готово».
        var ochistka = new ZanyatayaOchistka { Derzhatel = "Spotify (PID 13248)" };
        var model = Sobrat(ochistka, new ZaglushkaZanyatyh(), Nahodka("кэш"));

        await Ochistit(model);

        model.Stuck[0].Resheno.Should().BeFalse();
        model.Stuck[0].Note.Should().Contain("удалить всё равно не вышло");
        model.Stuck[0].DeystviyaVidny.Should().BeTrue("два выхода ещё остались");
    }

    [Fact]
    public async Task Otkladyvanie_na_zagruzku_ne_probuet_udalit_seychas()
    {
        // Файл держат прямо сейчас, и удалить его сию секунду нельзя ничем.
        // Повтор после откладывания дал бы вторую строку отказа в отчёте.
        var ochistka = new ZanyatayaOchistka { Derzhatel = "Система" };
        var sluzhba = new ZaglushkaZanyatyh();
        var model = Sobrat(ochistka, sluzhba, Nahodka("кэш"));

        await Ochistit(model);
        var bylo = ochistka.Vyzovov;

        await model.Stuck[0].OtlozhitCommand.ExecuteAsync(null);

        sluzhba.Otlozheno.Should().ContainSingle();
        ochistka.Vyzovov.Should().Be(bylo, "повторного удаления тут быть не должно");
        model.Stuck[0].Resheno.Should().BeTrue();
    }

    [Fact]
    public async Task Zavershenie_processa_eto_otdelnoe_deystvie()
    {
        var ochistka = new ZanyatayaOchistka { Derzhatel = "Spotify (PID 13248)" };
        var sluzhba = new ZaglushkaZanyatyh();
        var model = Sobrat(ochistka, sluzhba, Nahodka("кэш"));

        await Ochistit(model);
        ochistka.OtdayotUdalennoe = true;

        await model.Stuck[0].ZavershitCommand.ExecuteAsync(null);

        sluzhba.Zaversheno.Should().ContainSingle();
        model.Stuck[0].Resheno.Should().BeTrue();
    }

    [Fact]
    public async Task Udalyonnoe_povtorom_dohodit_do_svodki_i_do_osvobozhdennogo()
    {
        // Иначе итог очистки говорит «не удалось» про файл, которого уже нет, и
        // «освобождено ноль» после освобождённого гигабайта.
        var ochistka = new ZanyatayaOchistka
        {
            Derzhatel = "Spotify (PID 13248)",
            UdaetsyaSVtorogoRaza = true,
        };

        var model = Sobrat(ochistka, new ZaglushkaZanyatyh(), Nahodka("кэш"));

        await Ochistit(model);

        model.FreedBytes.Should().Be(1000);
        model.Running[^1].Status.Should().Be(DeleteStatus.Deleted);
        model.ReportSummary.Should().Contain("1 объект удалён");
        model.ReportSummary.Should().Contain("0 не удалось");
    }

    /// <summary>Очистка, которая всё «не может» из-за держателя.</summary>
    private sealed class ZanyatayaOchistka : ICleanupService
    {
        public DeleteMode Mode { get; set; } = DeleteMode.Permanent;

        public string? Derzhatel { get; set; }

        /// <summary>С этого мига очистка начинает удавать. Ставится вручную.</summary>
        public bool OtdayotUdalennoe { get; set; }

        /// <summary>
        /// Первый проход отказывает, все следующие удаются. Это ровно то, что
        /// делает настоящая очистка после освобождения файла.
        /// </summary>
        public bool UdaetsyaSVtorogoRaza { get; set; }

        /// <summary>Отчёт приходит прерванным, как после кнопки «Остановить».</summary>
        public bool Prervat { get; set; }

        public int Vyzovov { get; private set; }

        public Task<CleanupReport> RunAsync(
            IReadOnlyList<Finding> findings,
            IProgress<CleanupProgress>? progress,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(findings);
            Vyzovov++;

            var udayotsya = OtdayotUdalennoe || (UdaetsyaSVtorogoRaza && Vyzovov > 1);

            var ishody = new List<DeleteOutcome>();
            var bayt = 0L;

            foreach (var nahodka in findings)
            {
                var ishod = udayotsya
                    ? new DeleteOutcome(nahodka.Path, DeleteStatus.Deleted, nahodka.SizeBytes)
                    : new DeleteOutcome(
                        nahodka.Path, DeleteStatus.Failed, 0, "файл занят", Derzhatel);

                ishody.Add(ishod);
                bayt += ishod.BytesFreed;

                progress?.Report(new CleanupProgress(
                    ishody.Count, findings.Count, bayt, nahodka.Path, ishod));
            }

            return Task.FromResult(new CleanupReport(ishody, bayt, Cancelled: Prervat));
        }
    }
}
