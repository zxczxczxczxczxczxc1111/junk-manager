using System.Globalization;
using JunkManager.Core.Apps;
using Microsoft.Management.Deployment;
using WinGetOptions = Microsoft.Management.Deployment.UninstallOptions;
using WinGetStatus = Microsoft.Management.Deployment.UninstallResultStatus;

namespace JunkManager.Deletion;

internal static class WinGetUninstaller
{
    // Keep COM activation off the UI thread. Cancellation stops observation, not a live transaction.
    internal static Task<UninstallProcessExit> RunAsync(InstalledProgram program, WinGetUninstallRequest request) =>
        Task.Run(async () =>
        {
            var stage = "подключение";
            try
            {
                var manager = new PackageManager();
                var user = request.Scope is ProgramScope.User or ProgramScope.User32;
                var catalogOptions = new CreateCompositePackageCatalogOptions
                {
                    CompositeSearchBehavior = CompositeSearchBehavior.LocalCatalogs,
                    InstalledScope = user ? PackageInstallScope.User : PackageInstallScope.System,
                };
                // No remote catalogs are added. Offline means offline, including discovery.
                var connection = manager.CreateCompositePackageCatalog(catalogOptions).Connect();
                if (connection.Status != ConnectResultStatus.Ok)
                { return Failure("не удалось открыть локальный список установленных пакетов: " + connection.Status); }
                var search = new FindPackagesOptions();
                stage = "поиск пакета";
                search.Filters.Add(new PackageMatchFilter
                {
                    Field = PackageMatchField.ProductCode,
                    Option = PackageFieldMatchOption.EqualsCaseInsensitive,
                    Value = request.ProductCode,
                });
                var found = connection.PackageCatalog.FindPackages(search);
                if (found.Status != FindPackagesResultStatus.Ok || found.Matches.Count != 1)
                { return Failure("не найдена единственная установка с выбранным кодом продукта; обнови список программ"); }
                var package = found.Matches[0].CatalogPackage;
                stage = "проверка установки";
                var installed = package.InstalledVersion;
                if (installed is null || !installed.GetMetadata(PackageVersionMetadataField.InstallerType)
                        .Equals("portable", StringComparison.OrdinalIgnoreCase)
                    || !installed.GetMetadata(PackageVersionMetadataField.InstalledScope)
                        .Equals(user ? "user" : "machine", StringComparison.OrdinalIgnoreCase)
                    || !installed.Version.Equals(program.Version, StringComparison.Ordinal)
                    || !SamePath(installed.GetMetadata(PackageVersionMetadataField.InstalledLocation), program.InstallLocation))
                { return Failure("тип, версия или папка установки изменились; обнови список программ"); }
                // C#/WinRT collections are indexed deliberately; IEnumerable is not implemented by older servers.
                var codes = installed.ProductCodes;
                if (codes.Count != 1 || !codes[0].Equals(request.ProductCode, StringComparison.OrdinalIgnoreCase))
                { return Failure("код продукта в локальном каталоге не совпадает с выбранной регистрацией"); }
                stage = "удаление";
                var result = await manager.UninstallPackageAsync(package, new WinGetOptions
                {
                    PackageUninstallMode = PackageUninstallMode.Silent,
                    Force = false,
                });
                if (result.Status == WinGetStatus.Ok) { return new UninstallProcessExit(0, string.Empty, true); }
                var code = result.ExtendedErrorCode?.HResult ?? unchecked((int)result.UninstallerErrorCode);
                return Failure("удаление не выполнено: " + result.Status + " (0x" + code.ToString("X8", CultureInfo.InvariantCulture)
                    + "). " + result.ExtendedErrorCode?.Message);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException
                or InvalidCastException or UnauthorizedAccessException or IOException or TypeInitializationException
                or TypeLoadException or DllNotFoundException or EntryPointNotFoundException or NotSupportedException
                or System.ComponentModel.Win32Exception)
            {
                // A missing COM server must become a visible result, not an unobserved task obituary.
                System.Diagnostics.Trace.TraceError(ex.ToString());
                return Failure(stage + ": локальный API недоступен. Проверь, установлен ли «Установщик приложений» Microsoft. " + ex.Message);
            }
        });

    private static bool SamePath(string actual, string? expected) => !string.IsNullOrWhiteSpace(expected)
        && Path.TrimEndingDirectorySeparator(actual).Equals(Path.TrimEndingDirectorySeparator(expected), StringComparison.OrdinalIgnoreCase);

    private static UninstallProcessExit Failure(string reason) => new(-1, "WinGet: " + reason, true);
}
