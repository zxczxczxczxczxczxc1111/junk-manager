using System.Text.RegularExpressions;

namespace JunkManager.Core.Apps;

public sealed partial record WinGetUninstallRequest(string ProductCode, ProgramScope Scope)
{
    [GeneratedRegex("^\\s*(?:winget(?:\\.exe)?|\"winget(?:\\.exe)?\")\\s+uninstall\\s+--product-code\\s+(?:\"(?<id>[A-Za-z0-9_.+\\-]+)\"|(?<id>[A-Za-z0-9_.+\\-]+))\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PortableCommand();

    public static bool TryCreate(InstalledProgram program, out WinGetUninstallRequest? request, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(program);
        request = null;
        var match = PortableCommand().Match(program.UninstallString ?? string.Empty);
        var code = match.Groups["id"].Value;
        // Registry identity gets the vote. A friendly display name gets no ballot.
        if (program.Installer != InstallerKind.WinGetPortable || !match.Success || code.Length > 255
            || program.Scope is not (ProgramScope.User or ProgramScope.User32 or ProgramScope.Machine32 or ProgramScope.Machine64)
            || program.Registrations.Count != 1
            || program.Registrations[0].Scope != program.Scope
            || !code.Equals(program.Registrations[0].KeyName, StringComparison.OrdinalIgnoreCase))
        {
            reason = "WinGet: команда удаления не совпадает с регистрацией выбранной программы";
            return false;
        }
        request = new(code, program.Scope);
        reason = null;
        return true;
    }
}
