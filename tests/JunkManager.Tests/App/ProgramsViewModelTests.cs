using System.IO;
using FluentAssertions;
using JunkManager.App.Services;
using JunkManager.App.ViewModels;
using JunkManager.Core;
using JunkManager.Core.Apps;
using JunkManager.Deletion;
using Xunit;

namespace JunkManager.Tests.App;

[Trait("Class", "Sandbox")]
public sealed class ProgramsViewModelTests
{
    [Fact]
    public async Task Background_sizes_keep_the_list_usable_and_preserve_selection_when_sorted()
    {
        var size = new TaskCompletionSource<ProgramSizeEstimate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakePrograms { Inventory = new([Program("Large"), Program("Small") with { EstimatedSizeBytes = 5 }], []),
            Measure = (_, ct) => size.Task.WaitAsync(ct) };
        using var model = new ProgramsViewModel(service, true);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.IsBusy.Should().BeFalse();
        model.Rows[0].Name.Should().Be("Small");
        var large = model.Rows.Single(row => row.Name == "Large");
        large.IsSelected = true;
        large.SizePending.Should().BeTrue();
        size.SetResult(new(100, false, "folder"));
        await model.SizeCalculation;
        model.Rows[0].Should().BeSameAs(large);
        model.SelectedCount.Should().Be(1);
        large.SizeBytes.Should().Be(100);
        large.Source.EstimatedSizeBytes.Should().BeNull("display measurement must not rewrite uninstall evidence");
    }

    [Fact]
    public async Task Refresh_cancels_old_measurement_and_does_not_overwrite_new_rows()
    {
        var first = new TaskCompletionSource<ProgramSizeEstimate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<ProgramSizeEstimate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var service = new FakePrograms { Inventory = new([Program("Fixture")], []),
            Measure = (_, ct) => (++calls == 1 ? first.Task : second.Task).WaitAsync(ct) };
        using var model = new ProgramsViewModel(service, true);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        var old = model.SizeCalculation;
        await model.LoadAsync(TestContext.Current.CancellationToken);
        second.SetResult(new(200, true, "partial"));
        await Task.WhenAll(old, model.SizeCalculation);
        model.Rows.Single().SizeBytes.Should().Be(200);
        model.Rows.Single().Size.Should().StartWith("≥");
    }

    [Fact]
    public async Task Removal_preview_cancels_pending_folder_reads()
    {
        var size = new TaskCompletionSource<ProgramSizeEstimate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakePrograms { Inventory = new([Program("Fixture")], []), Measure = (_, ct) => size.Task.WaitAsync(ct) };
        using var model = new ProgramsViewModel(service, true);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.Rows[0].IsSelected = true;
        model.ReviewRemovalCommand.Execute(null);
        await model.SizeCalculation;
        model.IsConfirmRemoval.Should().BeTrue();
        model.Rows[0].SizePending.Should().BeFalse();
        service.RemoveCalls.Should().Be(0);
    }
    [Fact]
    public async Task Default_sort_and_confirmation_put_largest_first_and_unknown_sizes_last()
    {
        var service = new FakePrograms { Inventory = new([
            Program("AUnknown"), Program("BSmall") with { EstimatedSizeBytes = 5 },
            Program("ZLarge") with { EstimatedSizeBytes = 500 }], []) };
        using var model = new ProgramsViewModel(service, true);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.SortIndex.Should().Be(1);
        model.Rows.Select(row => row.Name).Should().Equal("ZLarge", "BSmall", "AUnknown");
        foreach (var row in model.Rows) row.IsSelected = true;
        model.Search = "Small";
        model.ReviewRemovalCommand.Execute(null);
        model.SelectedPrograms.Select(row => row.Name).Should().Equal("ZLarge", "BSmall", "AUnknown");
        model.SortIndex = 0;
        model.SelectedPrograms.Select(row => row.Name).Should().Equal("AUnknown", "BSmall", "ZLarge");
    }

