using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;
using FluentAssertions;

namespace JunkManager.Tests.Architecture;

/// <summary>
/// Rules that hold because the metadata says so, not because a reviewer
/// remembered them. Every check reads the compiled assembly: a comment saying
/// "we never delete from here" and an assembly that does are indistinguishable
/// until somebody looks.
/// </summary>
[Trait("Class", "Sandbox")]
public sealed class ArchitectureTests
{
    /// <summary>
    /// Every API that changes the file system. Referencing one from Core or
    /// Safety means the layer boundary has already been crossed, whatever the
    /// code around it says.
    /// </summary>
    private static readonly (string Type, string Member)[] Menyayushchie =
    [
        ("System.IO.File", "Delete"),
        ("System.IO.File", "Create"),
        ("System.IO.File", "Move"),
        ("System.IO.File", "Copy"),
        ("System.IO.File", "WriteAllText"),
        ("System.IO.File", "WriteAllBytes"),
        ("System.IO.File", "WriteAllLines"),
        ("System.IO.File", "AppendAllText"),
        ("System.IO.File", "SetLastWriteTimeUtc"),
        ("System.IO.Directory", "Delete"),
        ("System.IO.Directory", "CreateDirectory"),
        ("System.IO.Directory", "Move"),
        ("System.IO.FileInfo", "Delete"),
        ("System.IO.FileInfo", "MoveTo"),
        ("System.IO.DirectoryInfo", "Delete"),
        ("System.IO.DirectoryInfo", "MoveTo"),
    ];

    private static string Sborka(string name) => Path.Combine(AppContext.BaseDirectory, name + ".dll");

    [Fact]
    public void Skaner_metadannyh_nahodit_udalenie_tam_gde_ono_tochno_est()
    {
        // Самопроверка инструмента, и она обязательна. Без неё все проверки
        // ниже зелены ровно до тех пор, пока сканер вообще ничего не находит,
        // и отличить «нарушений нет» от «сканер сломан» будет нечем.
        // В тестовой сборке File.Delete и DirectoryInfo.Delete точно есть:
        // ими убирается песочница.
        var nayden = Ssylki(Sborka("JunkManager.Tests"));

        nayden.Should().Contain("System.IO.File::Delete",
            "песочница удаляет свои файлы, и сканер обязан это видеть");
    }

    [Theory]
    [InlineData("JunkManager.Core")]
    [InlineData("JunkManager.Safety")]
    public void Core_i_Safety_ne_menyayut_faylovuyu_sistemu(string sborka)
    {
        var ssylki = Ssylki(Sborka(sborka));

        var narusheniya = Menyayushchie
            .Select(m => $"{m.Type}::{m.Member}")
            .Where(ssylki.Contains)
            .ToList();

        narusheniya.Should().BeEmpty(
            "{0} обязан только читать. Всё, что меняет диск, живёт в JunkManager.Deletion", sborka);
    }

    [Fact]
    public void Safety_ne_ssylaetsya_ni_na_Core_ni_na_Deletion()
    {
        // Направление ссылок односторонее. Обратная ссылка позволила бы Safety
        // спрашивать у правил, что ему разрешать, и предохранитель превратился
        // бы в вежливую просьбу.
        var ssylki = SsylkiNaSborki(Sborka("JunkManager.Safety"));

        ssylki.Should().NotContain("JunkManager.Core");
        ssylki.Should().NotContain("JunkManager.Deletion");
        ssylki.Should().NotContain("PresentationFramework", "в слое безопасности нет интерфейса");
    }

    [Theory]
    [InlineData("JunkManager.Core")]
    [InlineData("JunkManager.Safety")]
    [InlineData("JunkManager.Deletion")]
    public void V_yadre_net_ni_odnogo_okna(string sborka)
    {
        var ssylki = SsylkiNaSborki(Sborka(sborka));

        ssylki.Should().NotContain("PresentationFramework");
        ssylki.Should().NotContain("System.Windows.Forms");

        Ssylki(Sborka(sborka)).Should().NotContain(
            s => s.Contains("MessageBox", StringComparison.Ordinal),
            "ядро не разговаривает с человеком напрямую, оно возвращает результат");
    }

    [Fact]
    public void Sborki_proekta_na_meste()
    {
        // Тавтологии тут нет: если сборка не доехала до выходного каталога,
        // все проверки выше зелены и не проверяют ничего.
        foreach (var name in new[] { "JunkManager.Core", "JunkManager.Safety", "JunkManager.Deletion" })
        {
            File.Exists(Sborka(name)).Should().BeTrue("сборка {0} обязана быть рядом с тестами", name);
        }
    }

