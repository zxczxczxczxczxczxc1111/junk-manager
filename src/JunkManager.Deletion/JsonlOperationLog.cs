using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JunkManager.Deletion;

/// <summary>
/// Один прочитанный журнал: шапка прогона и всё, что в нём записано.
/// </summary>
/// <param name="StartedUtc">
/// Из шапки файла. Null значит, что шапки не было: файл писала не эта
/// программа, или его обрезали. Придумывать время вместо null нельзя, иначе
/// чужой файл встанет в список как самая свежая очистка.
/// </param>
public sealed record OperationLogRun(
    string FilePath,
    DateTimeOffset? StartedUtc,
    IReadOnlyList<DeleteOutcome> Outcomes);

/// <summary>
/// The journal on disk, one line of JSON per outcome.
/// </summary>
/// <remarks>
/// <para>
/// The spec asks for a single <c>&lt;timestamp&gt;.json</c> per run. This writes
/// <c>&lt;timestamp&gt;.jsonl</c> instead, and the deviation is deliberate. A
/// single JSON document can only be finished at the end of the run, so the one
/// case the journal exists for, working out what a run removed after it died
/// halfway, is exactly the case where a whole-document journal is empty. Rewriting
/// the whole array after each entry would keep the extension and turn a
/// ten-thousand-file clean into ten thousand full rewrites.
/// </para>
/// <para>
/// One line is appended and flushed per outcome, so a killed process leaves
/// everything it had already done. Flush reaches the operating system, not the
/// platters: a process kill is survived, a power cut is not, and buying the
/// second would cost a synchronous write-through per file.
/// </para>
/// </remarks>
public sealed class JsonlOperationLog : IOperationLog, IDisposable
{
    private static readonly JsonSerializerOptions Options = new()
    {
        // One line per record, so never indented.
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly StreamWriter _pisatel;
    private readonly SemaphoreSlim _zamok = new(1, 1);
    private bool _zakryt;

    private JsonlOperationLog(string filePath, StreamWriter pisatel)
    {
        FilePath = filePath;
        _pisatel = pisatel;
    }

    /// <summary>The file this run is writing to.</summary>
    public string FilePath { get; }

    /// <summary>
    /// Where journals live when nobody says otherwise:
    /// <c>%LOCALAPPDATA%\JunkManager\history</c>.
    /// </summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JunkManager",
        "history");

    /// <summary>
    /// Opens a journal for one run. The name carries the start time, so two runs
    /// never share a file and the directory sorts chronologically on its own.
    /// </summary>
    /// <param name="directory">
    /// Null means <see cref="DefaultDirectory"/>. Tests pass their sandbox, which
    /// is the only reason this is a parameter at all.
    /// </param>
    public static JsonlOperationLog CreateForRun(string? directory = null, TimeProvider? time = null)
    {
        var katalog = directory ?? DefaultDirectory;
        Directory.CreateDirectory(katalog);

        var nachalo = (time ?? TimeProvider.System).GetUtcNow();
        var imya = nachalo.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".jsonl";
        var put = Path.Combine(katalog, imya);

        // FileShare.Read and not None: while a clean is running, somebody looking
        // at the journal to see what it has already taken is a legitimate thing
        // to do, and locking them out buys nothing.
        var potok = new FileStream(
            put, FileMode.CreateNew, FileAccess.Write, FileShare.Read);

        var pisatel = new StreamWriter(potok, new UTF8Encoding(false)) { AutoFlush = true };

        var zhurnal = new JsonlOperationLog(put, pisatel);
        zhurnal.ZapisatStroku(new ZagolovokZapusk(
            "run",
            nachalo,
            Environment.MachineName,
            Environment.UserName,
            Environment.ProcessId));

        return zhurnal;
    }

