using System.Text.Json;
using JunkManager.Deletion;
using JunkManager.Tests.Infrastructure;
using Xunit;
using FluentAssertions;

namespace JunkManager.Tests.Deletion;

/// <summary>
/// Журнал проверяется по содержимому файла, а не по вызовам: он существует для
/// того, кто откроет этот файл через неделю, и единственный способ проверить,
/// что ему там что-то будет видно, это прочитать файл.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class JsonlOperationLogTests : IDisposable
{
    private readonly SandboxFixture _pesochnica = new();

    public void Dispose() => _pesochnica.Dispose();

    private static DateTimeOffset Moment(int chas, int minuta, int sekunda) =>
        new(2026, 9, 5, chas, minuta, sekunda, TimeSpan.Zero);

    [Fact]
    public async Task Zhurnal_pishet_zagolovok_i_stroku_na_kazhdyy_iskhod()
    {
        using var zhurnal = JsonlOperationLog.CreateForRun(_pesochnica.Root);

        await zhurnal.RecordAsync(
            new DeleteOutcome(@"C:\a", DeleteStatus.Deleted, 100),
            TestContext.Current.CancellationToken);
        await zhurnal.RecordAsync(
            new DeleteOutcome(@"C:\b", DeleteStatus.Skipped, 0, "занят"),
            TestContext.Current.CancellationToken);

        var stroki = JsonlOperationLog.ReadLines(zhurnal.FilePath);

        stroki.Should().HaveCount(3, "заголовок запуска плюс два исхода");
        stroki[0].Should().Contain("\"Kind\":\"run\"");
        stroki[1].Should().Contain("\"Status\":\"Deleted\"");
        stroki[2].Should().Contain("\"Status\":\"Skipped\"");
    }

    [Fact]
    public async Task Zhurnal_viden_do_zakrytiya_a_ne_tolko_posle()
    {
        // Смысл всего формата. Если запись видна только после закрытия, то
        // единственный случай, ради которого журнал заведён, а именно «процесс
        // умер посередине», оставляет пустой файл.
        using var zhurnal = JsonlOperationLog.CreateForRun(_pesochnica.Root);

        await zhurnal.RecordAsync(
            new DeleteOutcome(@"C:\uspel", DeleteStatus.Deleted, 7),
            TestContext.Current.CancellationToken);

        var soderzhimoe = string.Join(Environment.NewLine, JsonlOperationLog.ReadLines(zhurnal.FilePath));

        soderzhimoe.Should().Contain("uspel",
            "строка обязана быть на диске сразу, а не ждать Dispose");
    }

    [Fact]
    public async Task Zhurnal_ne_ekraniruet_kirillicu()
    {
        using var zhurnal = JsonlOperationLog.CreateForRun(_pesochnica.Root);

        await zhurnal.RecordAsync(
            new DeleteOutcome(@"C:\temp", DeleteStatus.Skipped, 0, "файл занят процессом Проводник"),
            TestContext.Current.CancellationToken);

        var soderzhimoe = string.Join(Environment.NewLine, JsonlOperationLog.ReadLines(zhurnal.FilePath));

        soderzhimoe.Should().Contain("файл занят процессом Проводник",
            "журнал читают люди, а \\u0444\\u0430\\u0439\\u043b читать нельзя");
    }

    [Fact]
    public async Task Zhurnal_razbiraetsya_obratno_stroka_za_strokoy()
    {
        using var zhurnal = JsonlOperationLog.CreateForRun(_pesochnica.Root);

        await zhurnal.RecordAsync(
            new DeleteOutcome(@"C:\x", DeleteStatus.Failed, 55, "каталог ушёл не весь"),
            TestContext.Current.CancellationToken);

        var stroki = JsonlOperationLog.ReadLines(zhurnal.FilePath);

        // Формат обязан быть машинно читаемым, иначе это просто лог.
        using var razobrannaya = JsonDocument.Parse(stroki[1]);
        var koren = razobrannaya.RootElement;

        koren.GetProperty("Path").GetString().Should().Be(@"C:\x");
        koren.GetProperty("BytesFreed").GetInt64().Should().Be(55);
        koren.GetProperty("Reason").GetString().Should().Be("каталог ушёл не весь");
    }

    [Fact]
    public void Zhurnal_kazhdogo_zapuska_v_svoyom_fayle()
    {
        using var pervyy = JsonlOperationLog.CreateForRun(
            _pesochnica.Root, new FiksirovannoeVremya(Moment(3, 0, 0)));
        using var vtoroy = JsonlOperationLog.CreateForRun(
            _pesochnica.Root, new FiksirovannoeVremya(Moment(4, 0, 0)));

        pervyy.FilePath.Should().NotBe(vtoroy.FilePath);
        Path.GetFileName(pervyy.FilePath).Should().Be("20260905-030000-000.jsonl");
        Path.GetFileName(vtoroy.FilePath).Should().Be("20260905-040000-000.jsonl");
    }

    [Fact]
    public void Zhurnal_po_umolchaniyu_lozhitsya_v_LOCALAPPDATA()
    {
        // Путь берётся у системы, а не собирается из строк: на машине с
        // перенесённым профилем склеенный вручную путь укажет в никуда.
        var ozhidaemyy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JunkManager",
            "history");

        JsonlOperationLog.DefaultDirectory.Should().Be(ozhidaemyy);
    }

    [Fact]
    public async Task Zhurnal_posle_zakrytiya_otkazyvaetsya_pisat()
    {
        var zhurnal = JsonlOperationLog.CreateForRun(_pesochnica.Root);
        zhurnal.Dispose();

        var zapis = async () => await zhurnal.RecordAsync(
            new DeleteOutcome(@"C:\pozdno", DeleteStatus.Deleted, 1),
            TestContext.Current.CancellationToken);

        await zapis.Should().ThrowAsync<ObjectDisposedException>(
            "молчаливая потеря записи хуже громкого отказа");
    }
}
