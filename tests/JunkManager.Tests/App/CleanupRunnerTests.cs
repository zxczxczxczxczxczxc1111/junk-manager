using FluentAssertions;
using JunkManager.Core;
using JunkManager.Deletion;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.App;

[Trait("Class", "Sandbox")]
public sealed class CleanupRunnerTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _pesochnica;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_file_is_preserved_after_preview(bool singleFileRule)
    {
        // Yesterday's scan is not permission to delete today's replacement.
        var folder = SozdatPapku("changed", 1, 3);
        var path = Directory.GetFiles(folder).Single();
        var rule = new JunkManager.Core.Rules.RuleDefinition("changed", "test", [singleFileRule ? path : folder], "Safe", "cache")
        { Tier = RiskTier.Safe };
        var scan = await new JunkManager.Core.Scanning.FileScanner().ScanAsync([rule], null, TestContext.Current.CancellationToken);
        scan.Findings.Should().ContainSingle();
        await File.WriteAllTextAsync(path, "new content after preview", TestContext.Current.CancellationToken);
        var report = await new CleanupRunner(new FileDeleter(new SpisokZhurnala())).RunAsync(scan.Findings,
            DeleteMode.Permanent, null, TestContext.Current.CancellationToken);
        report.DeletedCount.Should().Be(0);
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Be("new content after preview");
        report.Outcomes.Single().Reason.Should().Contain("изменился");
    }

    public CleanupRunnerTests(SandboxFixture pesochnica) => _pesochnica = pesochnica;

    private string SozdatPapku(string imya, int faylov, int baytVFayle)
    {
        // Имя разводится по случаю: приспособление одно на класс, и две проверки
        // с одинаковым именем папки чинили бы друг другу диск.
        var papka = Path.Combine(_pesochnica.Root, $"{imya}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(papka);

        for (var i = 0; i < faylov; i++)
        {
            File.WriteAllBytes(Path.Combine(papka, $"f{i}.bin"), new byte[baytVFayle]);
        }

        return papka;
    }

    /// <summary>
    /// Путь под запрещённым корнем, которого НЕ СУЩЕСТВУЕТ.
    /// </summary>
    /// <remarks>
    /// Раньше здесь стоял живой каталог Windows, и это стоило машины.
    /// 06.09.2026 мутатор был убит на полпути, оставил в дереве мутацию
    /// «список запрещённых корней отключён», и проверки этого класса запустили
    /// НАСТОЯЩЕЕ рекурсивное удаление каталога Windows. Снесло всё, где у
    /// администратора есть право удаления: drivers\etc и GAC целиком, а вместе
    /// с GAC перестал грузиться Windows PowerShell.
    ///
    /// Отсюда правило: проверка обязана иметь ВТОРОЙ барьер, не зависящий от
    /// проверяемого кода. Здесь он такой: даже если предохранитель откажет
    /// молча, удалять нечего, потому что пути нет. Сам список корней при этом
    /// проверяется прямым вызовом в SafetyGuardTests, где удаления нет вовсе.
    /// </remarks>
    private static readonly string ZapreshchennyyNesushchestvuyushchiy =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "junk-manager-takogo-puti-net-i-ne-budet");

    private static Finding Celikom(string put, long bayt) =>
        new(Path.GetFileName(put), put, bayt, RiskTier.Safe,
            "пересоздастся", FindingSource.Rule, RuleId: "Тест");

    private static Finding Poelementno(string put, long bayt, IReadOnlyList<string> celi) =>
        new(Path.GetFileName(put), put, bayt, RiskTier.Safe,
            "мусор программ", FindingSource.Rule, RuleId: "Тест", LastUsedDays: null,
            Scope: DeleteScope.SelectedEntries, Targets: celi);

    [Fact]
    public async Task Nahodka_celikom_zabiraet_papku()
    {
        var papka = SozdatPapku("celikom", faylov: 3, baytVFayle: 1000);
        var zhurnal = new SpisokZhurnala();
        var runner = new CleanupRunner(new FileDeleter(zhurnal));

        var otchet = await runner.RunAsync(
            [Celikom(papka, 3000)], DeleteMode.Permanent, progress: null, CancellationToken.None);

        Directory.Exists(papka).Should().BeFalse();
        otchet.DeletedCount.Should().Be(1);
        otchet.BytesFreed.Should().Be(3000);
    }

    [Fact]
    public async Task Poelementnaya_nahodka_ostavlyaet_papku_na_meste()
    {
        // Ради этого случая существует DeleteScope. Здесь путём находки бывает
        // пользовательский %TEMP%, и удаление корня ломает работающие программы.
        var papka = SozdatPapku("poelementno", faylov: 4, baytVFayle: 500);
        var celi = Directory.GetFiles(papka).Take(2).ToList();

        var zhurnal = new SpisokZhurnala();
        var runner = new CleanupRunner(new FileDeleter(zhurnal));

        var otchet = await runner.RunAsync(
            [Poelementno(papka, 1000, celi)], DeleteMode.Permanent, null, CancellationToken.None);

        Directory.Exists(papka).Should().BeTrue("каталог обязан остаться");
        Directory.GetFiles(papka).Should().HaveCount(2, "уйти должны ровно перечисленные цели");
        otchet.BytesFreed.Should().Be(1000);
    }

    [Fact]
    public async Task A_file_replaced_by_a_directory_keeps_new_contents()
    {
        var root = SozdatPapku("replaced-file", 1, 10);
        var target = Directory.GetFiles(root).Single();
        var finding = Poelementno(root, 10, [target]);
        File.Delete(target);
        Directory.CreateDirectory(target);
        var keep = Path.Combine(target, "new-document.txt");
        File.WriteAllText(keep, "A pathname is not a lifetime contract.");
        var report = await new CleanupRunner(new FileDeleter(new SpisokZhurnala())).RunAsync(
            [finding], DeleteMode.Permanent, null, TestContext.Current.CancellationToken);
        File.ReadAllText(keep).Should().Be("A pathname is not a lifetime contract.");
        report.BytesFreed.Should().Be(0);
        report.DeletedCount.Should().Be(0);
    }

    [Fact]
    public async Task A_process_started_after_preview_prevents_cleanup()
    {
        var root = SozdatPapku("running-owner", 1, 10);
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var finding = Poelementno(root, 10, Directory.GetFiles(root)) with { RequiredStoppedProcesses = [current.ProcessName] };
        var report = await new CleanupRunner(new FileDeleter(new SpisokZhurnala())).RunAsync(
            [finding], DeleteMode.Permanent, null, TestContext.Current.CancellationToken);
        report.SkippedCount.Should().Be(1);
        Directory.GetFiles(root).Should().ContainSingle();
    }

    [Fact]
    public async Task Osvobozhdennoe_schitaetsya_po_faktu_a_ne_po_obeshchaniyu_skanirovaniya()
    {
        // Размер из сканирования это прогноз. Если между сканированием и
        // удалением файл вырос или похудел, в отчёте обязано быть измеренное.
        var papka = SozdatPapku("raskhozhdenie", faylov: 1, baytVFayle: 100);
        var zhurnal = new SpisokZhurnala();
        var runner = new CleanupRunner(new FileDeleter(zhurnal));

        var otchet = await runner.RunAsync(
            [Celikom(papka, 999_999)], DeleteMode.Permanent, null, CancellationToken.None);

        otchet.BytesFreed.Should().Be(100, "в отчёте измеренное, а не обещанное сканированием");
    }

    [Fact]
    public async Task Otkaz_predohranitelya_eto_Skipped_a_ne_Failed()
    {
        // Разница не косметическая: Skipped значит «решили не брать», Failed
        // значит «пытались и не смогли». В журнале это разные истории.
        var zhurnal = new SpisokZhurnala();
        var runner = new CleanupRunner(new FileDeleter(zhurnal));

        var otchet = await runner.RunAsync(
            [Celikom(ZapreshchennyyNesushchestvuyushchiy, 1)], DeleteMode.Permanent, null, CancellationToken.None);

        otchet.SkippedCount.Should().Be(1);
        otchet.FailedCount.Should().Be(0);
        otchet.Outcomes[0].Reason.Should().NotBeNullOrEmpty("отказ без причины неотличим от бага");
    }

    [Fact]
    public async Task Otkaz_predohranitelya_ne_dohodit_do_udalitelya()
    {
        // Отказ обязан быть виден в журнале ровно один раз и как отказ. Если
        // предохранитель пропустил путь дальше, запись всё равно появится, но
        // уже со статусом удаления, и молчаливое расхождение здесь стоит папки.
        var zhurnal = new SpisokZhurnala();
        var runner = new CleanupRunner(new FileDeleter(zhurnal));

        await runner.RunAsync(
            [Celikom(ZapreshchennyyNesushchestvuyushchiy, 1)], DeleteMode.Permanent, null, CancellationToken.None);

        zhurnal.Zapisi.Should().ContainSingle();
        zhurnal.Zapisi[0].Status.Should().Be(DeleteStatus.Skipped);
    }

    [Fact]
    public async Task Otmena_ostanavlivaet_i_pomechaet_otchet()
    {
        var papki = Enumerable.Range(0, 6)
            .Select(i => SozdatPapku($"otmena{i}", faylov: 1, baytVFayle: 10))
            .ToList();

        using var otmena = new CancellationTokenSource();
        var zhurnal = new SpisokZhurnala();
        var runner = new CleanupRunner(new FileDeleter(zhurnal));

        var hod = new SinhronnyyHod<CleanupProgress>(p =>
        {
            if (p.Done >= 2)
            {
                otmena.Cancel();
            }
        });

        var otchet = await runner.RunAsync(
            [.. papki.Select(p => Celikom(p, 10))], DeleteMode.Permanent, hod, otmena.Token);

        otchet.Cancelled.Should().BeTrue();
        otchet.Outcomes.Count.Should().BeLessThan(6, "остановка это остановка");
        papki.Count(Directory.Exists).Should().BeGreaterThan(0, "часть папок осталась нетронутой");
    }

    [Fact]
    public async Task Otmena_ne_vydayotsya_za_uspeshnyy_progon()
    {
        // Прерванная очистка возвращает меньше записей и в сводке выглядит как
        // маленькая, но успешная. Флаг это единственное, что отличает одно
        // от другого.
        var papka = SozdatPapku("odna", faylov: 1, baytVFayle: 10);
        using var otmena = new CancellationTokenSource();
        await otmena.CancelAsync();

        var zhurnal = new SpisokZhurnala();
        var runner = new CleanupRunner(new FileDeleter(zhurnal));

        var otchet = await runner.RunAsync(
            [Celikom(papka, 10)], DeleteMode.Permanent, null, otmena.Token);

        otchet.Cancelled.Should().BeTrue();
        otchet.Outcomes.Should().BeEmpty();
        Directory.Exists(papka).Should().BeTrue();
    }

    [Fact]
    public async Task Kazhdyy_ishod_popadaet_v_zhurnal()
    {
        var pervaya = SozdatPapku("zhurnal1", faylov: 1, baytVFayle: 10);
        var vtoraya = SozdatPapku("zhurnal2", faylov: 1, baytVFayle: 10);

        var zhurnal = new SpisokZhurnala();
        var runner = new CleanupRunner(new FileDeleter(zhurnal));

        await runner.RunAsync(
            [Celikom(pervaya, 10), Celikom(vtoraya, 10)],
            DeleteMode.Permanent, null, CancellationToken.None);

        zhurnal.Zapisi.Should().HaveCount(2);
    }

    [Fact]
    public async Task Hod_soobshchaet_dolyu_i_nakoplennoe()
    {
        var papki = Enumerable.Range(0, 4)
            .Select(i => SozdatPapku($"hod{i}", faylov: 1, baytVFayle: 25))
            .ToList();

        var zamechennoe = new List<CleanupProgress>();
        var zhurnal = new SpisokZhurnala();
        var runner = new CleanupRunner(new FileDeleter(zhurnal));

        await runner.RunAsync(
            [.. papki.Select(p => Celikom(p, 25))],
            DeleteMode.Permanent,
            new SinhronnyyHod<CleanupProgress>(zamechennoe.Add),
            CancellationToken.None);

        zamechennoe.Should().NotBeEmpty();
        zamechennoe[^1].Done.Should().Be(4);
        zamechennoe[^1].Total.Should().Be(4);
        zamechennoe[^1].Share.Should().Be(1.0);
        zamechennoe[^1].BytesFreed.Should().Be(100);
    }

    [Fact]
    public void Dolya_pustogo_paketa_ne_delitsya_na_nol()
    {
        // Пустой пакет до бегуна не доходит, но доля считается формулой, а
        // формула обязана быть определена на всех входах: иначе первый же
        // вызов из другого места даёт NaN, и полоса рисуется в никуда.
        new CleanupProgress(0, 0, 0, "").Share.Should().Be(0);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1031:Do not catch general exception types",
        Justification =
            "Падение надо перевезти через границу потока. Без этого исключение на выделенном " +
            "потоке остаётся необработанным и валит весь процесс прогона: вместо красной " +
            "проверки с текстом человек получает молчащий обвал набора.")]
    public void Perebor_uhodit_s_potoka_vyzyvayushchego()
    {
        // Удаление синхронное целиком: обход каталога, File.Delete,
        // Directory.Delete. Метод объявлен Task-возвращающим, и вызывающий
        // вправе считать, что работа ушла. Пока она НЕ уходила, окно замирало
        // на всё время очистки: полоса хода не перерисовывалась, кнопка
        // «Остановить» не нажималась, и продукт выглядел зависшим.
        var papka = SozdatPapku("potok", faylov: 3, baytVFayle: 100);
        var zhurnal = new PotokZhurnala();
        var runner = new CleanupRunner(new FileDeleter(zhurnal));

        // Выделенный поток, а НЕ поток пула. Работу из Task.Run пул волен
        // положить на любой свободный поток пула, в том числе на тот, с
        // которого её позвали, и проверка на потоке пула была бы шаткой. На
        // выделенный поток пул не кладёт ничего никогда.
        var vyzyvayushchiy = 0;
        Exception? upalo = null;

        var svoy = new Thread(() =>
        {
            vyzyvayushchiy = Environment.CurrentManagedThreadId;

            try
            {
                runner.RunAsync(
                    [Celikom(papka, 300)],
                    DeleteMode.Permanent,
                    progress: null,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                upalo = e;
            }
        })
        {
            IsBackground = true,
        };

        svoy.Start();

        svoy.Join(TimeSpan.FromSeconds(30)).Should().BeTrue("перебор обязан завершиться");
        upalo.Should().BeNull();

        zhurnal.Potoki.Should().NotBeEmpty("без записей сравнивать нечего и проверка пуста");
        zhurnal.Potoki.Should().NotContain(
            vyzyvayushchiy,
            "работа обязана уйти с потока вызывающего, иначе окно замирает до конца очистки");
    }

    [Fact]
    public async Task Adres_obrabotchika_ne_uhodit_v_faylovyy_udalitel()
    {
        // Находка обработчика Windows несёт АДРЕС, а не путь. Перебор отдавал её
        // файловому удалителю, предохранитель путей честно отвечал «путь не
        // абсолютный», и человек читал это как поломку продукта. Так корзину
        // нельзя было очистить из окна вообще никогда.
        var zhurnal = new SpisokZhurnala();
        var osobyy = new PoddelnyyOsobyy(
            new DeleteOutcome(
                FindingPath.VolumeCacheScheme + "Recycle Bin", DeleteStatus.Deleted, 4096));

        var runner = new CleanupRunner(new FileDeleter(zhurnal), osobyy);

        var otchet = await runner.RunAsync(
            [Obrabotchik("Recycle Bin", 4096)],
            DeleteMode.Permanent,
            progress: null,
            CancellationToken.None);

        osobyy.Prinyatye.Should().ContainSingle("адрес обязан уйти своему механизму");
        otchet.Outcomes.Should().ContainSingle().Which.Status.Should().Be(DeleteStatus.Deleted);
        otchet.BytesFreed.Should().Be(4096);

        zhurnal.Zapisi.Should().ContainSingle("исход чужими руками тоже обязан попасть в журнал");
        zhurnal.Zapisi[0].Reason.Should().NotContain(
            "не абсолютный", "это ответ предохранителя путей, которому адрес не показывают");
    }

    [Fact]
    public async Task Obychnaya_nahodka_ne_uhodit_v_osobyy_mehanizm()
    {
        // Обратные ворота. Иначе достаточно ошибиться в условии, и файлы
        // человека уедут в механизм, который их не удаляет, а отчёт скажет,
        // что всё сделано.
        var papka = SozdatPapku("obychnaya", faylov: 2, baytVFayle: 100);
        var zhurnal = new SpisokZhurnala();
        var osobyy = new PoddelnyyOsobyy(
            new DeleteOutcome("nikogda", DeleteStatus.Deleted, 0));

        var runner = new CleanupRunner(new FileDeleter(zhurnal), osobyy);

        await runner.RunAsync(
            [Celikom(papka, 200)], DeleteMode.Permanent, progress: null, CancellationToken.None);

        osobyy.Prinyatye.Should().BeEmpty("путь на диске удаляется файловым удалителем");
        Directory.Exists(papka).Should().BeFalse();
    }

    private static Finding Obrabotchik(string klyuch, long bayt) =>
        new(klyuch, FindingPath.VolumeCacheScheme + klyuch, bayt, RiskTier.Safe,
            "очистится силами Windows", FindingSource.VolumeCache);

    /// <summary>Особый механизм, который ничего не делает и всё помнит.</summary>
    private sealed class PoddelnyyOsobyy(DeleteOutcome otvet) : ISpecialCleaner
    {
        private readonly List<Finding> _prinyatye = [];

        public IReadOnlyList<Finding> Prinyatye => _prinyatye;

        public Task<DeleteOutcome> CleanAsync(Finding finding, CancellationToken ct)
        {
            _prinyatye.Add(finding);
            return Task.FromResult(otvet);
        }
    }

    /// <summary>Журнал, запоминающий, на каком потоке его позвали.</summary>
    private sealed class PotokZhurnala : IOperationLog
    {
        private readonly List<int> _potoki = [];

        public IReadOnlyList<int> Potoki
        {
            get
            {
                lock (_potoki)
                {
                    return [.. _potoki];
                }
            }
        }

        public Task RecordAsync(DeleteOutcome outcome, CancellationToken ct = default)
        {
            lock (_potoki)
            {
                _potoki.Add(Environment.CurrentManagedThreadId);
            }

            return Task.CompletedTask;
        }
    }
}
