using JunkManager.Deletion;
using JunkManager.Core.Apps;
using JunkManager.Safety;
using JunkManager.Tests.Infrastructure;
using Xunit;
using FluentAssertions;

namespace JunkManager.Tests.Deletion;

/// <summary>
/// Проверки, которые действительно кладут вещи в корзину.
/// </summary>
/// <remarks>
/// Класс LiveDestructive и предохранитель на каждом тесте: корзина одна на
/// пользователя, и тестовый мусор в ней это мусор в чужой корзине. В песочнице
/// на машине разработчика эти тесты не запускаются вовсе, поэтому весь код
/// вокруг обращения к оболочке проверяется отдельно, в RecycleBinTests.
///
/// Без этих тестов сам вызов IFileOperation не проверен ничем: все остальные
/// проверки выходят раньше, чем до него доходит дело, и набор был бы зелёным
/// при полностью неработающей корзине.
/// </remarks>
[Trait("Class", "LiveDestructive")]
[Collection(ObshcheeSostoyanieMashiny.Imya)]
public sealed class RecycleBinZhivyeTests : IDisposable
{
    private readonly SandboxFixture _pesochnica = new();

    public void Dispose() => _pesochnica.Dispose();

    private static VerifiedPath Propusk(string put)
    {
        SafetyGuard.TryVerifyForDeletion(put, out var propusk, out var prichina)
            .Should().BeTrue("тест обязан начинаться с настоящего пропуска, отказ: {0}", prichina);
        return propusk;
    }

    [Fact]
    public void Fayl_deystvitelno_popadaet_v_korzinu_a_ne_prosto_ischezaet()
    {
        VmFuse.RequireArmed();

        var fayl = _pesochnica.CreateFile("v-korzinu.dat", new string('k', 3000));
        var propusk = Propusk(fayl);
        var predmetovDo = KorzinaSchet.Predmetov(fayl);

        var itog = RecycleBinDeleter.Delete(propusk);

        itog.Status.Should().Be(DeleteStatus.Deleted, "причина отказа: {0}", itog.Reason);
        itog.BytesFreed.Should().Be(3000);
        File.Exists(fayl).Should().BeFalse("по старому пути файла быть не должно");
        KorzinaSchet.Predmetov(fayl).Should().Be(predmetovDo + 1,
            "предмет обязан оказаться В КОРЗИНЕ, иначе это обычное удаление под другим именем");
    }

    [Fact]
    public void Katalog_tozhe_popadaet_v_korzinu_odnim_predmetom()
    {
        VmFuse.RequireArmed();

        var kesh = _pesochnica.CreateDirectory("kesh-v-korzinu");
        File.WriteAllText(Path.Combine(kesh, "a.bin"), new string('a', 100));
        Directory.CreateDirectory(Path.Combine(kesh, "vnutri"));
        File.WriteAllText(Path.Combine(kesh, "vnutri", "b.bin"), new string('b', 200));

        var propusk = Propusk(kesh);
        var predmetovDo = KorzinaSchet.Predmetov(kesh);

        var itog = RecycleBinDeleter.Delete(propusk);

        itog.Status.Should().Be(DeleteStatus.Deleted, "причина отказа: {0}", itog.Reason);
        itog.BytesFreed.Should().Be(300, "размер снимается до операции, потом спрашивать уже не у кого");
        Directory.Exists(kesh).Should().BeFalse();
        KorzinaSchet.Predmetov(kesh).Should().Be(predmetovDo + 1,
            "каталог попадает в корзину одним предметом, а не по файлу на каждый");
    }

