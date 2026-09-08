using FluentAssertions;
using JunkManager.Core.Explain;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Scanning;

[Trait("Class", "Sandbox")]
public sealed class StorageAnalyzerTests
{
    [Fact]
    public async Task Analysis_counts_small_folders_reports_disks_and_does_not_follow_links_or_delete_files()
    {
        using var sandbox = new SandboxFixture();
        var profile = sandbox.CreateDirectory("profile");
        var local = sandbox.CreateDirectory(@"profile\AppData\Local");
        var roaming = sandbox.CreateDirectory(@"profile\AppData\Roaming");
        var app = sandbox.CreateDirectory(@"profile\AppData\Local\App");
        var own = Path.Combine(app, "small.bin");
        File.WriteAllBytes(own, new byte[11]);
        var disk = Path.Combine(app, "disk.vhdx");
        File.WriteAllBytes(disk, new byte[13]);
        var outside = sandbox.CreateDirectory("outside");
        File.WriteAllBytes(Path.Combine(outside, "foreign.bin"), new byte[999]);
        sandbox.CreateJunction(@"profile\AppData\Local\App\linked", outside);
        var options = new StorageAnalysisOptions(profile, local, roaming, Path.Combine(sandbox.Root, "windows"),
            Path.Combine(sandbox.Root, "programdata"), Path.Combine(sandbox.Root, "programs"), sandbox.Root);
        var result = await StorageAnalyzer.AnalyzeAsync(options, null, TestContext.Current.CancellationToken);
        // Tiny folders still exist, and junctions still are not invitations to somebody else's files.
        var item = result.Items.Single(row => row.Path == app);
        item.Bytes.Should().Be(24);
        item.Partial.Should().BeTrue();
        result.Items.Should().Contain(row => row.Path == disk && row.Group == "Виртуальные диски" && row.Bytes == 13);
        result.Items.Should().BeInDescendingOrder(row => row.Bytes);
        result.Issues.Should().Contain(message => message.Contains("ссылка пропущена", StringComparison.Ordinal));
        File.ReadAllBytes(own).Should().HaveCount(11);
        File.ReadAllBytes(disk).Should().HaveCount(13);
        File.ReadAllBytes(Path.Combine(outside, "foreign.bin")).Should().HaveCount(999);
    }

    [Fact]
    public async Task Cancelled_analysis_never_claims_a_complete_empty_disk()
    {
        using var sandbox = new SandboxFixture();
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        var options = new StorageAnalysisOptions(sandbox.Root, sandbox.Root, sandbox.Root, sandbox.Root,
            sandbox.Root, sandbox.Root, sandbox.Root, true);
        var result = await StorageAnalyzer.AnalyzeAsync(options, null, cancel.Token);
        // No files scanned is not the same as an empty disk, despite the tempting marketing copy.
        result.Cancelled.Should().BeTrue();
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Deep_scan_keeps_only_the_largest_25_files_and_csv_preserves_separators()
    {
        using var sandbox = new SandboxFixture();
        var volume = sandbox.CreateDirectory("volume");
        for (var size = 1; size <= 30; size++) File.WriteAllBytes(Path.Combine(volume, $"file-{size};x.bin"), new byte[size]);
        var missing = Path.Combine(sandbox.Root, "missing");
        var result = await StorageAnalyzer.AnalyzeAsync(new(missing, missing, missing, missing, missing, missing, volume, true),
            null, TestContext.Current.CancellationToken);
        // The top 25 is a ranking, not a reason to add every byte twice.
        result.Items.Should().HaveCount(25).And.BeInDescendingOrder(row => row.Bytes);
        result.Items.Min(row => row.Bytes).Should().Be(6);
        result.ToCsv().Should().Contain("\"" + Path.Combine(volume, "file-30;x.bin") + "\"");
    }
}
