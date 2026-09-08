using FluentAssertions;
using JunkManager.App.Startup;
using JunkManager.Safety;
using Xunit;

namespace JunkManager.Tests.App;

[Trait("Class", "Sandbox")]
public sealed class ElevationBootstrapTests
{
    private static bool NeverCalled(string[] args) =>
        throw new InvalidOperationException("перезапуск не должен был вызываться");

    [Fact]
    public void Decide_bez_zhelaniya_povysheniya_ne_perezapuskaet()
    {
        var decision = ElevationBootstrap.Decide(
            wantElevation: false, alreadyElevated: false, args: [], relaunch: NeverCalled);

        decision.Should().Be(ElevationDecision.NotRequested);
    }

    [Fact]
    public void Decide_uzhe_pod_administratorom_ne_perezapuskaet()
    {
        // A second UAC prompt for an already elevated process is pure noise.
        var decision = ElevationBootstrap.Decide(
            wantElevation: true, alreadyElevated: true, args: [], relaunch: NeverCalled);

        decision.Should().Be(ElevationDecision.AlreadyElevated);
    }

    [Fact]
    public void Decide_klyuch_no_elevate_perebivaet_nastroyku()
    {
        // UI tests run with this switch: a UAC dialog is a modal window from
        // another process, and FlaUI cannot dismiss it.
        var decision = ElevationBootstrap.Decide(
            wantElevation: true, alreadyElevated: false,
            args: [ElevationBootstrap.NoElevateSwitch], relaunch: NeverCalled);

        decision.Should().Be(ElevationDecision.NotRequested);
    }

    [Fact]
    public void Decide_otkaz_ot_UAC_daet_UserDeclined_a_ne_isklyuchenie()
    {
        // Saying no to UAC is a supported state, not a crash.
        var decision = ElevationBootstrap.Decide(
            wantElevation: true, alreadyElevated: false, args: [],
            relaunch: _ => false);

        decision.Should().Be(ElevationDecision.UserDeclined);
    }

    [Fact]
    public void Decide_perezapusk_peredaet_klyuch_no_elevate_chtoby_ne_zaciklit()
    {
        string[]? passed = null;

        var decision = ElevationBootstrap.Decide(
            wantElevation: true, alreadyElevated: false, args: ["--scan"],
            relaunch: a => { passed = a; return true; });

        decision.Should().Be(ElevationDecision.Relaunched);
        passed.Should().Equal("--scan", ElevationBootstrap.NoElevateSwitch);
    }

    [Fact]
    public void Okno_ne_derzhit_svoego_otveta_pro_prava_administratora()
    {
        // Ответ «мы администратор» один на весь продукт и живёт в Safety. Своя
        // копия здесь разошлась бы с той, по которой принимает решения удаление,
        // и в день расхождения окно показало бы одно, а Deletion сделал другое.
        ElevationBootstrap.IsElevated.Should().Be(Elevation.IsElevated);
    }

    [Fact]
    public void Imena_argumentov_lichnosti_berutsya_iz_Safety()
    {
        // Передача личности целиком лежит на Elevation, и повторять её здесь
        // нечем и незачем. Тест сторожит другое: что окно и CLI не разошлись в
        // именах, иначе сверка профиля молча стала бы «ничего не передано».
        ElevationBootstrap.ArgumentSid.Should().Be(Elevation.ArgumentSid);
        ElevationBootstrap.ArgumentProfile.Should().Be(Elevation.ArgumentProfile);
    }
}
