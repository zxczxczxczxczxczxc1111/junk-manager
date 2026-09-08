using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Rules;
using JunkManager.Core.Scanning;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Scanning;

[Trait("Class", "Sandbox")]
public sealed class KnownCacheCoverageTests
{
    [Fact]
    public async Task Running_apps_still_show_cache_and_keep_the_deletion_guard()
    {
        using var sandbox = new SandboxFixture();
        var root = sandbox.CreateDirectory("running-cache");
        var file = Path.Combine(root, "cached.bin");
        File.WriteAllBytes(file, new byte[37]);
        var rule = new RuleDefinition("running", "Кэш", [root], "Safe", "Загрузится заново.")
        { Tier = RiskTier.Safe, ProcessNames = ["FixtureApp"] };
        var result = await new FileScanner(processRefusal: _ => "сначала закройте FixtureApp")
            .ScanAsync([rule], null, TestContext.Current.CancellationToken);
        // Running is a reason to postpone deletion, not to hide thirty-seven perfectly visible bytes.
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.SizeBytes.Should().Be(37);
        finding.RequiredStoppedProcesses.Should().Equal("FixtureApp");
        finding.Consequence.Should().Contain("сначала закройте FixtureApp");
        result.Skipped.Should().BeEmpty();
        File.ReadAllBytes(file).Should().HaveCount(37);
    }

    [Fact]
    public async Task Built_in_rules_find_missing_caches_without_counting_sessions_or_programs()
    {
        using var sandbox = new SandboxFixture();
        var local = sandbox.CreateDirectory("local");
        var roaming = sandbox.CreateDirectory("roaming");
        string Add(string root, string relative, int size)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[size]);
            return path;
        }
        var audio = Add(local, @"Spotify\Data\ab\audio", 101);
        var storeAudio = Add(local, @"Packages\SpotifyAB.SpotifyMusic_zpdnekdrzrea0\LocalCache\Spotify\Data\ab\audio", 113);
        var steam = Add(local, @"Steam\htmlcache\Default\Cache\Cache_Data\data_1", 103);
        var crash = Add(local, @"CrashDumps\recent.dmp", 107);
        var telegram = Add(roaming, @"Telegram Desktop\tdata\user_data#2\media_cache\part", 109);
        var preserved = new[]
        {
            Add(local, @"Spotify\Spotify.exe", 1000),
            Add(local, @"Packages\SpotifyAB.SpotifyMusic_zpdnekdrzrea0\LocalState\Spotify\prefs", 1000),
            Add(local, @"Steam\htmlcache\Default\Cookies", 1000),
            Add(local, @"CrashDumps\notes.txt", 1000),
            Add(roaming, @"Telegram Desktop\tdata\key_datas", 1000),
            Add(roaming, @"Telegram Desktop\tdata\user_data#2\settings", 1000),
        };
        string[] ids = ["spotify-cache", "spotify-audio-cache", "steam-htmlcache", "crash-dumps-user", "telegram-media-cache"];
        var rules = BuiltInCatalog.LoadRules().Where(rule => ids.Contains(rule.Id)).Select(rule => rule with
        {
            Paths = rule.Paths.Select(path => path.Replace("%LOCALAPPDATA%", local, StringComparison.Ordinal)
                .Replace("%APPDATA%", roaming, StringComparison.Ordinal)).ToArray(),
        }).ToArray();
        var result = await new FileScanner(processRefusal: _ => null).ScanAsync(rules, null, TestContext.Current.CancellationToken);
        // A large folder is not a cleanup target, no matter how exciting its number looks.
        result.Findings.SelectMany(finding => finding.DeletionTargets).Should().BeEquivalentTo(audio, storeAudio, steam, crash, telegram);
        result.TotalBytes.Should().Be(533);
        result.Findings.Where(finding => finding.RuleId is "telegram-media-cache" or "crash-dumps-user")
            .Should().OnlyContain(finding => finding.Tier == RiskTier.Risk);
        preserved.Should().OnlyContain(path => File.Exists(path));
    }

    [Fact]
    public void Spotify_media_is_default_cleanup_but_other_storage_and_private_files_stay_protected()
    {
        // A requested media-cache exception must not become a skeleton key for browser databases.
        var rule = BuiltInCatalog.LoadRules().Single(rule => rule.Id == "spotify-audio-cache");
        rule.Tier.Should().Be(RiskTier.Safe);
        rule.Paths.Should().Contain("%LOCALAPPDATA%\\Spotify\\Storage");
        var local = @"C:\Users\fixture\AppData\Local";
        CleanupPathPolicy.IsProtected(local + @"\Spotify\Storage\track", local).Should().BeFalse();
        CleanupPathPolicy.IsProtected(local + @"\SpotifyOther\Storage\track", local).Should().BeTrue();
        CleanupPathPolicy.IsProtected(local + @"\Other\Storage\track", local).Should().BeTrue();
        CleanupPathPolicy.IsProtected(local + @"\Spotify\Storage\Documents\draft", local).Should().BeTrue();
        CleanupPathPolicy.IsProtected(local + @"\Spotify\Storage\Local Storage\data", local).Should().BeTrue();
        CleanupPathPolicy.IsProtected(@"D:\Other\Spotify\Storage\track", local).Should().BeTrue();
    }
}
