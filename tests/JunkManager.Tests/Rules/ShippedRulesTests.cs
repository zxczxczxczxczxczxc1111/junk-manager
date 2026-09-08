using JunkManager.Core;
using JunkManager.Core.Rules;
using JunkManager.Safety;
using Xunit;
using FluentAssertions;

namespace JunkManager.Tests.Rules;

/// <summary>
/// Проверки НАСТОЯЩЕГО набора правил, который уезжает вместе с продуктом.
/// </summary>
/// <remarks>
/// Правило, указывающее в запрещённый корень, не ломает ничего заметного: guard
/// молча его отклонит, находок не будет, и никто никогда не узнает, что целая
/// категория мусора не чистится. Такие правила ловятся здесь, а не в отчётах
/// через полгода.
/// </remarks>
[Trait("Class", "Sandbox")]
public sealed class ShippedRulesTests
{
    private static IReadOnlyList<RuleDefinition> Pravila() =>
        BuiltInCatalog.LoadRules();

    /// <summary>
    /// Самая глубокая часть пути без масок. Guard проверяет строку и отклоняет
    /// звёздочку как недопустимый символ, поэтому спрашивать его надо про
    /// неподвижную часть, а она же и определяет, в какой корень правило метит.
    /// </summary>
    private static string NepodvizhnayaChast(string obrazec)
    {
        var razvernutyy = PathExpander.ExpandEnvironment(obrazec);
        var segmenty = razvernutyy.Split(Path.DirectorySeparatorChar);

        var konec = segmenty.Length;
        for (var i = 0; i < segmenty.Length; i++)
        {
            if (segmenty[i].Contains('*', StringComparison.Ordinal)
                || segmenty[i].Contains('?', StringComparison.Ordinal))
            {
                konec = i;
                break;
            }
        }

        return string.Join(Path.DirectorySeparatorChar, segmenty.Take(konec));
    }

    [Fact]
    public void Nabor_pravil_ne_pust_i_zagruzhaetsya()
    {
        var pravila = Pravila();

        pravila.Should().HaveCountGreaterThan(30, "иначе это не чистильщик диска, а демонстрация");
        pravila.Select(p => p.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Ni_odno_pravilo_ne_metit_v_zapreshchennyy_koren()
    {
        var narusheniya = new List<string>();

        foreach (var pravilo in Pravila())
        {
            foreach (var obrazec in pravilo.Paths)
            {
                var nepodvizhnaya = NepodvizhnayaChast(obrazec);

                if (!SafetyGuard.TryVerify(nepodvizhnaya, out _, out var prichina))
                {
                    narusheniya.Add($"{pravilo.Id}: {obrazec} -> {prichina}");
                }
            }
        }

        narusheniya.Should().BeEmpty(
            "правило в запрещённом корне не находит НИЧЕГО и делает это молча");
    }

    [Fact]
    public void Kazhdoe_pravilo_obyasnyaet_posledstviya_chelovecheski()
    {
        foreach (var pravilo in Pravila())
        {
            pravilo.Consequence.Length.Should().BeGreaterThan(30,
                "правило '{0}': «последствие» короче тридцати знаков это не объяснение, "
                + "а отписка, а человек по нему решает, удалять или нет",
                pravilo.Id);

            pravilo.Consequence.Should().NotContain("—",
                "правило '{0}': длинное тире в интерфейсе запрещено", pravilo.Id);
        }
    }

    [Fact]
    public void Stupen_Risk_stavitsya_tolko_tam_gde_est_chto_teryat()
    {
        // Правило: Safe означает «вернётся само, вы не заметите». Если категория
        // целиком помечена Risk, значит ступень перестала что-либо значить.
        var pravila = Pravila();
        var riskovyh = pravila.Count(p => p.Tier == RiskTier.Risk);

        riskovyh.Should().BeLessThan(pravila.Count / 3,
            "если треть правил рискованные, ступень перестала различать");
    }

    [Fact]
    public void Imena_pravil_ne_povtoryayutsya_mezhdu_kategoriyami()
    {
        // Одинаковое имя в двух категориях в интерфейсе выглядит как дубль
        // строки, и человек снимает галочку не с того.
        var pravila = Pravila();

        pravila.Select(p => p.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Kategorii_zapolneny_u_vseh_pravil()
    {
        Pravila().Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Category));
    }
}
