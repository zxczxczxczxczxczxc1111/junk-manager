using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace JunkManager.Core.Interop;

/// <summary>
/// SQLite as Windows already ships it. C:\Windows\System32\winsqlite3.dll is
/// version 3.43.2 on Windows 11 26100, verified 05.09.2026, which is well past
/// the 3.27 that introduced VACUUM INTO. Taking Microsoft.Data.Sqlite instead
/// would drag a native bundle into a single-file publish for functionality that
/// is already installed.
/// </summary>
/// <remarks>
/// <para>
/// There is no opener that creates a file, and that is deliberate rather than an
/// oversight: JunkManager.Core is the layer that only reads, and an opener with
/// SQLITE_OPEN_CREATE would be a way to write to disk that the architecture test
/// cannot see, because it goes out through a native call instead of System.IO.
/// A zero-length file is already a valid empty database, so the test that seeds
/// one creates the file itself and opens it here like any other.
/// </para>
/// <para>
/// DllImport rather than LibraryImport, same trade as everywhere else in this
/// solution: the generator emits unsafe code and demands AllowUnsafeBlocks
/// across the whole project.
/// </para>
/// <para>
/// Strings cross as UTF-8 byte buffers rather than as marshalled strings. The
/// sqlite3 C API takes UTF-8, and the DllImport default would hand it the system
/// ANSI code page, which turns any path with a Cyrillic letter in it into a
/// file-not-found nobody can explain. Doing the encoding here rather than
/// through MarshalAs also settles CA2101, which knows about Unicode marshalling
/// and treats everything else as a best-fit-mapping hazard.
/// </para>
/// </remarks>
public static class WinSqlite
{
    public const int Ok = 0;
    public const int Row = 100;
    public const int CantOpen = 14;

    private const int OpenReadWriteFlag = 0x0002;

    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open_v2", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int Open(byte[] filename, out nint db, int flags, nint vfs);

    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_close_v2", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CloseV2(nint db);

    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_exec", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int ExecRaw(
        nint db, byte[] sql, nint callback, nint arg, out nint errorMessage);

    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_prepare_v2", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int Prepare(
        nint db, byte[] sql, int bytes, out nint statement, nint tail);

    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_step", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int Step(nint statement);

    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_int64", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern long ColumnInt64(nint statement, int column);

    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_finalize", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int FinalizeStatement(nint statement);

    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_free", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void Free(nint memory);

    /// <summary>
    /// Opens an existing file for read and write, never creating one. Returns
    /// the raw sqlite code rather than throwing: "the database is held by
    /// another process" is an ordinary answer this product has to show a person,
    /// not an exceptional one.
    /// </summary>
    /// <remarks>
    /// The handle comes back even on failure and still has to be closed:
    /// sqlite3_open_v2 allocates the connection object before it can discover it
    /// cannot open the file, and dropping it there leaks for the life of the
    /// process.
    /// </remarks>
    public static int TryOpenExisting(string path, out nint db) =>
        Open(Utf8(path), out db, OpenReadWriteFlag, nint.Zero);

    public static int Close(nint db) => CloseV2(db);

    public static int Exec(nint db, string sql) => Exec(db, sql, out _);

    /// <param name="error">
    /// What sqlite said went wrong, null when nothing did. Worth the extra
    /// parameter: the numeric code alone turns "no such table" and "disk full"
    /// into the same shrug.
    /// </param>
    public static int Exec(nint db, string sql, out string? error)
    {
        var rc = ExecRaw(db, Utf8(sql), nint.Zero, nint.Zero, out var message);
        error = message == nint.Zero ? null : Marshal.PtrToStringUTF8(message);

        if (message != nint.Zero)
        {
            // sqlite3_exec allocates the message with sqlite3_malloc, so it has
            // to go back the same way or it leaks for the life of the process.
            Free(message);
        }

        return rc;
    }

    /// <summary>
    /// One number out of one query. Returns -1 when the statement did not
    /// prepare or produced no row, which every caller here treats as "could not
    /// be counted" rather than as a value.
    /// </summary>
    public static long ScalarInt64(nint db, string sql)
    {
        if (Prepare(db, Utf8(sql), -1, out var statement, nint.Zero) != Ok)
        {
            return -1;
        }

        try
        {
            return Step(statement) == Row ? ColumnInt64(statement, 0) : -1;
        }
        finally
        {
            _ = FinalizeStatement(statement);
        }
    }

    /// <summary>
    /// UTF-8 with the terminating zero the C API expects. The zero is added by
    /// hand because GetBytes does not add one, and a string handed to sqlite
    /// without it reads past the buffer.
    /// </summary>
    private static byte[] Utf8(string value)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(value) + 1];
        Encoding.UTF8.GetBytes(value, bytes);
        return bytes;
    }

    /// <summary>
    /// A sqlite return code as text, so callers building a refusal message do
    /// not each pick their own culture and quietly differ.
    /// </summary>
    public static string Opisat(int code) => code.ToString(CultureInfo.CurrentCulture);
}