    [Fact]
    public async Task Protected_packages_are_normal_exclusions_but_unknown_protection_is_reported()
    {
        var package = new MsixPackage("SystemFixture", "System fixture", "Fixture", null)
            { ProtectionKnown = true, NonRemovable = true };
        var service = new FakePrograms { Inventory = ProgramInventory.Merge([], [package]) };
        using var model = new ProgramsViewModel(service, true);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.Notice.Should().BeEmpty();
        model.Rows.Should().BeEmpty();
        service.Inventory.OwnershipPrograms.Should().ContainSingle();
        service.Inventory = ProgramInventory.Merge([], [package with { ProtectionKnown = false }]);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.Notice.Should().Contain("защита пакета не определена");
    }

    [Fact]
    public async Task Search_keeps_hidden_selection_and_refresh_preserves_it_by_identity()
    {
        var service = new FakePrograms { Inventory = new([Program("Alpha"), Program("Beta")], []) };
        using var model = new ProgramsViewModel(service, true);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.Rows[0].IsSelected = true;
        model.Search = "Beta";
        model.Rows.Should().ContainSingle(row => row.Name == "Beta");
        model.SelectedCount.Should().Be(1);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.SelectedPrograms.Should().ContainSingle(row => row.Name == "Alpha");
        model.ReviewRemovalCommand.Execute(null);
        model.SelectedPrograms.Should().ContainSingle(row => row.Name == "Alpha");
        service.RemoveCalls.Should().Be(0, "a preview is not an uninstall command wearing a hat");
    }

