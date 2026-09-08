using FluentAssertions;
using JunkManager.Core.Interop;
using JunkManager.Core.Sources.VolumeCache;
using JunkManager.Deletion;
using JunkManager.Safety;
using Xunit;

namespace JunkManager.Tests.Deletion;

/// <summary>
/// Refusals, and nothing else. The whole point of this class is that it runs on
/// the developer machine, where the fuse is down: a gate proved only inside the
/// guest is a gate nobody has seen refuse anything.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class VolumeCachePurgerFuseTests
{
    private static readonly Guid Eskizy = new("{889900c3-59f3-4c2f-ae21-a409ea01e605}");
    private static readonly Guid ObshchiyClsid = new("{C0E13E61-0CC6-11d1-BBB6-0060978B2AE6}");

    /// <summary>
    /// A handler name that is deliberately NOT in the registry. It is what makes
    /// this file safe to mutate: mutate.ps1 removes the fuse on purpose to see
    /// whether anything notices, and if the entry named a live handler the run
    /// would empty a real cache on the developer machine before the assertion
    /// ever got to fail. With a name nothing answers to, the fall-through ends
    /// at "ключ обработчика не открывается" and no COM object is even created.
    /// </summary>
    private const string NetTakogoObrabotchika = "JunkManagerTests-NetTakogoObrabotchika";

    [Fact]
    public void Purge_neizvestnogo_obrabotchika_ne_sozdaet_obekt()
    {
        // Предохранитель ВМ снят решением владельца 06.09.2026, и раньше именно
        // он отказывал здесь первым. Проверка осталась, потому что осталась суть:
        // на несуществующий ключ реестра продукт обязан ответить отказом РАНЬШЕ,
        // чем создаст COM-объект. Имя обработчика при этом приходит из прохода,
        // а между проходом и очисткой ключ мог исчезнуть.
        var entry = new VolumeCacheEntry(NetTakogoObrabotchika, Eskizy, "Нет такого");

        var itog = VolumeCachePurger.Purge(entry, CancellationToken.None);

        itog.Outcome.Should().Be(PurgeOutcome.Failed);
        itog.Reason.Should().Contain("ключ обработчика не открывается");
    }

    [Fact]
    public void Purge_DownloadsFolder_otkazyvaet_dazhe_pri_vzvedennom_predohranitele()
    {
        // The blacklist is not a display filter. Even with the fuse armed, even
        // with somebody handing the entry in by hand, this handler never runs.
        // The CLSID is the one shared by twelve handlers, so the refusal has to
        // come from the key name and from nothing else.
        var entry = new VolumeCacheEntry("DownloadsFolder", ObshchiyClsid, "Загрузки");

        var act = () => VolumeCachePurger.Purge(entry, CancellationToken.None);

        act.Should().Throw<InvalidOperationException>()
           .Which.Message.Should().Contain("чёрн");
    }

    [Fact]
    public void Chernyy_spisok_ostalsya_edinstvennymi_vorotami_i_stoit_pervym()
    {
        // После снятия предохранителя 06.09.2026 чёрный список это ЕДИНСТВЕННОЕ,
        // что стоит между чужим вызовом и настоящей очисткой. Значит он обязан
        // отказывать до всякой другой работы: до открытия ключа, до создания
        // объекта. Ключ `DownloadsFolder` в реестре есть, и любой другой порядок
        // означал бы, что папка «Загрузки» очищается по дороге к проверке.
        var entry = new VolumeCacheEntry("DownloadsFolder", ObshchiyClsid, "Загрузки");

        var act = () => VolumeCachePurger.Purge(entry, CancellationToken.None);

        act.Should().Throw<InvalidOperationException>()
           .Which.Message.Should().NotContain("ключ обработчика",
               "отказ обязан прийти от чёрного списка, а не от попытки открыть ключ: "
               + "значит список спросили раньше всякой работы с реестром");
    }

    [Fact]
    public void Purge_sravnivaet_imya_klyucha_bez_oglyadki_na_registr_i_probely()
    {
        // A handler entry is read out of the registry, and the registry does not
        // promise casing. A blacklist that only matches the exact spelling is a
        // blacklist that misses "downloadsfolder".
        var entry = new VolumeCacheEntry("  downloadsFOLDER  ", ObshchiyClsid, "Загрузки");

        var act = () => VolumeCachePurger.Purge(entry, CancellationToken.None);

        act.Should().Throw<InvalidOperationException>()
           .Which.Message.Should().Contain("чёрн");
    }
}

/// <summary>
/// The other half: the call that actually frees space. It only runs in the
/// guest, and only with both fuse conditions up.
/// </summary>
[Trait("Class", "LiveDestructive")]
public sealed class VolumeCachePurgerLiveTests
{
    [Fact]
    public void Purge_eskizov_v_goste_osvobozhdaet_mesto()
    {
        VmFuse.RequireArmed();

        var entry = new VolumeCacheEntry(
            "Thumbnail Cache", new Guid("{889900c3-59f3-4c2f-ae21-a409ea01e605}"), "Эскизы");

        var before = EmptyVolumeCacheProbeHelper.Bytes(entry);
        before.Should().BeGreaterThan(0, "перед прогоном эскизы надо посеять");

        var result = VolumeCachePurger.Purge(entry, CancellationToken.None);

        result.Outcome.Should().Be(PurgeOutcome.Purged, "причина: {0}", result.Reason);
        EmptyVolumeCacheProbeHelper.Bytes(entry).Should().BeLessThan(before);
    }
}

internal static class EmptyVolumeCacheProbeHelper
{
    internal static long Bytes(VolumeCacheEntry entry) =>
        EmptyVolumeCacheInterop.Probe(entry, CancellationToken.None).SpaceUsedBytes;
}
