using FluentAssertions;
using Xunit;

namespace JunkManager.Tests.Theme;

[Trait("Class", "Sandbox")]
public sealed class ComboStyleTests
{
    [Fact]
    public void Popup_ne_animiruetsya_sistemoy()
    {
        // The system fade is the system look, and the mockup animates the list
        // with its own easing over 140 ms.
        var doc = XamlSource.Load("Controls.Combo.xaml");

        var popup = doc.Descendants().Single(e => e.Name.LocalName == "Popup");
        popup.Attribute("PopupAnimation")!.Value.Should().Be("None");
    }

    [Fact]
    public void Vypadayushchiy_spisok_ne_redaktiruemyy()
    {
        var doc = XamlSource.Load("Controls.Combo.xaml");

        var combo = doc.Descendants().Single(e =>
            e.Name.LocalName == "Style" &&
            e.Attribute("TargetType")!.Value.Contains("ComboBox}", StringComparison.Ordinal));

        combo.Elements().Should().Contain(s =>
            s.Attribute("Property")!.Value == "IsEditable" &&
            s.Attribute("Value")!.Value == "False");
    }

    [Fact]
    public void Zakrytyy_spisok_beret_shablon_vmeste_so_znacheniem()
    {
        // Один Content без ContentTemplate рисует ToString объекта. Ровно так
        // экран настроек показывал «VariantVybora { Podpis = Безвозвратно,
        // Znachenie = Permanent }», и нашлось это только снимком живого окна.
        var doc = XamlSource.Load("Controls.Combo.xaml");

        var presenter = doc.Descendants()
            .Where(e => e.Name.LocalName == "ContentPresenter")
            .Single(e => e.Attribute("Content")?.Value.Contains(
                "SelectionBoxItem", StringComparison.Ordinal) == true);

        presenter.Attribute("ContentTemplate")!.Value.Should().Contain(
            "SelectionBoxItemTemplate",
            "иначе шаблон строки до закрытого списка не доезжает");
    }

    [Fact]
    public void Nikto_ne_polzuetsya_DisplayMemberPath()
    {
        // Проверено зондом 05.09.2026: при одном DisplayMemberPath свойство
        // SelectionBoxItemTemplate остаётся null, и закрытый список печатает
        // ToString. Свой шаблон это чинить не умеет, поэтому путь закрыт
        // целиком, а не оставлен ловушкой для следующего экрана.
        foreach (var fayl in XamlSource.Files())
        {
            System.Xml.Linq.XDocument.Load(fayl).Descendants()
                .Should().NotContain(
                    e => e.Attribute("DisplayMemberPath") != null,
                    "в {0} есть DisplayMemberPath: ставить надо ItemTemplate, "
                    + "иначе закрытый список покажет имя типа",
                    Path.GetFileName(fayl));
        }
    }

    [Fact]
    public void U_ComboBoxItem_est_svoy_shablon()
    {
        // Without an implicit ComboBoxItem style the popup rows keep the system
        // highlight even though the popup border is ours.
        var doc = XamlSource.Load("Controls.Combo.xaml");

        doc.Descendants().Should().Contain(e =>
            e.Name.LocalName == "Style" &&
            e.Attribute("TargetType")!.Value.Contains("ComboBoxItem", StringComparison.Ordinal) &&
            e.Attribute(XamlSource.X + "Key") == null);
    }

    [Fact]
    public void Strochka_spiska_ne_zalivaetsya_sistemnoy_kistyu()
    {
        // Подсветка строки берётся из токенов поверхности. Пустой шаблон без
        // своей подсветки означал бы, что IsHighlighted рисует системный синий
        // из SystemColors, и перебивка кистей осталась бы единственной защитой.
        var doc = XamlSource.Load("Controls.Combo.xaml");

        var item = doc.Descendants().Single(e =>
            e.Name.LocalName == "Style" &&
            e.Attribute("TargetType")!.Value.Contains("ComboBoxItem", StringComparison.Ordinal));

        var triggery = item.Descendants()
            .Where(e => e.Name.LocalName == "Trigger")
            .Select(e => e.Attribute("Property")!.Value)
            .ToList();

        triggery.Should().Contain("IsHighlighted", "иначе клавиатурная подсветка остаётся системной");
        triggery.Should().Contain("IsSelected", "выбранная строка обязана быть отличима без цвета");
    }

    [Fact]
    public void Zakrytaya_korobka_beret_svoy_stil_a_ne_knopochnyy()
    {
        // Закрытая коробка это ToggleButton, и неявный стиль кнопок из
        // Controls.Buttons.xaml применится к ней сам, если свой не назначен.
        // Получится кнопка с прописной разрядкой вместо поля со значением, и
        // сборка на это не скажет ни слова.
        var doc = XamlSource.Load("Controls.Combo.xaml");

        var toggle = doc.Descendants()
            .Single(e => e.Attribute(XamlSource.X + "Name")?.Value == "PART_Toggle");

        toggle.Attribute("Style")!.Value.Should().Contain("ComboToggleStyle");
    }

    [Fact]
    public void Sam_spisok_ne_beret_sistemnyy_vid()
    {
        // OverridesDefaultStyle=False оставляет части шаблона по умолчанию
        // доступными, и внутрь просачивается оформление Aero. Проверяется у
        // всех трёх стилей сразу: пропустить один это пропустить всё.
        var doc = XamlSource.Load("Controls.Combo.xaml");

        var stili = doc.Descendants()
            .Where(e => e.Name.LocalName == "Style" && e.Attribute("TargetType") is not null)
            .ToList();

        stili.Should().HaveCount(3);

        foreach (var stil in stili)
        {
            stil.Elements().Should().Contain(
                s => s.Name.LocalName == "Setter"
                     && s.Attribute("Property")!.Value == "OverridesDefaultStyle"
                     && s.Attribute("Value")!.Value == "True",
                "стиль {0} не отказался от вида по умолчанию",
                stil.Attribute(XamlSource.X + "Key")?.Value ?? stil.Attribute("TargetType")!.Value);
        }
    }
}
