using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Sources.Detect;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Sources;

[Trait("Class", "Sandbox")]
public sealed class DetectorTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _sandbox;

    public DetectorTests(SandboxFixture sandbox) => _sandbox = sandbox;

    private static void Napolnit(string directory, int files, int bytes)
    {
        Directory.CreateDirectory(directory);

        for (var i = 0; i < files; i++)
        {
            File.WriteAllBytes(Path.Combine(directory, $"f{i}.bin"), new byte[bytes]);
        }
    }

    /// <summary>Backdates the whole subtree, directory entry included.</summary>
    private static void Sostarit(string directory, int days)
    {
        var kogda = DateTime.UtcNow.AddDays(-days);

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, kogda);
        }

        Directory.SetLastWriteTimeUtc(directory, kogda);
    }

    /// <summary>
    /// A directory the guard REFUSES, created where the detectors are actually
    /// pointed in production. The sandbox lives under %TEMP%, which the guard
    /// explicitly allows, so the refusal branch cannot be reached from there at
    /// all: everything under the profile except AppData is denied, and that is
    /// the profile the abandoned-folder detector is written for.
    /// </summary>
    private static string SozdatVProfile()
    {
        var put = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            $"JunkManagerTests-{Guid.NewGuid():N}");

        Directory.CreateDirectory(put);
        return put;
    }

    [Theory]
    [InlineData(".ssh")]
    [InlineData(".gnupg")]
    [InlineData(".aws")]
    [InlineData(".azure")]
    [InlineData(".kube")]
    [InlineData(".docker")]
    [InlineData(".config")]
    [InlineData(".SSH")]
    public void IsExcluded_klyuchevye_katalogi_ne_trogayutsya(string name)
    {
        DetectorExclusions.IsExcluded(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("Discord")]
    [InlineData(".cache")]
    [InlineData("nekiy-katalog")]
    public void IsExcluded_obychnye_katalogi_ne_isklyuchayutsya(string name)
    {
        DetectorExclusions.IsExcluded(name).Should().BeFalse();
    }

    [Fact]
    public async Task ElectronCacheDetector_nahodit_kesh_izvestnogo_vladeltsa()
    {
        var root = _sandbox.CreateDirectory("electron");
        Napolnit(Path.Combine(root, "discord", "Cache", "Cache_Data"), 8, 200_000);
        Napolnit(Path.Combine(root, "discord", "User Data"), 2, 100);

        var result = await new ElectronCacheDetector()
            .ScanAsync([root], CancellationToken.None);

        result.Findings.Should().ContainSingle();
        result.Findings[0].Source.Should().Be(FindingSource.Detector);
        result.Findings[0].Tier.Should().Be(RiskTier.Risk, "локальные избранные GIF не всегда восстанавливаются");
        result.Findings[0].Path.Should().EndWith(Path.Combine("discord", "Cache", "Cache_Data"));
    }

    [Fact]
    public async Task Unknown_cache_owner_requires_review_and_keeps_account_data()
    {
        // A folder named Cache is evidence for inspection, not a psychic reading of its owner.
        var root = _sandbox.CreateDirectory("unknown-cache-owner");
        Napolnit(Path.Combine(root, "UnlistedApp", "Cache"), 2, 19);
        Napolnit(Path.Combine(root, "UnlistedApp", "Local Storage"), 1, 47);
        Napolnit(Path.Combine(root, "UnlistedApp", "Service Worker", "CacheStorage"), 1, 53);
        var result = await new ElectronCacheDetector().ScanAsync([root], CancellationToken.None);
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.SizeBytes.Should().Be(38);
        finding.Tier.Should().Be(RiskTier.Risk);
        finding.RequiredStoppedProcesses.Should().Contain("UnlistedApp");
        File.Exists(Path.Combine(root, "UnlistedApp", "Local Storage", "f0.bin")).Should().BeTrue();
        File.Exists(Path.Combine(root, "UnlistedApp", "Service Worker", "CacheStorage", "f0.bin")).Should().BeTrue();
    }

    [Fact]
    public async Task ElectronCacheDetector_imya_nazyvaet_prilozhenie_a_ne_papku_kesha()
    {
        var root = _sandbox.CreateDirectory("electron-imya");
        Napolnit(Path.Combine(root, "discord", "Cache", "Cache_Data"), 8, 200_000);

        var result = await new ElectronCacheDetector().ScanAsync([root], CancellationToken.None);

        result.Findings.Should().ContainSingle()
            .Which.Name.Should().Contain("discord",
                "человек выбирает по имени приложения, а «Кэш Cache» не говорит ему ничего");
    }

    [Fact]
    public async Task ElectronCacheDetector_nahodit_tri_formy_i_sohranyaet_ServiceWorker()
    {
        var root = _sandbox.CreateDirectory("electron-formy");
        Napolnit(Path.Combine(root, "discord", "Cache", "Cache_Data"), 4, 400_000);
        Napolnit(Path.Combine(root, "discord", "Code Cache", "js"), 4, 400_000);
        Napolnit(Path.Combine(root, "discord", "GPUCache"), 4, 400_000);
        Napolnit(Path.Combine(root, "discord", "Service Worker", "CacheStorage"), 4, 400_000);

        var result = await new ElectronCacheDetector().ScanAsync([root], CancellationToken.None);

        result.Findings.Select(f => Path.GetFileName(f.Path))
            .Should().BeEquivalentTo(["Cache_Data", "Code Cache", "GPUCache"]);
        result.Findings.SelectMany(f => f.DeletionTargets).Should().NotContain(path => path.Contains("Service Worker", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ElectronCacheDetector_ne_zalezaet_v_isklyuchennye_katalogi()
    {
        var root = _sandbox.CreateDirectory("electron-excluded");
        Napolnit(Path.Combine(root, ".ssh", "Cache", "Cache_Data"), 8, 200_000);

        var result = await new ElectronCacheDetector().ScanAsync([root], CancellationToken.None);

        result.Findings.Should().BeEmpty(".ssh не трогается ни при каком содержимом");
    }

    [Fact]
    public async Task ElectronCacheDetector_nahodit_dazhe_malenkiy_kesh()
    {
        var root = _sandbox.CreateDirectory("electron-meloch");
        Napolnit(Path.Combine(root, "discord", "Cache", "Cache_Data"), 3, 1);

        var result = await new ElectronCacheDetector().ScanAsync([root], CancellationToken.None);

        result.Findings.Should().ContainSingle().Which.SizeBytes.Should().Be(3);
    }

    [Fact]
    public async Task ElectronCacheDetector_ne_zahodit_vnutr_junction()
    {
        var root = _sandbox.CreateDirectory("electron-junction");
        var chuzhoe = _sandbox.CreateDirectory("electron-junction-target");
        Napolnit(Path.Combine(chuzhoe, "Cache", "Cache_Data"), 8, 200_000);
        _sandbox.CreateJunction(Path.Combine("electron-junction", "NekoeApp"), chuzhoe);

        var result = await new ElectronCacheDetector().ScanAsync([root], CancellationToken.None);

        result.Findings.Should().BeEmpty("за ссылкой лежит чужой каталог, а не кэш этого приложения");
    }

    [Fact]
    public async Task ElectronCacheDetector_otkaz_guard_ne_stanovitsya_nahodkoy()
    {
        var koren = SozdatVProfile();

        try
        {
            var kesh = Path.Combine(koren, "NekoeApp", "Cache", "Cache_Data");
            Napolnit(kesh, 4, 400_000);

            SafetyGuard.TryVerify(kesh, out _, out _)
                .Should().BeFalse("guard обязан отказать, иначе тест проверяет не ту ветку");

            var result = await new ElectronCacheDetector().ScanAsync([koren], CancellationToken.None);

            result.Findings.Should().BeEmpty("guard отказал, значит предлагать это нельзя");
            result.Skipped.Should().ContainSingle().Which.Path.Should().Be(koren);
        }
        finally
        {
            Directory.Delete(koren, recursive: true);
        }
    }

    [Fact]
    public async Task AbandonedFolderDetector_ne_udalyayet_nastroyki_po_vozrastu()
    {
        var root = _sandbox.CreateDirectory("abandoned");
        var stale = Path.Combine(root, ".starye-nastroyki");
        var fresh = Path.Combine(root, ".svezhie-nastroyki");
        Napolnit(stale, 4, 500_000);
        Napolnit(fresh, 4, 500_000);
        Sostarit(stale, 400);

        var result = await new AbandonedFolderDetector()
            .ScanAsync([root], idleDays: 180, CancellationToken.None);

        result.Findings.Should().BeEmpty("возраст каталога не доказывает ненужность настроек");
        result.Skipped.Should().Contain(item => item.Path == stale && item.Reason.Contains("возраст", StringComparison.Ordinal));
        Directory.EnumerateFiles(stale).Should().HaveCount(4);
    }

    [Fact]
    public async Task AbandonedFolderDetector_pomechaet_osnovanie_v_Consequence()
    {
        var root = _sandbox.CreateDirectory("abandoned-reason");
        var stale = Path.Combine(root, ".zabytoe");
        Napolnit(stale, 4, 500_000);
        Sostarit(stale, 400);

        var result = await new AbandonedFolderDetector()
            .ScanAsync([root], idleDays: 180, CancellationToken.None);

        result.Findings.Should().BeEmpty();
        result.Skipped.Should().ContainSingle().Which.Reason.Should().Contain("не менялся");
    }

    [Fact]
    public async Task AbandonedFolderDetector_ne_beret_isklyuchennye_katalogi()
    {
        var root = _sandbox.CreateDirectory("abandoned-excluded");
        var stale = Path.Combine(root, ".ssh");
        Napolnit(stale, 4, 500_000);
        Sostarit(stale, 400);

        var result = await new AbandonedFolderDetector()
            .ScanAsync([root], idleDays: 180, CancellationToken.None);

        result.Findings.Should().BeEmpty(
            "день, когда продукт предложит удалить .ssh, это последний день продукта");
    }

    [Fact]
    public async Task AbandonedFolderDetector_ne_beret_meloch()
    {
        var root = _sandbox.CreateDirectory("abandoned-meloch");
        var stale = Path.Combine(root, ".krohotnoe");
        Napolnit(stale, 3, 1000);
        Sostarit(stale, 400);

        var result = await new AbandonedFolderDetector()
            .ScanAsync([root], idleDays: 180, CancellationToken.None);

        result.Findings.Should().BeEmpty("возраст не доказывает ненужность независимо от размера");
        result.Skipped.Should().ContainSingle().Which.Path.Should().Be(stale);
    }

    [Fact]
    public async Task AbandonedFolderDetector_svezhiy_fayl_vnutri_otmenyaet_zabroshennost()
    {
        // Отметка времени у самого каталога меняется при добавлении и удалении
        // записей, но НЕ при записи в уже существующий файл. Программа, которая
        // каждый день дописывает один и тот же журнал, оставляет каталог со
        // старой отметкой, и обнаружитель, смотрящий только на каталог, объявит
        // её заброшенной.
        var root = _sandbox.CreateDirectory("abandoned-svezhiy-fayl");
        var katalog = Path.Combine(root, ".zhivoe");
        Napolnit(katalog, 4, 500_000);
        Sostarit(katalog, 400);
        File.SetLastWriteTimeUtc(Path.Combine(katalog, "f0.bin"), DateTime.UtcNow);

        var result = await new AbandonedFolderDetector()
            .ScanAsync([root], idleDays: 180, CancellationToken.None);

        result.Findings.Should().BeEmpty("внутри лежит файл, изменённый сегодня");
    }

    [Fact]
    public async Task AbandonedFolderDetector_ne_beret_katalogi_bez_tochki()
    {
        var root = _sandbox.CreateDirectory("abandoned-bez-tochki");
        var stale = Path.Combine(root, "Obychnyy Katalog");
        Napolnit(stale, 4, 500_000);
        Sostarit(stale, 400);

        var result = await new AbandonedFolderDetector()
            .ScanAsync([root], idleDays: 180, CancellationToken.None);

        result.Findings.Should().BeEmpty(
            "обнаружитель заброшенного смотрит только на точечные каталоги профиля");
    }

    [Fact]
    public async Task AbandonedFolderDetector_ne_schitaet_chuzhoe_za_ssylkoy()
    {
        // Обход с AttributesToSkip = ReparsePoint пропускает ссылки, НАЙДЕННЫЕ
        // внутри, но не корень обхода: сам каталог-ссылку он раскрывает честно.
        // Поэтому проверка стоит до обхода, а не внутри него.
        var root = _sandbox.CreateDirectory("abandoned-junction");
        var chuzhoe = _sandbox.CreateDirectory("abandoned-junction-target");
        Napolnit(chuzhoe, 4, 500_000);
        Sostarit(chuzhoe, 400);
        var ssylka = _sandbox.CreateJunction(Path.Combine("abandoned-junction", ".ssylka"), chuzhoe);

        // Отметку САМОЙ ссылки тоже назад: свежая ссылка отсекается отсечкой по
        // возрасту, и тогда проверка на ссылку ничего не доказывает. Мутация
        // «обнаружитель заходит внутрь junction» ровно на этом и выжила.
        Directory.SetLastWriteTimeUtc(ssylka, DateTime.UtcNow.AddDays(-400));

        var result = await new AbandonedFolderDetector()
            .ScanAsync([root], idleDays: 180, CancellationToken.None);

        result.Findings.Should().BeEmpty("за ссылкой лежит чужой каталог, а не забытые настройки");
    }

    [Fact]
    public async Task AbandonedFolderDetector_otkaz_guard_ne_stanovitsya_nahodkoy()
    {
        var koren = SozdatVProfile();

        try
        {
            var stale = Path.Combine(koren, ".zabytoe");
            Napolnit(stale, 4, 400_000);
            Sostarit(stale, 400);

            SafetyGuard.TryVerify(stale, out _, out _)
                .Should().BeFalse("guard обязан отказать, иначе тест проверяет не ту ветку");

            var result = await new AbandonedFolderDetector()
                .ScanAsync([koren], idleDays: 180, CancellationToken.None);

            result.Findings.Should().BeEmpty("guard отказал, значит предлагать это нельзя");
            result.Skipped.Should().ContainSingle().Which.Path.Should().Be(stale);
        }
        finally
        {
            Directory.Delete(koren, recursive: true);
        }
    }
}
