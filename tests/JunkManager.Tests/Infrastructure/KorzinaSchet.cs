using System.Runtime.InteropServices;

namespace JunkManager.Tests.Infrastructure;

/// <summary>
/// Сколько предметов лежит в корзине тома. Существует ради одной проверки,
/// которую иначе сделать нечем: «безвозвратное удаление действительно прошло
/// мимо корзины». Без неё тест на безвозвратность проверяет только то, что
/// файла нет по старому пути, а это верно и для отправки в корзину.
/// </summary>
internal static class KorzinaSchet
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    /// <summary>
    /// Предметов в корзине тома, которому принадлежит путь. Ненулевой HRESULT
    /// это исключение, а не ноль: тихий ноль превратил бы сломанную проверку в
    /// вечно зелёную.
    /// </summary>
    public static long Predmetov(string putNaTome)
    {
        var koren = Path.GetPathRoot(Path.GetFullPath(putNaTome))
            ?? throw new InvalidOperationException($"у пути {putNaTome} нет корня тома");

        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        var hr = SHQueryRecycleBin(koren, ref info);

        if (hr != 0)
        {
            throw new InvalidOperationException(
                $"SHQueryRecycleBin для '{koren}' вернул HRESULT 0x{hr:X8}");
        }

        return info.i64NumItems;
    }
}
