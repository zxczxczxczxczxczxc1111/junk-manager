using System.Diagnostics.CodeAnalysis;

namespace JunkManager.Core.Apps;

/// <summary>
/// How the program is removed. This is not trivia: the uninstall string in the
/// registry opens a repair wizard for two thirds of MSI entries, so the product
/// builds the call itself, and it can only do that once it knows the kind.
/// </summary>
[SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification =
        "Ноль оставлен неопределённым намеренно, как в RiskTier и FindingSource. " +
        "Незаполненный вид установщика не должен читаться как Msi и уж тем более как Unknown: " +
        "первое запустит msiexec по пустому коду продукта, второе спрячет ошибку разбора.")]
public enum InstallerKind
{
    /// <summary>Windows Installer. Removed by msiexec with the product code.</summary>
    Msi = 1,

    /// <summary>Nullsoft. Quiet switch is /S, capital letter, and it is case sensitive.</summary>
    Nsis = 2,

    /// <summary>Inno Setup. Recognised by the "Inno Setup: App Path" value.</summary>
    InnoSetup = 3,

    /// <summary>Squirrel, the Electron installer. Update.exe --uninstall -s.</summary>
    Squirrel = 4,

    /// <summary>Packaged app. Never removed through this path, see MsixPackageReader.</summary>
    Msix = 5,

    /// <summary>Nothing recognised it. A quiet removal is not offered for these.</summary>
    Unknown = 6,
    /// <summary>Portable files owned by Windows Package Manager.</summary>
    WinGetPortable = 7,
}

/// <summary>
/// Which of the four places the program was registered in. Kept on the record
/// rather than reduced to a boolean: HKCU and HKLM are different installs
/// belonging to different people, and merging them loses one of them.
/// </summary>
[SuppressMessage(
    "Design", "CA1008:Enums should have zero value",
    Justification = "Тот же довод, что и у InstallerKind: незаполненная область не должна " +
        "читаться как машинная, иначе HKCU-запись уедет в ветку, требующую прав администратора.")]
public enum ProgramScope
{
    Machine64 = 1,
    Machine32 = 2,
    User = 3,
    Msix = 4,
    User32 = 5,
}

/// <summary>
/// One row exactly as the registry holds it, before anything is decided about
/// it. The reader produces these, and every rule about hiding, collapsing and
/// classifying is a pure function over them, so all of it is testable without a
/// registry.
/// </summary>
public sealed record RawUninstallEntry(
    string KeyName,
    ProgramScope Scope,
    string? DisplayName,
    string? DisplayVersion,
    string? Publisher,
    string? InstallLocation,
    string? UninstallString,
    string? QuietUninstallString,
    string? InstallDate,
    int SystemComponent,
    string? ParentKeyName,
    string? ReleaseType,
    string? WindowsInstaller,
    string? InnoAppPath)
{
    public string? DisplayIcon { get; init; }
    public string? WinGetInstallerType { get; init; }
    public long? EstimatedSizeBytes { get; init; }
}

/// <summary>
/// One installed program as the product shows it.
/// </summary>
/// <param name="Id">
/// Registry key name plus scope. Unique across the whole list, and stable
/// between runs, which is what lets a person's selection survive a rescan.
/// </param>
/// <param name="InstalledOn">
/// Null when InstallDate could not be believed. On a live machine Discord
/// answers 20253330, the thirty-third month of 2025, and a parser that only
/// checks the shape happily turns that into a date.
/// </param>
public sealed record InstalledProgram(
    string Id,
    string DisplayName,
    string Publisher,
    string Version,
    string? InstallLocation,
    string? UninstallString,
    string? QuietUninstallString,
    InstallerKind Installer,
    ProgramScope Scope,
    DateOnly? InstalledOn)
{
    public string? PackageFullName { get; init; }
    public string? UserSid { get; init; }
    public string? ExecutablePath { get; init; }
    public long? EstimatedSizeBytes { get; init; }
    public IReadOnlyList<ProgramRegistration> Registrations { get; init; } = [];
}

public sealed record ProgramRegistration(ProgramScope Scope, string KeyName);
