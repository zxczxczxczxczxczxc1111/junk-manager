using FluentAssertions;
using JunkManager.Deletion;
using Xunit;

namespace JunkManager.Tests.Apps;

[Trait("Class", "Sandbox")]
public sealed class WinGetWorkerProtocolTests
{
    [Fact]
    public async Task Incomplete_worker_invitation_does_not_start_an_operation()
    {
        // An internal switch is not a blank cheque for a background deletion.
        WinGetUserWorker.IsRequested([]).Should().BeFalse();
        WinGetUserWorker.IsRequested(["apps", "--winget-user-worker"]).Should().BeFalse();
        (await WinGetUserWorker.RunAsync(["--winget-user-worker"])).Should().Be(2);
        (await WinGetUserWorker.RunAsync(["--winget-user-worker", "invalid-pipe", "1"])).Should().Be(2);
    }
}
