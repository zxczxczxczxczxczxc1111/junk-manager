using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Sources.Platform;
using JunkManager.Deletion;
using JunkManager.Safety;
using Xunit;

namespace JunkManager.Tests.Sources;

[Trait("Class", "Sandbox")]
public sealed class PlatformToolSourceTests
{
    private static string RulesDir => Path.Combine(AppContext.BaseDirectory, "rules", "sources");

    [Fact]
    public async Task ScanAsync_nahodki_neset_istochnik_PlatformTool_i_stupen_Risk()
    {
        var result = await new PlatformToolSource().ScanAsync(RulesDir, null, CancellationToken.None);

        result.Findings.Should().OnlyContain(f => f.Source == FindingSource.PlatformTool);
        result.Findings.Should().OnlyContain(f => f.Tier == RiskTier.Risk,
            "и склад компонентов, и старые драйверы снимают возможность отката");
        result.Findings.Should().OnlyContain(f => !FindingPath.IsFileSystem(f.Path));
    }

    [Fact]
    public async Task ScanAsync_bez_prav_administratora_daet_Skipped_a_ne_nol_bez_obyasneniya()
    {
        var result = await new PlatformToolSource().ScanAsync(RulesDir, null, CancellationToken.None);

        // Either DISM answered, or it refused and said so. Silence is the one
        // outcome that is not allowed: it reads exactly like "nothing found".
        (result.Findings.Any(f => f.Path.Contains("component-store", StringComparison.Ordinal))
         || result.Skipped.Any(s => s.Path.Contains("component-store", StringComparison.Ordinal)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ScanAsync_otmena_pomechaet_rezultat()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await new PlatformToolSource().ScanAsync(RulesDir, null, cts.Token);

        result.Cancelled.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteDriverAsync_krivoe_imya_otkazyvaet_do_zapuska_processa()
    {
        // Предохранитель ВМ снят решением владельца 06.09.2026, и проверка имени
        // осталась ЕДИНСТВЕННЫМИ воротами этого метода. Отказ обязан случиться до
        // запуска pnputil: запускать чужой процесс с кривым аргументом ради того,
        // чтобы посмотреть, что он ответит, нельзя.
        //
        // Проверка нарочно берёт кривое имя, а не настоящее. С настоящим она
        // удалила бы драйвер на машине, где её запустили, и прогон набора стал бы
        // сам разрушительной операцией. По той же причине здесь нет проверки на
        // CleanComponentStoreAsync: у неё аргументов нет вовсе, и любой её вызов
        // это настоящий запуск DISM. Её команда закреплена отдельно, проверкой
        // CleanupArguments_nikogda_ne_soderzhat_ResetBase.
        var act = async () =>
            await PlatformToolExecutor.DeleteDriverAsync("; rm -rf", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*не похоже на опубликованное имя*");
    }
}
