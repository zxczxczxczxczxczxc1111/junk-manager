using FluentAssertions;
using JunkManager.Core.Apps;
using Xunit;

namespace JunkManager.Tests.Apps;

[Trait("Class", "Sandbox")]
public sealed class ProgramInventoryTests
{
    [Fact]
    public void Invalid_MSIX_output_and_unknown_protection_are_not_an_empty_success()
    {
        // Missing metadata is not permission wearing a false moustache.
        MsixPackageReader.Parse("not-json").Skipped.Should().ContainSingle();
        var unknown = new MsixPackage("Foo_1_x64__abc", "Foo", "Acme", null);
        unknown.CanUninstall(out var reason).Should().BeFalse();
        reason.Should().NotBeNullOrWhiteSpace();
        ProgramInventory.Merge([], [unknown]).Programs.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelled_probe_does_not_report_an_unknown_MSIX_identity()
    {
        // Cancellation is an instruction, not another package metadata value.
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancelled.CancelAsync();
        var program = new InstalledProgram("Msix:missing", "Missing", "Acme", "1", null,
            null, null, InstallerKind.Msix, ProgramScope.Msix, null);
        var probe = () => new ProgramInventory().ProbeAsync(program, cancelled.Token);

        await probe.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Merge_keeps_Win32_and_package_and_excludes_protected_dependencies()
    {
        // A framework is not an app, however persuasive its display name gets.
        var win32 = new InstalledProgram("User:Foo", "Foo", "Acme", "1", null,
            null, null, InstallerKind.Unknown, ProgramScope.User, null);
        var main = new MsixPackage("Foo_1_x64__abc", "Foo", "Acme", null)
        { ProtectionKnown = true, Version = "1" };
        var framework = main with { FullName = "Framework_1_x64__abc", IsFramework = true };
        var system = main with { FullName = "System_1_x64__abc", NonRemovable = true };
        var bundle = main with { FullName = "Foo_1_neutral_~_abc", IsBundle = true };

        var result = ProgramInventory.Merge([win32], [main, framework, system, bundle]);

        result.Programs.Should().HaveCount(2);
        result.Programs.Select(p => p.Id).Should().OnlyHaveUniqueItems();
        result.Programs.Should().Contain(p => p.Installer == InstallerKind.Msix);
        result.Skipped.Should().HaveCount(3);
        result.OwnershipPrograms.Should().HaveCount(5);
        result.OwnershipPrograms.Should().Contain(p => p.PackageFullName == framework.FullName);
        result.OwnershipPrograms.Should().Contain(p => p.PackageFullName == system.FullName);
        result.IsComplete.Should().BeTrue("excluding a protected package from removal does not lose its ownership");
    }
}
