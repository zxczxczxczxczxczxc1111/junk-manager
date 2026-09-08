using FluentAssertions;
using JunkManager.App.ViewModels;
using JunkManager.Core.Explain;
using Xunit;

namespace JunkManager.Tests.App;

[Trait("Class", "Sandbox")]
public sealed class StorageViewModelTests
{
    [Fact]
    public async Task Analysis_starts_only_on_request_and_search_includes_paths_and_groups()
    {
        var calls = 0;
        using var model = new StorageViewModel((deep, progress, ct) =>
        {
            calls++;
            return Task.FromResult(new StorageAnalysis([new("Модели", @"C:\cache\weights", 50, "Проверь содержимое"),
                new("AppData", @"C:\apps\Spotify", 10, "Кэш")], [], false));
        });
        // Opening a tab should not punish curiosity with an unsolicited disk marathon.
        await ((IScreenViewModel)model).PriPokazeAsync(CancellationToken.None);
        calls.Should().Be(0);
        await model.AnalyzeCommand.ExecuteAsync(null);
        calls.Should().Be(1);
        model.State.Phase.Should().Be(ScreenPhase.Ready);
        model.Search = "spotify";
        model.VisibleRows.Should().ContainSingle().Which.Bytes.Should().Be(10);
        model.Search = "Модели";
        model.VisibleRows.Should().ContainSingle().Which.Bytes.Should().Be(50);
        model.Search = "missing";
        model.VisibleRows.Should().BeEmpty();
    }
}
