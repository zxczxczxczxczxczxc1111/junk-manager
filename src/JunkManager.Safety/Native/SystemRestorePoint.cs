using System.Globalization;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace JunkManager.Safety;

/// <param name="SequenceNumber">
/// The number Windows gave the point. Zero means there is no point, whatever the
/// status said: the status answers "was the call accepted", the number answers
/// "does the thing exist".
/// </param>
public sealed record RestorePointResult(bool Ok, long SequenceNumber, string? Reason);

/// <summary>
/// One call into srclient.dll. A delegate rather than a direct call so the
/// refusal branches can be driven on a machine where System Protection is off:
/// on a fresh Windows 11 it is off, so those branches would otherwise exist only
/// where nobody runs them.
/// </summary>
public delegate int SrSetRestorePoint(string opisanie, out long nomer);

/// <summary>
/// A system restore point before the first registry change of a session,
/// section 9.2 of the spec.
/// </summary>
/// <remarks>
/// <para>
/// SRSetRestorePointW from srclient.dll and not the WMI class the spec names.
/// System.Management is a separate NuGet package on .NET 10, and this solution
/// takes the platform raw everywhere else too: COM for the cleanup handlers,
/// winsqlite3.dll for SQLite. The WMI class calls this very export. Presence
/// checked by fact on build 26100 on 05.09.2026.
/// </para>
/// <para>
/// DllImport rather than LibraryImport, the same trade as in DiskSpace: the
/// source generator emits unsafe code for any signature carrying a string and
/// demands AllowUnsafeBlocks across the whole project. DefaultDllImportSearchPaths
/// is not decoration: without it the loader would take an srclient.dll lying
/// next to the executable (CA5392).
/// </para>
/// </remarks>
public static class SystemRestorePoint
{
    /// <summary>szDescription holds 256 wide chars INCLUDING the terminator.</summary>
    internal const int MaxOpisaniya = 255;

    private const uint BEGIN_SYSTEM_CHANGE = 100;
    private const uint END_SYSTEM_CHANGE = 101;
    private const uint MODIFY_SETTINGS = 12;

    private const int ERROR_SUCCESS = 0;
    private const int ERROR_ACCESS_DENIED = 5;
    private const int ERROR_SERVICE_DISABLED = 1058;

    /// <summary>
    /// Delegates to <see cref="Elevation.IsElevated"/> rather than asking again,
    /// for the reason written on RebootDeleteScheduler: two copies of this check
    /// drift, and the day they disagree one component refuses work another has
    /// already begun.
    /// </summary>
    public static bool IsElevated => Elevation.IsElevated;

    public static RestorePointResult Create(string opisanie)
    {
        // A unique description prevents yesterday's point from impersonating today's worker.
        var description = string.IsNullOrWhiteSpace(opisanie) ? opisanie
            : (opisanie.Length > 215 ? opisanie[..215] : opisanie) + " " + Guid.NewGuid().ToString("N");
        return Sozdat(IsElevated, description, Nativnyy, ConfirmVisible);
    }

    /// <summary>
    /// Почему точку не станем даже пытаться создавать. Null значит можно.
    /// </summary>
    /// <remarks>
    /// Чистая функция, как <c>RebootDeleteScheduler.Otkaz</c>: обе ветки отказа
    /// обязаны проверяться без прав администратора и без включённой защиты
    /// системы, то есть на машине, где план и пишется.
    /// </remarks>
    internal static string? Otkaz(bool elevated, string opisanie)
    {
        if (!elevated)
        {
            return "точка восстановления не создана: нужны права администратора";
        }

        if (string.IsNullOrWhiteSpace(opisanie))
        {
            return "точка восстановления не создана: у неё обязано быть описание, "
                + "иначе в списке восстановления её не отличить от чужой";
        }

        return null;
    }

    internal static RestorePointResult Sozdat(
        bool elevated, string? opisanie, SrSetRestorePoint vyzov,
        Func<string, long?>? confirmVisible = null)
    {
        ArgumentNullException.ThrowIfNull(vyzov);

        // Отсутствие строки и пустая строка означают тут ровно одно: точки без
        // описания не будет. Сведение их в одно ЗДЕСЬ снимает у компилятора
        // неизвестность дальше по коду, и потому не нужны ни подавление CA1062
        // у Create, ни оператор «доверься мне» перед обрезкой. Бросать на null
        // нельзя: пустое описание это штатный отказ словами, а не поломка.
        var celoe = opisanie ?? string.Empty;

        var otkaz = Otkaz(elevated, celoe);

        if (otkaz is not null)
        {
            return new RestorePointResult(false, 0, otkaz);
        }

        var korotkoe = celoe.Length > MaxOpisaniya ? celoe[..MaxOpisaniya] : celoe;

        int status;
        long nomer;

        try
        {
            status = vyzov(korotkoe, out nomer);
        }
        catch (DllNotFoundException ex)
        {
            // Урезанные образы идут без srclient.dll. Это не поломка продукта,
            // но и молчать нельзя: страховки не будет.
            return new RestorePointResult(
                false, 0, $"точка восстановления недоступна на этой системе: {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            return new RestorePointResult(
                false, 0, $"точка восстановления недоступна на этой системе: {ex.Message}");
        }

        if (status != ERROR_SUCCESS)
        {
            return new RestorePointResult(false, 0, Slovami(status));
        }

        // Accepted is not completed; VSS apparently enjoys publishing receipts late.
        if (confirmVisible is not null)
        {
            var visible = confirmVisible(korotkoe);
            if (visible is not > 0)
            {
                return new RestorePointResult(false, 0,
                    "создание точки принято системой, но точка не подтверждена в списке восстановления за отведённое время");
            }

            nomer = visible.Value;
        }

        return new RestorePointResult(true, nomer, null);
    }

