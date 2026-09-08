using FluentAssertions;
using JunkManager.Core;
using JunkManager.Deletion;
using Xunit;

namespace JunkManager.Tests.Deletion;

/// <summary>
/// What the person reads when a finding is not a place on disk.
/// </summary>
/// <remarks>
/// <para>
/// Класс `Sandbox`: ни одна проверка здесь ничего не удаляет. Взяты только те
/// пути, которые отказывают ДО любой разрушительной операции на ЛЮБОЙ машине,
/// включая гость со взведённым предохранителем: чёрный список и незнакомый
/// адрес. Проверять отказ предохранителя настоящим зарегистрированным
/// обработчиком нельзя: в госте предохранитель взведён, и такая проверка
/// очистила бы корзину по-настоящему.
/// </para>
/// <para>
/// Сам отказ предохранителя закреплён отдельно, в `VolumeCachePurgerFuseTests`.
/// </para>
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class SpecialCleanerTests
{
    [Theory]
    [InlineData(FindingSource.VolumeCache)]
    [InlineData(FindingSource.PlatformTool)]
    public async Task Wrong_scheme_is_refused_before_dispatch(FindingSource source)
    {
        // Substring is not a permission system, despite its impressive confidence.
        var result = await new SpecialCleaner().CleanAsync(new Finding("wrong", "x", 0, RiskTier.Risk, "test", source),
            TestContext.Current.CancellationToken);
        result.Status.Should().Be(DeleteStatus.Skipped);
        result.Reason.Should().Contain("не соответствует");
    }

    private static Finding Obrabotchik(string klyuch) =>
        new(klyuch, FindingPath.VolumeCacheScheme + klyuch, 1024, RiskTier.Safe,
            "очистится силами Windows", FindingSource.VolumeCache);

    private static Finding Utilita(string hvost) =>
        new(hvost, FindingPath.PlatformToolScheme + hvost, 1024, RiskTier.Risk,
            "уберётся утилитой", FindingSource.PlatformTool);

    [Fact]
    public async Task Chernyy_spisok_nazyvaetsya_slovami_a_ne_putyom()
    {
        // `DownloadsFolder` Windows считает очищаемым, мы не считаем: это файлы
        // человека. Отказ обязан объяснить ПОЧЕМУ. Раньше на этом месте стояло
        // «путь не абсолютный», то есть ответ предохранителя путей, которому
        // адрес вообще не показывают.
        var itog = await new SpecialCleaner().CleanAsync(
            Obrabotchik("DownloadsFolder"), TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        itog.Reason.Should().NotBeNullOrWhiteSpace();
        itog.Reason.Should().NotContain(
            "не абсолютный", "адрес обработчика предохранителю путей не показывают вовсе");
        itog.Reason.Should().Contain(
            "чёрном списке", "человек обязан прочитать, что именно помешало");
        itog.BytesFreed.Should().Be(0);
    }

    [Fact]
    public async Task Ischeznuvshiy_obrabotchik_ne_vydaetsya_za_polomku()
    {
        // Список обработчиков читается при проходе, а очистка идёт позже.
        // Обработчик мог исчезнуть между ними, и это пропуск, а не провал.
        var itog = await new SpecialCleaner().CleanAsync(
            Obrabotchik("JunkManagerTakogoObrabotchikaNet"),
            TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        itog.Reason.Should().Contain("не зарегистрирован");
        itog.BytesFreed.Should().Be(0);
    }

    [Fact]
    public async Task Neznakomaya_utilita_nazyvaetsya_a_ne_zapuskaetsya()
    {
        // Ворота против «запустим что-нибудь похожее». Адрес платформенной
        // утилиты собирается кодом, и незнакомый хвост это наша же ошибка, а не
        // повод запустить чужой процесс наугад.
        var itog = await new SpecialCleaner().CleanAsync(
            Utilita("nesushchestvuyushchaya/utilita"), TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        itog.Reason.Should().Contain("не называет ни одну известную");
        itog.BytesFreed.Should().Be(0);
    }

    [Fact]
    public async Task Istochnik_bez_mehanizma_govorit_ob_etom_pryamo()
    {
        // Схем в FindingPath четыре, а механизмов тут два. Молчаливый пропуск
        // на оставшихся означал бы находку, которая исчезает из отчёта без
        // объяснения.
        var reestr = new Finding(
            "запись", FindingPath.RegistryScheme + "HKCU/Software/Nechto", 0, RiskTier.Safe,
            "запись уйдёт", FindingSource.Registry);

        var itog = await new SpecialCleaner().CleanAsync(
            reestr, TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        itog.Reason.Should().Contain("механизма удаления для такого источника");
    }
}
