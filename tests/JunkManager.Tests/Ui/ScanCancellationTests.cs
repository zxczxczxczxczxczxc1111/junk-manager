using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Ui;

[Trait("Class", "Ui")]
[Collection(UiNabor.Imya)]
public sealed class ScanCancellationTests(UiFixture stend)
{
    [Fact]
    public void Cancel_button_stops_the_scan_and_rescan_finishes()
    {
        // A button that only changes its own mood is not a cancellation mechanism.
        stend.ObespechitProhod();
        stend.Perejti("overview");
        stend.Nazhat("overview-rescan");
        UiFixture.Podozhdat(() => stend.Est("loading-cancel"), TimeSpan.FromSeconds(5)).Should().BeTrue();
        stend.Nazhat("loading-cancel");
        UiFixture.Podozhdat(() => !stend.Est("loading-cancel"), TimeSpan.FromSeconds(10)).Should().BeTrue(
            "остановка должна вернуть управление, а не ждать завершения полного прохода");
        stend.Nazhat(stend.Est("empty-action") ? "empty-action" : "overview-rescan");
        UiFixture.Podozhdat(() => stend.Est("overview-goto-files"), TimeSpan.FromMinutes(2)).Should().BeTrue();
        stend.Imya("overview-total").Should().MatchRegex(@"\d+([,.]\d+)?\s*(Б|КБ|МБ|ГБ|ТБ)");
    }
}
