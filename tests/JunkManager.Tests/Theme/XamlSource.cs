using System.Xml.Linq;
using FluentAssertions;

namespace JunkManager.Tests.Theme;

/// <summary>
/// A hygiene test that finds no files passes. That is the worst possible
/// outcome, so the source set is verified before anything reads it.
/// </summary>
public static class XamlSource
{
    public static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    public static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string Root => Path.Combine(AppContext.BaseDirectory, "XamlSource");

    public static IReadOnlyList<string> Files()
    {
        Directory.Exists(Root).Should().BeTrue(
            "каталог {0} собирается копированием из src/JunkManager.App. " +
            "Если его нет, проверки разметки молча зелёные, а это хуже красных", Root);

        var files = Directory.GetFiles(Root, "*.xaml", SearchOption.AllDirectories);
        files.Should().NotBeEmpty("копирование разметки в вывод тестов сломано");
        return files;
    }

    public static XDocument Load(string fileName)
    {
        var match = Files().SingleOrDefault(f =>
            Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));

        match.Should().NotBeNull("файл {0} обязан быть скопирован в вывод тестов", fileName);
        return XDocument.Load(match!);
    }

    /// <summary>Every x:Key of a given element name, mapped to its text value.</summary>
    public static IReadOnlyDictionary<string, string> KeyedValues(XDocument doc, string elementName)
    {
        ArgumentNullException.ThrowIfNull(doc);

        return doc.Descendants()
            .Where(e => e.Name.LocalName == elementName && e.Attribute(X + "Key") is not null)
            .ToDictionary(
                e => e.Attribute(X + "Key")!.Value,
                e => e.Value.Trim(),
                StringComparer.Ordinal);
    }
}
