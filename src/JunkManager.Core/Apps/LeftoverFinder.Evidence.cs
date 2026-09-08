using JunkManager.Core.Registry;
using JunkManager.Safety;
using Microsoft.Win32;

namespace JunkManager.Core.Apps;

public sealed record LeftoverSearchLocations(IReadOnlyList<string> DataRoots,
    IReadOnlyList<string> ShortcutRoots, bool ReadRegistry = true);

public static partial class LeftoverFinder
{
    public static async Task<LeftoverSearch> FindAfterRemovalAsync(InstalledProgram removed, UninstallResult result,
        IProgramInventory? inventory = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(result);
        ct.ThrowIfCancellationRequested();
        if (!result.ProgramId.Equals(removed.Id, StringComparison.OrdinalIgnoreCase)
            || result.Outcome is not (UninstallOutcome.Removed or UninstallOutcome.AlreadyAbsent))
        { return new([], [new(removed.DisplayName, "удаление программы не подтверждено")]); }
        var source = inventory ?? new ProgramInventory();
        var presence = await source.ProbeAsync(removed, ct).ConfigureAwait(false);
        if (presence.Presence != ProgramPresence.Absent)
        {
            return new([], [new(removed.DisplayName, presence.Reason ?? "удаление не подтверждено текущей регистрацией")]);
        }
        var snapshot = await source.ReadAsync(ct).ConfigureAwait(false);
        return await Task.Run(() => FindAfterRemoval(removed, result, snapshot, null, ct), ct).ConfigureAwait(false);
    }

