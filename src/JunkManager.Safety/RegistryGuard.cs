using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;

namespace JunkManager.Safety;

/// <summary>
/// The registry guard, built the other way round from <see cref="SafetyGuard"/>:
/// there everything is allowed except the denied roots, here everything is
/// denied except the branches named below.
/// </summary>
/// <remarks>
/// <para>
/// The reason is in section 5.4 of the spec. A file system has a shape a person
/// recognises, so a denial list over it is readable. The registry has no such
/// shape, and a wrong key there does not fill a recycle bin, it stops the
/// machine from booting.
/// </para>
/// <para>
/// The allow list lives here and NOT in rules/sources/registry-branches.json.
/// That file says which of these branches a scan actually walks, so it can only
/// narrow the set. A guard whose allow list is an editable file is a guard
/// anybody widens with a text editor, and the same argument already keeps
/// ForbiddenRoots inside this assembly.
/// </para>
/// </remarks>
public static partial class RegistryGuard
{
    /// <summary>
    /// The only branches a scan may walk. No wildcards on purpose: a wildcard
    /// here is an allow list that nobody can read out loud.
    /// </summary>
    internal static IReadOnlyList<(RegistryHive Hive, string SubKey)> Allowed { get; } =
    [
        (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
        (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\App Paths"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),

        // Тестовая ветка из раздела 4 спеки. Без неё guard нечем проверить, не
        // трогая настоящий автозапуск, а проверка guard-а на живом автозапуске
        // это ровно тот класс тестов, который однажды сотрёт чужую строку.
        // В поставляемый файл веток она не входит, и это проверяется тестом.
        (RegistryHive.CurrentUser, @"Software\JunkManagerTests"),
    ];

    /// <summary>
    /// Refused for the branch itself and for everything under it, whatever the
    /// allow list says. Consulted BEFORE the allow list, so the refusal names
    /// the real problem and an allow-list entry added by mistake cannot open
    /// SYSTEM.
    /// </summary>
    internal static IReadOnlyList<(RegistryHive Hive, string SubKey)> DeniedForever { get; } =
    [
        (RegistryHive.LocalMachine, "SYSTEM"),
        (RegistryHive.LocalMachine, "SECURITY"),
        (RegistryHive.LocalMachine, "SAM"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Classes\CLSID"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Classes\Installer"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Policies"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies"),
        (RegistryHive.CurrentUser, @"Software\Classes\CLSID"),
        (RegistryHive.CurrentUser, @"Software\Policies"),
        (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies"),
    ];

    /// <summary>
    /// May a scan walk this branch. Says nothing about deleting it: that is what
    /// <see cref="TryVerifyKey"/> is for.
    /// </summary>
    /// <param name="normalized">
    /// Empty on refusal, always. The single spelling of the branch on success,
    /// so the address in the journal does not depend on how the rule file was
    /// typed.
    /// </param>
    public static bool TryVerifyBranch(
        RegistryHive hive,
        string subKey,
        RegistryView view,
        out string normalized,
        [NotNullWhen(false)] out string? reason)
    {
        normalized = string.Empty;

        if (!Enum.IsDefined(view))
        {
            reason = $"неизвестный вид реестра {(int)view}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(subKey))
        {
            reason = "ветка пустая, а пустая ветка это улей целиком";
            return false;
        }

        if (subKey.Any(char.IsControl))
        {
            reason = "в имени ветки есть управляющие символы";
            return false;
        }

        var chistaya = Normalize(subKey);

        if (chistaya.Length == 0)
        {
            reason = "ветка состоит из одних разделителей";
            return false;
        }

        foreach (var segment in chistaya.Split('\\'))
        {
            if (segment is "." or "..")
            {
                // Точечные переходы в реестре не значат ничего осмысленного, но
                // строку с ними легко пронести мимо сравнения имён.
                reason = "в имени ветки есть переходы вида . и ..";
                return false;
            }
        }

        foreach (var (deniedHive, deniedSubKey) in DeniedForever)
        {
            if (hive == deniedHive && IsAtOrUnder(chistaya, deniedSubKey))
            {
                reason =
                    $@"ветка {RegistryAddress.ShortHive(hive)}\{deniedSubKey} " +
                    "запрещена безусловно и навсегда";
                return false;
            }
        }

        foreach (var (allowedHive, allowedSubKey) in Allowed)
        {
            if (hive == allowedHive && IsAtOrUnder(chistaya, allowedSubKey))
            {
                // Написание берётся из СПИСКА, а не у вызывающего. Реестр
                // регистронезависим, поэтому одна и та же ветка приезжает из
                // файла правил в пяти написаниях, и адрес в журнале зависел бы
                // от того, как её набрали. Хвост ниже разрешённого корня
                // остаётся как есть: своего написания у него нет.
                var hvost = chistaya[allowedSubKey.Length..];
                normalized = allowedSubKey + hvost;
                reason = null;
                return true;
            }
        }

        reason =
            $@"ветка {RegistryAddress.ShortHive(hive)}\{chistaya} " +
            "не входит в список разрешённых к обходу";
        return false;
    }

    /// <summary>
    /// A pass into deleting one value. Everything
    /// <see cref="TryVerifyBranch"/> checks, plus the rule that a default value
    /// is never deletable.
    /// </summary>
    public static bool TryVerifyValue(
        RegistryHive hive,
        string subKey,
        string valueName,
        RegistryView view,
        out VerifiedRegistryValue verified,
        [NotNullWhen(false)] out string? reason)
    {
        verified = default;

        if (subKey is null)
        {
            reason = "ветка не задана";
            return false;
        }
        if (IsCautiousValue(hive, subKey, valueName, view))
        {
            verified = new VerifiedRegistryValue(hive, Normalize(subKey), valueName, view);
            reason = null;
            return true;
        }

        if (string.IsNullOrEmpty(valueName))
        {
            // У ключа значение по умолчанию одно, и оно и есть смысл ключа.
            // Строка журнала "удалено значение без имени" не объясняет ничего
            // ни через день, ни через год.
            reason = "значение по умолчанию не удаляется никогда";
            return false;
        }

        if (valueName.Any(char.IsControl))
        {
            reason = "в имени значения есть управляющие символы";
            return false;
        }

        if (!TryVerifyBranch(hive, subKey, view, out var chistaya, out reason))
        {
            return false;
        }

        verified = new VerifiedRegistryValue(hive, chistaya, valueName, view);
        reason = null;
        return true;
    }

    /// <summary>
    /// A pass into deleting a whole key. Stricter than a value pass by one rule:
    /// the key must lie strictly UNDER an allowed branch and never be one.
    /// "App Paths\foo.exe" is an entry; "App Paths" is the container Windows
    /// keeps, and a product that can delete the container is one bug away from
    /// deregistering every application at once.
    /// </summary>
    public static bool TryVerifyKey(
        RegistryHive hive,
        string subKey,
        RegistryView view,
        out VerifiedRegistryKey verified,
        [NotNullWhen(false)] out string? reason)
    {
        verified = default;

        if (!TryVerifyBranch(hive, subKey, view, out var chistaya, out reason))
        {
            return false;
        }

        foreach (var (allowedHive, allowedSubKey) in Allowed)
        {
            if (hive == allowedHive
                && chistaya.Equals(Normalize(allowedSubKey), StringComparison.OrdinalIgnoreCase))
            {
                reason =
                    $"{RegistryAddress.Format(hive, chistaya, null)} это разрешённый корень целиком, " +
                    "он не удаляется";
                return false;
            }
        }

        verified = new VerifiedRegistryKey(hive, chistaya, view);
        reason = null;
        return true;
    }

    /// <summary>
    /// One spelling per branch, whatever spelling the caller used: no leading,
    /// trailing or doubled separators, no padding around a segment.
    /// </summary>
    internal static string Normalize(string subKey) => string.Join(
        '\\',
        subKey.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// True when <paramref name="candidate"/> IS <paramref name="root"/> or lies
    /// under it. Segment by segment and never StartsWith: "Run" is a string
    /// prefix of "RunOnce", and RunOnce is a different key. The file system twin
    /// of this bug is documented on <see cref="SafetyGuard.IsAtOrUnder"/>.
    /// </summary>
    internal static bool IsAtOrUnder(string candidate, string root)
    {
        var c = Normalize(candidate);
        var r = Normalize(root);

        return c.Equals(r, StringComparison.OrdinalIgnoreCase)
            || c.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
