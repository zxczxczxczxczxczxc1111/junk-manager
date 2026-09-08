using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using JunkManager.Core.Apps;
using JunkManager.Deletion;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Microsoft.Win32;
using Xunit;

namespace JunkManager.Tests.Deletion;

[Trait("Class", "LiveDestructive")]
public sealed class ProgramUninstallExecutionTests
{
    public ProgramUninstallExecutionTests() => VmFuse.RequireArmed();

    [Fact]
    public async Task WinGet_request_for_another_registration_is_refused_before_launch()
    {
        // A caller cannot swap the parcel after the UI approved its address.
        var program = Program("jm-test-package") with
        {
            Installer = InstallerKind.WinGetPortable,
            UninstallString = "winget uninstall --product-code jm-test-package",
            Registrations = [new(ProgramScope.User, "jm-test-package")],
        };
        var command = new UninstallCommand("WinGet", [], true)
            { WinGet = new("another-package", ProgramScope.User) };
        var result = await new UninstallRunner(new SpisokZhurnala(), new Inventory(ProgramPresence.Present)).RunAsync(
            program, command, TimeSpan.FromSeconds(20), false, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(UninstallOutcome.Refused);
        result.ProcessId.Should().BeNull();
    }

    [Fact]
    public async Task Failure_explanation_from_standard_output_reaches_the_result()
    {
        // Some uninstallers report disasters to stdout. The pipe is not a shredder.
        var command = Command("[Console]::Out.WriteLine('fixture catalog unavailable'); exit 42");
        var result = await new UninstallRunner(new SpisokZhurnala(), new Inventory(ProgramPresence.Present)).RunAsync(
            Program("jm-test-" + Guid.NewGuid().ToString("N")), command, TimeSpan.FromSeconds(20), false,
            TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(UninstallOutcome.Failed);
        result.Reason.Should().Contain("fixture catalog unavailable");
    }

    [Fact]
    public async Task Incomplete_process_tree_cannot_become_removed_even_with_absent_registration()
    {
        var program = Program("jm-test-" + Guid.NewGuid().ToString("N"));
        var command = Command("# Missing process evidence is not success.\nexit 0");
        var runner = new UninstallRunner(new SpisokZhurnala(), new Inventory(ProgramPresence.Absent), new MissingProcessTree());
        var result = await runner.RunAsync(program, command, TimeSpan.FromSeconds(20), false, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(UninstallOutcome.Unconfirmed);
        result.ProcessTreeVerified.Should().BeFalse();
        result.Reason.Should().Contain("дерево");
        (await runner.RunAsync(program, command, TimeSpan.FromSeconds(20), false, TestContext.Current.CancellationToken))
            .Outcome.Should().Be(UninstallOutcome.Refused);
    }

    [Fact]
    public async Task Bootstrapper_child_finishes_before_removal_is_reported()
    {
        using var sandbox = new SandboxFixture();
        var marker = Path.Combine(sandbox.Root, "child.bin");
        var childScript = """
            # Bootstrapper vanished; the transaction still has a pulse.
            Start-Sleep -Seconds 3
            [IO.File]::WriteAllText($env:JM_FIXTURE_MARKER, 'child finished')
            """;
        var command = Command("""
            # This parent leaves deliberately; its child is the actual fixture operation.
            [Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
            $request = [Console]::In.ReadToEnd() | ConvertFrom-Json
            $env:JM_FIXTURE_MARKER = $request.Marker
            Start-Process -WindowStyle Hidden -FilePath $request.Executable -ArgumentList @('-NoProfile', '-NonInteractive', '-EncodedCommand', $request.Child)
            exit 0
            """) with { StandardInput = JsonSerializer.Serialize(new { Marker = marker, Executable = MsixPackageReader.PowerShellExecutable,
                Child = Convert.ToBase64String(Encoding.Unicode.GetBytes(childScript)) }) };
        var result = await new UninstallRunner(new SpisokZhurnala(), new Inventory(ProgramPresence.Absent)).RunAsync(
            Program("jm-test-" + Guid.NewGuid().ToString("N")), command, TimeSpan.FromSeconds(20), false, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(UninstallOutcome.Removed);
        File.ReadAllText(marker).Should().Be("child finished");
    }

    [Fact]
    public async Task Observation_deadline_leaves_transaction_alive()
    {
        var program = Program("jm-test-" + Guid.NewGuid().ToString("N"));
        var command = Command("# Even this pretend transaction gets to finish.\nStart-Sleep -Seconds 3\nexit 0");
        var result = await new UninstallRunner(new SpisokZhurnala(), new Inventory(ProgramPresence.Absent)).RunAsync(
            program, command, new UninstallOptions { ObservationWindow = TimeSpan.FromMilliseconds(200) }, false, null, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(UninstallOutcome.StillRunning);
        using var process = Process.GetProcessById(result.ProcessId!.Value);
        process.HasExited.Should().BeFalse();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Exit_zero_does_not_confirm_removal_and_both_full_pipes_are_drained()
    {
        var program = Program("jm-test-" + Guid.NewGuid().ToString("N"));
        var command = Command("""
            # Both pipes deserve attention; deadlock is not a progress indicator.
            1..128 | ForEach-Object { [Console]::Out.Write(('x' * 4096)); [Console]::Error.Write(('y' * 4096)) }
            exit 0
            """);
        var result = await new UninstallRunner(new SpisokZhurnala(), new Inventory(ProgramPresence.Present)).RunAsync(
            program, command, new UninstallOptions { ConfirmationWindow = TimeSpan.Zero }, false, null, TestContext.Current.CancellationToken);
        result.ExitCode.Should().Be(0);
        result.Outcome.Should().Be(UninstallOutcome.Unconfirmed);
        result.RemovalConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Fixture_removal_is_confirmed_by_the_real_registration_probe()
    {
        var name = "jm-test-" + Guid.NewGuid().ToString("N");
        var branch = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + name;
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(branch)) { key.SetValue("DisplayName", "JunkManager fixture"); }
        try
        {
            var command = Command("""
                # Only this fixture key gets a funeral.
                [Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
                $request = [Console]::In.ReadToEnd() | ConvertFrom-Json
                if ($request.Name -notmatch '^jm-test-[a-f0-9]{32}$') { exit 42 }
                Remove-Item -LiteralPath ('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\' + $request.Name) -ErrorAction Stop
                exit 0
                """) with { StandardInput = JsonSerializer.Serialize(new { Name = name }) };
            var result = await new UninstallRunner(new SpisokZhurnala()).RunAsync(Program(name), command,
                TimeSpan.FromSeconds(30), false, TestContext.Current.CancellationToken);
            result.Outcome.Should().Be(UninstallOutcome.Removed);
            result.RemovalConfirmed.Should().BeTrue();
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(branch, false); }
    }

    [Fact]
    public async Task Cancellation_stops_queue_but_current_process_finishes_and_duplicate_is_refused()
    {
        using var sandbox = new SandboxFixture();
        var marker = Path.Combine(sandbox.Root, "finished.bin");
        var program = Program("jm-test-" + Guid.NewGuid().ToString("N"));
        var command = Command("""
            # Cancellation belongs to the observer, not to this pretend transaction.
            [Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
            $request = [Console]::In.ReadToEnd() | ConvertFrom-Json
            Start-Sleep -Seconds 3
            [IO.File]::WriteAllText($request.Marker, 'finished')
            exit 0
            """) with { StandardInput = JsonSerializer.Serialize(new { Marker = marker }) };
        var runner = new UninstallRunner(new SpisokZhurnala(), new Inventory(ProgramPresence.Absent));
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
        var result = await runner.RunQueueAsync([new(program, command), new(Program("never-start"), command)],
            new UninstallOptions(), null, stop.Token);
        result.Results.Should().ContainSingle();
        result.Cancelled.Should().BeTrue();
        result.Results[0].Outcome.Should().Be(UninstallOutcome.StillRunning);
        var duplicate = await runner.RunAsync(program, command, TimeSpan.FromSeconds(10), false, TestContext.Current.CancellationToken);
        duplicate.Outcome.Should().Be(UninstallOutcome.Refused);
        using var process = Process.GetProcessById(result.Results[0].ProcessId!.Value);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        File.ReadAllText(marker).Should().Be("finished");
    }

    private static InstalledProgram Program(string name) => new("User:" + name, name, "Fixture", "1", null,
        null, null, InstallerKind.Unknown, ProgramScope.User, null);
    private static UninstallCommand Command(string script) => new(MsixPackageReader.PowerShellExecutable,
        ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], true);

    private sealed class Inventory(ProgramPresence presence) : IProgramInventory
    {
        public Task<ProgramInventorySnapshot> ReadAsync(CancellationToken ct = default) => Task.FromResult(new ProgramInventorySnapshot([], []));
        public Task<ProgramPresenceResult> ProbeAsync(InstalledProgram program, CancellationToken ct = default) => Task.FromResult(new ProgramPresenceResult(presence));
    }
    private sealed class MissingProcessTree : IUninstallProcessTreeReader
    {
        public UninstallProcessTreeSnapshot Read() => new([], "fixture: Toolhelp access denied");
    }
}
