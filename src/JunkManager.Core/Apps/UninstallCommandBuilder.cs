using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using JunkManager.Core.Sources.Platform;

namespace JunkManager.Core.Apps;

/// <param name="Quiet">
/// Whether this runs without a window. False is not a refusal, it is a fact the
/// runner has to act on: an unattended installer waiting for a click is a
/// process that never ends.
/// </param>
public sealed record UninstallCommand(
    string Executable, IReadOnlyList<string> Arguments, bool Quiet)
{
    public string? StandardInput { get; init; }
}

[SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification = "Тот же довод, что и у DeleteStatus: незаполненный исход не должен читаться " +
        "как Removed, иначе поиск следов запустится по программе, которая осталась стоять.")]
public enum UninstallOutcome
{
    /// <summary>The uninstaller finished and said it removed the program.</summary>
    Removed = 1,

    /// <summary>Deliberately not run. Reason is always filled.</summary>
    Refused = 2,

    /// <summary>Ran and failed. Nobody decided this.</summary>
    Failed = 3,

    /// <summary>Still running when the clock ran out, and killed.</summary>
    TimedOut = 4,

    /// <summary>Stopped by the person.</summary>
    Cancelled = 5,
    Unconfirmed = 6,
    Busy = 7,
    AlreadyAbsent = 8,
    StillRunning = 9,
}

/// <summary>
/// What happened to one uninstall attempt.
/// </summary>
/// <remarks>
/// Declared here rather than next to UninstallRunner, and that is not tidiness.
/// LeftoverFinder lives in Core and must see the outcome, because without it the
/// finder has no right to look for anything at all. Core cannot reference
/// Deletion: the graph goes downwards only, and the architecture test says so.
/// A description of a result belongs in Core, and the one call that starts an
/// uninstaller belongs in Deletion, exactly as Finding and FileDeleter split.
/// </remarks>
public sealed record UninstallResult(
    string ProgramId, UninstallOutcome Outcome, int? ExitCode, long BytesFreed, string? Reason)
{
    public bool RemovalConfirmed { get; init; }
    public bool RebootRequired { get; init; }
    public bool RebootInitiated { get; init; }
    public bool QueueCancelled { get; init; }
    public int? ProcessId { get; init; }
    public string? Executable { get; init; }
    public TimeSpan Elapsed { get; init; }
    public bool ProcessTreeVerified { get; init; }
}

