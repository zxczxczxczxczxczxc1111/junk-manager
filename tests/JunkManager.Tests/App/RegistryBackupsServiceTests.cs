using System.Text;
using FluentAssertions;
using JunkManager.App.Services;
using Xunit;

namespace JunkManager.Tests.App;

/// <summary>
/// Список своих бэкапов и откат по одному из них.
/// </summary>
/// <remarks>
/// Служба смотрит СВОЙ каталог и только его. Импорт .reg это ЗАПИСЬ в реестр, и
/// продукт, импортирующий файл, который написал не он, перестаёт быть продуктом,
/// удаляющим только доказанное.
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class RegistryBackupsServiceTests : IDisposable
{
    private readonly string _katalog =
        Path.Combine(Path.GetTempPath(), "jm-bekapy-" + Guid.NewGuid().ToString("N"));

    public RegistryBackupsServiceTests() => Directory.CreateDirectory(_katalog);

    public void Dispose()
    {
        if (Directory.Exists(_katalog))
        {
            Directory.Delete(_katalog, recursive: true);
        }
    }

    /// <summary>Файл ровно того вида, какой пишет reg.exe: UTF-16LE с меткой.</summary>
    private string Nastoyashchiy(string imya)
    {
        var put = Path.Combine(_katalog, imya);
        var tekst = "Windows Registry Editor Version 5.00\r\n\r\n"
            + "[HKEY_CURRENT_USER\\Software\\JunkManagerTests]\r\n";

        File.WriteAllText(put, tekst, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        return put;
    }

    [Fact]
    public async Task Spisok_otdaet_svezhie_pervymi()
    {
        // Человек, которому нужен откат, ищет последний бэкап, а не первый.
        var stariy = Nastoyashchiy("20260901-100000-000-HKCU-Run.reg");
        var svezhiy = Nastoyashchiy("20260905-190000-000-HKCU-Run.reg");
        File.SetLastWriteTimeUtc(stariy, new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(svezhiy, new DateTime(2026, 9, 5, 19, 0, 0, DateTimeKind.Utc));

        var spisok = await new RegistryBackupsService(_katalog)
            .ListAsync(TestContext.Current.CancellationToken);

        spisok.Should().HaveCount(2);
        spisok[0].FileName.Should().Be("20260905-190000-000-HKCU-Run.reg");
    }

    [Fact]
    public async Task Negodnyy_fayl_pokazan_i_pomechen_negodnym_a_ne_spryatan()
    {
        // Спрятать значит оставить человека гадать, куда делся его бэкап.
        // Показать без пометки значит предложить импортировать неизвестно что.
        Nastoyashchiy("horoshiy.reg");
        File.WriteAllText(Path.Combine(_katalog, "obrezannyy.reg"), "мусор", Encoding.UTF8);

        var spisok = await new RegistryBackupsService(_katalog)
            .ListAsync(TestContext.Current.CancellationToken);

        spisok.Should().HaveCount(2);

        var plohoy = spisok.Single(b => b.FileName == "obrezannyy.reg");
        plohoy.Valid.Should().BeFalse();
        plohoy.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Otsutstvuyushchiy_katalog_eto_pustoy_spisok_a_ne_padenie()
    {
        // До первой очистки реестра каталога нет вовсе, и это норма.
        var sluzhba = new RegistryBackupsService(Path.Combine(_katalog, "takogo-net"));

        var spisok = await sluzhba.ListAsync(TestContext.Current.CancellationToken);

        spisok.Should().BeEmpty();
    }

    [Fact]
    public async Task Vosstanovlenie_negodnogo_fayla_ne_nachinaetsya()
    {
        // Импорт файла, который не является экспортом реестра, хуже отсутствия
        // отката: человек после него уверен, что машину восстановили.
        File.WriteAllText(Path.Combine(_katalog, "obrezannyy.reg"), "мусор", Encoding.UTF8);

        var sluzhba = new RegistryBackupsService(_katalog);
        var spisok = await sluzhba.ListAsync(TestContext.Current.CancellationToken);

        var itog = await sluzhba.RestoreAsync(spisok[0], TestContext.Current.CancellationToken);

        itog.Ok.Should().BeFalse();
        itog.Reason.Should().Contain("импорт не начинался");
    }

    [Fact]
    public async Task Chuzhoy_katalog_ne_chitaetsya_vovse()
    {
        // Служба смотрит СВОЙ каталог и только его. Подложенный файл сделан
        // ЗАВЕДОМО ГОДНЫМ: иначе отказ пришёл бы от проверки файла, а проверка
        // сверяла бы не ту ветку, что обещает её имя.
        var sluzhba = new RegistryBackupsService(_katalog);

        sluzhba.Directory.Should().Be(_katalog);

        var chuzhoy = Path.Combine(
            Path.GetTempPath(), "chuzhoy-" + Guid.NewGuid().ToString("N") + ".reg");

        File.WriteAllText(
            chuzhoy,
            "Windows Registry Editor Version 5.00\r\n\r\n"
                + "[HKEY_CURRENT_USER\\Software\\JunkManagerTests]\r\n",
            new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

        try
        {
            var podlozhennyy = new RegistryBackupFile(
                chuzhoy, Path.GetFileName(chuzhoy), DateTimeOffset.UtcNow, 100, true, null);

            var itog = await sluzhba.RestoreAsync(podlozhennyy, TestContext.Current.CancellationToken);

            itog.Ok.Should().BeFalse();
            itog.Reason.Should().Contain("не из каталога бэкапов");
        }
        finally
        {
            File.Delete(chuzhoy);
        }
    }

    [Fact]
    public async Task Vyhod_iz_kataloga_tochkami_ne_prohodit()
    {
        // Сравнение строк пропустило бы такой путь: он НАЧИНАЕТСЯ с каталога
        // бэкапов и заканчивается где угодно.
        var sluzhba = new RegistryBackupsService(_katalog);

        var vyhod = Path.Combine(_katalog, "..", "chuzhoy.reg");

        var podlozhennyy = new RegistryBackupFile(
            vyhod, "chuzhoy.reg", DateTimeOffset.UtcNow, 100, true, null);

        var itog = await sluzhba.RestoreAsync(podlozhennyy, TestContext.Current.CancellationToken);

        itog.Ok.Should().BeFalse();
        itog.Reason.Should().Contain("не из каталога бэкапов");
    }
}