    /// <summary>
    /// Reads a journal that may still be open for writing.
    /// </summary>
    /// <remarks>
    /// Not a convenience wrapper. <c>File.ReadAllLines</c> asks for
    /// <c>FileShare.Read</c>, which conflicts with the writer's open handle, so
    /// the obvious way to read a live journal fails with "used by another
    /// process". Flushing every line would then buy nothing: the only reader who
    /// benefits is one looking while the run is still going. This opens with
    /// <c>FileShare.ReadWrite</c>, which is the share mode that makes the format
    /// deliver what it promises.
    /// </remarks>
    public static IReadOnlyList<string> ReadLines(string filePath)
    {
        using var potok = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var chtec = new StreamReader(potok, new UTF8Encoding(false));

        var stroki = new List<string>();
        while (chtec.ReadLine() is { } stroka)
        {
            stroki.Add(stroka);
        }

        return stroki;
    }

    /// <summary>
    /// Reads one journal file back: the run header and every outcome in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Лежит здесь, а не у экрана журнала, намеренно. Формат строки описан
    /// ровно один раз, в <c>ZagolovokZapusk</c> и <c>ZapisIskhoda</c> рядом.
    /// Читатель в другой сборке пришлось бы держать свою копию этих записей, и
    /// первая же правка формата разошлась бы молча: писатель пишет новое поле,
    /// читатель про него не знает.
    /// </para>
    /// <para>
    /// Первая строка это ШАПКА, а не исход. Разобранная как исход, она даёт
    /// строку без пути и со статусом, которого не бывает, и на экране журнала
    /// человек читает её как удаление.
    /// </para>
    /// </remarks>
    /// <exception cref="JsonException">
    /// Строка не разбирается. Бросается наружу: решать, показывать ли остальные
    /// файлы, это дело вызывающего, а не читателя одного файла.
    /// </exception>
    public static OperationLogRun ReadRun(string filePath)
    {
        DateTimeOffset? nachalo = null;
        var ishody = new List<DeleteOutcome>();

        foreach (var stroka in ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(stroka))
            {
                continue;
            }

            using var razobrannaya = JsonDocument.Parse(stroka);
            var koren = razobrannaya.RootElement;

            var vid = koren.TryGetProperty(nameof(ZapisIskhoda.Kind), out var kind)
                ? kind.GetString()
                : null;

            switch (vid)
            {
                case "run":
                    nachalo = JsonSerializer
                        .Deserialize<ZagolovokZapusk>(stroka, Options)?.StartedUtc;
                    break;

                case "outcome":
                    var zapis = JsonSerializer.Deserialize<ZapisIskhoda>(stroka, Options)
                        ?? throw new JsonException($"пустая запись в {filePath}");

                    ishody.Add(new DeleteOutcome(
                        zapis.Path, zapis.Status, zapis.BytesFreed,
                        zapis.Reason, zapis.HoldingProcess));
                    break;

                default:
                    // Неизвестный вид записи это НЕ ошибка чтения: формат
                    // растёт, а старая версия продукта обязана открывать
                    // журнал, написанный новой, показывая то, что понимает.
                    break;
            }
        }

        return new OperationLogRun(filePath, nachalo, ishody);
    }

    public async Task RecordAsync(DeleteOutcome outcome, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ObjectDisposedException.ThrowIf(_zakryt, this);

        // Ждём замок без токена намеренно. Отменённая запись в журнал означает
        // удалённый файл, о котором не осталось следа, а это ровно та дыра, ради
        // закрытия которой журнал и заведён.
        await _zamok.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            ZapisatStroku(new ZapisIskhoda(
                "outcome",
                DateTimeOffset.UtcNow,
                outcome.Path,
                outcome.Status,
                outcome.BytesFreed,
                outcome.Reason,
                outcome.HoldingProcess));
        }
        finally
        {
            _zamok.Release();
        }
    }

    private void ZapisatStroku<T>(T zapis) =>
        _pisatel.WriteLine(JsonSerializer.Serialize(zapis, Options));

    public void Dispose()
    {
        if (_zakryt)
        {
            return;
        }

        _zakryt = true;
        _pisatel.Dispose();
        _zamok.Dispose();
    }

    private sealed record ZagolovokZapusk(
        string Kind,
        DateTimeOffset StartedUtc,
        string Machine,
        string User,
        int ProcessId);

    private sealed record ZapisIskhoda(
        string Kind,
        DateTimeOffset AtUtc,
        string Path,
        DeleteStatus Status,
        long BytesFreed,
        string? Reason,
        string? HoldingProcess);
}
