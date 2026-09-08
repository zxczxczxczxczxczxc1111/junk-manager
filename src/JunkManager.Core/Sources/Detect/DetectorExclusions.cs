namespace JunkManager.Core.Sources.Detect;

/// <summary>
/// Directories a detector never enters and never proposes, and that the
/// leftover search never offers either. Hard-coded in the
/// detector rather than in a rule file on purpose: a rule file is editable, and
/// the day somebody edits ".ssh" out of it is the day the product proposes
/// deleting private keys because they had not been touched in a year.
/// </summary>
public static class DetectorExclusions
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ssh", ".gnupg", ".aws", ".azure", ".kube", ".docker", ".config",

        // Каталоги без точки, принадлежащие самой Windows. Попали сюда не из
        // осторожности вообще, а по замеру живой машины 05.09.2026: поиск
        // следов, научившись брать каталог без метки внутри, предложил ровно
        // эти два первыми.
        //
        // VirtualStore хранит то, что программа записала в Program Files и что
        // Windows перенаправила в профиль. Это данные человека, лежащие не там,
        // где он думает, и потому вдвойне невосстановимые.
        //
        // Package Cache хранит установочные пакеты, которыми Windows чинит и
        // удаляет уже установленные программы. После его очистки удаление
        // половины машины начинает просить исходный дистрибутив.
        //
        // Packages это данные приложений Store, Microsoft это общий каталог
        // компонентов. Ни один не след удалённой программы, и оба крупные
        // настолько, что попадают в кандидаты на любой машине.
        "VirtualStore", "Package Cache", "Packages", "Microsoft",
    };

    public static bool IsExcluded(string directoryName)
    {
        ArgumentNullException.ThrowIfNull(directoryName);

        return Names.Contains(directoryName.Trim());
    }
}
