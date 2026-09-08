using System.Text.Json;
using Xunit;
using FluentAssertions;
using JunkManager.Core;
using JunkManager.Core.Reporting;

namespace JunkManager.Tests.Reporting;

[Trait("Class", "Sandbox")]
public sealed class ScanJsonTests
{
    private static ScanResult Obrazec() => new(
        [
            new Finding("Кэш Chrome", @"C:\Users\kto-to\AppData\Local\Google\Chrome\Cache",
                1_500_000, RiskTier.Safe, "Страницы подгрузятся из сети.",
                FindingSource.Rule, "chrome-cache", 12),
            new Finding("Очередь обновлений", @"C:\Windows\SoftwareDistribution\Download",
                8_000_000, RiskTier.Risk, "Обновления скачаются заново.",
                FindingSource.VolumeCache, null, null),
        ],
        [new SkippedItem(@"C:\Windows\System32", "путь находится в запрещённом корне C:\\Windows")]);

    [Fact]
    public void Serialize_razbiraetsya_obratno()
    {
        var json = ScanJson.Serialize(Obrazec());

        var obratno = ScanJson.Deserialize(json);

        obratno.Should().NotBeNull();
        obratno!.Findings.Should().HaveCount(2);
        obratno.Skipped.Should().ContainSingle();
        obratno.TotalBytes.Should().Be(9_500_000);
        obratno.SafeBytes.Should().Be(1_500_000);
        obratno.RiskBytes.Should().Be(8_000_000);
    }

    [Fact]
    public void Serialize_u_kazhdoy_nahodki_est_neputoe_consequence()
    {
        // На это поле опирается интерфейс: находка без объяснения последствия
        // выводится как строка «удалить непонятно что».
        var json = ScanJson.Serialize(Obrazec());

        using var doc = JsonDocument.Parse(json);
        var findings = doc.RootElement.GetProperty("Findings").EnumerateArray().ToList();

        findings.Should().NotBeEmpty();
        findings.Should().OnlyContain(
            f => !string.IsNullOrWhiteSpace(f.GetProperty("Consequence").GetString()));
    }

    [Fact]
    public void Serialize_stupen_i_istochnik_vyhodyat_imenami_a_ne_ciframi()
    {
        // Цифра в отчёте это приглашение перепутать её со ступенью из старой
        // редакции, где их было три. Имя однозначно.
        var json = ScanJson.Serialize(Obrazec());

        json.Should().Contain("\"Safe\"").And.Contain("\"Risk\"");
        json.Should().Contain("\"Rule\"").And.Contain("\"VolumeCache\"");
        json.Should().NotContain("\"Tier\": 1");
    }

    [Fact]
    public void Serialize_russkiy_tekst_ostaetsya_chitaemym()
    {
        var json = ScanJson.Serialize(Obrazec());

        json.Should().Contain("Кэш Chrome");
        json.Should().NotContain("\\u041a", "экранирование кириллицы делает отчёт нечитаемым");
    }

    [Fact]
    public void Serialize_nahodki_idut_ot_krupnyh_k_melkim()
    {
        var json = ScanJson.Serialize(Obrazec());
        var obratno = ScanJson.Deserialize(json)!;

        obratno.Findings.Select(f => f.SizeBytes).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Serialize_otmena_vidna_v_otchete()
    {
        // Частичный результат обязан отличаться от полного даже в JSON: иначе
        // приёмка посчитает прерванный скан за честный ноль.
        var otmenennyy = Obrazec() with { Cancelled = true };

        ScanJson.Deserialize(ScanJson.Serialize(otmenennyy))!.Cancelled.Should().BeTrue();
    }

    [Fact]
    public void Serialize_propushchennoe_ne_teryaetsya()
    {
        // Скан, который молча выбрасывает то, что не смог прочитать, показывает
        // число поменьше и покрасивее и прячет ровно то, что важнее всего.
        var obratno = ScanJson.Deserialize(ScanJson.Serialize(Obrazec()))!;

        obratno.Skipped.Should().ContainSingle()
            .Which.Reason.Should().Contain("запрещённом корне");
    }
}
