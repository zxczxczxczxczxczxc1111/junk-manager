using FluentAssertions;
using JunkManager.Core.Registry;
using JunkManager.Core.Rules;
using JunkManager.Tests.Infrastructure;
using Microsoft.Win32;
using Xunit;

namespace JunkManager.Tests.Registry;

[Trait("Class", "Sandbox")]
public sealed class RegistryExclusionTests
{
    [Fact]
    public async Task Non_path_values_are_preserved_without_claiming_a_read_failure()
    {
        using var branch = new RegistrySandbox();
        branch.SetOwnDefault("fixture");
        branch.SetDword("count", 42);
        branch.SetString("empty", "");
        var result = await Scan(branch, RegistryEntryKind.Value, _ => TargetState.Exists);
        result.Findings.Should().BeEmpty();
        result.Skipped.Should().HaveCount(3).And.OnlyContain(item => item.IsExpectedExclusion);
        result.Coverage.Should().ContainSingle().Which.SkippedCount.Should().Be(0);
    }

    [Fact]
    public async Task App_path_without_default_target_is_not_a_broken_link_or_read_failure()
    {
        using var branch = new RegistrySandbox();
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(branch.SubKey + @"\fixture.exe"))
            key.SetValue("Path", @"C:\fixture", RegistryValueKind.String);
        var result = await Scan(branch, RegistryEntryKind.Key, _ => TargetState.Missing);
        result.Findings.Should().BeEmpty();
        result.Skipped.Should().ContainSingle().Which.IsExpectedExclusion.Should().BeTrue();
        result.Coverage.Single().SkippedCount.Should().Be(0);
    }

    [Theory]
    [InlineData(TargetState.Missing, 1, 0)]
    [InlineData(TargetState.Unknown, 0, 1)]
    [InlineData(TargetState.Exists, 0, 0)]
    public async Task Real_targets_still_distinguish_missing_unknown_and_existing(TargetState state, int findings, int gaps)
    {
        // The warning label changed; the scanner is still required to do its job. Shocking.
        using var branch = new RegistrySandbox();
        branch.SetString("app", @"C:\jm-fixture\app.exe");
        var result = await Scan(branch, RegistryEntryKind.Value, _ => state);
        result.Findings.Should().HaveCount(findings);
        result.Coverage.Single().SkippedCount.Should().Be(gaps);
        result.Skipped.Where(item => item.IsExpectedExclusion).Should().BeEmpty();
    }

    private static Task<RegistryScanResult> Scan(RegistrySandbox branch, RegistryEntryKind kind, Func<string, TargetState> probe) =>
        new RegistryScanner(probe).ScanAsync([new RegistryScanRule("fixture", RegistryHive.CurrentUser,
            branch.SubKey, RegistryView.Default, kind, "Fixture", "Fixture only")], null, TestContext.Current.CancellationToken);
}