    public static LeftoverSearch FindAfterRemoval(InstalledProgram removed, UninstallResult result,
        ProgramInventorySnapshot inventory, LeftoverSearchLocations? locations = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(inventory);
        ct.ThrowIfCancellationRequested();
        if (!inventory.IsComplete || !result.ProgramId.Equals(removed.Id, StringComparison.OrdinalIgnoreCase)
            || result.Outcome is not (UninstallOutcome.Removed or UninstallOutcome.AlreadyAbsent)
            || inventory.OwnershipPrograms.Any(p => p.Id.Equals(removed.Id, StringComparison.OrdinalIgnoreCase)))
        { return new([], [new(removed.DisplayName, "удаление или полный список владельцев не подтверждены")]); }

        locations ??= new(Korni(),
            [Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
             Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
             Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
             Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)]);
        var found = new List<Leftover>();
        var skipped = new List<SkippedItem>();
        if (!string.IsNullOrWhiteSpace(removed.InstallLocation) && Directory.Exists(removed.InstallLocation))
        {
            AddInstallLeftovers(removed, inventory.OwnershipPrograms, found, skipped, ct);
        }
        else if (!string.IsNullOrWhiteSpace(removed.ExecutablePath) && File.Exists(removed.ExecutablePath))
        {
            found.Add(Evaluate(removed, inventory.OwnershipPrograms, LeftoverKind.File,
                removed.ExecutablePath, removed.ExecutablePath, true));
        }
        var tokens = Tokeny(removed.Publisher, removed.DisplayName);
        foreach (var root in locations.DataRoots)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(root)) { continue; }
                foreach (var path in Directory.GetDirectories(root))
                {
                    if (found.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) { continue; }
                    if (SovpadaetImya(Path.GetFileName(path), tokens)
                        || Path.GetFileName(path).Equals(removed.DisplayName, StringComparison.OrdinalIgnoreCase))
                    { found.Add(Evaluate(removed, inventory.OwnershipPrograms, LeftoverKind.Directory, path, null, false)); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { skipped.Add(new(root, "каталог остатков не прочитан: " + ex.Message)); }
        }
        foreach (var link in ProgramShortcutReader.Read(locations.ShortcutRoots, skipped, ct))
        {
            if (OwnsTarget(removed, link.Target))
            { found.Add(Evaluate(removed, inventory.OwnershipPrograms, LeftoverKind.Shortcut, link.Path, link.Target, true)); }
        }
        if (locations.ReadRegistry)
        {
            foreach (var reference in ReadReferences(skipped, ct))
            {
                if (!OwnsTarget(removed, reference.MissingTarget)) { continue; }
                var candidate = Evaluate(removed, inventory.OwnershipPrograms,
                    reference.Kind == RegistryEntryKind.Key ? LeftoverKind.RegistryKey : LeftoverKind.RegistryValue,
                    reference.Address, reference.MissingTarget, true);
                found.Add(candidate with { RegistryFinding = reference });
            }
        }
        return new(found, skipped);
    }

    private static void AddInstallLeftovers(InstalledProgram removed, IReadOnlyList<InstalledProgram> owners,
        List<Leftover> found, List<SkippedItem> skipped, CancellationToken ct)
    {
        var pending = new Stack<string>();
        pending.Push(removed.InstallLocation!);
        var examined = 0;
        while (pending.TryPop(out var path))
        {
            ct.ThrowIfCancellationRequested();
            if (++examined > 100000)
            { skipped.Add(new(path, "поиск отдельных остатков остановлен: слишком много объектов")); break; }
            try
            {
                var directory = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
                var candidate = Evaluate(removed, owners, directory ? LeftoverKind.Directory : LeftoverKind.File,
                    path, path, true);
                if (candidate.CanDelete)
                {
                    found.Add(candidate);
                    continue;
                }
                skipped.Add(new(path, "сохраняется: " + candidate.Basis));
                // A protected neighbor is not a pardon for the abandoned DLL beside it.
                // Only a container blocked by its contents may be split; owners and user paths stay closed.
                if (!directory || candidate.Evidence.Any(e => e.Negative && e.Code != "ProtectedPath")
                    || !ProgramLeftoverGuard.TryVerify(path, removed.InstallLocation!, false, out _, out _))
                { continue; }
                foreach (var child in Directory.EnumerateFileSystemEntries(path))
                {
                    ct.ThrowIfCancellationRequested();
                    if (pending.Count + examined >= 100000)
                    { skipped.Add(new(path, "часть каталога не проверена: слишком много объектов")); break; }
                    pending.Push(child);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { skipped.Add(new(path, "остаток не прочитан и сохраняется: " + ex.Message)); }
        }
    }

    public static Leftover Evaluate(InstalledProgram removed, IReadOnlyList<InstalledProgram> owners,
        LeftoverKind kind, string path, string? targetPath, bool explicitConnection)
    {
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var evidence = new List<LeftoverEvidence>
        {
            new(explicitConnection ? "ExactConnection" : "NameOnly", explicitConnection
                ? "точный каталог установки, объект внутри него или явная ссылка на исполняемый файл программы"
                : "совпало только имя; содержимое не доказано как мусор"),
        };
        var filesystem = kind is LeftoverKind.Directory or LeftoverKind.File or LeftoverKind.Shortcut;
        var ownedPath = kind is LeftoverKind.Directory or LeftoverKind.File ? path : targetPath;
        if (ownedPath is not null && (ProgramLeftoverGuard.IsUserData(ownedPath)
            || ProgramLeftoverGuard.IsAtOrUnder(ownedPath, Environment.GetFolderPath(Environment.SpecialFolder.Windows))))
        { evidence.Add(new("SystemOrUserFiles", "цель относится к системе или пользовательским данным", true)); }
        if (owners.Any(owner => owner.Id.Equals(removed.Id, StringComparison.OrdinalIgnoreCase)
            || ProgramLeftoverGuard.IsAtOrUnder(ownedPath, owner.InstallLocation)
            || ProgramLeftoverGuard.IsAtOrUnder(owner.InstallLocation, ownedPath)
            || (!string.IsNullOrWhiteSpace(owner.ExecutablePath) && ProgramLeftoverGuard.IsAtOrUnder(owner.ExecutablePath, ownedPath))))
        { evidence.Add(new("OtherOwner", "путь используется установленной программой или общим компонентом", true)); }
        if (ProgramLeftoverGuard.IsAtOrUnder(AppContext.BaseDirectory, ownedPath)
            || ProgramLeftoverGuard.IsAtOrUnder(ownedPath, AppContext.BaseDirectory))
        { evidence.Add(new("CurrentApplication", "каталог принадлежит работающему Junk Manager", true)); }
        if (!explicitConnection && !string.IsNullOrWhiteSpace(removed.Publisher)
            && owners.Any(owner => owner.Publisher.Equals(removed.Publisher, StringComparison.OrdinalIgnoreCase)))
        { evidence.Add(new("SharedPublisher", "у издателя остались другие установленные программы", true)); }
        IReadOnlyList<ProgramFileStamp> files = [];
        if (filesystem && explicitConnection)
        {
            string? refusal = null;
            var installRoot = removed.InstallLocation ?? Path.GetDirectoryName(removed.ExecutablePath);
            if (string.IsNullOrWhiteSpace(installRoot)
                || !ProgramLeftoverGuard.TrySnapshot(path, installRoot, kind == LeftoverKind.Shortcut, out files, out refusal))
            { evidence.Add(new("ProtectedPath", refusal ?? "нет точного каталога установки", true)); }
        }
        else if (filesystem && ProgramLeftoverGuard.IsUserData(path))
        { evidence.Add(new("UserFiles", "путь относится к пользовательским данным", true)); }
        var confidence = evidence.Any(e => e.Negative) ? LeftoverConfidence.Blocked :
            explicitConnection ? LeftoverConfidence.Strong : LeftoverConfidence.Weak;
        return new(kind, path, files.Where(f => !f.IsDirectory).Sum(f => f.Length), string.Join("; ", evidence.Select(e => e.Description)))
        { Evidence = evidence, Confidence = confidence, TargetPath = targetPath, Snapshot = files };
    }

    private static bool OwnsTarget(InstalledProgram program, string target) =>
        ProgramLeftoverGuard.IsAtOrUnder(target, program.InstallLocation)
        || (!string.IsNullOrWhiteSpace(program.ExecutablePath)
            && string.Equals(target, program.ExecutablePath, StringComparison.OrdinalIgnoreCase));

    private static List<RegistryFinding> ReadReferences(List<SkippedItem> skipped, CancellationToken ct)
    {
        var results = new List<RegistryFinding>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            if (hive == RegistryHive.CurrentUser && !ProgramInventory.CanReadCurrentUser(out var refusal))
            { skipped.Add(new("HKCU", refusal)); continue; }
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                if (view == RegistryView.Registry64 && !Environment.Is64BitOperatingSystem) { continue; }
                foreach (var suffix in new[] { "Run", "RunOnce", "App Paths" })
                {
                    ct.ThrowIfCancellationRequested();
                    var branch = @"SOFTWARE\Microsoft\Windows\CurrentVersion\" + suffix;
                    try
                    {
                        using var root = RegistryKey.OpenBaseKey(hive, view);
                        using var key = root.OpenSubKey(branch);
                        if (key is null) { continue; }
                        foreach (var name in suffix == "App Paths" ? key.GetSubKeyNames() : key.GetValueNames())
                        {
                            using var child = suffix == "App Paths" ? key.OpenSubKey(name) : null;
                            var raw = (child ?? key).GetValue(child is null ? name : string.Empty, null,
                                RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                            if (raw is null || !RegistryTargetExtractor.TryExtract(raw, out var target, out _)) { continue; }
                            // An unreadable executable is not a missing one, even if File.Exists shrugs.
                            try { _ = File.GetAttributes(target); continue; }
                            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { /* A dead reference, finally. */ }
                            var subkey = child is null ? branch : branch + "\\" + name;
                            var allowed = child is null
                                ? RegistryGuard.TryVerifyValue(hive, subkey, name, view, out _, out _)
                                : RegistryGuard.TryVerifyKey(hive, subkey, view, out _, out _);
                            if (!allowed) { continue; }
                            if (!RegistryScanner.IsTargetMissing(target, view)) { continue; }
                            var snapshot = RegistryEntrySnapshot.Capture(hive, subkey,
                                child is null ? name : string.Empty, view, child is not null);
                            if (snapshot is null) { continue; }
                            results.Add(new(hive, subkey, child is null ? name : string.Empty, view,
                                child is null ? RegistryEntryKind.Value : RegistryEntryKind.Key, raw, target,
                                name, "Удаляется явная ссылка на отсутствующий файл удалённой программы", "program-leftover-reference")
                                { Snapshot = snapshot });
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    { skipped.Add(new($"{hive} {view}\\{branch}", ex.Message)); }
                }
            }
        }
        return results;
    }
}
