using Xunit;
using FluentAssertions;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;

namespace JunkManager.Tests.Safety;

[Trait("Class", "Sandbox")]
public sealed class PathResolverTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _sandbox;

    public PathResolverTests(SandboxFixture sandbox) => _sandbox = sandbox;

    [Fact]
    public void TryResolveFinalPath_obychnyy_katalog_daet_sam_sebya()
    {
        var dir = _sandbox.CreateDirectory("plain");

        PathResolver.TryResolveFinalPath(dir, out var resolved, out var err)
            .Should().BeTrue("ошибка: {0}", err);
        resolved.Should().BeEquivalentTo(dir);
    }

    [Fact]
    public void TryResolveFinalPath_junction_daet_cel_a_ne_ssylku()
    {
        var target = _sandbox.CreateDirectory("real-target");
        var link = _sandbox.CreateJunction("link", target);

        PathResolver.TryResolveFinalPath(link, out var resolved, out _).Should().BeTrue();
        resolved.Should().BeEquivalentTo(target, "junction обязан разрешаться в цель");
        resolved.Should().NotBeEquivalentTo(link);
    }

    [Fact]
    public void TryResolveFinalPath_ischeznuvshiy_put_daet_otkaz_s_prichinoy()
    {
        var gone = Path.Combine(_sandbox.Root, "never-existed");

        PathResolver.TryResolveFinalPath(gone, out var resolved, out var err).Should().BeFalse();
        resolved.Should().BeEmpty();
        err.Should().NotBeNullOrWhiteSpace("отказ обязан называть причину от системы");
    }

    [Fact]
    public void TryVerifyForDeletion_podmena_na_junction_v_zapret_otklonyaetsya()
    {
        // Ради этого всё и затевалось: правило указывало на безобидный каталог,
        // а между сканированием и удалением его подменили ссылкой в System32.
        var bait = _sandbox.CreateDirectory("bait");

        SafetyGuard.TryVerifyForDeletion(bait, out _, out var doPodmeny)
            .Should().BeTrue("до подмены путь разрешён, причина отказа: {0}", doPodmeny);

        Directory.Delete(bait);
        _sandbox.CreateJunction("bait", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"));

        SafetyGuard.TryVerifyForDeletion(bait, out _, out var reason)
            .Should().BeFalse("после подмены путь ведёт в System32");
        reason.Should().Contain("ссылк");
    }

    [Fact]
    public void TryVerifyForDeletion_podmena_na_RAZRESHENNYY_chuzhoy_katalog_tozhe_otklonyaetsya()
    {
        // Опаснее предыдущего случая, и найден он был мутацией. Подмена на
        // System32 отбивается ещё и повторной проверкой правил: разрешённый путь
        // сам по себе запрещён. А вот подмена на ДРУГОЙ разрешённый каталог все
        // проверки правил проходит, и остановить её может ровно одно: сверка
        // того, куда путь ведёт, с тем, что просило правило.
        var chuzhoy = _sandbox.CreateDirectory("chuzhoy-no-razreshennyy");
        var primanka = _sandbox.CreateDirectory("bait-2");

        SafetyGuard.TryVerifyForDeletion(primanka, out _, out _).Should().BeTrue();
        SafetyGuard.TryVerify(chuzhoy, out _, out _)
            .Should().BeTrue("цель подмены обязана быть РАЗРЕШЁННОЙ, иначе тест проверяет не то");

        Directory.Delete(primanka);
        _sandbox.CreateJunction("bait-2", chuzhoy);

        SafetyGuard.TryVerifyForDeletion(primanka, out _, out var reason)
            .Should().BeFalse("правило просило один каталог, а стоим мы в другом");
        reason.Should().Contain("ссылк");
    }

    [Fact]
    public void TryVerifyForDeletion_ischeznuvshiy_put_otklonyaetsya_a_ne_padaet()
    {
        var gone = Path.Combine(_sandbox.Root, "never-existed-2");

        var act = () => SafetyGuard.TryVerifyForDeletion(gone, out _, out _);
        act.Should().NotThrow();
        SafetyGuard.TryVerifyForDeletion(gone, out _, out var reason).Should().BeFalse();
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryVerifyForDeletion_stroka_proshla_a_dolzhna_byla_ne_proyti_ne_byvaet()
    {
        // Проверка по дескриптору не ослабляет строковую: запрещённый путь
        // обязан отклоняться до того, как дело дойдёт до открытия чего-либо.
        var zapret = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        SafetyGuard.TryVerifyForDeletion(zapret, out _, out var reason).Should().BeFalse();
        reason.Should().Contain("запрещённом корне");
    }

    [Fact]
    public void TryVerifyForDeletion_obychnyy_katalog_daet_propusk_s_odinakovymi_polyami()
    {
        var dir = _sandbox.CreateDirectory("plain-2");

        SafetyGuard.TryVerifyForDeletion(dir, out var propusk, out var reason)
            .Should().BeTrue("причина отказа: {0}", reason);

        propusk.Value.Should().BeEquivalentTo(dir);
        propusk.Resolved.Should().BeEquivalentTo(propusk.Value,
            "у каталога без ссылок объявленный и разрешённый пути совпадают");
    }
}
