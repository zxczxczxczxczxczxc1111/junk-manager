using FluentAssertions;
using JunkManager.Safety;
using Xunit;

namespace JunkManager.Tests.Safety;

[Trait("Class", "Sandbox")]
public sealed class SystemRestorePointTests
{
    private sealed class Schetchik
    {
        public int Vyzovov { get; set; }
    }

    /// <summary>Вызов, который обязан не случиться. Считает свои приглашения.</summary>
    private static SrSetRestorePoint NeDolzhenZvatsya(Schetchik schetchik) =>
        (string opisanie, out long nomer) =>
        {
            schetchik.Vyzovov++;
            nomer = 1;
            return 0;
        };

    [Fact]
    public void Accepted_call_without_visible_point_is_not_success()
    {
        // A successful receipt cannot restore Windows, much to the receipt's surprise.
        var result = SystemRestorePoint.Sozdat(true, "delayed",
            (string _, out long number) => { number = 0; return 0; }, _ => null);
        result.Ok.Should().BeFalse();
        result.Reason.Should().Contain("не подтверждена");
    }

    [Fact]
    public void Visible_point_supplies_the_real_sequence_after_async_creation()
    {
        // VSS may return zero while its worker is still sharpening a pencil.
        var result = SystemRestorePoint.Sozdat(true, "delayed",
            (string _, out long number) => { number = 0; return 0; }, _ => 314);
        result.Ok.Should().BeTrue();
        result.SequenceNumber.Should().Be(314);
    }

    [Fact]
    public void Bez_prav_administratora_tochka_ne_sozdaetsya_i_sistema_ne_trogaetsya()
    {
        // Не «позвать и получить отказ системы»: Win32 отвечает на это голым
        // отказом в доступе, а человек читает такое как «защита системы сломана».
        var schet = new Schetchik();

        var itog = SystemRestorePoint.Sozdat(
            elevated: false, "очистка реестра", NeDolzhenZvatsya(schet));

        itog.Ok.Should().BeFalse();
        itog.Reason.Should().Contain("администратор");
        schet.Vyzovov.Should().Be(0, "до системы дело дойти не должно вовсе");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Tochka_bez_opisaniya_ne_sozdaetsya(string? opisanie)
    {
        // Точка без описания неотличима в списке восстановления от чужой, и
        // человек выбирает вслепую ровно в тот момент, когда ему плохо.
        var schet = new Schetchik();

        var itog = SystemRestorePoint.Sozdat(
            elevated: true, opisanie!, NeDolzhenZvatsya(schet));

        itog.Ok.Should().BeFalse();
        itog.Reason.Should().Contain("описание");
        schet.Vyzovov.Should().Be(0);
    }

    [Fact]
    public void Dlinnoe_opisanie_obrezaetsya_a_ne_otklonyaetsya()
    {
        // Поле szDescription это ровно 256 широких символов вместе с
        // завершающим нулём. Строка длиннее едет в маршалинг как есть и портит
        // структуру, а падение выглядит как отказ системы.
        var dlinnoe = new string('я', 400);
        string? uvidennoe = null;

        var itog = SystemRestorePoint.Sozdat(elevated: true, dlinnoe,
            (string opisanie, out long nomer) =>
            {
                uvidennoe = opisanie;
                nomer = 42;
                return 0;
            });

        itog.Ok.Should().BeTrue();
        uvidennoe.Should().NotBeNull();
        uvidennoe!.Length.Should().Be(SystemRestorePoint.MaxOpisaniya);
    }

    [Fact]
    public void Udachnyy_vyzov_otdaet_nomer_tochki()
    {
        var itog = SystemRestorePoint.Sozdat(elevated: true, "очистка реестра",
            (string opisanie, out long nomer) =>
            {
                nomer = 77;
                return 0;
            });

        itog.Ok.Should().BeTrue();
        itog.SequenceNumber.Should().Be(77);
        itog.Reason.Should().BeNull();
    }

    [Fact]
    public void Nulevoy_nomer_pri_nulevom_statuse_eto_vsyo_ravno_tochka()
    {
        // Раньше здесь стояло обратное: «номер это единственное доказательство
        // того, что точку завели». Замер в госте 07.09.2026 это опроверг.
        //
        // Windows 11 сборки 26200 отвечает на SRSetRestorePointW nStatus=0 и
        // llSequenceNumber=0, а точку при этом СОЗДАЁТ и показывает её в списке
        // восстановления. Так отвечают оба типа, и MODIFY_SETTINGS, и
        // APPLICATION_INSTALL; размеры структур сверены Marshal.SizeOf, дело не
        // в маршалинге. То есть прежнее правило делало страховку вечно
        // провальной, и человеку писали «точка не создана» при существующей
        // точке. Соврать про отсутствие страховки хуже, чем не знать номера.
        var itog = SystemRestorePoint.Sozdat(elevated: true, "очистка реестра",
            (string opisanie, out long nomer) =>
            {
                nomer = 0;
                return 0;
            });

        itog.Ok.Should().BeTrue();
        itog.SequenceNumber.Should().Be(0);
        itog.Reason.Should().BeNull();
    }

    [Fact]
    public void Otklyuchennaya_zashchita_nazyvaetsya_slovami_a_ne_kodom()
    {
        // 1058 это ERROR_SERVICE_DISABLED. На свежей Windows 11 защита системы
        // выключена по умолчанию, и это самый частый отказ. «Ошибка 1058»
        // отправляет человека в поиск, «защита системы выключена» в настройки.
        var itog = SystemRestorePoint.Sozdat(elevated: true, "очистка реестра",
            (string opisanie, out long nomer) =>
            {
                nomer = 0;
                return 1058;
            });

        itog.Ok.Should().BeFalse();
        itog.Reason.Should().Contain("защита системы");
        itog.Reason.Should().Contain("1058", "код нужен для поиска, слова для человека");
    }

    [Fact]
    public void Neizvestnyy_kod_ne_teryaetsya()
    {
        var itog = SystemRestorePoint.Sozdat(elevated: true, "очистка реестра",
            (string opisanie, out long nomer) =>
            {
                nomer = 0;
                return 12345;
            });

        itog.Ok.Should().BeFalse();
        itog.Reason.Should().Contain("12345");
    }
}
