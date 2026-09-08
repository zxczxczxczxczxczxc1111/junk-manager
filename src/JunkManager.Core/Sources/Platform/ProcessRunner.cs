using System.Diagnostics;
using System.Text;

namespace JunkManager.Core.Sources.Platform;

public sealed record ToolRun(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Runs a platform utility and brings back everything it said, including the
/// exit code. Nothing here interprets: a non-zero exit is data for the caller,
/// because DISM answers 740 for "needs elevation" and pnputil answers 259 for
/// "nothing matched", and both are normal outcomes rather than failures.
/// </summary>
public static class ProcessRunner
{
    /// <summary>
    /// The full path to a utility that ships with Windows.
    /// </summary>
    /// <remarks>
    /// Process.Start with a bare name searches the application directory FIRST,
    /// so a file called dism.exe dropped next to JunkManager.exe runs instead of
    /// the real one. On a tool that deletes drivers, registry keys and files
    /// that is a hijack, not a nuisance. Exactly the same reasoning as
    /// DefaultDllImportSearchPaths.System32 on every DllImport here.
    /// </remarks>
    public static string SystemTool(string exeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exeName);

        // По диапазону, а не через string.Contains(char): последний тянет за
        // собой CA1307 про StringComparison, а сравнивать разделители по
        // культуре нечего.
        if (Path.IsPathRooted(exeName)
            || exeName.AsSpan().IndexOfAny(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) >= 0)
        {
            // A caller handing a path in here is a caller working around the
            // whole point of the method.
            throw new ArgumentException(
                "ожидается имя утилиты, а не путь: путь сюда подставляют ровно тогда, " +
                "когда хотят подменить утилиту",
                nameof(exeName));
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), exeName);
    }

    public static async Task<ToolRun> RunAsync(
        string exe, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        // CA1062 is on at latest-all, and it is right here: a null argument list
        // would otherwise blow up somewhere inside the process plumbing, where
        // the message no longer says which caller got it wrong.
        ArgumentNullException.ThrowIfNull(arguments);

        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Console utilities answer in the OEM code page. Reading them as
            // UTF-8 turns every non-ASCII character into a replacement mark, and
            // the parser then fails on text that was perfectly fine.
            StandardOutputEncoding = ConsoleEncoding(),
            StandardErrorEncoding = ConsoleEncoding(),
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"не удалось запустить {exe}");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);

        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Killing the child is the only way out: a hung DISM holds the
            // component store lock and the next run inherits the problem.
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already gone between the timeout and the kill. Nothing to do.
            }

            throw;
        }

        return new ToolRun(
            process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));
    }

    /// <summary>
    /// The code page console utilities actually print in. Asking the console
    /// itself beats hard-coding 866 or 437: the answer differs per machine and
    /// per shell, and a wrong guess corrupts every non-ASCII line silently.
    /// </summary>
    private static Encoding ConsoleEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        try
        {
            return Encoding.GetEncoding(Console.OutputEncoding.CodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            // A WPF process has no console at all, and this question has no
            // answer there. Without the fallback the first call to DISM or
            // pnputil from the window would throw before starting anything,
            // which a person reads as "the utility is missing".
            return Encoding.UTF8;
        }
    }
}
