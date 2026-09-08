using FluentAssertions;
using JunkManager.Core.Apps;
using JunkManager.Deletion;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Apps;

[Trait("Class", "Sandbox")]
public sealed class LeftoverOwnershipTests
{
    [Fact]
    public async Task Exact_Foo_is_deletable_next_to_Foobar_and_shared_owner_blocks_it()
    {
        // The separator is the difference between ownership and a string's delusions.
        using var sandbox = new SandboxFixture();
        var root = sandbox.CreateDirectory("Foo");
        sandbox.CreateFile("Foo/left.bin", "leftover");
        var other = Program("Foobar", sandbox.CreateDirectory("Foobar"));
        var program = Program("Foo", root);
        var candidate = LeftoverFinder.Evaluate(program, [other], LeftoverKind.Directory, root, root, true);
        candidate.Confidence.Should().Be(LeftoverConfidence.Strong);
        candidate.IsSelectedByDefault.Should().BeFalse();
        var blocked = LeftoverFinder.Evaluate(program, [other with { InstallLocation = root }], LeftoverKind.Directory, root, root, true);
        blocked.CanDelete.Should().BeFalse();
        blocked.Evidence.Should().Contain(e => e.Code == "OtherOwner" && e.Negative);
        var result = await new FileDeleter(new SpisokZhurnala()).DeleteProgramLeftoverAsync(program, candidate,
            DeleteMode.Permanent, new Inventory([other]), TestContext.Current.CancellationToken);
        result.Status.Should().Be(DeleteStatus.Deleted);
        Directory.Exists(root).Should().BeFalse();
        Directory.Exists(other.InstallLocation).Should().BeTrue();
    }

    [Fact]
    public async Task Proven_libraries_and_cache_are_removed_while_neighboring_documents_survive()
    {
        // One README must not hold every abandoned DLL hostage for eternity.
        using var sandbox = new SandboxFixture();
        var root = sandbox.CreateDirectory("Foo");
        var readme = Path.GetFullPath(sandbox.CreateFile("Foo/README.txt", "keep the manual"));
        var database = Path.GetFullPath(sandbox.CreateFile("Foo/cache/customer.db", "keep the records"));
        var document = Path.GetFullPath(sandbox.CreateFile("Foo/bin/taxes.docx", "keep the document"));
        var library = Path.GetFullPath(sandbox.CreateFile("Foo/bin/library.dll", "library"));
        var cache = Path.GetFullPath(sandbox.CreateFile("Foo/cache/old.bin", "cache"));
        var cleanCache = Path.GetFullPath(sandbox.CreateFile("Foo/GPUCache/old.bin", "shader"));
        var program = Program("Foo", root);
        var search = LeftoverFinder.FindAfterRemoval(program,
            new(program.Id, UninstallOutcome.Removed, 0, 0, null) { RemovalConfirmed = true },
            new ProgramInventorySnapshot([], []), new([], [], false), TestContext.Current.CancellationToken);

        search.Skipped.Select(s => s.Path).Should().Contain([readme, database, document]);
        var candidates = search.Found.Where(f => f.CanDelete).ToArray();
        candidates.Should().Contain(f => f.Path == library && f.Kind == LeftoverKind.File);
        candidates.Should().Contain(f => f.Path == cache && f.Kind == LeftoverKind.File);
        candidates.Should().Contain(f => f.Path == Path.GetDirectoryName(cleanCache) && f.Kind == LeftoverKind.Directory);
        candidates.Should().OnlyContain(f => !f.IsSelectedByDefault && f.Snapshot.Count > 0);
        foreach (var candidate in candidates)
        {
            var outcome = await new FileDeleter(new SpisokZhurnala()).DeleteProgramLeftoverAsync(program, candidate,
                DeleteMode.Permanent, new Inventory([]), TestContext.Current.CancellationToken);
            outcome.Status.Should().Be(DeleteStatus.Deleted, candidate.Basis);
        }
        File.ReadAllText(readme).Should().Be("keep the manual");
        File.ReadAllText(database).Should().Be("keep the records");
        File.ReadAllText(document).Should().Be("keep the document");
        File.Exists(library).Should().BeFalse();
        File.Exists(cache).Should().BeFalse();
        File.Exists(cleanCache).Should().BeFalse();
        Directory.Exists(root).Should().BeTrue();
    }

