using FluentAssertions;
using JunkManager.Core.Registry;
using JunkManager.Deletion;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Microsoft.Win32;
using Xunit;

namespace JunkManager.Tests.Live;

[Trait("Class", "LiveDestructive")]
[Collection(ObshcheeSostoyanieMashiny.Imya)]
public sealed class RegistryCautiousTests
{
    [Theory]
    [InlineData(RegistryScanMode.ComServers)]
    [InlineData(RegistryScanMode.FileAssociations)]
    public async Task User_reference_is_selected_individually_and_restores_without_touching_neighbours(RegistryScanMode mode)
    {
        VmFuse.RequireArmed();
        using var files = new SandboxFixture();
        var id = Guid.NewGuid().ToString("B");
        var branch = mode == RegistryScanMode.ComServers ? @"Software\Classes\CLSID" : @"Software\Classes";
        var owner = branch + "\\" + (mode == RegistryScanMode.ComServers ? id : "JunkManager.Test" + Guid.NewGuid().ToString("N"));
        var leaf = owner + (mode == RegistryScanMode.ComServers ? @"\InprocServer32" : @"\shell\open\command");
        var missing = @"C:\JunkManagerAbsentFixture\" + Guid.NewGuid().ToString("N")
            + (mode == RegistryScanMode.ComServers ? ".dll" : ".exe");
        try
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(leaf))
            {
                key.SetValue(string.Empty, missing);
                key.SetValue("Neighbour", "before");
            }
            var rule = new RegistryScanRule("fixture", RegistryHive.CurrentUser, branch, RegistryView.Default,
                RegistryEntryKind.Value, "Fixture", "Remove only the missing reference") { Mode = mode };
            var scan = await new RegistryScanner().ScanAsync([rule], null, TestContext.Current.CancellationToken);
            var found = scan.Findings.Single(item => item.SubKey == leaf);
            found.ManualSelectionOnly.Should().BeTrue();
            var result = await new RegistryCleanupRunner(new RegistryExecutor(new SpisokZhurnala(), new RegistryBackup(files.Root)))
                .RunAsync([found], null, TestContext.Current.CancellationToken);
            result.DeletedCount.Should().Be(1);
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(leaf, writable: true))
            {
                key!.GetValueNames().Should().NotContain(string.Empty);
                key.GetValue("Neighbour").Should().Be("before");
                key.SetValue("Neighbour", "after");
            }
            var restored = await RegistryRollback.ImportAsync(Directory.GetFiles(files.Root, "*.reg").Single(),
                RegistryView.Default, TestContext.Current.CancellationToken);
            restored.Ok.Should().BeTrue(restored.Reason);
            using var after = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(leaf);
            after!.GetValue(string.Empty).Should().Be(missing);
            after.GetValue("Neighbour").Should().Be("after");
        }
        finally
        {
            // This unique fixture owns exactly one leaf tree, unlike the rest of Classes.
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(owner, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task Shared_dlls_preserve_positive_counts_and_remove_only_missing_zero_count()
    {
        VmFuse.RequireArmed();
        const string branch = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
        var zero = @"C:\JunkManagerAbsentFixture\" + Guid.NewGuid().ToString("N") + ".dll";
        var positive = @"C:\JunkManagerAbsentFixture\" + Guid.NewGuid().ToString("N") + ".dll";
        var typeLibrary = @"C:\JunkManagerAbsentFixture\" + Guid.NewGuid().ToString("N") + ".tlb";
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.CreateSubKey(branch);
        key.SetValue(zero, 0, RegistryValueKind.DWord);
        key.SetValue(positive, 2, RegistryValueKind.DWord);
        key.SetValue(typeLibrary, 0, RegistryValueKind.DWord);
        try
        {
            var rule = new RegistryScanRule("fixture", RegistryHive.LocalMachine, branch, RegistryView.Registry64,
                RegistryEntryKind.Value, "Fixture", "Remove only the unused counter") { Mode = RegistryScanMode.SharedDlls };
            var scan = await new RegistryScanner().ScanAsync([rule], null, TestContext.Current.CancellationToken);
            scan.Findings.Should().NotContain(item => item.ValueName == positive);
            scan.Findings.Should().NotContain(item => item.ValueName == typeLibrary);
            scan.Skipped.Where(item => item.Path.Contains(positive, StringComparison.Ordinal)
                || item.Path.Contains(typeLibrary, StringComparison.Ordinal))
                .Should().HaveCount(2).And.OnlyContain(item => item.IsExpectedExclusion);
            var found = scan.Findings.Single(item => item.ValueName == zero);
            found.ManualSelectionOnly.Should().BeTrue();
            var result = await new RegistryCleanupRunner(new RegistryExecutor(new SpisokZhurnala()))
                .RunAsync([found], null, TestContext.Current.CancellationToken);
            result.DeletedCount.Should().Be(1);
            key.GetValue(positive).Should().Be(2);
            key.GetValue(typeLibrary).Should().Be(0);
            key.GetValueNames().Should().NotContain(zero);
        }
        finally
        {
            // Remove only our GUID-named values. The rest of Windows can keep its paperwork.
            key.DeleteValue(zero, throwOnMissingValue: false);
            key.DeleteValue(positive, throwOnMissingValue: false);
            key.DeleteValue(typeLibrary, throwOnMissingValue: false);
        }
    }
}
