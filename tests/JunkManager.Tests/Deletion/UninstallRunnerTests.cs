using FluentAssertions;
using JunkManager.Core.Apps;
using JunkManager.Deletion;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Deletion;

[Trait("Class", "Sandbox")]
public sealed class UninstallRunnerTests
{
    [Fact]
    public void Reused_parent_pid_cannot_adopt_a_process_started_after_parent_exit()
    {
        var started = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        UninstallRunner.CanAssociateChild(started, started.AddSeconds(2), started.AddSeconds(1)).Should().BeTrue();
        UninstallRunner.CanAssociateChild(started, started.AddSeconds(2), started.AddSeconds(3)).Should().BeFalse();
        UninstallRunner.CanAssociateChild(started, null, started.AddSeconds(-1)).Should().BeFalse();
    }

    private static InstalledProgram Programma() =>
        new("Machine64:K", "Некая программа", "Некто", "1.0.0", null,
            @"C:\Prog\setup.exe", null, InstallerKind.Unknown, ProgramScope.Machine64, null);

    [Fact]
    public async Task RunAsync_bez_predohranitelya_otkazyvaet_i_v_zhurnal_ne_pishet()
    {
        // Состояние предохранителя задаётся ЯВНО, а не читается у машины.
        // Прогон в госте 07.09.2026 показал, почему: там предохранитель взведён
        // по замыслу, и проверка отказа падала, хотя продукт был исправен.
        // Проверка, зелёная только на машине разработчика, не проверка.
        var nevzvedennyy = () => VmFuse.RequireArmed(envValue: null, markerExists: false);

        var zhurnal = new SpisokZhurnala();

        // Команда намеренно негромкая и путь намеренно несуществующий: если
        // предохранитель когда-нибудь перестанет срабатывать, следующая же
        // проверка откажет, и ни один чужой процесс из теста не запустится.
        var komanda = new UninstallCommand(@"C:\takogo-fayla-net\uninst.exe", ["/remove"], Quiet: false);

        var act = async () => await new UninstallRunner(zhurnal, nevzvedennyy).RunAsync(
            Programma(), komanda, TimeSpan.FromSeconds(1),
            razreshitOkno: false, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*предохранитель*");
        zhurnal.Zapisi.Should().BeEmpty("отказ предохранителя это не операция удаления");
    }

    [Theory]
    [InlineData(0, UninstallExitStatus.Success)]
    [InlineData(3010, UninstallExitStatus.RebootRequired)]
    [InlineData(1641, UninstallExitStatus.RebootInitiated)]
    [InlineData(1605, UninstallExitStatus.AlreadyAbsent)]
    [InlineData(1602, UninstallExitStatus.Cancelled)]
    [InlineData(1618, UninstallExitStatus.Busy)]
    [InlineData(1603, UninstallExitStatus.Failed)]
    public void Razobrat_kody_vyhoda_chitayutsya_kak_ishody(int kod, UninstallExitStatus ozhidaemyy)
    {
        // 1605 это «продукта уже нет», и читать его как провал значит пугать
        // человека там, где всё как раз в порядке.
        UninstallRunner.ClassifyExitCode(kod, InstallerKind.Msi).Should().Be(ozhidaemyy);
    }

    [Fact]
    public void Non_msi_exit_code_is_not_given_MSI_semantics()
    {
        // Numbers do not acquire MSI citizenship by walking past msiexec.
        UninstallRunner.ClassifyExitCode(1605, InstallerKind.Unknown).Should().Be(UninstallExitStatus.Failed);
    }
}
