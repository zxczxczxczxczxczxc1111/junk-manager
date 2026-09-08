using FluentAssertions;
using JunkManager.Core.Sources.Platform;
using Xunit;

namespace JunkManager.Tests.Sources;

[Trait("Class", "Sandbox")]
public sealed class ProcessRunnerTests
{
    [Theory]
    [InlineData("reg.exe")]
    [InlineData("dism.exe")]
    [InlineData("pnputil.exe")]
    public void SystemTool_daet_put_v_System32_a_ne_goloe_imya(string imya)
    {
        // Process.Start по голому имени ищет файл СНАЧАЛА рядом с приложением.
        // Файл dism.exe, положенный рядом с JunkManager.exe, запустился бы
        // вместо настоящего, а этот продукт удаляет драйверы, ветки реестра и
        // файлы. Это подмена, а не неудобство.
        var put = ProcessRunner.SystemTool(imya);

        Path.IsPathFullyQualified(put).Should().BeTrue();
        put.Should().StartWith(Environment.GetFolderPath(Environment.SpecialFolder.System));
        File.Exists(put).Should().BeTrue("утилита {0} входит в состав Windows", imya);
    }

    [Theory]
    [InlineData(@"C:\Users\ExampleUser\reg.exe")]
    [InlineData(@"..\reg.exe")]
    [InlineData("sub/reg.exe")]
    public void SystemTool_put_vmesto_imeni_otkazyvaet(string chuzhoy)
    {
        // Путь сюда подставляют ровно тогда, когда хотят обойти саму проверку.
        var act = () => ProcessRunner.SystemTool(chuzhoy);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SystemTool_pustoe_imya_otkazyvaet()
    {
        var act = () => ProcessRunner.SystemTool("   ");

        act.Should().Throw<ArgumentException>();
    }
}