    private static long? ConfirmVisible(string description)
    {
        // The command is fixed; descriptions travel as JSON data, never as shell syntax.
        const string script = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; "
            + "$ErrorActionPreference='Stop'; $d=[Console]::In.ReadLine() | ConvertFrom-Json; "
            + "$end=[DateTime]::UtcNow.AddSeconds(30); do { "
            + "$p=Get-CimInstance -Namespace root/default -ClassName SystemRestore | "
            + "Where-Object { $_.Description -ceq $d } | Sort-Object SequenceNumber -Descending | Select-Object -First 1; "
            + "if ($p) { [Console]::WriteLine($p.SequenceNumber); exit 0 }; Start-Sleep -Milliseconds 500 "
            + "} while ([DateTime]::UtcNow -lt $end); exit 2";
        var start = new ProcessStartInfo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            process.StandardInput.WriteLine(JsonSerializer.Serialize(description));
            process.StandardInput.Close();
            if (!process.WaitForExit(40_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return null;
            }

            _ = errors.GetAwaiter().GetResult();
            return process.ExitCode == 0
                && long.TryParse(output.GetAwaiter().GetResult().Trim(), CultureInfo.InvariantCulture, out var number)
                ? number : null;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Slovami(int status) => status switch
    {
        ERROR_SERVICE_DISABLED => string.Create(
            CultureInfo.InvariantCulture,
            $"точка восстановления не создана: защита системы выключена на этом томе (код {status})"),
        ERROR_ACCESS_DENIED => string.Create(
            CultureInfo.InvariantCulture,
            $"точка восстановления не создана: отказано в доступе (код {status})"),
        _ => string.Create(
            CultureInfo.InvariantCulture,
            $"точка восстановления не создана: система ответила кодом {status}"),
    };

    /// <summary>
    /// BEGIN then END with the same sequence number. A point opened and never
    /// closed stays half made, and the pairing costs one extra call.
    /// </summary>
    private static int Nativnyy(string opisanie, out long nomer)
    {
        nomer = 0;

        var svedeniya = new RESTOREPOINTINFOW
        {
            dwEventType = BEGIN_SYSTEM_CHANGE,
            dwRestorePtType = MODIFY_SETTINGS,
            llSequenceNumber = 0,
            szDescription = opisanie,
        };

        if (!SRSetRestorePointW(ref svedeniya, out var sostoyanie))
        {
            // Функция вернула ложь и не заполнила статус ничем осмысленным.
            // Отдаём последнюю ошибку Win32, иначе причина теряется совсем.
            var poslednyaya = Marshal.GetLastWin32Error();
            return poslednyaya == 0 ? -1 : poslednyaya;
        }

        if (sostoyanie.nStatus != ERROR_SUCCESS)
        {
            return (int)sostoyanie.nStatus;
        }

        nomer = sostoyanie.llSequenceNumber;

        var zakrytie = new RESTOREPOINTINFOW
        {
            dwEventType = END_SYSTEM_CHANGE,
            dwRestorePtType = MODIFY_SETTINGS,
            llSequenceNumber = sostoyanie.llSequenceNumber,
            szDescription = opisanie,
        };

        // Итог закрытия НЕ отменяет уже созданную точку: она в списке
        // восстановления с момента BEGIN. Проглатывать его всё равно нельзя,
        // поэтому неудача закрытия становится кодом отказа. Номер к этому
        // моменту уже присвоен и уедет наружу вместе с причиной.
        return SRSetRestorePointW(ref zakrytie, out var itogZakrytiya)
            ? (int)itogZakrytiya.nStatus
            : Marshal.GetLastWin32Error();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RESTOREPOINTINFOW
    {
        public uint dwEventType;
        public uint dwRestorePtType;
        public long llSequenceNumber;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STATEMGRSTATUS
    {
        public uint nStatus;
        public long llSequenceNumber;
    }

    [DllImport("srclient.dll", EntryPoint = "SRSetRestorePointW", SetLastError = true,
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SRSetRestorePointW(
        ref RESTOREPOINTINFOW svedeniya, out STATEMGRSTATUS sostoyanie);
}
