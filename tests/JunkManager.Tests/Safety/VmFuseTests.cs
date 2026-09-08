using Xunit;
using FluentAssertions;
using JunkManager.Safety;

namespace JunkManager.Tests.Safety;

[Trait("Class", "Sandbox")]
public sealed class VmFuseTests
{
    [Fact]
    public void RequireArmed_bez_markera_i_bez_peremennoy_brosaet()
    {
        // Both conditions absent: this is the developer machine.
        var act = () => VmFuse.RequireArmed(envValue: null, markerExists: false);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*предохранитель*");
    }

    [Fact]
    public void RequireArmed_tolko_peremennaya_bez_markera_brosaet()
    {
        // The env var alone must never be enough: a stray script sets it by accident.
        var act = () => VmFuse.RequireArmed(envValue: "1", markerExists: false);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RequireArmed_tolko_marker_bez_peremennoy_brosaet()
    {
        // The marker alone is not enough either: a backup restore can put it back.
        var act = () => VmFuse.RequireArmed(envValue: null, markerExists: true);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("01")]
    public void RequireArmed_lyuboe_znachenie_krome_rovno_edinicy_brosaet(string envValue)
    {
        // Exact match only. Trimming or truthiness would widen the gate silently.
        var act = () => VmFuse.RequireArmed(envValue, markerExists: true);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RequireArmed_oba_usloviya_propuskaet()
    {
        var act = () => VmFuse.RequireArmed(envValue: "1", markerExists: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void Soobshchenie_ob_otkaze_nazyvaet_oba_usloviya_i_ih_tekushchee_sostoyanie()
    {
        // A refusal that does not say what is missing turns into a bug report.
        var act = () => VmFuse.RequireArmed(envValue: "0", markerExists: false);

        act.Should().Throw<InvalidOperationException>()
           .Which.Message.Should()
           .Contain(VmFuse.EnvName).And
           .Contain(VmFuse.MarkerPath).And
           .Contain("нет");
    }

    [Fact]
    public void IsArmed_vzveden_rovno_na_poligone_i_bolshe_nigde()
    {
        // Not a tautology: it ties the fuse to a fact it does not read, the machine
        // name, and it fails in BOTH directions. Armed on a developer box means
        // destructive tests are about to run against real files. Disarmed on the
        // polygon means they will be skipped there while the run still reports green.
        //
        // The first version of this test said "false on this machine" and went red
        // the moment it first ran inside the polygon, which is exactly right for a
        // wrong assertion and exactly why the run had to happen before trusting it.
        var naPoligone = string.Equals(
            Environment.MachineName, VmFuse.PolygonComputerName, StringComparison.OrdinalIgnoreCase);

        VmFuse.IsArmed.Should().Be(naPoligone,
            "машина {0}, полигон это {1}", Environment.MachineName, VmFuse.PolygonComputerName);
    }
}
