using Microsoft.Win32;

namespace JunkManager.Tests.Infrastructure;

/// <summary>
/// Своя ветка в HKCU на один набор тестов. Ветка взята из раздела 4 спеки, она
/// же единственная тестовая ветка, разрешённая RegistryGuard.
/// </summary>
/// <remarks>
/// Убирается на Dispose, то есть и при падении теста тоже: xunit зовёт Dispose
/// независимо от исхода. Переживает это не всё: убитый процесс оставит подключ
/// на месте. Это осознанно и не лечится дешевле: мусор остаётся строго внутри
/// HKCU\Software\JunkManagerTests, то есть в ветке, которую спека отвела ровно
/// под это, а следующий прогон работает в своём подключе с новым GUID.
/// </remarks>
public sealed class RegistrySandbox : IDisposable
{
    public const string Koren = @"Software\JunkManagerTests";

    private bool _zakryt;

    public RegistrySandbox()
    {
        SubKey = Koren + "\\" + Guid.NewGuid().ToString("N");

        // Microsoft.Win32.Registry.CurrentUser вызывается ЗДЕСЬ, а не в тестах: пространство
        // имён JunkManager.Tests.Registry перекрывает простое имя типа
        // Microsoft.Win32.Registry, и в тестовых файлах эта строка не
        // компилируется вовсе.
        using var klyuch = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(SubKey, writable: true)
            ?? throw new InvalidOperationException(
                $"не удалось создать тестовую ветку HKCU\\{SubKey}");
    }

    /// <summary>Ветка без улья: то, что кладётся в RegistryScanRule.</summary>
    public string SubKey { get; }

    public void SetString(string name, string value) =>
        Zapisat(name, value, RegistryValueKind.String);

    public void SetExpandString(string name, string value) =>
        Zapisat(name, value, RegistryValueKind.ExpandString);

    public void SetDword(string name, int value) =>
        Zapisat(name, value, RegistryValueKind.DWord);

    /// <summary>Подключ со значением по умолчанию: форма записи App Paths.</summary>
    public void SetSubKeyDefault(string subKeyName, string value)
    {
        using var klyuch = Microsoft.Win32.Registry.CurrentUser
            .CreateSubKey(SubKey + "\\" + subKeyName, writable: true)
            ?? throw new InvalidOperationException($"не удалось создать подключ {subKeyName}");

        klyuch.SetValue(null, value, RegistryValueKind.String);
    }

    /// <summary>
    /// Кладёт значение по умолчанию в саму ветку. Отдельным методом, потому что
    /// это ЛОВУШКА для сканера, а не обычная запись.
    /// </summary>
    public void SetOwnDefault(string value)
    {
        using var klyuch = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SubKey, writable: true)
            ?? throw new InvalidOperationException($"тестовая ветка HKCU\\{SubKey} исчезла");

        klyuch.SetValue(null, value, RegistryValueKind.String);
    }

    private void Zapisat(string name, object value, RegistryValueKind kind)
    {
        using var klyuch = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SubKey, writable: true)
            ?? throw new InvalidOperationException($"тестовая ветка HKCU\\{SubKey} исчезла");

        klyuch.SetValue(name, value, kind);
    }

    public void Dispose()
    {
        if (_zakryt)
        {
            return;
        }

        _zakryt = true;

        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(SubKey, throwOnMissingSubKey: false);
        }
        catch (System.Security.SecurityException ex)
        {
            // Громко: оставленная ветка это следующий прогон, который видит
            // чужие значения и объясняет их своим кодом.
            Console.Error.WriteLine($"тестовая ветка не убралась: HKCU\\{SubKey}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"тестовая ветка не убралась: HKCU\\{SubKey}: {ex.Message}");
        }
    }
}
