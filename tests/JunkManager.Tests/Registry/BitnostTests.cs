using FluentAssertions;
using JunkManager.Core.Registry;
using Xunit;

namespace JunkManager.Tests.Registry;

/// <summary>
/// The 32-bit twin of a path.
/// </summary>
/// <remarks>
/// Каталоги задаются здесь строками, а не берутся из окружения: проверка
/// обязана давать один и тот же ответ на английской и русской системе, на
/// диске C и на диске D.
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class BitnostTests
{
    private const string ProgramFiles = @"C:\Program Files";
    private const string ProgramFilesX86 = @"C:\Program Files (x86)";
    private const string System32 = @"C:\Windows\system32";
    private const string SysWow64 = @"C:\Windows\SysWOW64";

    private static string? Blizhnec(string put) =>
        Bitnost.Blizhnec32(put, ProgramFiles, ProgramFilesX86, System32, SysWow64);

    [Fact]
    public void Similar_directory_prefix_does_not_redirect()
    {
        // A shared spelling prefix is not a shared directory, despite its enthusiasm.
        Blizhnec(@"C:\Program FilesExtra\app.exe").Should().BeNull();
        Blizhnec(@"C:\Windows\System32Extra\app.dll").Should().BeNull();
    }

    [Fact]
    public void Program_Files_perevoditsya_v_x86()
    {
        Blizhnec(@"C:\Program Files\Foo\foo.exe")
            .Should().Be(@"C:\Program Files (x86)\Foo\foo.exe");
    }

    [Fact]
    public void System32_perevoditsya_v_SysWOW64()
    {
        Blizhnec(@"C:\Windows\System32\foo.dll")
            .Should().Be(@"C:\Windows\SysWOW64\foo.dll");
    }

    [Fact]
    public void Uzhe_perenapravlennyy_put_ne_perevoditsya_vtoroy_raz()
    {
        // Ворота против «(x86) (x86)»: «Program Files (x86)» начинается с
        // «Program Files», и наивная проверка префикса переводит его снова.
        Blizhnec(@"C:\Program Files (x86)\Foo\foo.exe").Should().BeNull();
        Blizhnec(@"C:\Windows\SysWOW64\foo.dll").Should().BeNull();
    }

    [Fact]
    public void Chuzhoy_put_ne_trogaetsya()
    {
        Blizhnec(@"D:\Games\foo.exe").Should().BeNull();
        Blizhnec(@"C:\Users\stend\AppData\Local\Foo\foo.exe").Should().BeNull();
    }

    [Fact]
    public void Registr_bukv_ne_meshaet()
    {
        // В реестре пути пишут как попало, и «PROGRAM FILES» это тот же
        // каталог.
        Blizhnec(@"C:\PROGRAM FILES\Foo\foo.exe")
            .Should().Be(@"C:\Program Files (x86)\Foo\foo.exe");
    }
}