    [Fact]
    public async Task Errors_allow_retry_and_empty_inventory_is_a_real_screen()
    {
        var service = new FakePrograms { ReadError = new IOException("fixture failure") };
        using var model = new ProgramsViewModel(service, true);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.State.Phase.Should().Be(ScreenPhase.Error);
        model.State.RetryCommand.Should().BeSameAs(model.RefreshCommand);
        service.ReadError = null;
        await model.RefreshCommand.ExecuteAsync(null);
        model.State.Phase.Should().Be(ScreenPhase.Ready);
        model.NoMatches.Should().BeTrue();
        model.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Queue_results_keep_unconfirmed_and_pending_programs_visible()
    {
        var first = Program("Alpha");
        var second = Program("Beta");
        var service = new FakePrograms
        {
            Inventory = new([first, second], []),
            Removal = new(new([new(first.Id, UninstallOutcome.StillRunning, null, 0, "still alive")], true), [], []),
        };
        using var model = new ProgramsViewModel(service, true);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        foreach (var row in model.Rows) row.IsSelected = true;
        model.ReviewRemovalCommand.Execute(null);
        await model.RemoveCommand.ExecuteAsync(null);
        model.IsReport.Should().BeTrue();
        model.Results.Should().HaveCount(2);
        model.Results[0].Status.Should().Contain("работает");
        model.Results[1].Status.Should().Be("Не запускалось");
        model.Rows.Should().HaveCount(2);
        model.Summary.Should().Contain("0 из 2");
    }

    [Fact]
    public async Task Leftovers_require_a_separate_selection_and_confirmation()
    {
        var program = Program("Alpha");
        var removed = new UninstallResult(program.Id, UninstallOutcome.Removed, 0, 0, null) { RemovalConfirmed = true };
        var strong = new ProgramLeftoverCandidate(program, removed,
            new(LeftoverKind.File, @"C:\fixture\cache.bin", 5, "exact owner") { Confidence = LeftoverConfidence.Strong });
        var weak = new ProgramLeftoverCandidate(program, removed, new(LeftoverKind.Directory, @"C:\fixture\Alpha", 5, "name only"));
        var service = new FakePrograms { Inventory = new([program], []), Removal = new(new([removed], false), [strong, weak], []) };
        using var model = new ProgramsViewModel(service, true);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.Rows[0].IsSelected = true;
        model.ReviewRemovalCommand.Execute(null);
        await model.RemoveCommand.ExecuteAsync(null);
        model.Rows.Should().BeEmpty();
        model.SelectedLeftoverCount.Should().Be(0);
        model.Leftovers[1].IsSelected = true;
        model.Leftovers[1].IsSelected.Should().BeFalse();
        model.Leftovers[0].IsSelected = true;
        model.ReviewLeftoversCommand.Execute(null);
        model.CleanupNote.Should().Be(service.CleanupNote);
        service.Cleaned.Should().BeEmpty();
        await model.CleanLeftoversCommand.ExecuteAsync(null);
        service.Cleaned.Should().ContainSingle().Which.Should().Be(strong);
        model.Leftovers.Should().ContainSingle(row => row.Candidate == weak);
    }

    [Fact]
    public async Task Cancellation_releases_wait_prompt_and_returns_to_report()
    {
        var program = Program("Alpha");
        var service = new FakePrograms { Inventory = new([program], []), WaitForDecision = true };
        using var model = new ProgramsViewModel(service, true);
        await model.LoadAsync(TestContext.Current.CancellationToken);
        model.Rows[0].IsSelected = true;
        model.ReviewRemovalCommand.Execute(null);
        var running = model.RemoveCommand.ExecuteAsync(null);
        await service.WaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        model.CancelCommand.Execute(null);
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        model.IsBusy.Should().BeFalse();
        model.IsReport.Should().BeTrue();
        model.WaitingForDecision.Should().BeFalse();
    }

    [Fact]
    public void Machine_program_without_elevation_cannot_be_selected()
    {
        var row = new ProgramRowViewModel(Program("Machine") with { Scope = ProgramScope.Machine64 }, false);
        row.IsSelected = true;
        row.IsSelected.Should().BeFalse();
        row.Refusal.Should().Contain("администратор");
    }

    private static InstalledProgram Program(string name) => new(name, name, "Fixture", "1", null, null, null,
        InstallerKind.Msix, ProgramScope.Msix, null) { PackageFullName = name + "_1.0.0.0_x64__fixture" };

    private sealed class FakePrograms : IProgramsService
    {
        public string CleanupNote => "Permanent files; registry export disabled";
        public ProgramInventorySnapshot Inventory { get; set; } = new([], []);
        public ProgramsRemovalReport Removal { get; init; } = new(new([], false), [], []);
        public IOException? ReadError { get; set; }
        public Func<InstalledProgram, CancellationToken, Task<ProgramSizeEstimate>>? Measure { get; init; }
        public Task<ProgramSizeEstimate> MeasureSizeAsync(InstalledProgram program, IReadOnlyList<InstalledProgram> owners, CancellationToken ct) =>
            Measure?.Invoke(program, ct) ?? Task.FromResult(new ProgramSizeEstimate(program.EstimatedSizeBytes, false, "fixture"));
        public int RemoveCalls { get; private set; }
        public bool WaitForDecision { get; init; }
        public TaskCompletionSource WaitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<ProgramLeftoverCandidate> Cleaned { get; private set; } = [];
        public Task<ProgramInventorySnapshot> ReadAsync(CancellationToken ct) => ReadError is { } error
            ? Task.FromException<ProgramInventorySnapshot>(error) : Task.FromResult(Inventory);
        public async Task<ProgramsRemovalReport> RemoveAsync(IReadOnlyList<InstalledProgram> programs, UninstallOptions options,
            IProgress<UninstallProgress>? progress, CancellationToken ct)
        {
            RemoveCalls++;
            if (WaitForDecision)
            {
                var waiting = options.ContinueWaitingAsync!(new(programs[0].Id, "fixture.exe", 1, TimeSpan.FromMinutes(5), false), ct);
                WaitStarted.SetResult();
                await waiting.ConfigureAwait(false);
            }
            return Removal;
        }
        public Task<IReadOnlyList<DeleteOutcome>> CleanLeftoversAsync(IReadOnlyList<ProgramLeftoverCandidate> items,
            IProgress<string>? progress, CancellationToken ct)
        {
            Cleaned = items;
            return Task.FromResult<IReadOnlyList<DeleteOutcome>>(items.Select(item => new DeleteOutcome(item.Item.Path, DeleteStatus.Deleted, item.Item.SizeBytes)).ToArray());
        }
    }
}
