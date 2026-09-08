using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Interop;
using JunkManager.Deletion;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Sources;

[Trait("Class", "Sandbox")]
public sealed class SqliteCompactorTests : IClassFixture<SandboxFixture>
{
    private readonly SandboxFixture _sandbox;

    public SqliteCompactorTests(SandboxFixture sandbox) => _sandbox = sandbox;

    /// <summary>
    /// Builds a real database with real free pages, not a stub. The file is
    /// created by the test rather than by WinSqlite: a zero-length file IS an
    /// empty SQLite database, so the product never needs an opener that can
    /// create files, and JunkManager.Core stays a reader.
    /// </summary>
    /// <param name="strok">
    /// Row count, and it is a parameter rather than a constant because the
    /// source has a threshold: a database that frees less than a megabyte is
    /// deliberately not a finding, so a test about findings has to seed past it
    /// and a test about the threshold has to seed below it.
    /// </param>
    private string Poseyat(string name, bool withFreePages, int strok = 4000)
    {
        var path = _sandbox.CreateFile(name, string.Empty);
        var db = OtkryitIliUpast(path);

        try
        {
            WinSqlite.Exec(db, "CREATE TABLE t(id INTEGER PRIMARY KEY, a TEXT); BEGIN;").Should().Be(WinSqlite.Ok);

            for (var i = 0; i < strok; i++)
            {
                WinSqlite.Exec(db, $"INSERT INTO t(a) VALUES('{new string('x', 200)}')");
            }

            WinSqlite.Exec(db, "COMMIT;").Should().Be(WinSqlite.Ok);

            var sql = withFreePages ? "DELETE FROM t WHERE rowid % 2 = 0;" : "VACUUM;";
            WinSqlite.Exec(db, sql, out var error).Should().Be(WinSqlite.Ok, "{0}", error);
        }
        finally
        {
            WinSqlite.Close(db);
        }

        return path;
    }

    private static nint OtkryitIliUpast(string path)
    {
        var rc = WinSqlite.TryOpenExisting(path, out var db);

        if (rc != WinSqlite.Ok)
        {
            WinSqlite.Close(db);
            throw new InvalidOperationException($"sqlite3_open_v2 вернул {rc} для {path}");
        }

        return db;
    }

    private static long Strok(string path)
    {
        var db = OtkryitIliUpast(path);

        try
        {
            return WinSqlite.ScalarInt64(db, "SELECT COUNT(*) FROM t");
        }
        finally
        {
            WinSqlite.Close(db);
        }
    }

    [Fact]
    public void Measure_chitaet_ocenku_ne_menyaya_bazu_i_sosednie_fayly()
    {
        // An honest lower estimate beats a read-only scan that moonlights as a file shredder.
        var path = Poseyat("s-dyrami.db", withFreePages: true);
        var before = new FileInfo(path).Length;
        var bytes = File.ReadAllBytes(path);
        var modified = File.GetLastWriteTimeUtc(path);
        var neighbour = path + ".jm-vacuum-probe";
        File.WriteAllText(neighbour, "This is not your temporary file.");

        var estimate = SqliteCompactor.Measure(path);

        estimate.Failure.Should().BeNull();
        estimate.CurrentBytes.Should().Be(before);
        estimate.ReclaimableBytes.Should().BeGreaterThan(0);
        new FileInfo(path).Length.Should().Be(before, "замер не имеет права менять базу");
        File.ReadAllBytes(path).Should().Equal(bytes);
        File.GetLastWriteTimeUtc(path).Should().Be(modified);
        File.ReadAllText(neighbour).Should().Be("This is not your temporary file.");
        File.Delete(neighbour);
    }

    [Fact]
    public void Measure_plotnaya_baza_daet_nol_k_osvobozhdeniyu()
    {
        var path = Poseyat("plotnaya.db", withFreePages: false);

        var estimate = SqliteCompactor.Measure(path);

        estimate.Failure.Should().BeNull();
        estimate.ReclaimableBytes.Should().BeLessThan(8192, "сжимать тут нечего");
    }

    [Fact]
    public void Measure_zanyataya_baza_daet_prichinu_a_ne_isklyuchenie()
    {
        // Verified: sqlite3_open_v2 answers 14 (SQLITE_CANTOPEN) on a file held
        // with FileShare.None.
        var path = Poseyat("zanyataya.db", withFreePages: true);
        using var hold = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var estimate = SqliteCompactor.Measure(path);

        estimate.Failure.Should().NotBeNullOrWhiteSpace();
        estimate.ReclaimableBytes.Should().Be(0);
    }

