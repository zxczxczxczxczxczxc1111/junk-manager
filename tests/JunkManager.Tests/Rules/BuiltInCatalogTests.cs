using FluentAssertions;
using JunkManager.Core.Rules;
using Xunit;

namespace JunkManager.Tests.Rules;

[Trait("Class", "Sandbox")]
public sealed class BuiltInCatalogTests
{
    private static readonly string[] ExpectedRegistryBranches =
    [
        "run-hkcu", "runonce-hkcu", "app-paths-hkcu", "run-hklm-64", "run-hklm-32",
        "runonce-hklm-64", "runonce-hklm-32", "app-paths-hklm-64",
    ];

    [Fact]
    public void Vstroennyy_katalog_sohranyaet_kazhdoe_postavlyaemoe_pravilo()
    {
        // This inventory is deliberate: losing a family is not a diet plan.
        string[] expected =
        [
            "prefetch", "temp-user", "windows-temp", "windows-logs", "panther",
            "system32-logfiles", "crash-dumps-user", "windows-error-reporting", "internet-explorer-cache",
            "steam-htmlcache", "epic-webcache", "battlenet-cache", "unity-shader-cache",
            "nuget-http-cache", "npm-cache", "yarn-cache", "pip-cache", "vs-component-cache",
            "vscode-cache", "dotnet-sdk-temp", "chrome-cache", "chrome-code-cache", "chrome-gpu-cache",
            "edge-cache", "edge-code-cache", "edge-gpu-cache", "chromium-crashpad", "opera-cache",
            "yandex-cache", "nvidia-shader-cache", "amd-shader-cache", "intel-shader-cache",
            "discord-cache", "slack-cache", "teams-cache", "spotify-cache", "office-inetcache", "adobe-media-cache",
        ];

        var rules = BuiltInCatalog.LoadRules();
        rules.Select(rule => rule.Id).Should().Contain(expected).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Vstroennye_opisaniya_vseh_istochnikov_dostupny_bez_puti_k_faylam()
    {
        // Windows cleanup cannot explain itself by pointing at a missing folder.
        var handlers = BuiltInCatalog.LoadVolumeCacheDescriptions();
        handlers.Should().HaveCount(30);
        handlers.Should().ContainKey("Thumbnail Cache").And.ContainKey("Recycle Bin");
        var branches = BuiltInCatalog.LoadRegistryRules();
        branches.Select(branch => branch.Id).Should().Contain(ExpectedRegistryBranches);
        var tools = BuiltInCatalog.LoadPlatformToolTexts();
        tools.DismName.Should().Be("Резервные копии компонентов Windows");
        tools.DriverName.Should().Be("Вытесненные версии драйверов");
        tools.DismConsequence.Should().NotBeNullOrWhiteSpace();
        tools.DriverConsequence.Should().NotBeNullOrWhiteSpace();
    }
}
