using FluentAssertions;
using JunkManager.Core.Sources.Platform;
using Xunit;

namespace JunkManager.Tests.Sources;

[Trait("Class", "Sandbox")]
public sealed class PnpDriverStoreTests
{
    private static string Xml =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "obraztsy", "pnputil-drivers.xml"));

    [Fact]
    public void Parse_chitaet_vse_pakety_i_privyazku_k_ustroystvam()
    {
        var all = PnpDriverStore.Parse(Xml);

        all.Should().HaveCount(5);
        all.Single(p => p.PublishedName == "oem11.inf").HasDevices.Should().BeTrue();
        all.Single(p => p.PublishedName == "oem12.inf").HasDevices.Should().BeFalse();

        // <Devices> missing entirely is not the same XML as <Devices />, and both
        // mean the same thing: nothing is using this package.
        all.Single(p => p.PublishedName == "oem14.inf").HasDevices.Should().BeFalse();
    }

    [Fact]
    public void SelectRemovable_beret_tolko_vytesnennye_versii()
    {
        var removable = PnpDriverStore.SelectRemovable(PnpDriverStore.Parse(Xml))
            .Select(p => p.PublishedName)
            .ToList();

        removable.Should().BeEquivalentTo(["oem12.inf", "oem14.inf"]);
    }

    [Fact]
    public void SelectRemovable_ne_beret_edinstvennuyu_versiyu_dazhe_bez_ustroystv()
    {
        // odinokiy.inf has no devices and no sibling. It may simply be an
        // optional package nothing has plugged in yet, and "unused" is not
        // "superseded". Removing it is somebody else's decision.
        var removable = PnpDriverStore.SelectRemovable(PnpDriverStore.Parse(Xml));

        removable.Should().NotContain(p => p.PublishedName == "oem77.inf");
    }

    [Fact]
    public void SelectRemovable_ne_beret_aktivnuyu_dazhe_esli_ona_staraya()
    {
        var all = PnpDriverStore.Parse(Xml);

        PnpDriverStore.SelectRemovable(all).Should().NotContain(p => p.HasDevices,
            "активная версия определяется по устройствам, а не по номеру");
    }

    [Fact]
    public void Parse_pustoy_vyvod_daet_pustoy_spisok_a_ne_isklyuchenie()
    {
        var empty = """<?xml version="1.0" encoding="utf-8"?><PnpUtil Version="10.0.26100" />""";

        PnpDriverStore.Parse(empty).Should().BeEmpty();
    }

    [Fact]
    public void Parse_bityy_xml_daet_pustoy_spisok_a_ne_isklyuchenie()
    {
        PnpDriverStore.Parse("<PnpUtil><Driver").Should().BeEmpty();
    }

    [Fact]
    public void DeleteArguments_bez_uninstall_i_bez_force()
    {
        var args = PnpDriverStore.DeleteArguments("oem12.inf");

        args.Should().BeEquivalentTo(["/delete-driver", "oem12.inf"],
            options => options.WithStrictOrdering());
        args.Should().NotContain("/force", "принудительное удаление вырывает драйвер из-под работающего устройства");
        args.Should().NotContain("/uninstall");
    }

    [Theory]
    [InlineData("oem12")]
    [InlineData("oem12.txt")]
    [InlineData(@"..\..\Windows\System32\drivers\etc\hosts")]
    [InlineData("oem12.inf /force")]
    public void DeleteArguments_ne_prinimaet_chuzhoe_imya(string name)
    {
        var act = () => PnpDriverStore.DeleteArguments(name);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryMeasure_na_zhivoy_mashine_schitaet_ili_chestno_priznaetsya()
    {
        foreach (var package in PnpDriverStore.SelectRemovable(PnpDriverStore.LoadLive()))
        {
            var measured = PnpDriverStore.TryMeasure(package, out var bytes, out var reason);

            if (measured)
            {
                bytes.Should().BeGreaterThan(0);
            }
            else
            {
                reason.Should().NotBeNullOrWhiteSpace("причина обязана быть названа");
            }
        }
    }
}