    /// <summary>
    /// Every member this assembly references from outside itself, as
    /// "Namespace.Type::Member". Reading the MemberReference table is enough
    /// here: the layer boundary is an assembly boundary, so a call anywhere in
    /// the assembly has to leave a reference in it.
    /// </summary>
    private static ImmutableHashSet<string> Ssylki(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        var result = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);

        foreach (var handle in md.MemberReferences)
        {
            var mr = md.GetMemberReference(handle);
            var member = md.GetString(mr.Name);
            var parent = ImyaRoditelya(md, mr.Parent);

            if (parent is not null)
            {
                result.Add($"{parent}::{member}");
            }
        }

        return result.ToImmutable();
    }

    private static string? ImyaRoditelya(MetadataReader md, EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeReference:
            {
                var tr = md.GetTypeReference((TypeReferenceHandle)parent);
                var ns = md.GetString(tr.Namespace);
                var name = md.GetString(tr.Name);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }

            case HandleKind.TypeDefinition:
            {
                var td = md.GetTypeDefinition((TypeDefinitionHandle)parent);
                var ns = md.GetString(td.Namespace);
                var name = md.GetString(td.Name);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }

            default:
                // TypeSpec (a generic instantiation) and the rest are not
                // interesting for these rules and are skipped deliberately.
                return null;
        }
    }

    private static ImmutableHashSet<string> SsylkiNaSborki(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        return [.. md.AssemblyReferences
            .Select(h => md.GetString(md.GetAssemblyReference(h).Name))];
    }

    /// <summary>
    /// Every API that changes the registry. Same reasoning as Menyayushchie:
    /// a comment saying "we only read here" and an assembly that writes are
    /// indistinguishable until somebody looks at the metadata.
    /// </summary>
    private static readonly string[] MenyayushchieReestr =
    [
        "Microsoft.Win32.RegistryKey::DeleteValue",
        "Microsoft.Win32.RegistryKey::DeleteSubKey",
        "Microsoft.Win32.RegistryKey::DeleteSubKeyTree",
        "Microsoft.Win32.RegistryKey::SetValue",
    ];

    [Fact]
    public void Skaner_vidit_udalenie_v_reestre_tam_gde_ono_tochno_est()
    {
        // Самопроверка, как и у файловой. Без неё правило ниже зелено ровно до
        // тех пор, пока сканер вообще ничего не находит.
        Ssylki(Sborka("JunkManager.Deletion"))
            .Should().Contain("Microsoft.Win32.RegistryKey::DeleteValue",
                "RegistryExecutor удаляет значения, и сканер обязан это видеть");
    }

    [Fact]
    public void V_code_behind_predstavleniy_net_nichego_krome_konstruktora()
    {
        // Разбор метаданных, а не ревью: правило, которое держится на внимании
        // читающего дифф, живёт ровно до первого срочного исправления.
        var sborka = typeof(JunkManager.App.Views.ShellWindow).Assembly;

        var predstavleniya = sborka.GetTypes()
            .Where(t => t.Namespace?.StartsWith("JunkManager.App.Views", StringComparison.Ordinal) == true)
            .Where(t => !t.Name.Contains("XamlGenerated", StringComparison.Ordinal))
            .ToList();

        predstavleniya.Should().NotBeEmpty("иначе проверка молча зелёная");

        foreach (var tip in predstavleniya)
        {
            var svoi = tip
                .GetMethods(System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Where(m => m.Name != "InitializeComponent")
                // Connect генерируется разбором XAML и приезжает явной
                // реализацией интерфейса, то есть с полным именем.
                .Where(m => !m.Name.EndsWith("Connect", StringComparison.Ordinal))
                // _CreateDelegate тоже пишет генератор разметки, а не человек:
                // он появляется от разбора XAML и подчёркивание в начале имени
                // как раз и означает «сгенерировано». Отсеивается по имени, а
                // не по атрибуту: атрибута на нём нет вовсе, проверено выводом
                // падения 05.09.2026.
                .Where(m => m.Name != "_CreateDelegate")
                .Select(m => m.Name)
                .ToList();

            svoi.Should().BeEmpty(
                "в code-behind {0} есть свой метод: логика живёт в модели представления", tip.Name);
        }
    }

    [Theory]
    [InlineData("JunkManager.Core")]
    [InlineData("JunkManager.Safety")]
    public void Core_i_Safety_ne_menyayut_reestr(string sborka)
    {
        var ssylki = Ssylki(Sborka(sborka));

        MenyayushchieReestr.Where(ssylki.Contains).Should().BeEmpty(
            "{0} обязан только читать реестр. Всё, что его меняет, живёт в RegistryExecutor", sborka);
    }
}
