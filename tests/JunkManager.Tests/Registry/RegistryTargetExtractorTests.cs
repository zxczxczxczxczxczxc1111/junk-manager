using FluentAssertions;
using JunkManager.Core.Registry;
using Xunit;

namespace JunkManager.Tests.Registry;

[Trait("Class", "Sandbox")]
public sealed class RegistryTargetExtractorTests
{
    // Файловая система, которую видит разборщик в тестах. Ровно два живых
    // файла: без них случай "путь без кавычек с пробелами" не проверяется
    // вовсе, а именно он и разваливается в чужих чистильщиках.
    private static bool Est(string put) =>
        put.Equals(@"C:\Program Files\App\app.exe", StringComparison.OrdinalIgnoreCase)
        || put.Equals(@"C:\Program Files\Old App\old.exe", StringComparison.OrdinalIgnoreCase);

    [Theory]
    // Кавычки вокруг пути, аргументы за ними.
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --minimized", @"C:\Program Files\App\app.exe")]
    [InlineData("\"C:\\Program Files\\App\\app.exe\"", @"C:\Program Files\App\app.exe")]
    // Без кавычек, пробел внутри имени каталога: спасает только опрос ФС.
    [InlineData(@"C:\Program Files\App\app.exe --minimized", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Program Files\Old App\old.exe", @"C:\Program Files\Old App\old.exe")]
    // Без пробелов вовсе.
    [InlineData(@"C:\Windows\notepad.exe", @"C:\Windows\notepad.exe")]
    // Битая ссылка без кавычек: ни один префикс не существует, но первое слово
    // оканчивается расширением исполняемого файла.
    [InlineData(@"C:\jm-net-takogo\app.exe -q", @"C:\jm-net-takogo\app.exe")]
    // Короткое имя 8.3 остаётся как есть: существование по нему Win32 решает сам.
    [InlineData(@"C:\PROGRA~1\App\app.exe", @"C:\PROGRA~1\App\app.exe")]
    // Префикс пути ядра и префикс длинного пути.
    [InlineData(@"\??\C:\Windows\notepad.exe", @"C:\Windows\notepad.exe")]
    [InlineData(@"\\?\C:\Windows\notepad.exe", @"C:\Windows\notepad.exe")]
    // rundll32: цель это библиотека, а не сам rundll32.
    [InlineData(
        "C:\\Windows\\system32\\rundll32.exe \"C:\\Program Files\\App\\lib.dll\",Zapusk",
        @"C:\Program Files\App\lib.dll")]
    [InlineData(
        @"rundll32.exe C:\Windows\system32\jm-net-takogo.dll,Zapusk",
        @"C:\Windows\system32\jm-net-takogo.dll")]
    // regsvr32 с ключами перед путём.
    [InlineData(
        "\"C:\\Windows\\system32\\regsvr32.exe\" /s \"C:\\jm\\biblioteka.ocx\"",
        @"C:\jm\biblioteka.ocx")]
    public void TryExtract_razbiraet_nastoyashchie_formy_znacheniy(string syroe, string ozhidaetsya)
    {
        var est = RegistryTargetExtractor.TryExtract(syroe, out var cel, out var otkaz, Est);

        est.Should().BeTrue(otkaz);
        cel.Should().Be(ozhidaetsya);
    }

    [Theory]
    [InlineData("", "пустое значение")]
    [InlineData("   ", "одни пробелы")]
    [InlineData("\"C:\\App\\app.exe", "кавычка не закрыта")]
    [InlineData(
        @"rundll32.exe shell32.dll,Control_RunDLL foo.cpl",
        "библиотека без пути: искать её пришлось бы правилами загрузчика")]
    [InlineData(
        @"%JM_NET_TAKOY_PEREMENNOY%\app.exe",
        "нераскрытая переменная окружения")]
    [InlineData(
        @"C:\Program Files\Dead App\app.exe -q",
        "ни один префикс не существует, а первое слово C:\\Program не исполняемый файл")]
    [InlineData(@"\\server\share\app.exe", "сетевой путь")]
    [InlineData("notepad.exe", "путь не абсолютный")]
    [InlineData(@"C:\App\a<b>.exe", "недопустимые символы")]
    public void TryExtract_nerazobrannoe_znachenie_eto_otkaz_a_ne_nahodka(string syroe, string pochemu)
    {
        var est = RegistryTargetExtractor.TryExtract(syroe, out var cel, out var otkaz, Est);

        est.Should().BeFalse(pochemu);
        cel.Should().BeEmpty("при отказе цели нет вовсе, иначе её кто-нибудь прочитает");
        otkaz.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryExtract_raskryvaet_peremennye_okruzheniya()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var est = RegistryTargetExtractor.TryExtract(
            @"%LOCALAPPDATA%\jm-net-takoy-programmy\zapusk.exe", out var cel, out var otkaz, Est);

        est.Should().BeTrue(otkaz);
        cel.Should().Be(Path.Combine(local, "jm-net-takoy-programmy", "zapusk.exe"));
    }

    [Fact]
    public void TryExtract_otkazyvaet_kogda_toma_net_na_meste()
    {
        // Вынутая флешка. Файла нет, но и записи нет: вернут диск, и она
        // оживёт. Самый дорогой класс ложных находок во всём модуле, поэтому
        // буква ищется живьём, а не берётся наугад.
        var svobodnaya = "DEFGHIJKLMNOPQRSTUVWXYZ"
            .Select(b => b + @":\")
            .FirstOrDefault(k => !Directory.Exists(k));

        svobodnaya.Should().NotBeNull("на машине обязана найтись хоть одна незанятая буква");

        var est = RegistryTargetExtractor.TryExtract(
            svobodnaya + @"jm\app.exe", out _, out var otkaz, Est);

        est.Should().BeFalse();
        otkaz.Should().Contain("корня тома");
    }
}