/// <summary>
/// Turns a registry string into a call that actually removes something.
/// </summary>
/// <remarks>
/// The string is rewritten rather than executed. Counted on this machine on
/// 05.09.2026: of 149 msiexec uninstall strings, 102 carry /I and only 47 carry
/// /X. Running them as written opens a repair or install wizard for two thirds
/// of installed MSI programs. A string that cannot be taken apart is not run at
/// all: handing unparsed text to a shell works often enough to be trusted and
/// fails in exactly the way that starts the wrong program.
/// </remarks>
public static partial class UninstallCommandBuilder
{
    private static readonly string[] ShellHosts = ["cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe"];
    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}")]
    private static partial Regex KodProdukta();

    public static IReadOnlyList<string> TihieKlyuchi(InstallerKind kind) => kind switch
    {
        // Capital S, and it is case sensitive: NSIS ignores /s and shows a window.
        InstallerKind.Nsis => ["/S"],
        InstallerKind.InnoSetup => ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"],
        InstallerKind.Squirrel => ["--uninstall", "-s"],
        InstallerKind.Msi => ["/qn", "/norestart"],
        // Msix is removed through its own path and never reaches here.
        _ => [],
    };

    public static bool TryKodProdukta(string stroka, out string kod)
    {
        var sovpadenie = KodProdukta().Match(stroka ?? string.Empty);

        kod = sovpadenie.Success ? sovpadenie.Value : string.Empty;
        return sovpadenie.Success;
    }

    /// <summary>Production entry point. Asks the real file system.</summary>
    public static bool TryBuild(
        InstalledProgram program, out UninstallCommand command, out string? reason) =>
        TryBuild(program, File.Exists, out command, out reason);

    /// <param name="sushchestvuet">
    /// Existence check, injected. Unquoted paths can only be split by asking
    /// where the executable actually ends, and a test that had to lay out real
    /// files under C:\Program Files to check that is a test nobody runs.
    /// </param>
    public static bool TryBuild(
        InstalledProgram program,
        Func<string, bool> sushchestvuet,
        out UninstallCommand command,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(sushchestvuet);

        command = new UninstallCommand(string.Empty, [], Quiet: false);

        if (program.Installer == InstallerKind.Msix)
        {
            reason = "пакеты MSIX удаляются своим механизмом, а не строкой из реестра";
            return false;
        }

        if (program.Installer == InstallerKind.Msi)
        {
            var istochnik = program.Registrations.FirstOrDefault(r => Guid.TryParse(r.KeyName, out _))?.KeyName
                ?? program.UninstallString ?? program.QuietUninstallString ?? string.Empty;

            if (!TryKodProdukta(istochnik, out var kod))
            {
                reason = $"в строке удаления не найден код продукта: '{istochnik}'";
                return false;
            }

            // Built from scratch, not edited: replacing /I with /X inside the
            // original string would keep whatever else the installer wrote there.
            // Не голое имя: CreateProcess ищет сначала рядом с приложением,
            // и msiexec.exe, положенный туда, запустился бы вместо настоящего.
            // Эта команда идёт под правами администратора.
            command = new UninstallCommand(
                ProcessRunner.SystemTool("msiexec.exe"),
                ["/X" + kod, "/qn", "/norestart"], Quiet: true);
            reason = null;
            return true;
        }

        // A quiet string, when the installer wrote one, is the installer's own
        // answer to "how do I remove this without asking". 31 programs on this
        // machine have one. It beats anything assembled here.
        if (!string.IsNullOrWhiteSpace(program.QuietUninstallString))
        {
            if (TryRazobrat(program.QuietUninstallString, sushchestvuet,
                    out var tihiyExe, out var tihieArgs, out var tihayaPrichina))
            {
                command = new UninstallCommand(tihiyExe, tihieArgs, Quiet: true);
                reason = null;
                return true;
            }

            reason = tihayaPrichina;
            return false;
        }

        if (string.IsNullOrWhiteSpace(program.UninstallString))
        {
            reason = "строки удаления нет вовсе: удалять нечем";
            return false;
        }

        if (!TryRazobrat(program.UninstallString, sushchestvuet,
                out var exe, out var sobstvennye, out var prichina))
        {
            reason = prichina;
            return false;
        }

        var klyuchi = TihieKlyuchi(program.Installer);
        var argumenty = new List<string>(sobstvennye);

        foreach (var klyuch in klyuchi)
        {
            if (!argumenty.Contains(klyuch, StringComparer.OrdinalIgnoreCase))
            {
                argumenty.Add(klyuch);
            }
        }

        command = new UninstallCommand(exe, argumenty, Quiet: klyuchi.Count > 0);
        reason = null;
        return true;
    }

    /// <summary>
    /// Splits a registry string into an executable and its arguments.
    /// </summary>
    /// <param name="sushchestvuet">
    /// Existence check, injected so the parser is testable without laying out
    /// files. Production passes File.Exists.
    /// </param>
    /// <remarks>
    /// Six strings on this machine hold a path with spaces and no quotes, so
    /// splitting on the first space is not an option. Unquoted paths are
    /// resolved by asking the file system where the executable actually ends:
    /// the longest prefix that is a real file wins. When no prefix is a real
    /// file, the answer is a refusal, because the alternative is guessing which
    /// program to start.
    /// </remarks>
    public static bool TryRazobrat(
        string stroka,
        Func<string, bool> sushchestvuet,
        out string exe,
        out IReadOnlyList<string> args,
        out string? reason)
    {
        ArgumentNullException.ThrowIfNull(sushchestvuet);

        exe = string.Empty;
        args = [];

        if (string.IsNullOrWhiteSpace(stroka))
        {
            reason = "строка удаления пустая";
            return false;
        }

        var ochishcheno = Environment.ExpandEnvironmentVariables(stroka.Trim());

        if (ochishcheno[0] == '"')
        {
            var zakryvayushchaya = ochishcheno.IndexOf('"', 1);

            if (zakryvayushchaya < 0)
            {
                reason = $"незакрытая кавычка в строке удаления: '{ochishcheno}'";
                return false;
            }

            exe = ochishcheno[1..zakryvayushchaya];
            if (zakryvayushchaya + 1 < ochishcheno.Length && !char.IsWhiteSpace(ochishcheno[zakryvayushchaya + 1]))
            {
                exe = string.Empty;
                reason = "после пути деинсталлятора нет разделителя";
                return false;
            }

            if (!TryArguments(ochishcheno[(zakryvayushchaya + 1)..], out args, out reason))
            {
                exe = string.Empty;
                return false;
            }

            if (!sushchestvuet(exe))
            {
                exe = string.Empty;
                args = [];
                reason = $"деинсталлятор не найден: '{ochishcheno}'";
                return false;
            }

            if (!PolnyyPut(ref exe, ref args, out reason))
            {
                return false;
            }

            reason = null;
            return true;
        }

        // Longest existing prefix, checked at every space. "C:\Program Files\X
        // Y\uninst.exe /S" has three candidate boundaries and only one of them
        // is a file.
        for (var i = ochishcheno.Length; i > 0; i--)
        {
            if (i != ochishcheno.Length && !char.IsWhiteSpace(ochishcheno[i]))
            {
                continue;
            }

            var kandidat = ochishcheno[..i].TrimEnd();

            if (kandidat.Length == 0 || !sushchestvuet(kandidat))
            {
                continue;
            }

            exe = kandidat;
            if (!TryArguments(ochishcheno[i..], out args, out reason))
            {
                exe = string.Empty;
                return false;
            }

            if (!PolnyyPut(ref exe, ref args, out reason))
            {
                return false;
            }

            reason = null;
            return true;
        }

        reason = $"строка удаления не разбирается, запуск не производится: '{ochishcheno}'";
        return false;
    }

    /// <summary>
    /// Refuses an executable that is not fully qualified.
    /// </summary>
    /// <remarks>
    /// The existence check above asks the file system about the current
    /// directory, while CreateProcess later walks its own list: application
    /// directory, current directory, System32, PATH. For a relative name those
    /// are two different files, and which one runs is decided by whoever put an
    /// exe in the right place. Every real uninstall string in the registry
    /// carries an absolute path, so nothing legitimate is lost.
    /// </remarks>
    private static bool PolnyyPut(ref string exe, ref IReadOnlyList<string> args, out string? reason)
    {
        var name = Path.GetFileName(exe);
        if (!Path.GetExtension(exe).Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || ShellHosts.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            reason = "строка удаления ссылается на скрипт или интерпретатор команд";
            exe = string.Empty;
            args = [];
            return false;
        }

        if (Path.IsPathFullyQualified(exe) && !exe.StartsWith(@"\\", StringComparison.Ordinal)
            && !exe.Any(char.IsControl))
        {
            reason = null;
            return true;
        }

        reason = $"в строке удаления не полный путь до деинсталлятора: '{exe}'";
        exe = string.Empty;
        args = [];
        return false;
    }

    // Returns the concrete list rather than the interface: CA1859, and it is
    // right here anyway, the only callers are inside this class.
    private static bool TryArguments(string hvost, out IReadOnlyList<string> args, out string? reason)
    {
        var itog = new List<string>();
        var index = 0;
        while (index < hvost.Length)
        {
            while (index < hvost.Length && char.IsWhiteSpace(hvost[index])) { index++; }
            if (index == hvost.Length) { break; }
            var argument = new System.Text.StringBuilder();
            var quoted = false;
            while (index < hvost.Length && (quoted || !char.IsWhiteSpace(hvost[index])))
            {
                var slashes = 0;
                while (index < hvost.Length && hvost[index] == '\\') { slashes++; index++; }
                if (index < hvost.Length && hvost[index] == '"')
                {
                    argument.Append('\\', slashes / 2);
                    if (slashes % 2 != 0) { argument.Append('"'); }
                    else if (quoted && index + 1 < hvost.Length && hvost[index + 1] == '"')
                    {
                        argument.Append('"');
                        index++;
                    }
                    else { quoted = !quoted; }
                    index++;
                }
                else
                {
                    argument.Append('\\', slashes);
                    if (index < hvost.Length && (quoted || !char.IsWhiteSpace(hvost[index])))
                    { argument.Append(hvost[index++]); }
                }
            }
            if (quoted)
            {
                args = [];
                reason = "незакрытая кавычка в аргументах удаления";
                return false;
            }
            itog.Add(argument.ToString());
        }
        args = itog;
        reason = null;
        return true;
    }
}