    [Fact]
    public async Task Granular_search_keeps_reparse_shared_and_user_subtrees_closed()
    {
        // A DLL costume does not make a shared folder or a junction our property.
        using var sandbox = new SandboxFixture();
        var root = sandbox.CreateDirectory("Foo");
        sandbox.CreateFile("Foo/README.txt", "preserve");
        var document = sandbox.CreateFile("Foo/User Data/private.bin", "private");
        var shared = sandbox.CreateFile("Foo/Common Files/shared.dll", "shared");
        var outside = sandbox.CreateFile("outside/other.dll", "outside");
        var link = Path.GetFullPath(sandbox.CreateJunction("Foo/redirect", Path.GetDirectoryName(outside)!));
        var own = Path.GetFullPath(sandbox.CreateFile("Foo/own.dll", "owned"));
        var program = Program("Foo", root);
        var search = LeftoverFinder.FindAfterRemoval(program,
            new(program.Id, UninstallOutcome.Removed, 0, 0, null) { RemovalConfirmed = true },
            new ProgramInventorySnapshot([], []), new([], [], false), TestContext.Current.CancellationToken);

        var candidate = search.Found.Should().ContainSingle(f => f.CanDelete).Subject;
        candidate.Path.Should().Be(own);
        search.Skipped.Should().Contain(s => s.Path == link);
        var result = await new FileDeleter(new SpisokZhurnala()).DeleteProgramLeftoverAsync(program, candidate,
            DeleteMode.Permanent, new Inventory([]), TestContext.Current.CancellationToken);
        result.Status.Should().Be(DeleteStatus.Deleted);
        File.ReadAllText(document).Should().Be("private");
        File.ReadAllText(shared).Should().Be("shared");
        File.ReadAllText(outside).Should().Be("outside");
        new DirectoryInfo(link).LinkTarget.Should().NotBeNull();
    }

    [Fact]
    public async Task Journal_counts_each_removed_byte_once_and_records_empty_directories()
    {
        // The summary may add numbers, but it does not get to mint a second copy of the bytes.
        using var sandbox = new SandboxFixture();
        var root = sandbox.CreateDirectory("Foo");
        var library = Path.GetFullPath(sandbox.CreateFile("Foo/library.dll", "12345"));
        var cache = Path.GetFullPath(sandbox.CreateFile("Foo/cache/old.bin", "1234567"));
        var program = Program("Foo", root);
        var candidate = LeftoverFinder.Evaluate(program, [], LeftoverKind.Directory, root, root, true);
        var journal = new SpisokZhurnala();

        var outcome = await new FileDeleter(journal).DeleteProgramLeftoverAsync(program, candidate,
            DeleteMode.Permanent, new Inventory([]), TestContext.Current.CancellationToken);

        outcome.Status.Should().Be(DeleteStatus.Deleted);
        outcome.BytesFreed.Should().Be(12);
        journal.Zapisi.Sum(entry => entry.BytesFreed).Should().Be(12);
        journal.Zapisi.Should().HaveCount(4);
        journal.Zapisi.Should().ContainSingle(entry => entry.Path == library && entry.BytesFreed == 5);
        journal.Zapisi.Should().ContainSingle(entry => entry.Path == cache && entry.BytesFreed == 7);
        journal.Zapisi.Should().ContainSingle(entry => entry.Path == root && entry.BytesFreed == 0);
    }

