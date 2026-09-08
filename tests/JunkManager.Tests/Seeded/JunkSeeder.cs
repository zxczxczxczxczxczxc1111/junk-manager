using System.Globalization;
using JunkManager.Deletion;
using JunkManager.Safety;
using Microsoft.Win32;

namespace JunkManager.Tests.Seeded;

/// <summary>
/// Раскладывает мусор по описи и убирает за собой то, что не убирается само.
/// </summary>
/// <remarks>
/// Сеятель работает только под взведённым предохранителем: он кладёт файлы в
/// настоящий %TEMP%, настоящий профиль и настоящий реестр, а не в песочницу. На
/// машине человека этому места нет.
/// </remarks>
public sealed class JunkSeeder : IDisposable
{
    private readonly List<FileStream> _uderzhivaemye = [];
    private readonly List<string> _reestrovyeImena = [];
    private bool _zakryt;

    private const string VetkaAvtozapuska = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private JunkSeeder()
    {
    }

    private readonly List<string> _otkazy = [];

    /// <summary>Что посеять НЕ удалось. Пустой список это норма.</summary>
    public IReadOnlyList<string> Otkazy => _otkazy;

    /// <summary>
    /// Кладёт весь посев. Отказ на одной записи не останавливает остальные:
    /// приёмка обязана показать полную картину, а не первую строку, на которой
    /// споткнулась.
    /// </summary>
    public static JunkSeeder Plant(IReadOnlyList<SeedEntry> seeds)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        VmFuse.RequireArmed();

        var seyatel = new JunkSeeder();

        foreach (var zapis in seeds)
        {
            try
            {
                seyatel.Polozhit(zapis);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                          or NotSupportedException or InvalidOperationException)
            {
                seyatel._otkazy.Add($"{zapis.Id}: {ex.Message}");
            }
        }