    [Fact]
    public async Task Rezhim_RecycleBin_u_udalitelya_vedet_v_korzinu_a_Permanent_mimo()
    {
        VmFuse.RequireArmed();

        var zhurnal = new SpisokZhurnala();
        var udalitel = new FileDeleter(zhurnal);

        var vKorzinu = _pesochnica.CreateFile("cherez-udalitel.dat", new string('x', 10));
        var mimo = _pesochnica.CreateFile("bezvozvratno.dat", new string('y', 10));

        var predmetovDo = KorzinaSchet.Predmetov(vKorzinu);

        var vKorzinuItog = await udalitel.DeleteAsync(
            Propusk(vKorzinu), DeleteMode.RecycleBin, TestContext.Current.CancellationToken);
        vKorzinuItog.Status.Should().Be(DeleteStatus.Deleted, "причина отказа: {0}", vKorzinuItog.Reason);
        var posleKorziny = KorzinaSchet.Predmetov(vKorzinu);

        var mimoItog = await udalitel.DeleteAsync(
            Propusk(mimo), DeleteMode.Permanent, TestContext.Current.CancellationToken);
        mimoItog.Status.Should().Be(DeleteStatus.Deleted, "причина отказа: {0}", mimoItog.Reason);

        posleKorziny.Should().Be(predmetovDo + 1, "режим RecycleBin обязан вести в корзину");
        KorzinaSchet.Predmetov(mimo).Should().Be(posleKorziny,
            "режим Permanent обязан идти мимо корзины, иначе он не безвозвратный");
        zhurnal.Zapisi.Should().HaveCount(2, "оба исхода обязаны попасть в журнал");
    }

    [Fact]
    public async Task Program_leftover_uses_the_recycle_bin_and_preserves_neighboring_documents()
    {
        VmFuse.RequireArmed();
        // Recycle means an actual bin item, not a comforting label on File.Delete.
        var root = _pesochnica.CreateDirectory("removed-program");
        var library = Path.GetFullPath(_pesochnica.CreateFile("removed-program/library.dll", "library"));
        var document = _pesochnica.CreateFile("removed-program/README.txt", "preserve");
        var program = new InstalledProgram("User:jm-leftover-recycle", "Removed", "Fixture", "1", root,
            null, null, InstallerKind.Unknown, ProgramScope.User, null);
        var search = LeftoverFinder.FindAfterRemoval(program,
            new(program.Id, UninstallOutcome.Removed, 0, 0, null) { RemovalConfirmed = true },
            new ProgramInventorySnapshot([], []), new([], [], false), TestContext.Current.CancellationToken);
        var candidate = search.Found.Should().ContainSingle(f => f.CanDelete).Which;
        candidate.Path.Should().Be(library);
        var before = KorzinaSchet.Predmetov(library);
        var journal = new SpisokZhurnala();

        var result = await new FileDeleter(journal).DeleteProgramLeftoverAsync(program, candidate,
            DeleteMode.RecycleBin, new AbsentInventory(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(DeleteStatus.Deleted, result.Reason);
        result.BytesFreed.Should().Be(7);
        journal.Zapisi.Should().ContainSingle().Which.BytesFreed.Should().Be(7);
        KorzinaSchet.Predmetov(library).Should().Be(before + 1);
        File.Exists(library).Should().BeFalse();
        File.ReadAllText(document).Should().Be("preserve");
    }

    [Fact]
    public void ScheduleOnReboot_stavit_put_v_ochered_sistemy()
    {
        VmFuse.RequireArmed();

        // Файл в песочнице: запись в очередь переживёт перезагрузку гостя, но
        // гость откатывается на снимок, а сама запись указывает на путь под
        // %TEMP%, которого к тому моменту не будет. Вреда нет, проверка есть.
        var fayl = _pesochnica.CreateFile("posle-perezagruzki.dat", "содержимое");
        var propusk = Propusk(fayl);

        RebootDeleteScheduler.IsElevated.Should().BeTrue(
            "гость работает под администратором, иначе проверять нечего");

        RebootDeleteScheduler.TryScheduleOnReboot(propusk, out var prichina)
            .Should().BeTrue("отказ: {0}", prichina);

        RebootDeleteScheduler.Scheduled().Should().Contain(
            p => string.Equals(p, propusk.Value, StringComparison.OrdinalIgnoreCase),
            "путь обязан оказаться в очереди системы, иначе планирование это пустое обещание");
    }

    private sealed class AbsentInventory : IProgramInventory
    {
        public Task<ProgramInventorySnapshot> ReadAsync(CancellationToken ct = default) => Task.FromResult(new ProgramInventorySnapshot([], []));
        public Task<ProgramPresenceResult> ProbeAsync(InstalledProgram program, CancellationToken ct = default) => Task.FromResult(new ProgramPresenceResult(ProgramPresence.Absent));
    }
}
