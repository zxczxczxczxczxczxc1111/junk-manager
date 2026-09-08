using Xunit;
using FluentAssertions;
using JunkManager.Core.Rules;
using JunkManager.Tests.Infrastructure;

namespace JunkManager.Tests.Rules;

[Trait("Class", "Sandbox")]
public sealed class PathExpanderTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _sandbox;

    public PathExpanderTests(SandboxFixture sandbox) => _sandbox = sandbox;

    [Fact]
    public void Expand_neopredelennaya_peremennaya_brosaet()
    {
        // Молча подставить сам текст %NOPE% значит получить путь, которого нет
        // нигде, и правило будет тихо находить ноль вечно. Отказ громкий.
        var act = () => PathExpander.Expand(@"%JUNKMANAGER_NO_SUCH_VAR%\cache").ToList();

        act.Should().Throw<RuleFormatException>().WithMessage("*JUNKMANAGER_NO_SUCH_VAR*");
    }

    [Fact]
    public void Expand_izvestnaya_peremennaya_raskryvaetsya()
    {
        var lokalnye = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var found = PathExpander.Expand(@"%LOCALAPPDATA%").ToList();

        found.Should().ContainSingle().Which.Should().BeEquivalentTo(lokalnye);
    }

    [Fact]
    public void Expand_nesushchestvuyushchiy_put_daet_pustotu_a_ne_stroku()
    {
        // Раскрытие возвращает только то, что существует. Иначе сканер пойдёт
        // считать размер каталога, которого нет, и получит исключение вместо нуля.
        var found = PathExpander.Expand(Path.Combine(_sandbox.Root, "net-takogo")).ToList();

        found.Should().BeEmpty();
    }

    [Fact]
    public void Expand_zvezdochka_raskryvaetsya_posegmentno()
    {
        var root = _sandbox.CreateDirectory("apps");
        Directory.CreateDirectory(Path.Combine(root, "one", "Cache"));
        Directory.CreateDirectory(Path.Combine(root, "two", "Cache"));
        Directory.CreateDirectory(Path.Combine(root, "three"));   // без Cache

        var found = PathExpander.Expand(Path.Combine(root, "*", "Cache")).ToList();

        found.Should().HaveCount(2);
        found.Should().OnlyContain(p => p.EndsWith("Cache", StringComparison.Ordinal));
    }

    [Fact]
    public void Expand_zvezdochka_v_poslednem_segmente_beret_i_fayly()
    {
        var root = _sandbox.CreateDirectory("hvost");
        File.WriteAllText(Path.Combine(root, "a.log"), "x");
        File.WriteAllText(Path.Combine(root, "b.log"), "x");
        File.WriteAllText(Path.Combine(root, "c.txt"), "x");
        Directory.CreateDirectory(Path.Combine(root, "d.log"));

        var found = PathExpander.Expand(Path.Combine(root, "*.log")).ToList();

        found.Should().HaveCount(3, "две записи файлами и одна каталогом, маска смотрит на имя");
    }

    [Fact]
    public void Expand_zvezdochka_ne_uhodit_na_uroven_glubzhe()
    {
        // Маска в правиле означает ровно один уровень. Рекурсивный обход это
        // совсем другая операция, и получить её случайно нельзя.
        var root = _sandbox.CreateDirectory("odin-uroven");
        Directory.CreateDirectory(Path.Combine(root, "a", "b", "Cache"));
        Directory.CreateDirectory(Path.Combine(root, "a", "Cache"));

        var found = PathExpander.Expand(Path.Combine(root, "*", "Cache")).ToList();

        found.Should().ContainSingle().Which.Should().BeEquivalentTo(Path.Combine(root, "a", "Cache"));
    }

    [Fact]
    public void Expand_ne_zahodit_vnutr_junction()
    {
        // Junction на уровне маски это место, где сканирование молча выходит из
        // профиля пользователя и уезжает по всему диску.
        var root = _sandbox.CreateDirectory("wild");
        var outside = _sandbox.CreateDirectory("outside");
        Directory.CreateDirectory(Path.Combine(outside, "Cache"));
        _sandbox.CreateJunction(Path.Combine("wild", "link"), outside);

        var found = PathExpander.Expand(Path.Combine(root, "*", "Cache")).ToList();

        found.Should().BeEmpty("обход внутрь reparse-точки запрещён на самом раннем шаге");
    }

    [Fact]
    public void Expand_sam_junction_kak_konechnaya_cel_tozhe_ne_vydaetsya()
    {
        // Если каталог сам по себе ссылка, отдавать его наружу нельзя: удаление
        // пойдёт по ссылке в чужое место.
        var outside = _sandbox.CreateDirectory("cel-ssylki");
        var link = _sandbox.CreateJunction("prosto-ssylka", outside);

        var found = PathExpander.Expand(link).ToList();

        found.Should().BeEmpty();
    }

    [Fact]
    public void Expand_nedostupnyy_katalog_propuskaetsya_a_ne_ronyaet_raskrytie()
    {
        // Правило раскрывается по всему диску, и один каталог без прав не должен
        // валить раскрытие остальных.
        var found = PathExpander.Expand(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "*", "нет-такого"))
            .ToList();

        found.Should().BeEmpty();
    }
}
