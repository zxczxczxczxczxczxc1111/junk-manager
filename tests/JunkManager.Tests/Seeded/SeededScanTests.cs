using JunkManager.App.Services;
using JunkManager.Core.Registry;
using JunkManager.Core.Scanning;
using JunkManager.Safety;
using Xunit;
using FluentAssertions;

namespace JunkManager.Tests.Seeded;

/// <summary>
/// Приёмка полноты обнаружения.
/// </summary>
/// <remarks>
/// Не Sandbox: сеятель кладёт файлы в настоящий %TEMP%, настоящий профиль и
/// настоящий реестр. На машине человека этому места нет.
///
/// Смысл этой приёмки в том, чего не умеет ни один юнит-тест: юнит проверяет,
/// что код делает написанное, и НЕ проверяет, что список правил куда-то попал,
/// что переменная окружения раскрылась и что обработчик зарегистрирован.
/// Продукт умеет отрапортовать «просмотрено сорок источников, найдено ноль» при
/// полностью зелёном наборе тестов.
/// </remarks>
[Trait("Class", "Seeded")]
public sealed class SeededScanTests
{
    [Fact]
    public async Task Poseyannyy_musor_nahoditsya_i_lovushki_ne_srabatyvayut()
    {
        VmFuse.RequireArmed();

        var opis = SeedEntry.Load(Path.Combine(AppContext.BaseDirectory, "posev.json"));
        opis.Should().NotBeEmpty("опись обязана доехать до гостя вместе с набором");

        using var seyatel = JunkSeeder.Plant(opis);

        // Тот же самый проход, что и в продукте, а не своя сборка источников.
        // Приёмка, которая складывает источники по-своему, проверяет свою копию
        // продукта: 05.09.2026 пять приманок из восемнадцати не находились
        // ровно потому, что механизм был построен и не подключён, и приёмка со
        // своей сборкой этого бы не показала.
        //
        // Поиск по образцу и платформенные утилиты включены явно: в продукте они
        // выключены по умолчанию (маска не знает, чем занят файл; DISM медленный
        // и требует прав), но приёмка обязана мерить ГЛУБИНУ, а не настройки.
        var plan = ScanPlan.PoUmolchaniyu with
        {
            PoiskPoObraztsu = true,
            PlatformennyeUtility = true,
        };

        var sIstochnikami = await PolnyyProhod.ScanAsync(
            plan,
            progress: null,
            TestContext.Current.CancellationToken);

        // Приёмка зовёт ТУ ЖЕ службу, что и окно. Своя сборка сканера
        // проверяла бы свою копию продукта, и ровно так 05.09.2026 пять
        // источников оказались собранными, зелёными и никем не вызываемыми.
        var reestr = await new RegistryScanService()
            .ScanAsync(null, TestContext.Current.CancellationToken);

        var verdikt = SeedJudge.Judge(opis, sIstochnikami.Findings, seyatel.Otkazy, reestr.Findings);

        // Печатается ВСЕГДА, а не только при падении: числа нужны и на зелёном
        // прогоне, иначе рост глубины не с чем сравнивать.
        TestContext.Current.TestOutputHelper?.WriteLine(verdikt.Report());
        Console.WriteLine(verdikt.Report());

        verdikt.SeedFailures.Should().BeEmpty(
            "приманка, которую не удалось посеять, не проверяет ничего, и молчать про это нельзя");
        verdikt.TrapsTriggered.Should().BeEmpty(
            "срабатывание на ловушке это готовое удаление чужого файла");
        verdikt.InForbiddenRoots.Should().BeEmpty(
            "находка в запрещённом корне это провал независимо от всего остального");
        verdikt.MissedRequired.Should().BeEmpty("обязательная приманка не найдена");
        verdikt.WrongSource.Should().BeEmpty(
            "нашлось не тем механизмом, который обязан был найти");

        // Only planted targets are selected; unrelated guest files did not join this experiment.
        var baits = opis.Where(seed => seed.MustFind && seed.Kind is SeedKind.File or SeedKind.Directory or SeedKind.CrashDump).ToArray();
        var selected = sIstochnikami.Findings.Where(finding => finding.Source is Core.FindingSource.Rule
                or Core.FindingSource.PatternScan or Core.FindingSource.Detector)
            .Select(finding => finding with
            {
                Scope = Core.DeleteScope.SelectedEntries,
                Targets = finding.DeletionTargets.Where(target => baits.Any(seed =>
                    SafetyGuard.IsAtOrUnder(target, seed.ExpandedPath))).ToArray(),
            }).Where(finding => finding.DeletionTargets.Count > 0).ToArray();
        var targets = selected.SelectMany(finding => finding.DeletionTargets).ToArray();
        targets.Should().NotBeEmpty();
        var trapPaths = opis.Where(seed => !seed.MustFind && seed.Kind != SeedKind.LockedFile)
            .SelectMany(seed => Directory.Exists(seed.ExpandedPath)
                ? Directory.GetFiles(seed.ExpandedPath, "*", SearchOption.AllDirectories)
                : [seed.ExpandedPath]).ToArray();
        var hashes = trapPaths.ToDictionary(path => path,
            path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
            StringComparer.OrdinalIgnoreCase);
        var deleted = await new JunkManager.Deletion.CleanupRunner(new JunkManager.Deletion.FileDeleter(new Infrastructure.SpisokZhurnala()))
            .RunAsync(selected, JunkManager.Deletion.DeleteMode.Permanent, null, TestContext.Current.CancellationToken);
        deleted.Outcomes.Should().OnlyContain(outcome => outcome.Status == JunkManager.Deletion.DeleteStatus.Deleted);
        targets.Should().OnlyContain(path => !File.Exists(path), "каждый выбранный файл должен действительно исчезнуть");
        foreach (var trap in hashes)
        {
            File.Exists(trap.Key).Should().BeTrue(trap.Key);
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(trap.Key)))
                .Should().Be(trap.Value, trap.Key);
        }
        foreach (var locked in opis.Where(seed => !seed.MustFind && seed.Kind == SeedKind.LockedFile))
            File.Exists(locked.ExpandedPath).Should().BeTrue("занятый контрольный файл сохраняется");
    }
}
