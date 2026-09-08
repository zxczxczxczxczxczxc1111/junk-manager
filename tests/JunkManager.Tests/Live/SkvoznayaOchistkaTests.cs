using System.Globalization;
using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Scanning;
using JunkManager.Core.Sources.Pattern;
using JunkManager.Deletion;
using JunkManager.Safety;
using JunkManager.Tests.Seeded;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Live;

/// <summary>
/// Сквозная проверка: посев, общий проход, НАСТОЯЩЕЕ удаление, разбор итога.
/// </summary>
/// <remarks>
/// <para>
/// Заведена 05.09.2026, когда выяснилось, что разрушительный класс состоит из
/// трёх наборов (корзина, склад Windows, повышение прав) и ни один не удаляет
/// найденное. Ядро удаления при этом проверено песочными тестами, но песочница
/// не отвечает на вопрос, ради которого продукт существует: то ли самое уходит
/// с диска, что показал проход.
/// </para>
/// <para>
/// Не Sandbox: сеятель кладёт файлы в настоящий %TEMP% и настоящий профиль, а
/// удаление тут безвозвратное. На машине человека этому места нет, и
/// предохранитель это первое, что здесь спрашивается.
/// </para>
/// </remarks>
[Trait("Class", "LiveDestructive")]
[Collection(ObshcheeSostoyanieMashiny.Imya)]
public sealed class SkvoznayaOchistkaTests
{
    [Fact]
    public async Task Nayti_udalit_i_ubedit_sya_chto_ushlo_imenno_naydennoe()
    {
        VmFuse.RequireArmed();

        var opis = SeedEntry.Load(Path.Combine(AppContext.BaseDirectory, "posev.json"));
        using var seyatel = JunkSeeder.Plant(opis);

        seyatel.Otkazy.Should().BeEmpty(
            "приманка, которую не удалось посеять, не проверяет ничего");

        var plan = ScanPlan.PoUmolchaniyu with
        {
            PoiskPoObraztsu = true,
            ObrazcyOverride = PatternScanOptions.Default(),
        };

        var itog = await PolnyyProhod.ScanAsync(
            plan,
            Path.Combine(AppContext.BaseDirectory, "rules"),
            progress: null,
            TestContext.Current.CancellationToken);

        // Берутся ТОЛЬКО находки, попавшие в приманки описи. Удалять в госте всё
        // подряд можно, гость для того и заведён, но тогда утверждение «ушло
        // именно найденное» ничего не значит: сверять будет не с чем.
        var primanki = opis
            .Where(s => s is { MustFind: true, Genuine: true })
            .Select(s => s.ExpandedPath)
            .ToList();

        var lovushki = opis.Where(s => !s.MustFind).Select(s => s.ExpandedPath).ToList();

        var kUdaleniyu = itog.Findings
            .Where(f => FindingPath.IsFileSystem(f.Path))
            .Where(f => primanki.Any(p => Sovpadaet(f.Path, p)))
            .ToList();

        kUdaleniyu.Should().NotBeEmpty(
            "если удалять нечего, тест не проверяет удаление, а молча проходит");

        // Ловушки обязаны быть на диске ДО удаления: иначе утверждение про их
        // целость после удаления проходит по причине «их и не было».
        foreach (var lovushka in lovushki)
        {
            (File.Exists(lovushka) || Directory.Exists(lovushka)).Should().BeTrue(
                "ловушка {0} не посеяна, и проверить на ней нечего", lovushka);
        }

        using var zhurnal = JsonlOperationLog.CreateForRun();
        var udalitel = new FileDeleter(zhurnal);

        long osvobozhdeno = 0;
        var udalennye = new List<string>();

        foreach (var nahodka in kUdaleniyu)
        {
            SafetyGuard.TryVerify(nahodka.Path, out var propusk, out var otkaz)
                .Should().BeTrue("guard отказал находке, которую сам же пропустил в список: {0}", otkaz);

            var ishod = nahodka.Scope == DeleteScope.Whole
                ? await udalitel.DeleteAsync(
                    propusk, DeleteMode.Permanent, TestContext.Current.CancellationToken)
                : await udalitel.DeleteSelectedAsync(
                    propusk,
                    nahodka.DeletionTargets,
                    DeleteMode.Permanent,
                    TestContext.Current.CancellationToken);

            if (ishod.Status != DeleteStatus.Deleted)
            {
                continue;
            }

            osvobozhdeno += ishod.BytesFreed;
            udalennye.Add(nahodka.Path);
        }

        udalennye.Should().NotBeEmpty("ни одна находка не удалилась, а удаление это весь продукт");

        // 1. Ушло то, что удаляли целиком.
        foreach (var put in udalennye)
        {
            if (kUdaleniyu.First(f => f.Path == put).Scope != DeleteScope.Whole)
            {
                continue;
            }

            (File.Exists(put) || Directory.Exists(put)).Should().BeFalse(
                "продукт отчитался об удалении {0}, а путь на месте", put);
        }

        // 2. Ловушки целы. Это главное утверждение теста: удаление, забравшее
        // лишнее, страшнее удаления, не забравшего нужное.
        foreach (var lovushka in lovushki)
        {
            (File.Exists(lovushka) || Directory.Exists(lovushka)).Should().BeTrue(
                "ЛОВУШКА {0} УДАЛЕНА, это готовый чужой потерянный файл", lovushka);
        }

        // 3. Журнал знает про каждое удаление. Журнал это единственный след,
        // который остаётся после безвозвратного удаления, и запись «удалено»
        // без строки в нём это удаление, которого никто не докажет.
        zhurnal.Dispose();
        var stroki = JsonlOperationLog.ReadLines(zhurnal.FilePath);

        foreach (var put in udalennye)
        {
            stroki.Should().Contain(
                s => s.Contains(Ekranirovat(put), StringComparison.OrdinalIgnoreCase),
                "в журнале нет строки про удаление {0}", put);
        }

        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"удалено находок {udalennye.Count} из {kUdaleniyu.Count}, освобождено {osvobozhdeno} байт, строк в журнале {stroki.Count}"));

        osvobozhdeno.Should().BeGreaterThan(0, "удаление, не освободившее ни байта, это не удаление");
    }

    /// <summary>Путь находки это сама приманка либо что-то внутри неё.</summary>
    private static bool Sovpadaet(string nahodka, string primanka) =>
        nahodka.Equals(primanka, StringComparison.OrdinalIgnoreCase)
        || nahodka.StartsWith(
            primanka.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Путь в строке JSONL записан с экранированными разделителями.</summary>
    private static string Ekranirovat(string put) => put.Replace("\\", "\\\\", StringComparison.Ordinal);
}