    [Fact]
    public void Measure_ne_baza_daet_prichinu_a_ne_isklyuchenie()
    {
        var path = _sandbox.CreateFile("ne-baza.db", "это просто текст, а не sqlite");

        SqliteCompactor.Measure(path).Failure.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Measure_ubiraet_za_soboy_vremennuyu_kopiyu()
    {
        var path = Poseyat("uborka.db", withFreePages: true);

        SqliteCompactor.Measure(path);

        Directory.EnumerateFiles(_sandbox.Root, "*.jm-vacuum-probe")
            .Should().BeEmpty("временная копия обязана исчезнуть в любом исходе");
    }

    [Fact]
    public async Task ScanAsync_szhatie_trebuet_ruchnogo_vybora_i_pomecheno_ocenkoy()
    {
        var path = Poseyat("dlya-scana.db", withFreePages: true, strok: 16000);

        var result = await new SqliteCompactSource()
            .ScanAsync([path], null, CancellationToken.None);

        result.Findings.Should().ContainSingle();
        result.Findings[0].Source.Should().Be(FindingSource.Vacuum);
        result.Findings[0].Tier.Should().Be(RiskTier.Risk);
        result.Findings[0].IsSizeEstimate.Should().BeTrue();
        result.Findings[0].Consequence.Should().Contain("приблизительный");
        result.Findings[0].SizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ScanAsync_melkiy_vyigrysh_ne_skryvaetsya()
    {
        // The user removed size thresholds. The scanner does not get a secret vote.
        var path = Poseyat("melkiy-vyigrysh.db", withFreePages: true);

        var estimate = SqliteCompactor.Measure(path);
        estimate.ReclaimableBytes.Should().BeInRange(
            1, 1024 * 1024 - 1, "иначе тест проверяет не порог, а что-то другое");

        var result = await new SqliteCompactSource()
            .ScanAsync([path], null, CancellationToken.None);

        result.Findings.Should().ContainSingle().Which.SizeBytes.Should().Be(estimate.ReclaimableBytes);
        result.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_plotnaya_baza_ne_daet_nahodki()
    {
        var path = Poseyat("plotnaya-scan.db", withFreePages: false);

        var result = await new SqliteCompactSource()
            .ScanAsync([path], null, CancellationToken.None);

        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_zapreshchennyy_put_ne_dohodit_do_sqlite()
    {
        // Кандидаты приезжают снаружи, и «база» в System32 это не база, а повод
        // остановиться до открытия файла. Пропуск обязан назвать запрет guard-а,
        // а не то, что sqlite споткнулся уже внутри.
        var zapret = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "config.db");

        var result = await new SqliteCompactSource()
            .ScanAsync([zapret], null, CancellationToken.None);

        result.Findings.Should().BeEmpty();
        result.Skipped.Should().ContainSingle();
        result.Skipped[0].Reason.Should().Contain("запрещённом корне");
    }

    [Fact]
    public async Task ScanAsync_zanyataya_baza_nazyvaet_derzhashchiy_process()
    {
        // Раздел 6 спеки: пропуск с УКАЗАНИЕМ ПРОЦЕССА, а не просто пропуск.
        // "База занята" без имени превращает решаемую ситуацию в пожатие
        // плечами: человеку нечего закрыть.
        var path = Poseyat("kto-derzhit.db", withFreePages: true);
        using var hold = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await new SqliteCompactSource()
            .ScanAsync([path], null, CancellationToken.None);

        result.Findings.Should().BeEmpty();
        result.Skipped.Should().ContainSingle();
        result.Skipped[0].HoldingProcess.Should().NotBeNullOrWhiteSpace();
        result.Skipped[0].HoldingProcess.Should().Contain(
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.CurrentCulture),
            "держит файл сам прогон тестов, и PID обязан быть назван");
    }

    [Fact]
    public void TryCompact_szhimaet_bazu_i_ne_teryaet_ni_odnoy_stroki()
    {
        // Раздел 6 спеки: "сожмётся, ничего не пропадёт". Утверждение про размер
        // без утверждения про строки это ровно половина обещания, и притом не та.
        var path = Poseyat("szhatie.db", withFreePages: true);
        var strokDo = Strok(path);
        var razmerDo = new FileInfo(path).Length;

        SafetyGuard.TryVerify(path, out var verified, out var reason).Should().BeTrue("{0}", reason);

        SqliteVacuumExecutor.TryCompact(verified, out var freed, out var otkaz)
            .Should().BeTrue("{0}", otkaz);

        freed.Should().BeGreaterThan(0);
        new FileInfo(path).Length.Should().Be(razmerDo - freed);
        Strok(path).Should().Be(strokDo, "VACUUM не удаляет строк, он убирает дыры");
    }

    [Fact]
    public async Task CleanupRunner_compacts_database_instead_of_deleting_the_file()
    {
        var path = Poseyat("runner.db", withFreePages: true);
        var count = Strok(path);
        var log = new SpisokZhurnala();
        var finding = new Finding("Compact", path, 1, RiskTier.Risk, "Fixture", FindingSource.Vacuum);
        var report = await new CleanupRunner(new FileDeleter(log)).RunAsync([finding], DeleteMode.Permanent, null,
            TestContext.Current.CancellationToken);
        File.Exists(path).Should().BeTrue("compaction is not a creative spelling of deletion");
        Strok(path).Should().Be(count);
        report.DeletedCount.Should().Be(1);
        log.Zapisi.Should().ContainSingle();
    }

    [Fact]
    public void Implicit_rowids_are_not_rewritten()
    {
        var path = _sandbox.CreateFile("implicit.db", string.Empty);
        var db = OtkryitIliUpast(path);
        try { WinSqlite.Exec(db, "CREATE TABLE t(a TEXT); INSERT INTO t(rowid,a) VALUES(77,'keep');").Should().Be(WinSqlite.Ok); }
        finally { WinSqlite.Close(db); }
        var before = File.ReadAllBytes(path);
        SafetyGuard.TryVerify(path, out var verified, out _).Should().BeTrue();
        SqliteVacuumExecutor.TryCompact(verified, out _, out var refusal).Should().BeFalse();
        refusal.Should().Contain("идентификатор");
        File.ReadAllBytes(path).Should().Equal(before);
    }

    [Fact]
    public void Wal_sidecar_is_preserved_and_estimate_is_refused()
    {
        var path = Poseyat("wal-sidecar.db", true);
        File.WriteAllText(path + "-wal", "pending transaction");
        var before = File.ReadAllBytes(path);
        SqliteCompactor.Measure(path).Failure.Should().Contain("WAL");
        File.ReadAllText(path + "-wal").Should().Be("pending transaction");
        File.ReadAllBytes(path).Should().Equal(before);
    }

    [Fact]
    public void TryCompact_zanyatuyu_bazu_ne_trogaet_i_govorit_pochemu()
    {
        var path = Poseyat("zanyataya-szhatie.db", withFreePages: true);
        var razmerDo = new FileInfo(path).Length;

        SafetyGuard.TryVerify(path, out var verified, out _).Should().BeTrue();

        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            SqliteVacuumExecutor.TryCompact(verified, out var freed, out var otkaz)
                .Should().BeFalse();
            freed.Should().Be(0);
            otkaz.Should().NotBeNullOrWhiteSpace();
        }

        new FileInfo(path).Length.Should().Be(razmerDo, "отказ обязан оставить базу как была");
    }

    [Fact]
    public void TryCompact_otkazyvaet_kogda_put_uvel_v_druguyu_storonu()
    {
        // Подмена между замером и сжатием. Проверка тут, а не только в
        // PathResolverTests: сжатие переписывает файл целиком, и переписать по
        // ссылке чужую базу ничем не лучше, чем её удалить.
        var baza = Poseyat(Path.Combine("kesh-podmena", "baza.db"), withFreePages: true);
        var chuzhaya = Poseyat(Path.Combine("chuzhoy-kesh", "baza.db"), withFreePages: true);
        var razmerChuzhoy = new FileInfo(chuzhaya).Length;

        SafetyGuard.TryVerify(baza, out var verified, out var reason).Should().BeTrue("{0}", reason);

        Directory.Delete(Path.Combine(_sandbox.Root, "kesh-podmena"), recursive: true);
        _sandbox.CreateJunction("kesh-podmena", Path.Combine(_sandbox.Root, "chuzhoy-kesh"));

        SqliteVacuumExecutor.TryCompact(verified, out var freed, out var otkaz)
            .Should().BeFalse("путь через junction ведёт в чужой каталог");
        freed.Should().Be(0);
        otkaz.Should().Contain("ссылк");
        new FileInfo(chuzhaya).Length.Should().Be(razmerChuzhoy, "чужая база не тронута");
    }
}
