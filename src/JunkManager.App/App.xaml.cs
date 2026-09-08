using System.IO;
using System.Windows;
using System.Windows.Interop;
using JunkManager.App.Services;
using JunkManager.App.Startup;
using JunkManager.App.ViewModels;
using JunkManager.App.Views;
using JunkManager.Core.Rules;
using JunkManager.Core.Scanning;
using JunkManager.Safety;

namespace JunkManager.App;

internal partial class App : Application
{
    /// <summary>
    /// Why the handed profile did not check out, or null when there was nothing
    /// to check or the check passed.
    /// </summary>
    /// <remarks>
    /// Read by the shell, which refuses to scan while it is set. Cleaning
    /// somebody else's profile is not recoverable, so this is not a warning the
    /// person can click past: it is a stop. The screen that shows it arrives
    /// with the shell in task 7; until then the value is set and nothing scans
    /// anyway, because nothing scans yet.
    /// </remarks>
    internal static string? OtkazProfilya { get; private set; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability", "CA2000:Dispose objects before losing scope",
        Justification =
            "Модели обзора и файлов живут столько же, сколько окно, и закрываются его " +
            "событием Closed чуть ниже. Анализатор события не видит. Обернуть в using " +
            "нельзя: using закрыл бы их сразу после Show, то есть до первого нажатия.")]
    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        // Настройки читаются ПЕРВЫМИ и синхронно: «Запускать с правами
        // администратора» решает, будет ли перезапуск, а перезапуск обязан
        // случиться до окна. Ждать тут нечего, кроме одного маленького файла.
        var nastroykiSluzhba = new SettingsService();
        var nastroyki = ProchitatNastroyki(nastroykiSluzhba);

        // Elevation happens before a window exists. A relaunch after the window
        // is up loses whatever the person had already selected.
        //
        // Ключ --no-elevate перебивает настройку, и разбирает ключ сам Decide,
        // поэтому проверять его здесь ещё раз значило бы держать две копии
        // одного правила.
        var decision = ElevationBootstrap.Decide(
            wantElevation: nastroyki.ElevateOnStart,
            ElevationBootstrap.IsElevated,
            e.Args,
            ElevationBootstrap.Relaunch);

        if (decision == ElevationDecision.Relaunched)
        {
            // The elevated copy is up. This process must not draw a window: two
            // Junk Managers over one disk is not twice the work, it is a race.
            Shutdown(0);
            return;
        }

        // A handed identity means we ARE the elevated copy. The check is what
        // keeps the product from cleaning the profile of whoever typed the
        // administrator password instead of the person at the keyboard.
        if (Elevation.TryReadHandedIdentity(e.Args, out _, out _, out _)
            && !Elevation.TryApplyOriginalProfile(e.Args, out var prichina))
        {
            OtkazProfilya = prichina;
        }

        // Reduced motion is applied to the tokens once, before any window can
        // start a storyboard against the old values.
        MotionPolicy.Apply(Resources, MotionPolicy.SystemAllowsAnimation);

        // Сканер создаётся один раз и живёт столько же, сколько окно: каталог
        // правил он читает в конструкторе, и пересоздание на каждое нажатие
        // означало бы перечитывание девяти файлов ради кнопки.
        var skaner = SobratSkaner(ScanPlan.PoUmolchaniyu with
        {
            Obnaruzhiteli = nastroyki.DetectorsEnabled,
            PoiskPoObraztsu = nastroyki.DeepScan,
            Programmy = nastroyki.LeftoverSearch,
        });

        // Итог прохода один на все экраны. Обзор кладёт, «Файлы» читают.
        var proshloe = new PosledniyProhod();
        var obzor = skaner is null ? null : new OverviewViewModel(skaner, proshloe);

        // Служба очистки настоящая и заводится здесь, а не внутри модели:
        // модель, которая сама себе создаёт то, что трогает диск, не
        // проверяется без диска вовсе.
        var ochistka = new CleanupService { Mode = nastroyki.Mode };
        var fayly = new FilesViewModel(proshloe, ochistka, new LockedFileService());

        var zhurnalSluzhba = new HistoryService();
        var zhurnal = new HistoryViewModel(zhurnalSluzhba);

        // Модуль реестра. Файл веток читается сразу: сломанный файл обязан
        // сказать о себе при запуске, а не при первом нажатии кнопки. Отказ
        // подменяется SlomannyyReestr, который БРОСАЕТ, а не отдаёт пустой
        // список: пустой список означает «посмотрели, ничего нет».
        var registryCleanup = new RegistryCleanupService(nastroykiSluzhba);
        var reestr = new RegistryViewModel(
            SobratReestr() ?? (IRegistryScanService)new SlomannyyReestr(),
            registryCleanup,
            new RegistryBackupsService(),
            ElevationBootstrap.IsElevated);

        // Сохранённое применяется к УЖЕ ИДУЩЕМУ процессу. Иначе переключатель
        // вступает в силу со следующего запуска, то есть выглядит сломанным
        // ровно в тот момент, когда его нажали.
        var nastroykiEkran = new SettingsViewModel(nastroykiSluzhba, primenennye =>
        {
            ochistka.Mode = primenennye.Mode;

            if (skaner is not null)
            {
                skaner.Plan = skaner.Plan with
                {
                    Obnaruzhiteli = primenennye.DetectorsEnabled,
                    PoiskPoObraztsu = primenennye.DeepScan,
                    Programmy = primenennye.LeftoverSearch,
                };
            }
        });

        var programmy = new ProgramsViewModel(new ProgramsService(nastroykiSluzhba, registryCleanup: registryCleanup), ElevationBootstrap.IsElevated);
        var okno = new ShellWindow
        {
            DataContext = new ShellViewModel(
                ElevationBootstrap.IsElevated,
                obzor: obzor,
                fayly: fayly,
                zhurnal: zhurnal,
                reestr: reestr,
                nastroyki: nastroykiEkran,
                programmy: programmy),
        };

        // Начальное состояние ставится ДО показа окна: иначе первый кадр
        // приходит с пустым состоянием по умолчанию, и на долю секунды видно
        // чужой текст.
        obzor?.PokazatNachalo();

        // Журнал читается сразу, не дожидаясь перехода в раздел: чтение
        // каталога это десятки миллисекунд, а экран, который начинает
        // думать в момент открытия, читается как медленный.
        _ = UbratStaroeIProchitat(zhurnalSluzhba, zhurnal, nastroyki.HistoryRetentionDays);

        // Реестр читается сразу, как журнал: три ветки и проба каждой цели на
        // диске это десятки миллисекунд, ничего не изменяется, а раздел без
        // этого вызова показывал бы пустоту без объяснения.
        _ = reestr.ProveritAsync(CancellationToken.None);

        _ = nastroykiEkran.ZagruzitAsync(ElevationBootstrap.IsElevated, CancellationToken.None);

        okno.Closed += (_, _) =>
        {
            obzor?.Dispose();

            // Закрытое окно обязано остановить идущую очистку. Иначе
            // удаление продолжается в процессе, у которого больше нет ни
            // одного окна, то есть человек его не видит и не остановит.
            fayly.Dispose();

            // Реестр по той же причине: с задачи 6 у него своя идущая очистка
            // и свой источник отмены.
            reestr.Dispose();
            programmy.Dispose();
        };

        // Mica needs a handle, and a handle exists only after SourceInitialized.
        okno.SourceInitialized += (_, _) =>
            MicaBackdrop.TryApply(new WindowInteropHelper(okno).Handle);

        MainWindow = okno;
        okno.Show();
    }

    /// <summary>
    /// Настройки для решения о повышении. Сломанный файл не мешает запуску.
    /// </summary>
    /// <remarks>
    /// Тихо взять значения по умолчанию здесь можно, а на экране настроек
    /// нельзя: окно обязано подняться при любом состоянии файла, а разговор о
    /// том, что файл сломан, ведёт экран, который умеет показать причину.
    /// </remarks>
    private static AppSettings ProchitatNastroyki(SettingsService sluzhba)
    {
        try
        {
            return sluzhba.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (System.Text.Json.JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Уборка старых записей, потом чтение. Именно в этом порядке: иначе экран
    /// показывает записи, которые исчезают через долю секунды.
    /// </summary>
    private static async Task UbratStaroeIProchitat(
        HistoryService sluzhba, HistoryViewModel ekran, int dney)
    {
        try
        {
            await sluzhba.UbratStaryeAsync(dney, CancellationToken.None).ConfigureAwait(true);
        }
        catch (IOException)
        {
            // Каталог занят целиком. Журнал от этого не портится, читаем как есть.
        }
        catch (UnauthorizedAccessException)
        {
        }

        await ekran.ZagruzitAsync(CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Сканер, либо null, если каталог правил сломан или пропал.
    /// </summary>
    /// <remarks>
    /// Null тут это ЧЕСТНЫЙ ответ, а не тишина: раздел «Обзор» останется с
    /// заглушкой вместо экрана и скажет, что читать правила нечем. Раньше здесь
    /// назывался второй признак, «правил 0» в подвале рельса, но подвал убран
    /// 06.09.2026, и ссылаться на него значило бы обещать сигнал, которого нет.
    /// Ронять
    /// окно нельзя: без него человеку нечем ни посмотреть на проблему, ни
    /// открыть настройки.
    /// </remarks>
    private static ScanService? SobratSkaner(ScanPlan plan)
    {
        try
        {
            return new ScanService(plan);
        }
        catch (RuleFormatException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Служба реестра, либо null, если файл веток сломан или не доехал.
    /// </summary>
    /// <remarks>
    /// Те же три ветки отказа, что у <see cref="SobratSkaner"/>, и по той же
    /// причине: окно обязано подняться и сказать словами, что проверять нечем.
    /// Окно, не поднявшееся из-за файла правил, не говорит ничего.
    /// </remarks>
    private static RegistryScanService? SobratReestr()
    {
        try
        {
            return new RegistryScanService();
        }
        catch (RuleFormatException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