    [Fact]
    public async Task Cancellation_after_one_file_keeps_remaining_files_and_counts_no_phantom_bytes()
    {
        // Cancellation may stop the broom; it cannot unwrite the receipt for the first file.
        using var sandbox = new SandboxFixture();
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var root = sandbox.CreateDirectory("Foo");
        var first = sandbox.CreateFile("Foo/a.dll", "12345");
        var second = sandbox.CreateFile("Foo/b.dll", "67890");
        var program = Program("Foo", root);
        var candidate = LeftoverFinder.Evaluate(program, [], LeftoverKind.Directory, root, root, true);
        var journal = new CancelAfterDeletionLog(cancelled);

        var result = await new FileDeleter(journal).DeleteProgramLeftoverAsync(program, candidate,
            DeleteMode.Permanent, new Inventory([]), cancelled.Token);

        result.Status.Should().Be(DeleteStatus.Cancelled);
        result.BytesFreed.Should().Be(5);
        journal.Entries.Sum(e => e.BytesFreed).Should().Be(5);
        journal.Entries.Should().ContainSingle(e => e.Status == DeleteStatus.Cancelled && e.BytesFreed == 0);
        File.Exists(first).Should().NotBe(File.Exists(second), "exactly one file was removed before cancellation");
        if (File.Exists(first)) { File.ReadAllText(first).Should().Be("12345"); }
        if (File.Exists(second)) { File.ReadAllText(second).Should().Be("67890"); }
    }

    [Fact]
    public async Task Repeating_a_completed_deletion_reports_absence_with_zero_bytes()
    {
        using var sandbox = new SandboxFixture();
        var root = sandbox.CreateDirectory("Foo");
        sandbox.CreateFile("Foo/left.dll", "12345");
        var program = Program("Foo", root);
        var candidate = LeftoverFinder.Evaluate(program, [], LeftoverKind.Directory, root, root, true);
        var journal = new SpisokZhurnala();
        var deleter = new FileDeleter(journal);

        var first = await deleter.DeleteProgramLeftoverAsync(program, candidate,
            DeleteMode.Permanent, new Inventory([]), TestContext.Current.CancellationToken);
        var second = await deleter.DeleteProgramLeftoverAsync(program, candidate,
            DeleteMode.Permanent, new Inventory([]), TestContext.Current.CancellationToken);

        first.Status.Should().Be(DeleteStatus.Deleted);
        second.Status.Should().Be(DeleteStatus.Skipped);
        second.BytesFreed.Should().Be(0);
        second.Reason.Should().Contain("уже отсутствует");
        journal.Zapisi.Sum(e => e.BytesFreed).Should().Be(5);
    }

    [Fact]
    public async Task A_new_hidden_owner_blocks_a_previously_strong_leftover()
    {
        // A dependency does not surrender its files just because the UI hides its row.
        using var sandbox = new SandboxFixture();
        var root = sandbox.CreateDirectory("Foo");
        var library = sandbox.CreateFile("Foo/shared.dll", "keep the dependency");
        var program = Program("Foo", root);
        var candidate = LeftoverFinder.Evaluate(program, [], LeftoverKind.Directory, root, root, true);
        candidate.CanDelete.Should().BeTrue();
        var hiddenOwner = Program("Framework", root) with { Installer = InstallerKind.Msix, Scope = ProgramScope.Msix };

        var result = await new FileDeleter(new SpisokZhurnala()).DeleteProgramLeftoverAsync(program, candidate,
            DeleteMode.Permanent, new Inventory([hiddenOwner]), TestContext.Current.CancellationToken);

        result.Status.Should().Be(DeleteStatus.Skipped);
        result.BytesFreed.Should().Be(0);
        File.ReadAllText(library).Should().Be("keep the dependency");
    }

