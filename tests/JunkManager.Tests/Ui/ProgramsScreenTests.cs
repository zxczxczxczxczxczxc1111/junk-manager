using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Ui;

[Trait("Class", "Ui")]
[Collection(UiNabor.Imya)]
public sealed class ProgramsScreenTests(UiFixture stend)
{
    [Fact(Explicit = true)]
    public async Task Installed_msix_is_removed_through_the_real_window()
    {
        // Run explicitly after vm-msix-fixture-guest.ps1; a random Store app is not a fixture.
        JunkManager.Safety.VmFuse.RequireArmed();
        var inventory = new JunkManager.Core.Apps.ProgramInventory();
        var before = await inventory.ReadAsync(TestContext.Current.CancellationToken);
        var fixture = before.Programs.Single(program => program.PackageFullName != null
            && program.PackageFullName.StartsWith("JunkManager.VmFixture_", StringComparison.Ordinal));
        stend.Perejti("apps");
        UiFixture.Podozhdat(() => stend.Est("apps-refresh"), TimeSpan.FromMinutes(2)).Should().BeTrue();
        stend.Nazhat("apps-refresh");
        UiFixture.Podozhdat(() => stend.Est("apps-search"), TimeSpan.FromMinutes(2)).Should().BeTrue();
        stend.Vvesti("apps-search", "JunkManager.VmFixture");
        UiFixture.Podozhdat(() => stend.Skolko("app-select") == 1, TimeSpan.FromSeconds(10)).Should().BeTrue();
        stend.OtmetitPervyy("app-select");
        stend.Nazhat("apps-review");
        stend.Nazhat("apps-confirm-remove");
        UiFixture.Podozhdat(() => stend.Est("apps-summary"), TimeSpan.FromMinutes(3)).Should().BeTrue();
        var after = await inventory.ProbeAsync(fixture, TestContext.Current.CancellationToken);
        after.Presence.Should().Be(JunkManager.Core.Apps.ProgramPresence.Absent, after.Reason);
        stend.TekstyVnutri("apps-results").Should().Contain(text => text.Contains("Удалена", StringComparison.OrdinalIgnoreCase));
        using var screenshot = stend.Snimok();
        UiPalette.Sohranit(screenshot, "programs-msix-removed");
        stend.BezymyannyeInteraktivnye().Should().BeEmpty();
        stend.Vylezshie().Should().BeEmpty();
        stend.Nazhat("apps-report-refresh");
        UiFixture.Podozhdat(() => stend.Est("apps-search"), TimeSpan.FromMinutes(2)).Should().BeTrue();
    }

    [Fact]
    public void Inventory_search_preview_back_and_empty_state_work_in_the_real_window()
    {
        JunkManager.Safety.VmFuse.RequireArmed();
        var id = Guid.NewGuid().ToString("B");
        var name = "JM UI fixture " + id;
        var branch = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + id;
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(branch))
        {
            key.SetValue("DisplayName", name);
            key.SetValue("Publisher", "Junk Manager VM fixture");
            key.SetValue("DisplayVersion", "1.0");
            key.SetValue("WindowsInstaller", 1);
            key.SetValue("UninstallString", "msiexec.exe /I" + id);
        }
        try
        {
            stend.Perejti("apps");
            UiFixture.Podozhdat(() => stend.Est("apps-refresh"), TimeSpan.FromMinutes(2)).Should().BeTrue();
            stend.Nazhat("apps-refresh");
            UiFixture.Podozhdat(() => stend.Est("apps-search"), TimeSpan.FromMinutes(2)).Should().BeTrue();
            stend.Vvesti("apps-search", name);
            UiFixture.Podozhdat(() => stend.Skolko("app-select") == 1, TimeSpan.FromSeconds(10)).Should().BeTrue();
            stend.Imena("app-select").Should().Contain(name);
            stend.OtmetitPervyy("app-select");
            UiFixture.Podozhdat(() => stend.Dostupna("apps-review"), TimeSpan.FromSeconds(5)).Should().BeTrue();
            stend.Nazhat("apps-review");
            UiFixture.Podozhdat(() => stend.Est("apps-confirm-remove"), TimeSpan.FromSeconds(10)).Should().BeTrue();
            stend.Dostupna("apps-confirm-remove").Should().BeTrue();
            stend.TekstyVnutri("apps-confirm-list").Should().Contain(name);
            // A preview must never launch this deliberately unregistered MSI. The GUID has suffered enough.
            stend.Nazhat("apps-back");
            stend.Vvesti("apps-search", "definitely-no-program-" + id);
            UiFixture.Podozhdat(() => stend.Est("apps-empty"), TimeSpan.FromSeconds(10)).Should().BeTrue();
            stend.Vvesti("apps-search", name);
            UiFixture.Podozhdat(() => stend.Skolko("app-select") == 1, TimeSpan.FromSeconds(10)).Should().BeTrue();
            stend.BezymyannyeInteraktivnye().Should().BeEmpty();
            stend.Vylezshie().Should().BeEmpty();
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(branch, false);
            if (stend.Est("apps-search")) stend.Vvesti("apps-search", string.Empty);
        }
    }
}
