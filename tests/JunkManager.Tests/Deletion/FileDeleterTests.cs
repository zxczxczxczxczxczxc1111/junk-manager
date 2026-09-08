using JunkManager.Deletion;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Xunit;
using FluentAssertions;

namespace JunkManager.Tests.Deletion;

/// <summary>
/// Единственное место в решении, которому позволено удалять с диска. Поэтому
/// проверок тут больше, чем у остальных: каждая описывает случай, когда удалить
/// можно не то.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class FileDeleterTests : IDisposable
{
    private readonly SandboxFixture _pesochnica = new();
    private readonly SpisokZhurnala _zhurnal = new();
    private readonly FileDeleter _udalitel;

    public FileDeleterTests() => _udalitel = new FileDeleter(_zhurnal);

    public void Dispose() => _pesochnica.Dispose();

    /// <summary>
    /// Пропуск добывается настоящим guard-ом, а не подделкой. Конструктор
    /// VerifiedPath внутренний именно для того, чтобы этот шаг нельзя было
    /// пропустить ни в проде, ни в тесте.
    /// </summary>
    private static VerifiedPath Propusk(string put)
    {
        SafetyGuard.TryVerifyForDeletion(put, out var propusk, out var prichina)
            .Should().BeTrue("тест обязан начинаться с настоящего пропуска, отказ: {0}", prichina);
        return propusk;
    }

    [Fact]
    public async Task DeleteAsync_udalyaet_fayl_mimo_korziny_i_schitaet_osvobozhdennoe()
    {
        var fayl = _pesochnica.CreateFile("kesh.dat", new string('x', 5000));
        var propusk = Propusk(fayl);
        var korzinaDo = KorzinaSchet.Predmetov(fayl);

        var itog = await _udalitel.DeleteAsync(
            propusk, DeleteMode.Permanent, TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Deleted);
        itog.BytesFreed.Should().Be(5000, "освобождено ровно столько, сколько занимал файл");
        File.Exists(fayl).Should().BeFalse();
        KorzinaSchet.Predmetov(fayl).Should().BeLessThanOrEqualTo(korzinaDo,
            "безвозвратное удаление обязано пройти мимо корзины, иначе оно не безвозвратное");
    }

    [Fact]
    public async Task DeleteAsync_zanyatyy_fayl_daet_Failed_a_ne_isklyuchenie()
    {
        var fayl = _pesochnica.CreateFile("zanyato.log");
        var propusk = Propusk(fayl);

        using var derzhatel = new FileStream(fayl, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var itog = await _udalitel.DeleteAsync(
            propusk, DeleteMode.Permanent, TestContext.Current.CancellationToken);

        // Именно Failed: «пропущено» значит «решили не брать», а решения тут не
        // было. Слово в отчёте решает, пойдёт человек искать выход или примет
        // отказ за замысел продукта.
        itog.Status.Should().Be(
            DeleteStatus.Failed, "занятый файл это неудача, а не принятое решение");
        itog.Reason.Should().NotBeNullOrWhiteSpace();
        itog.BytesFreed.Should().Be(0, "ничего не удалено, значит и освобождать нечего");
        File.Exists(fayl).Should().BeTrue();

        // Отказ без имени держателя это пожатие плечами, а не ответ: человеку
        // нечего закрыть и повторить.
        itog.HoldingProcess.Should().NotBeNull();
        itog.HoldingProcess.Should().Contain(
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.CurrentCulture),
            "держит файл именно этот процесс, и отказ обязан его назвать");
    }

    [Fact]
    public async Task DeleteAsync_ischeznuvshiy_put_daet_Skipped_a_ne_isklyuchenie()
    {
        var fayl = _pesochnica.CreateFile("isparilsya.tmp");
        var propusk = Propusk(fayl);

        // Между проверкой и удалением файл убрал кто-то другой. Это не ошибка
        // продукта и не повод падать: цель достигнута чужими руками.
        File.Delete(fayl);

        var itog = await _udalitel.DeleteAsync(
            propusk, DeleteMode.Permanent, TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        itog.Reason.Should().Contain("исчез");
    }

    [Fact]
    public async Task DeleteAsync_katalog_udalyaetsya_rekursivno_no_ne_cherez_ssylku()
    {
        var chuzhoy = _pesochnica.CreateDirectory("chuzhoe");
        var chuzhoyFayl = Path.Combine(chuzhoy, "ne-trogat.txt");
        File.WriteAllText(chuzhoyFayl, "это не наше");

        var kesh = _pesochnica.CreateDirectory("kesh");
        File.WriteAllText(Path.Combine(kesh, "a.bin"), new string('a', 100));
        Directory.CreateDirectory(Path.Combine(kesh, "vnutri"));
        File.WriteAllText(Path.Combine(kesh, "vnutri", "b.bin"), new string('b', 200));
        _pesochnica.CreateJunction(Path.Combine("kesh", "ssylka"), chuzhoy);

        var propusk = Propusk(kesh);

        var itog = await _udalitel.DeleteAsync(
            propusk, DeleteMode.Permanent, TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Deleted);
        Directory.Exists(kesh).Should().BeFalse("каталог обязан уйти целиком");
        File.Exists(chuzhoyFayl).Should().BeTrue(
            "внутри был junction наружу, и удаление обязано снять ссылку, а не вычистить цель");
        itog.BytesFreed.Should().Be(300,
            "считаются свои 100 и 200 байт, чужие за ссылкой не наши и в счёт не идут");
    }

    [Fact]
    public async Task DeleteAsync_podmena_puti_na_ssylku_posle_proverki_otklonyaetsya()
    {
        var chuzhoy = _pesochnica.CreateDirectory("cel-podmeny");
        var chuzhoyFayl = Path.Combine(chuzhoy, "zhivi.txt");
        File.WriteAllText(chuzhoyFayl, "меня трогать нельзя");

        var zhertva = _pesochnica.CreateDirectory("zhertva");
        File.WriteAllText(Path.Combine(zhertva, "svoy.bin"), "свой мусор");

        var propusk = Propusk(zhertva);

        // Классическая гонка TOCTOU: путь проверили, а потом под ним оказалась
        // ссылка на чужое. Пропуск, выданный минуту назад, обязан протухнуть.
        Directory.Delete(zhertva, recursive: true);
        _pesochnica.CreateJunction("zhertva", chuzhoy);

        var itog = await _udalitel.DeleteAsync(
            propusk, DeleteMode.Permanent, TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        itog.Reason.Should().Contain("ссылк");
        File.Exists(chuzhoyFayl).Should().BeTrue("цель подмены обязана уцелеть полностью");
        Directory.Exists(zhertva).Should().BeTrue("даже саму подсунутую ссылку мы не трогаем");
    }

    [Fact]
    public async Task DeleteAsync_katalog_s_zanyatym_faylom_daet_Failed_i_nazyvaet_derzhatelya()
    {
        var kesh = _pesochnica.CreateDirectory("s-zanyatym");
        File.WriteAllText(Path.Combine(kesh, "uydet.bin"), new string('a', 10));
        var zanyatyy = Path.Combine(kesh, "ne-uydet.bin");
        File.WriteAllText(zanyatyy, new string('b', 20));

        var propusk = Propusk(kesh);

        using var derzhatel = new FileStream(zanyatyy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var itog = await _udalitel.DeleteAsync(
            propusk, DeleteMode.Permanent, TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Failed,
            "каталог ушёл не весь, и это неудача, а не решение пропустить");
        itog.BytesFreed.Should().Be(10, "считается то, что действительно ушло, а не то, что обещали");
        itog.HoldingProcess.Should().Contain(
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.CurrentCulture));
        File.Exists(zanyatyy).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_fayl_tolko_dlya_chteniya_vsyo_ravno_udalyaetsya()
    {
        var fayl = _pesochnica.CreateFile("readonly.dat", new string('r', 42));
        File.SetAttributes(fayl, FileAttributes.ReadOnly);
        var propusk = Propusk(fayl);

        var itog = await _udalitel.DeleteAsync(
            propusk, DeleteMode.Permanent, TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Deleted,
            "флаг «только для чтения» ставят установщики на кэш, и это не защита от очистки");
        File.Exists(fayl).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_pishet_v_zhurnal_i_uspeh_i_neudachu()
    {
        var udalyaemyy = _pesochnica.CreateFile("uydet.tmp", "1234");
        var zanyatyy = _pesochnica.CreateFile("ostanetsya.tmp", "5678");

        var propuskUdalyaemogo = Propusk(udalyaemyy);
        var propuskZanyatogo = Propusk(zanyatyy);

        using (new FileStream(zanyatyy, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await _udalitel.DeleteAsync(
                propuskUdalyaemogo, DeleteMode.Permanent, TestContext.Current.CancellationToken);
            await _udalitel.DeleteAsync(
                propuskZanyatogo, DeleteMode.Permanent, TestContext.Current.CancellationToken);
        }

        _zhurnal.Zapisi.Should().HaveCount(2, "в журнал идёт КАЖДЫЙ исход, а не только удачный");
        _zhurnal.Zapisi[0].Status.Should().Be(DeleteStatus.Deleted);
        _zhurnal.Zapisi[0].Path.Should().Be(propuskUdalyaemogo.Value);
        _zhurnal.Zapisi[1].Status.Should().Be(DeleteStatus.Failed);
        _zhurnal.Zapisi[1].Path.Should().Be(propuskZanyatogo.Value);
    }

    [Fact]
    public async Task DeleteAsync_otmena_daet_Cancelled_i_nichego_ne_udalyaet()
    {
        var kesh = _pesochnica.CreateDirectory("bolshoy");
        for (var i = 0; i < 50; i++)
        {
            File.WriteAllText(Path.Combine(kesh, "f" + i + ".bin"), new string('z', 10));
        }

        var propusk = Propusk(kesh);
        using var otmena = new CancellationTokenSource();
        await otmena.CancelAsync();

        var itog = await _udalitel.DeleteAsync(propusk, DeleteMode.Permanent, otmena.Token);

        itog.Status.Should().Be(DeleteStatus.Cancelled,
            "отмена это отдельный исход: у неё другой смысл, чем у отказа");
        Directory.Exists(kesh).Should().BeTrue("отменённое удаление не доводится до конца");
        _zhurnal.Zapisi.Should().ContainSingle(
            "отменённая операция обязана попасть в журнал так же, как любая другая");
    }

    [Fact]
    public async Task DeleteAsync_otmena_pered_faylom_ostavlyaet_fayl_na_meste()
    {
        // Отдельно от каталога: у файла и у каталога это разные ветки кода, и
        // проверка одной из них ничего не говорит о второй.
        var fayl = _pesochnica.CreateFile("ne-uspeli.tmp", "содержимое");
        var propusk = Propusk(fayl);

        using var otmena = new CancellationTokenSource();
        await otmena.CancelAsync();

        var itog = await _udalitel.DeleteAsync(propusk, DeleteMode.Permanent, otmena.Token);

        itog.Status.Should().Be(DeleteStatus.Cancelled);
        File.Exists(fayl).Should().BeTrue("отменённое удаление не трогает файл");
    }

    [Fact]
    public async Task DeleteAsync_pustoy_propusk_otklonyaetsya_a_ne_padaet()
    {
        // default(VerifiedPath) снаружи получить можно всегда: это структура.
        // Значит удаление обязано пережить и такой вход.
        var itog = await _udalitel.DeleteAsync(
            default, DeleteMode.Permanent, TestContext.Current.CancellationToken);

        itog.Status.Should().Be(DeleteStatus.Skipped);
        itog.BytesFreed.Should().Be(0);
    }
}
