using FluentAssertions;
using JunkManager.Core.Apps;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Apps;

[Trait("Class", "Sandbox")]
public sealed class ProgramSizeReaderTests
{
    private static InstalledProgram Program(string? root, InstallerKind kind = InstallerKind.Msix) =>
        new("fixture", "Fixture", "Fixture", "1", root, null, null, kind, ProgramScope.User, null);

    [Theory]
    [InlineData(InstallerKind.Msix)]
    [InlineData(InstallerKind.Nsis)]
    public void Missing_installer_size_is_measured_including_hidden_files(InstallerKind kind)
    {
        using var fixture = new SandboxFixture();
        fixture.CreateFile("app.exe", "12345");
        var hidden = fixture.CreateFile("nested/hidden.bin", "1234567");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        var result = ProgramSizeReader.Read(Program(fixture.Root, kind), [], TestContext.Current.CancellationToken);
        result.Bytes.Should().Be(12);
        result.Partial.Should().BeFalse();
    }

    [Fact]
    public void Installer_estimate_is_kept_without_walking_the_folder()
    {
        var result = ProgramSizeReader.Read(Program(@"C:\not-present-fixture") with { EstimatedSizeBytes = 123 }, [], TestContext.Current.CancellationToken);
        result.Bytes.Should().Be(123);
    }

    [Fact]
    public void Executable_parent_is_a_fallback_when_install_location_is_missing()
    {
        using var fixture = new SandboxFixture();
        var exe = fixture.CreateFile("app.exe", "12345");
        var result = ProgramSizeReader.Read(Program(null) with { ExecutablePath = exe }, [], TestContext.Current.CancellationToken);
        result.Bytes.Should().Be(5);
        result.Partial.Should().BeTrue();
    }

    [Fact]
    public void Shared_parent_is_not_reported_as_one_program()
    {
        using var fixture = new SandboxFixture();
        var child = fixture.CreateDirectory("other");
        var result = ProgramSizeReader.Read(Program(fixture.Root), [Program(child) with { Id = "other" }], TestContext.Current.CancellationToken);
        result.Bytes.Should().BeNull();
        result.Detail.Should().Contain("общая");
    }

    [Fact]
    public void Broad_system_and_profile_roots_are_not_measured()
    {
        foreach (var path in new[] { Path.GetPathRoot(Environment.SystemDirectory)!, Environment.SystemDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) })
            ProgramSizeReader.Read(Program(path), [], TestContext.Current.CancellationToken).Bytes.Should().BeNull();
    }

    [Fact]
    public void Junction_targets_are_excluded_and_partial_bytes_are_labelled()
    {
        using var fixture = new SandboxFixture();
        using var external = new SandboxFixture();
        fixture.CreateFile("own.bin", "12345");
        external.CreateFile("outside.bin", new string('x', 1000));
        fixture.CreateJunction("linked", external.Root);
        var result = ProgramSizeReader.Read(Program(fixture.Root), [], TestContext.Current.CancellationToken);
        result.Bytes.Should().Be(5);
        result.Partial.Should().BeTrue();
        result.Partial.Should().BeTrue();
        ProgramSizeReader.Read(Program(Path.Combine(fixture.Root, "linked")), [], TestContext.Current.CancellationToken).Bytes.Should().BeNull();
    }

    [Fact]
    public void Traversal_limit_never_masquerades_as_a_complete_size()
    {
        using var fixture = new SandboxFixture();
        fixture.CreateFile("one", "123");
        fixture.CreateFile("two", "456");
        var result = ProgramSizeReader.Read(Program(fixture.Root), [], TestContext.Current.CancellationToken, maxEntries: 1);
        result.Partial.Should().BeTrue();
        result.Bytes.Should().Be(3);
    }

    [Fact]
    public void Cancellation_is_observed_before_traversal()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var act = () => ProgramSizeReader.Read(Program(null), [], cancellation.Token);
        act.Should().Throw<OperationCanceledException>();
    }
}