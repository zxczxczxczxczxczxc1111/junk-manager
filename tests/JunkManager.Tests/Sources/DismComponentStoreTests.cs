using FluentAssertions;
using JunkManager.Core.Rules;
using JunkManager.Core.Sources.Platform;
using Xunit;

namespace JunkManager.Tests.Sources;

[Trait("Class", "Sandbox")]
public sealed class DismComponentStoreTests
{
    private static string Obrazets(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "obraztsy", name));

    [Fact]
    public void Parse_nastoyashchiy_vyvod_daet_te_zhe_chisla()
    {
        var report = DismComponentStore.Parse(Obrazets("dism-analyze-en.txt"));

        // 3.38 GB, as DISM printed it. Gibibytes: 3.38 * 1024^3 = 3629247365.12,
        // truncated. The plan said 3628344771, which is 902594 bytes short of
        // that product: the number in the plan was arithmetic done by hand.
        report.BackupsBytes.Should().Be(3629247365);
        report.ActualSizeBytes.Should().Be(12079595520);
        report.ReclaimablePackages.Should().Be(2);
        report.CleanupRecommended.Should().BeTrue();
    }

    [Fact]
    public void Parse_vyvod_bez_ozhidaemyh_metok_brosaet_a_ne_daet_nol()
    {
        // Zero means "the component store is clean". Using it for "I did not
        // understand the answer" is the difference between a finding and a lie.
        var act = () => DismComponentStore.Parse(Obrazets("dism-bez-metok.txt"));

        act.Should().Throw<RuleFormatException>().WithMessage("*Backups and Disabled Features*");
    }

    [Fact]
    public void Parse_dva_probela_posle_dvoetochiya_ne_lomayut_razbor()
    {
        // Verified quirk: DISM prints "Cache and Temporary Data :  0 bytes" with
        // two spaces. A split on ": " loses the value.
        var report = DismComponentStore.Parse("""
        Actual Size of Component Store : 1.00 GB
        Backups and Disabled Features :  0 bytes
        Number of Reclaimable Packages : 0
        Component Store Cleanup Recommended : No
        """);

        report.BackupsBytes.Should().Be(0);
        report.CleanupRecommended.Should().BeFalse();
    }

    [Theory]
    [InlineData("512 bytes", 512L)]
    [InlineData("1.00 KB", 1024L)]
    [InlineData("1.50 MB", 1572864L)]
    [InlineData("11.25 GB", 12079595520L)]
    public void ParseSize_ponimaet_edinicy_dism(string text, long expected)
    {
        DismComponentStore.ParseSize(text).Should().Be(expected);
    }

    [Fact]
    public void AnalyzeArguments_soderzhat_English_i_tolko_chtenie()
    {
        var args = DismComponentStore.AnalyzeArguments();

        args.Should().Contain("/English", "иначе вывод придёт на языке системы");
        args.Should().Contain("/AnalyzeComponentStore");
        args.Should().NotContain(a => a.Contains("Cleanup", StringComparison.OrdinalIgnoreCase)
                                      && !a.Contains("Cleanup-Image", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CleanupArguments_nikogda_ne_soderzhat_ResetBase()
    {
        // /ResetBase permanently removes the ability to uninstall installed
        // updates. That is a price nobody asked for, so it is not a setting.
        var args = DismComponentStore.CleanupArguments();

        args.Should().NotContain(a => a.Contains("ResetBase", StringComparison.OrdinalIgnoreCase));
        args.Should().Contain("/StartComponentCleanup");
        args.Should().Contain("/English");
    }
}
