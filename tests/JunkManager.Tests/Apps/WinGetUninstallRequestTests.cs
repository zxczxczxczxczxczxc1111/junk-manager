using FluentAssertions;
using JunkManager.Core.Apps;
using Xunit;

namespace JunkManager.Tests.Apps;

[Trait("Class", "Sandbox")]
public sealed class WinGetUninstallRequestTests
{
    [Theory]
    [InlineData("Oven-sh.Bun_Microsoft.Winget.Source_8wekyb3d8bbwe")]
    [InlineData("BrechtSanders.WinLibs.POSIX.UCRT_Microsoft.Winget.Source_8wekyb3d8bbwe")]
    public void Real_portable_commands_are_bound_to_the_registration(string code)
    {
        // These are the user's actual command shapes, not a parser's imaginary friends.
        var program = Program(code);
        UninstallCommandBuilder.TryBuild(program, _ => false, out var command, out _).Should().BeTrue();
        command.WinGet.Should().Be(new WinGetUninstallRequest(code, ProgramScope.User));
        command.Arguments.Should().BeEmpty();
        command.Quiet.Should().BeTrue();
    }

    [Theory]
    [InlineData("winget uninstall --product-code Other.Package")]
    [InlineData("winget uninstall --product-code Test.Package --all")]
    [InlineData("winget uninstall --product-code Test.Package & calc.exe")]
    [InlineData("winget install --product-code Test.Package")]
    [InlineData("\"winget uninstall --product-code Test.Package")]
    [InlineData("C:\\untrusted\\winget.exe uninstall --product-code Test.Package")]
    public void Different_targets_and_extra_operations_are_refused(string command)
    {
        var program = Program("Test.Package") with { UninstallString = command };
        WinGetUninstallRequest.TryCreate(program, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Missing_or_ambiguous_registration_is_refused()
    {
        var program = Program("Test.Package");
        WinGetUninstallRequest.TryCreate(program with { Registrations = [] }, out _, out _).Should().BeFalse();
        WinGetUninstallRequest.TryCreate(program with { Registrations = [.. program.Registrations, new(ProgramScope.Machine64, "Test.Package")] }, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(ProgramScope.User)]
    [InlineData(ProgramScope.User32)]
    [InlineData(ProgramScope.Machine32)]
    [InlineData(ProgramScope.Machine64)]
    public void Scope_is_preserved_and_cannot_be_replaced(ProgramScope scope)
    {
        // A user installation must not acquire a machine passport by accident.
        var program = Program("Test.Package") with { Scope = scope, Registrations = [new(scope, "Test.Package")] };
        WinGetUninstallRequest.TryCreate(program, out var request, out _).Should().BeTrue();
        request!.Scope.Should().Be(scope);
        var different = scope == ProgramScope.User ? ProgramScope.Machine64 : ProgramScope.User;
        WinGetUninstallRequest.TryCreate(program with { Registrations = [new(different, "Test.Package")] }, out _, out _)
            .Should().BeFalse();
    }

    private static InstalledProgram Program(string code) => new("User:" + code, "Portable test", "test", "1", @"C:\test",
        "winget uninstall --product-code " + code, null, InstallerKind.WinGetPortable, ProgramScope.User, null)
    { Registrations = [new(ProgramScope.User, code)] };
}
