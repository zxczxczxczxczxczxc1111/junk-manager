using Xunit;
using FluentAssertions;
using JunkManager.Safety;

namespace JunkManager.Tests.Safety;

/// <summary>
/// Pure tests: no file is created, no path has to exist. The guard is a decision
/// about a string, and it must reach the same decision on any machine.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class SafetyGuardTests
{
    private static string Win(string tail) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), tail);

    private static string Local(string tail) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), tail);

    private static string Roaming(string tail) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), tail);

    private static string Profile(string tail) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), tail);

    // Пути пользователя строятся из окружения, а не пишутся строкой. Набор
    // гоняется и на машине разработчика, и в госте, где учётная запись зовётся
    // иначе, и захардкоженный профиль покраснел бы там без единого дефекта в коде.
    public static TheoryData<string> Zapreshchennye() =>
    [
        @"C:\",
        @"C:\Windows",
        @"C:\Windows\",
        @"C:\Windows\System32",
        @"C:\Windows\System32\drivers\etc\hosts",
        @"C:\Windows\WinSxS",
        @"C:\Windows\WinSxS\amd64_something",
        @"C:\Windows\Installer",
        @"C:\Windows\assembly",
        @"C:\Program Files",
        @"C:\Program Files\SomeApp\app.exe",
        @"C:\Program Files (x86)",
        @"C:\Users",
        @"C:\Users\ExampleUser",
        @"C:\Users\ExampleUser\Documents",
        Profile(string.Empty),
        Profile("Documents"),
        Profile("Downloads"),
        Profile("Desktop"),
        @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs",
    ];

    public static TheoryData<string> Razreshennye() =>
    [
        Local(@"Temp\thing"),
        Local(@"Temp"),
        Local(@"Google\Chrome\User Data\Default\Cache"),
        Local(@"Microsoft\Windows\INetCache\IE\junk.tmp"),
        Local(@"CrashDumps\app.dmp"),
        Local(@"Packages\Something\LocalCache"),
        Local(@"NVIDIA\DXCache"),
        Roaming(@"Something\Cache\file.bin"),
        Win(@"Temp\thing"),
        Win(@"Prefetch\APP.EXE-1234.pf"),
        Win(@"Logs\CBS\CBS.log"),
        Win(@"SoftwareDistribution\Download\something"),
        Win(@"SystemTemp\thing"),
        Win(@"Panther\setupact.log"),
        Win(@"System32\LogFiles\HTTPERR\httperr1.log"),
        Path.Combine(Path.GetTempPath(), "thing"),
    ];

    [Theory]
    [MemberData(nameof(Zapreshchennye))]
    public void TryVerify_zapreshchennye_otklonyayutsya(string path)
    {
        SafetyGuard.TryVerify(path, out _, out var reason).Should().BeFalse(
            "путь {0} обязан быть отклонён", path);
        reason.Should().NotBeNullOrWhiteSpace("отказ обязан называть причину");
    }

    [Theory]
    // Регистр, направление слэшей и лишние части не должны ничего менять.
    [InlineData(@"c:\windows\system32")]
    [InlineData(@"C:/Windows/System32")]
    [InlineData(@"C:\Windows\..\Windows\System32")]
    [InlineData(@"C:\Windows\System32\..\System32")]
    [InlineData(@"C:\Windows\.\System32")]
    [InlineData(@"\\?\C:\Windows\System32")]
    [InlineData(@"C:\Windows\System32\")]
    [InlineData("  C:\\Windows\\System32  ")]
    public void TryVerify_obhody_kanonizacii_ne_prohodyat(string path)
    {
        SafetyGuard.TryVerify(path, out _, out _).Should().BeFalse(
            "обход через {0} обязан быть закрыт", path);
    }

    [Theory]
    // Имя, которое лишь НАЧИНАЕТСЯ как запрещённое, запрещённым не является.
    [InlineData(@"C:\WindowsApps\cache")]
    [InlineData(@"C:\Windows.old.backup\cache")]
    [InlineData(@"C:\Users2\shared\cache")]
    [InlineData(@"C:\Program Files Custom\cache")]
    [InlineData(@"D:\ProgramData2\cache")]
    public void TryVerify_pohozhie_imena_ne_schitayutsya_zapreshchennymi(string path)
    {
        // Классическая ошибка через StartsWith: строка "C:\Windows" является
        // префиксом строки "C:\WindowsApps", хотя каталог в каталоге не лежит.
        SafetyGuard.TryVerify(path, out _, out var reason).Should().BeTrue(
            "{0} только начинается как запрещённый, но им не является. Причина отказа: {1}",
            path, reason);
    }

    [Theory]
    [MemberData(nameof(Razreshennye))]
    public void TryVerify_razreshennye_prohodyat(string path)
    {
        SafetyGuard.TryVerify(path, out var verified, out var reason)
            .Should().BeTrue("путь {0} обязан пройти, причина отказа: {1}", path, reason);
        verified.Value.Should().NotBeNullOrWhiteSpace();
        verified.Resolved.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("не абсолютный\\путь")]
    [InlineData(@"..\..\Windows")]
    [InlineData(@"\Windows\System32")]
    [InlineData(@"\\server\share\thing")]
    [InlineData(@"\\?\UNC\server\share\thing")]
    [InlineData("C:\\Windows\\Temp\\<>|")]
    [InlineData("C:\\Windows\\Temp\\звёздочка*")]
    [InlineData("C:\\Windows\\Temp\\вопрос?")]
    public void TryVerify_musor_na_vhode_otklonyaetsya_a_ne_padaet(string? path)
    {
        var act = () => SafetyGuard.TryVerify(path!, out _, out _);

        act.Should().NotThrow("мусор на входе это отказ, а не исключение");
        SafetyGuard.TryVerify(path!, out _, out var reason).Should().BeFalse();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryVerify_pri_otkaze_ne_otdayot_propusk()
    {
        // Отказ обязан оставлять пропуск пустым: иначе вызывающий код может
        // взять out-значение, не посмотрев на результат.
        SafetyGuard.TryVerify(@"C:\Windows\System32", out var verified, out _).Should().BeFalse();

        verified.Should().Be(default(VerifiedPath));
    }

    [Fact]
    public void VerifiedPath_nelzya_sobrat_snaruzhi()
    {
        // Конструктор internal, и это гарантия уровня типа, а не привычка.
        typeof(VerifiedPath).GetConstructors()
            .Should().BeEmpty("публичных конструкторов у пропуска быть не должно");
    }

    [Fact]
    public void Kanonizaciya_privodit_raznye_zapisi_odnogo_puti_k_odnoy_stroke()
    {
        var razreshennyy = Win(@"Temp\thing");
        var okolnyy = Win(@"System32\..\Temp\.\thing");

        SafetyGuard.TryVerify(razreshennyy, out var pervyy, out _).Should().BeTrue();
        SafetyGuard.TryVerify(okolnyy, out var vtoroy, out _).Should().BeTrue();

        vtoroy.Value.Should().Be(pervyy.Value);
        vtoroy.Should().Be(pervyy);
    }
}