        return seyatel;
    }

    private void Polozhit(SeedEntry zapis)
    {
        var put = zapis.ExpandedPath;

        switch (zapis.Kind)
        {
            case SeedKind.File:
                PolozhitFayl(put, zapis.Bytes, zapis.AgeDays);
                break;

            case SeedKind.CrashDump:
                PolozhitFayl(put, zapis.Bytes, 0);
                using (var dump = new FileStream(put, FileMode.Open, FileAccess.Write)) dump.Write("MDMP"u8);
                Sostarit(put, zapis.AgeDays);
                break;

            case SeedKind.Directory:
                PolozhitKatalog(put, zapis.Bytes, zapis.AgeDays);
                break;

            case SeedKind.LockedFile:
                PolozhitFayl(put, zapis.Bytes, zapis.AgeDays);
                // Поток остаётся открытым до конца прогона: в этом весь смысл
                // такого посева.
                _uderzhivaemye.Add(new FileStream(
                    put, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
                break;

            case SeedKind.RegistryValue:
                PolozhitZnachenieReestra(zapis);
                break;

            case SeedKind.RecycledFile:
                PolozhitVKorzinu(put, zapis.Bytes);
                break;

            case SeedKind.SqliteWithFreePages:
            case SeedKind.SqliteNoFreePages:
            case SeedKind.DriverPackage:
                throw new NotSupportedException(
                    $"вид посева {zapis.Kind} появляется вместе со своим источником находок. "
                    + "Сеять его раньше значит завести приманку, которую нечем найти по определению");

            default:
                throw new NotSupportedException($"неизвестный вид посева {zapis.Kind}");
        }
    }

    private static void PolozhitFayl(string put, long bayt, int vozrastDney)
    {
        var katalog = Path.GetDirectoryName(put)
            ?? throw new InvalidOperationException($"у пути {put} нет каталога");
        Directory.CreateDirectory(katalog);

        // Содержимое несжимаемое: на томе с сжатием NTFS нули дали бы файл
        // нулевого размера на диске, и замер занятого места врал бы. Источник
        // случайности криптографический не потому, что тут есть секрет, а
        // потому что CA5394 не различает «случайность для стойкости» и
        // «случайность ради энтропии», а глушить правило в коде, который
        // раскладывает файлы по чужой машине, не хочется вовсе.
        var soderzhimoe = System.Security.Cryptography.RandomNumberGenerator.GetBytes(checked((int)bayt));
        File.WriteAllBytes(put, soderzhimoe);

        Sostarit(put, vozrastDney);
    }

    private static void PolozhitKatalog(string put, long bayt, int vozrastDney)
    {
        Directory.CreateDirectory(put);

        // Три файла, а не один: правило имеет право сообщать про каталог целиком,
        // и посев из одного файла не отличил бы этот случай от посева файлом.
        var naFayl = Math.Max(1, bayt / 3);
        var ostatok = bayt - (naFayl * 2);

        for (var i = 0; i < 3; i++)
        {
            var razmer = i == 2 ? ostatok : naFayl;
            PolozhitFayl(
                Path.Combine(put, string.Create(CultureInfo.InvariantCulture, $"jm-seed-{i}.bin")),
                Math.Max(1, razmer),
                vozrastDney);
        }

        Sostarit(put, vozrastDney);
    }

    /// <summary>
    /// Ставит возраст И на запись, И на доступ, И на создание. Правило про
    /// «старше N дней» имеет право смотреть на любое из трёх, и посев не должен
    /// угадывать, на какое именно.
    /// </summary>
    private static void Sostarit(string put, int dney)
    {
        if (dney <= 0)
        {
            return;
        }

        var moment = DateTime.UtcNow.AddDays(-dney);

        if (Directory.Exists(put))
        {
            Directory.SetLastWriteTimeUtc(put, moment);
            Directory.SetLastAccessTimeUtc(put, moment);
            Directory.SetCreationTimeUtc(put, moment);
            return;
        }

        File.SetLastWriteTimeUtc(put, moment);
        File.SetLastAccessTimeUtc(put, moment);
        File.SetCreationTimeUtc(put, moment);
    }

    private void PolozhitZnachenieReestra(SeedEntry zapis)
    {
        using var vetka = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(VetkaAvtozapuska, writable: true)
            ?? throw new InvalidOperationException("не удалось открыть ветку автозапуска HKCU");

        var imya = "jm-seed-" + zapis.Id;
        vetka.SetValue(imya, zapis.ExpandedPath, RegistryValueKind.String);
        _reestrovyeImena.Add(imya);
    }

    private static void PolozhitVKorzinu(string put, long bayt)
    {
        PolozhitFayl(put, bayt, 0);

        if (!SafetyGuard.TryVerifyForDeletion(put, out var propusk, out var prichina))
        {
            throw new InvalidOperationException($"посеянный файл не прошёл guard: {prichina}");
        }

        var itog = RecycleBinDeleter.Delete(propusk);
        if (itog.Status != DeleteStatus.Deleted)
        {
            throw new InvalidOperationException($"не удалось положить в корзину: {itog.Reason}");
        }
    }

    /// <summary>
    /// Отпускает удерживаемые файлы и убирает значения реестра. Файлы посева
    /// НЕ убираются: гость откатывается на снимок, а уборка руками означала бы
    /// удаление того самого, что мы предлагаем найти продукту.
    /// </summary>
    public void Dispose()
    {
        if (_zakryt)
        {
            return;
        }

        _zakryt = true;

        foreach (var potok in _uderzhivaemye)
        {
            potok.Dispose();
        }

        _uderzhivaemye.Clear();

        if (_reestrovyeImena.Count == 0)
        {
            return;
        }

        using var vetka = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(VetkaAvtozapuska, writable: true);
        if (vetka is null)
        {
            return;
        }

        foreach (var imya in _reestrovyeImena)
        {
            vetka.DeleteValue(imya, throwOnMissingValue: false);
        }

        _reestrovyeImena.Clear();
    }
}
