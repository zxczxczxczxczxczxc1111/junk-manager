using Xunit;
using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Rules;
using JunkManager.Tests.Infrastructure;

namespace JunkManager.Tests.Rules;

[Trait("Class", "Sandbox")]
public sealed class RuleLoaderTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _sandbox;

    public RuleLoaderTests(SandboxFixture sandbox) => _sandbox = sandbox;

    [Fact]
    public void Load_korrektnyy_fayl_daet_pravila()
    {
        var dir = _sandbox.CreateDirectory("rules-ok");
        File.WriteAllText(Path.Combine(dir, "browsers.json"), """
        {
          "category": "Браузеры",
          "rules": [{
            "id": "chrome-cache",
            "name": "Кэш Chrome",
            "paths": ["%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\Cache"],
            "tier": "Safe",
            "consequence": "Страницы перезагрузятся из сети. Пароли не трогаются.",
            "olderThanDays": 0
          }]
        }
        """);

        var rules = RuleLoader.Load(dir);

        rules.Should().ContainSingle();
        rules[0].Id.Should().Be("chrome-cache");
        rules[0].Tier.Should().Be(RiskTier.Safe);
        rules[0].Category.Should().Be("Браузеры", "категория берётся из файла, а не из правила");
    }

    [Fact]
    public void Load_bityy_json_brosaet_a_ne_propuskaet_molcha()
    {
        // Fail-closed: a broken rules file must stop the load loudly. Skipping it
        // silently means the product quietly stops finding a whole category, and
        // that looks exactly like "there is no junk here".
        var dir = _sandbox.CreateDirectory("rules-broken");
        File.WriteAllText(Path.Combine(dir, "bad.json"), """{ "category": "X", "rules": [ """);

        var act = () => RuleLoader.Load(dir);

        act.Should().Throw<RuleFormatException>().WithMessage("*bad.json*");
    }

    [Fact]
    public void Load_pravilo_bez_consequence_ne_gruzitsya()
    {
        // A finding a human cannot understand is worse than no finding.
        var dir = _sandbox.CreateDirectory("rules-no-consequence");
        File.WriteAllText(Path.Combine(dir, "x.json"), """
        { "category": "X", "rules": [
          { "id": "a", "name": "A", "paths": ["%TEMP%\\a"], "tier": "Safe", "consequence": "  " } ] }
        """);

        var act = () => RuleLoader.Load(dir);

        act.Should().Throw<RuleFormatException>().WithMessage("*consequence*");
    }

    [Fact]
    public void Load_dublikat_id_v_raznyh_faylah_brosaet()
    {
        var dir = _sandbox.CreateDirectory("rules-dup");
        var body = """
        { "category": "X", "rules": [
          { "id": "same", "name": "A", "paths": ["%TEMP%\\a"], "tier": "Safe", "consequence": "ок" } ] }
        """;
        File.WriteAllText(Path.Combine(dir, "one.json"), body);
        File.WriteAllText(Path.Combine(dir, "two.json"), body);

        var act = () => RuleLoader.Load(dir);

        act.Should().Throw<RuleFormatException>().WithMessage("*same*");
    }

    [Fact]
    public void Load_neizvestnyy_tier_brosaet()
    {
        // "Refill" и "Review" существовали в раннем черновике. Устаревший файл
        // правил не должен молча загрузиться как Safe.
        var dir = _sandbox.CreateDirectory("rules-tier");
        File.WriteAllText(Path.Combine(dir, "x.json"), """
        { "category": "X", "rules": [
          { "id": "a", "name": "A", "paths": ["%TEMP%\\a"], "tier": "Review", "consequence": "ок" } ] }
        """);

        var act = () => RuleLoader.Load(dir);

        act.Should().Throw<RuleFormatException>().WithMessage("*Review*");
    }

    [Fact]
    public void Load_pravilo_bez_putey_brosaet()
    {
        var dir = _sandbox.CreateDirectory("rules-nopaths");
        File.WriteAllText(Path.Combine(dir, "x.json"), """
        { "category": "X", "rules": [
          { "id": "a", "name": "A", "paths": [], "tier": "Safe", "consequence": "ок" } ] }
        """);

        var act = () => RuleLoader.Load(dir);

        act.Should().Throw<RuleFormatException>().WithMessage("*paths*");
    }

    [Fact]
    public void Load_otsutstvuyushchiy_katalog_brosaet_a_ne_daet_pustoy_spisok()
    {
        // Пустой список правил и отсутствующий каталог выглядят на экране
        // одинаково: «мусора нет». Различать их обязан загрузчик.
        var act = () => RuleLoader.Load(Path.Combine(_sandbox.Root, "net-takogo-kataloga"));

        act.Should().Throw<RuleFormatException>().WithMessage("*не найден*");
    }

    [Fact]
    public void Load_pustoy_katalog_brosaet_tozhe()
    {
        // Тот же довод, что и у отсутствующего каталога, доведённый до конца.
        // Найдено 05.09.2026 на выкладке: `rules-validate` по пустому каталогу
        // печатал «правил загружено: 0» и выходил с нулём. То есть поставка, в
        // которую правила не доехали, проходила проверку и выглядела бы у
        // человека как «на диске чисто».
        var dir = Path.Combine(_sandbox.Root, "pustoy-katalog-pravil");
        Directory.CreateDirectory(dir);

        var act = () => RuleLoader.Load(dir);

        act.Should().Throw<RuleFormatException>().WithMessage("*ни одного правила*");
    }

    [Fact]
    public void Load_katalog_s_fayami_no_bez_pravil_brosaet()
    {
        // Файл на месте, а правил внутри ноль. Разница с пустым каталогом та,
        // что тут кто-то правила писал и стёр, и молчать об этом ещё хуже.
        var dir = Path.Combine(_sandbox.Root, "katalog-bez-pravil");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pusto.json"), """
            { "category": "Пусто", "rules": [] }
            """);

        var act = () => RuleLoader.Load(dir);

        act.Should().Throw<RuleFormatException>().WithMessage("*ни одного правила*");
    }

    [Fact]
    public void Load_nastoyashchie_pravila_proekta_gruzyatsya()
    {
        // Production data lives in Core, despite the fixture folder's nostalgia.
        var rules = BuiltInCatalog.LoadRules();

        rules.Should().NotBeEmpty();
        rules.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Consequence));
        rules.Select(r => r.Id).Should().OnlyHaveUniqueItems();
    }
}
