using FluentAssertions;
using JunkManager.Core.Explain;
using JunkManager.Core.Sources.Platform;
using Xunit;

namespace JunkManager.Tests.Scanning;

[Trait("Class", "Sandbox")]
public sealed class SystemStorageAnalysisTests
{
    [Fact]
    public async Task Dism_replaces_logical_size_and_shadow_bytes_are_grouped_by_storage_volume()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var path = Path.Combine(windows, "WinSxS");
        var calls = new List<IReadOnlyList<string>>();
        var shadowJson = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new { Path = @"\\?\Volume{11111111-1111-1111-1111-111111111111}\", Used = 17 },
            new { Path = @"\\?\Volume{11111111-1111-1111-1111-111111111111}\", Used = 23 },
        });
        var result = await SystemStorageAnalysis.AppendAsync(new([new("Windows", path, 999, "Logical")], [], false),
            windows, null, TestContext.Current.CancellationToken, (_, arguments, _, _) =>
            {
                calls.Add(arguments);
                // Windows is mocked here because a parser test does not need a servicing parade.
                return Task.FromResult(arguments.Contains("/Online")
                    ? new ToolRun(0, "Actual Size of Component Store : 10 GB\nBackups and Disabled Features : 2 GB\nNumber of Reclaimable Packages : 1\nComponent Store Cleanup Recommended : Yes", "")
                    : new ToolRun(0, shadowJson, ""));
            });
        result.Items.Single(row => row.Path == path).Bytes.Should().Be(10L * 1024 * 1024 * 1024);
        result.Items.Single(row => row.Group == "Теневые копии и восстановление").Bytes.Should().Be(40);
        result.Issues.Should().BeEmpty();
        calls.SelectMany(args => args).Should().NotContain("/StartComponentCleanup");
    }

    [Fact]
    public async Task Access_denied_keeps_partial_measurement_and_explains_missing_native_results()
    {
        var original = new StorageItem("Windows", @"C:\Windows\WinSxS", 99, "Логический размер", true);
        var result = await SystemStorageAnalysis.AppendAsync(new([original], [], false), @"C:\Windows", null,
            TestContext.Current.CancellationToken, (_, _, _, _) => Task.FromResult(new ToolRun(740, "", "Denied")));
        result.Items.Should().ContainSingle().Which.Should().Be(original);
        result.Issues.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("[{}]")]
    [InlineData("not-json")]
    [InlineData("[{\"Path\":\"C:\\\\anything\",\"Used\":-1}]")]
    public async Task Invalid_shadow_response_is_reported_instead_of_fabricating_zero(string response)
    {
        var result = await SystemStorageAnalysis.AppendAsync(new([], [], false), @"C:\Windows", null,
            TestContext.Current.CancellationToken, (_, arguments, _, _) => Task.FromResult(
                arguments.Contains("/Online") ? new ToolRun(740, "", "") : new ToolRun(0, response, "")));
        result.Items.Should().BeEmpty();
        result.Issues.Should().HaveCount(2);
    }
}
