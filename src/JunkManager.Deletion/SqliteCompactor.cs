using System.Buffers.Binary;
using JunkManager.Core.Scanning;

namespace JunkManager.Deletion;

public sealed record CompactEstimate(string Path, long CurrentBytes, long AfterVacuumBytes, string? Failure)
{
    public long ReclaimableBytes => Failure is null ? Math.Max(0, CurrentBytes - AfterVacuumBytes) : 0;
}

/// <summary>Reads only the header. Free pages are an estimate, not a promise of the final VACUUM size.</summary>
public static class SqliteCompactor
{
    public static CompactEstimate Measure(string databasePath)
    {
        if (!CleanupPathPolicy.TryVerify(databasePath, out var path, out var refusal))
            return new(databasePath, 0, 0, refusal);
        try
        {
            using var stream = new FileStream(path.Value, FileMode.Open, FileAccess.Read, FileShare.Read);
            var current = stream.Length;
            foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            {
                try
                {
                    _ = File.GetAttributes(path.Value + suffix);
                    return new(databasePath, current, current, "есть журнал или WAL; закройте приложение и дайте ему завершить транзакции");
                }
                catch (FileNotFoundException) { /* Absence is useful evidence, for a change. */ }
            }
            Span<byte> header = stackalloc byte[100];
            stream.ReadExactly(header);
            if (!header[..16].SequenceEqual("SQLite format 3\0"u8))
                return new(databasePath, current, current, "неподдерживаемый формат базы");
            var encodedPage = BinaryPrimitives.ReadUInt16BigEndian(header[16..]);
            var pageSize = encodedPage == 1 ? 65536 : encodedPage;
            var pageCount = BinaryPrimitives.ReadUInt32BigEndian(header[28..]);
            var freePages = BinaryPrimitives.ReadUInt32BigEndian(header[36..]);
            if (pageSize < 512 || (pageSize & (pageSize - 1)) != 0 || current % pageSize != 0
                || header[18] != 1 || header[19] != 1
                || BinaryPrimitives.ReadUInt32BigEndian(header[24..]) != BinaryPrimitives.ReadUInt32BigEndian(header[92..])
                || pageCount != current / pageSize || freePages >= pageCount)
                return new(databasePath, current, current, "заголовок или режим базы не позволяет надёжно оценить свободные страницы");
            return new(databasePath, current, current - (long)freePages * pageSize, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(databasePath, 0, 0, $"база не читается: {ex.Message}");
        }
    }
}