    [Fact]
    public async Task Cancellation_during_inventory_returns_a_recorded_cancelled_outcome()
    {
        using var sandbox = new SandboxFixture();
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var root = sandbox.CreateDirectory("Foo");
        var file = sandbox.CreateFile("Foo/keep.dll", "keep");
        var program = Program("Foo", root);
        var candidate = LeftoverFinder.Evaluate(program, [], LeftoverKind.Directory, root, root, true);
        var journal = new SpisokZhurnala();

        var result = await new FileDeleter(journal).DeleteProgramLeftoverAsync(program, candidate,
            DeleteMode.Permanent, new CancellingInventory(cancelled), cancelled.Token);

        result.Status.Should().Be(DeleteStatus.Cancelled);
        journal.Zapisi.Should().ContainSingle(e => e.Status == DeleteStatus.Cancelled && e.BytesFreed == 0);
        File.ReadAllText(file).Should().Be("keep");
    }

    [Fact]
    public async Task Documents_and_content_added_after_preview_survive()
    {
        using var sandbox = new SandboxFixture();
        var root = sandbox.CreateDirectory("Foo");
        sandbox.CreateFile("Foo/left.bin", "leftover");
        var program = Program("Foo", root);
        var candidate = LeftoverFinder.Evaluate(program, [], LeftoverKind.Directory, root, root, true);
        candidate.CanDelete.Should().BeTrue();
        var document = sandbox.CreateFile("Foo/taxes.docx", "human data");
        var result = await new FileDeleter(new SpisokZhurnala()).DeleteProgramLeftoverAsync(program, candidate,
            DeleteMode.Permanent, new Inventory([]), TestContext.Current.CancellationToken);
        result.Status.Should().Be(DeleteStatus.Skipped);
        File.ReadAllText(document).Should().Be("human data");
    }

    [Fact]
    public void Name_only_shared_publisher_and_current_application_never_get_delete_permission()
    {
        using var sandbox = new SandboxFixture();
        var root = sandbox.CreateDirectory("Vendor");
        var program = Program("Foo", root);
        var weak = LeftoverFinder.Evaluate(program, [], LeftoverKind.Directory, root, null, false);
        weak.Confidence.Should().Be(LeftoverConfidence.Weak);
        weak.CanDelete.Should().BeFalse();
        LeftoverFinder.Evaluate(program, [Program("Bar", sandbox.CreateDirectory("Bar"))], LeftoverKind.Directory,
            root, null, false).Evidence.Should().Contain(e => e.Code == "SharedPublisher");
        LeftoverFinder.Evaluate(program with { InstallLocation = AppContext.BaseDirectory }, [], LeftoverKind.Directory,
            AppContext.BaseDirectory, AppContext.BaseDirectory, true).CanDelete.Should().BeFalse();
    }

    private static InstalledProgram Program(string name, string location) => new("User:" + name, name, "Vendor", "1",
        location, null, null, InstallerKind.Unknown, ProgramScope.User, null);
    private sealed class CancelAfterDeletionLog(CancellationTokenSource cancellation) : IOperationLog
    {
        public List<DeleteOutcome> Entries { get; } = [];
        public async Task RecordAsync(DeleteOutcome outcome, CancellationToken ct = default)
        {
            Entries.Add(outcome);
            if (outcome.Status == DeleteStatus.Deleted) { await cancellation.CancelAsync(); }
        }
    }
    private sealed class Inventory(IReadOnlyList<InstalledProgram> owners) : IProgramInventory
    {
        public Task<ProgramInventorySnapshot> ReadAsync(CancellationToken ct = default) =>
            Task.FromResult(new ProgramInventorySnapshot([], []) { OwnershipPrograms = owners });
        public Task<ProgramPresenceResult> ProbeAsync(InstalledProgram program, CancellationToken ct = default) => Task.FromResult(new ProgramPresenceResult(ProgramPresence.Absent));
    }
    private sealed class CancellingInventory(CancellationTokenSource cancellation) : IProgramInventory
    {
        public Task<ProgramInventorySnapshot> ReadAsync(CancellationToken ct = default) => Task.FromResult(new ProgramInventorySnapshot([], []));
        public async Task<ProgramPresenceResult> ProbeAsync(InstalledProgram program, CancellationToken ct = default)
        {
            await cancellation.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return new(ProgramPresence.Absent);
        }
    }
}
