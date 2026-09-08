using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Ui;

[Trait("Class", "Ui")]
[Collection(UiNabor.Imya)]
public sealed class ReviewFixesScreenTests(UiFixture stend)
{
    [Fact]
    public void Program_without_estimate_gets_a_measured_size_in_the_real_list()
    {
        JunkManager.Safety.VmFuse.RequireArmed();
        using var files = new JunkManager.Tests.Infrastructure.SandboxFixture();
        files.CreateFile("payload.bin", new string('x', 10 * 1024));
        var id = Guid.NewGuid().ToString("B");
        var name = "JM size fixture " + id;
        var path = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + id;
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path))
        {
            key.SetValue("DisplayName", name);
            key.SetValue("InstallLocation", files.Root);
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
            UiFixture.Podozhdat(() => stend.Skolko("app-size") == 1 && stend.Imena("app-size").Any(value => value.Contains("10", StringComparison.Ordinal)),
                TimeSpan.FromMinutes(1)).Should().BeTrue();
            using var screenshot = stend.Snimok();
            UiPalette.Sohranit(screenshot, "program-measured-size");
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(path, false);
            if (stend.Est("apps-search")) stend.Vvesti("apps-search", string.Empty);
        }
    }
    [Fact]
    public void Registry_restrictions_are_compact_and_details_can_be_opened()
    {
        JunkManager.Safety.VmFuse.RequireArmed();
        var name = "JM UI unsupported " + Guid.NewGuid().ToString("N");
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        key.SetValue(name, "fixture without an absolute file path");
        try
        {
            stend.Perejti("registry");
            stend.Nazhat("registry-scan");
            UiFixture.Podozhdat(() => stend.Est("restriction-details-toggle"), TimeSpan.FromMinutes(2)).Should().BeTrue();
            stend.Imya("restriction-note").Should().NotContain(name);
            stend.Nazhat("restriction-details-toggle");
            UiFixture.Podozhdat(() => stend.Est("restriction-details"), TimeSpan.FromSeconds(5)).Should().BeTrue();
            stend.Imya("restriction-details").Should().Contain(name);
            using var screenshot = stend.Snimok();
            UiPalette.Sohranit(screenshot, "review-fixes-registry-details");
            stend.Nazhat("restriction-details-toggle");
        }
        finally { key.DeleteValue(name, false); }
    }

    [Fact]
    public void Settings_fit_the_enlarged_window_at_normal_scale()
    {
        stend.Perejti("settings");
        var window = stend.RamkaOkna;
        var scale = stend.MasshtabProcentov / 100.0;
        var work = System.Windows.SystemParameters.WorkArea;
        ((double)window.Width).Should().BeApproximately(Math.Min(1920, work.Width) * scale, 2);
        ((double)window.Height).Should().BeApproximately(Math.Min(900, work.Height) * scale, 2);
        if (stend.MasshtabProcentov == 100 && work.Height >= 1000)
        {
            // Saving settings should not require an archaeological expedition below the window.
            var save = stend.Ramka("settings-save");
            save.Height.Should().BeGreaterThan(0);
            save.Top.Should().BeGreaterThan(window.Top);
            save.Bottom.Should().BeLessThanOrEqualTo(window.Bottom);
        }
        using var screenshot = stend.Snimok();
        UiPalette.Sohranit(screenshot, "review-fixes-settings");
    }
}
