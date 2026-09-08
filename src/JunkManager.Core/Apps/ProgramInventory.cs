using JunkManager.Safety;
using System.Diagnostics.CodeAnalysis;

namespace JunkManager.Core.Apps;

public enum ProgramPresence { Unknown, Present, Absent }

public sealed record ProgramPresenceResult(ProgramPresence Presence, string? Reason = null);

public sealed record ProgramInventorySnapshot(
    IReadOnlyList<InstalledProgram> Programs, IReadOnlyList<SkippedItem> Skipped)
{
    public IReadOnlyList<InstalledProgram> OwnershipPrograms { get; init; } = Programs;
    public bool IsComplete { get; init; } = true;
}

public interface IProgramInventory
{
    Task<ProgramInventorySnapshot> ReadAsync(CancellationToken ct = default);
    Task<ProgramPresenceResult> ProbeAsync(InstalledProgram program, CancellationToken ct = default);
}

/// <summary>A single list, with failures kept separate from an empty machine.</summary>
public sealed class ProgramInventory : IProgramInventory
{
    public async Task<ProgramInventorySnapshot> ReadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var win32 = await Task.Run(InstalledProgramReader.ReadDetailed, ct).ConfigureAwait(false);
        var packages = await MsixPackageReader.ReadDetailedAsync(ct).ConfigureAwait(false);
        var merged = Merge(win32.Programs, packages.Packages);
        return merged with
        {
            Skipped = [.. win32.Skipped, .. packages.Skipped, .. merged.Skipped],
            OwnershipPrograms = [.. win32.OwnershipPrograms,
                .. merged.OwnershipPrograms.Where(p => p.Installer == InstallerKind.Msix)],
            IsComplete = win32.Skipped.Count == 0 && packages.Skipped.Count == 0,
        };
    }

    public async Task<ProgramPresenceResult> ProbeAsync(
        InstalledProgram program, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        ct.ThrowIfCancellationRequested();
        if (program.Installer != InstallerKind.Msix)
        {
            return await Task.Run(() => InstalledProgramReader.Probe(program), ct).ConfigureAwait(false);
        }

        if (!CanReadCurrentUser(out var reason))
        {
            return new(ProgramPresence.Unknown, reason);
        }
        if (string.IsNullOrWhiteSpace(program.PackageFullName))
        { return new(ProgramPresence.Unknown, "нет точного полного имени пакета"); }

        var snapshot = await MsixPackageReader.ReadDetailedAsync(ct).ConfigureAwait(false);
        if (snapshot.Skipped.Count > 0)
        {
            return new(ProgramPresence.Unknown, string.Join("; ", snapshot.Skipped.Select(s => s.Reason)));
        }

        return new(snapshot.Packages.Any(p => string.Equals(p.FullName, program.PackageFullName,
            StringComparison.OrdinalIgnoreCase)) ? ProgramPresence.Present : ProgramPresence.Absent);
    }

    public static ProgramInventorySnapshot Merge(
        IReadOnlyList<InstalledProgram> win32, IReadOnlyList<MsixPackage> packages)
    {
        ArgumentNullException.ThrowIfNull(win32);
        ArgumentNullException.ThrowIfNull(packages);
        var programs = new List<InstalledProgram>(win32);
        var owners = new List<InstalledProgram>(win32);
        var skipped = new List<SkippedItem>();
        foreach (var package in packages)
        {
            var program = new InstalledProgram("Msix:" + package.FullName, package.DisplayName,
                package.Publisher, package.Version, package.RootFolder, null, null,
                InstallerKind.Msix, ProgramScope.Msix, null)
            {
                PackageFullName = package.FullName,
                UserSid = Elevation.CurrentSid,
            };
            owners.Add(program);
            if (!package.CanUninstall(out var reason))
            {
                skipped.Add(new(package.FullName, reason) { IsExpectedExclusion = package.ProtectionKnown });
                continue;
            }

            programs.Add(program);
        }

        return new([.. programs.OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)], skipped)
        { OwnershipPrograms = owners };
    }

    public static bool CanReadCurrentUser([NotNullWhen(false)] out string? reason)
    {
        var requested = Environment.GetEnvironmentVariable("USERPROFILE");
        var actual = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Environment remapping cannot turn the administrator's HKCU into somebody else's hive.
        if (!string.IsNullOrWhiteSpace(requested) && !string.Equals(
                Path.TrimEndingDirectorySeparator(requested), Path.TrimEndingDirectorySeparator(actual),
                StringComparison.OrdinalIgnoreCase))
        {
            reason = "процесс работает под другим пользователем; его HKCU и MSIX не проверяются";
            return false;
        }

        reason = null;
        return true;
    }
}
