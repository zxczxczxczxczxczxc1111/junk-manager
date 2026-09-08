using System.Windows;
using FluentAssertions;
using JunkManager.App.Services;
using Xunit;

namespace JunkManager.Tests.App;

[Trait("Class", "Sandbox")]
public sealed class MotionPolicyTests
{
    private static ResourceDictionary Slovar() => new()
    {
        ["MotionFast"] = new Duration(TimeSpan.FromMilliseconds(150)),
        ["MotionMedium"] = new Duration(TimeSpan.FromMilliseconds(200)),
    };

    [Fact]
    public void Pri_zaprete_dvizheniya_dlitelnosti_stanovyatsya_nulyom()
    {
        var slovar = Slovar();

        MotionPolicy.Apply(slovar, animationsAllowed: false);

        ((Duration)slovar["MotionFast"]!).TimeSpan.Should().Be(TimeSpan.Zero);
        ((Duration)slovar["MotionMedium"]!).TimeSpan.Should().Be(TimeSpan.Zero);
        slovar["MotionEnabled"].Should().Be(false);
    }

    [Fact]
    public void Pri_razreshennom_dvizhenii_dlitelnosti_ne_trogayutsya()
    {
        var slovar = Slovar();

        MotionPolicy.Apply(slovar, animationsAllowed: true);

        ((Duration)slovar["MotionFast"]!).TimeSpan.Should().Be(TimeSpan.FromMilliseconds(150));
        ((Duration)slovar["MotionMedium"]!).TimeSpan.Should().Be(TimeSpan.FromMilliseconds(200));
        slovar["MotionEnabled"].Should().Be(true);
    }

    [Fact]
    public void Otsutstvuyushchiy_klyuch_eto_oshibka_a_ne_tishina()
    {
        // Переименовали токен и забыли здесь: без этой проверки продукт просто
        // перестанет уважать настройку уменьшения движения, и никто не узнает.
        var pustoy = new ResourceDictionary();

        var act = () => MotionPolicy.Apply(pustoy, animationsAllowed: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MotionFast*");
    }

    [Fact]
    public void Tokeny_iz_vlozhennogo_slovarya_tozhe_glushatsya()
    {
        // Форма ровно та, что в App.xaml: длительности лежат в Tokens.xaml,
        // а Apply получает Application.Resources, у которого своих записей нет
        // вовсе. Плоский словарь выше эту форму не проверяет, и проверка,
        // зелёная на плоском и красная на живом, хуже отсутствующей.
        var verhniy = new ResourceDictionary();
        verhniy.MergedDictionaries.Add(Slovar());

        MotionPolicy.Apply(verhniy, animationsAllowed: false);

        ((Duration)verhniy["MotionFast"]!).TimeSpan.Should().Be(TimeSpan.Zero);
        ((Duration)verhniy["MotionMedium"]!).TimeSpan.Should().Be(TimeSpan.Zero);
    }
}
