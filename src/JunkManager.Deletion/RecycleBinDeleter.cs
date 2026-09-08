using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using JunkManager.Safety;

namespace JunkManager.Deletion;

/// <summary>
/// Sends a path to the recycle bin through <c>IFileOperation</c>.
/// </summary>
/// <remarks>
/// <para>
/// Not <c>SHFileOperation</c>, which is the older and shorter way: it takes a
/// double-null-terminated path list capped at MAX_PATH and reports one code for
/// the whole batch. <c>IFileOperation</c> handles long paths and says which item
/// failed, and a cleaner works on exactly the deep, long paths the old API
/// cannot express.
/// </para>
/// <para>
/// Every call runs on its own STA thread. The shell's file operation is an
/// apartment-threaded COM object; test runners and thread-pool callbacks are MTA,
/// and calling it from there fails in ways that look like a broken path rather
/// than a broken apartment.
/// </para>
/// </remarks>
public static class RecycleBinDeleter
{
    internal static DeleteOutcome DeleteProgramLeftover(string path, string installRoot, bool shortcut,
        IReadOnlyList<ProgramFileStamp> expected, CancellationToken ct)
    {
        return VStaPotoke(() =>
        {
            if (ct.IsCancellationRequested)
            { return new DeleteOutcome(path, DeleteStatus.Cancelled, 0, "перемещение в корзину отменено"); }
            if (!ProgramLeftoverGuard.TrySnapshot(path, installRoot, shortcut, out var files, out var reason))
            { return new DeleteOutcome(path, DeleteStatus.Skipped, 0, reason); }
            if (!files.SequenceEqual(expected))
            { return new DeleteOutcome(path, DeleteStatus.Skipped, 0, "содержимое остатка изменилось перед перемещением в корзину"); }
            var drive = new DriveInfo(Path.GetPathRoot(path) ?? string.Empty);
            if (drive.DriveType != DriveType.Fixed)
            { return new DeleteOutcome(path, DeleteStatus.Skipped, 0, "корзина для этого носителя не подтверждена; постоянное удаление не выполнялось"); }
            if (ct.IsCancellationRequested)
            { return new DeleteOutcome(path, DeleteStatus.Cancelled, 0, "перемещение в корзину отменено"); }
            return Otpravit(path, files.Where(f => !f.IsDirectory).Sum(f => f.Length));
        });
    }

    /// <summary>
    /// Moves the path to the recycle bin. The path is re-verified first, exactly
    /// as permanent deletion re-verifies: recycling somebody else's directory
    /// through a swapped junction is no better than deleting it.
    /// </summary>
    public static DeleteOutcome Delete(VerifiedPath path)
    {
        var zayavlennyy = path.Value;

        if (string.IsNullOrEmpty(zayavlennyy))
        {
            return new DeleteOutcome(
                string.Empty, DeleteStatus.Skipped, 0, "пустой пропуск: удалять нечего");
        }

        if (!File.Exists(zayavlennyy) && !Directory.Exists(zayavlennyy))
        {
            return new DeleteOutcome(
                zayavlennyy, DeleteStatus.Skipped, 0, "путь исчез между проверкой и удалением");
        }

        if (!SafetyGuard.TryVerifyForDeletion(zayavlennyy, out var svezhiy, out var prichina))
        {
            return new DeleteOutcome(zayavlennyy, DeleteStatus.Skipped, 0, prichina);
        }

        var put = svezhiy.Value;

        // Размер снимается ДО операции: после неё файл лежит в корзине под
        // другим именем, и спросить его будет уже не у кого.
        var razmer = Razmer(put);

        return VStaPotoke(() => Otpravit(put, razmer));
    }

    private static DeleteOutcome Otpravit(string put, long razmer)
    {
        object? operaciya = null;
        object? predmet = null;

        try
        {
            var tip = Type.GetTypeFromCLSID(Native.CLSID_FileOperation)
                ?? throw new InvalidOperationException("оболочка не отдала класс FileOperation");

            operaciya = Activator.CreateInstance(tip)
                ?? throw new InvalidOperationException("не удалось создать FileOperation");

            var fo = (Native.IFileOperation)operaciya;

            fo.SetOperationFlags(
                Native.FOF_SILENT
                | Native.FOF_NOCONFIRMATION
                | Native.FOF_NOERRORUI
                | Native.FOFX_EARLYFAILURE
                | Native.FOFX_RECYCLEONDELETE);

            predmet = Native.SHCreateItemFromParsingName(put, IntPtr.Zero, Native.IID_IShellItem);

            fo.DeleteItem((Native.IShellItem)predmet, IntPtr.Zero);
            fo.PerformOperations();

            fo.GetAnyOperationsAborted(out var prervano);
            if (prervano)
            {
                return new DeleteOutcome(
                    put, DeleteStatus.Failed, 0, "оболочка прервала операцию");
            }

            // Проверка фактом, а не доверием к коду возврата: IFileOperation
            // умеет вернуть S_OK, ничего не сделав, если оболочка тихо отказала
            // на конкретном предмете.
            if (File.Exists(put) || Directory.Exists(put))
            {
                return new DeleteOutcome(
                    put, DeleteStatus.Failed, 0, "оболочка отчиталась об успехе, но путь на месте");
            }

            return new DeleteOutcome(put, DeleteStatus.Deleted, razmer);
        }
        catch (COMException ex)
        {
            return new DeleteOutcome(
                put, DeleteStatus.Failed, 0, "оболочка отказала: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new DeleteOutcome(put, DeleteStatus.Skipped, 0, "отказано в доступе: " + ex.Message);
        }
        finally
        {
            if (predmet is not null)
            {
                Marshal.FinalReleaseComObject(predmet);
            }

            if (operaciya is not null)
            {
                Marshal.FinalReleaseComObject(operaciya);
            }
        }
    }

    private static long Razmer(string put)
    {
        try
        {
            if (File.Exists(put))
            {
                var svedeniya = new FileInfo(put);
                return svedeniya.LinkTarget is null ? svedeniya.Length : 0;
            }

            long itog = 0;
            foreach (var fayl in Directory.EnumerateFiles(put, "*", SearchOption.TopDirectoryOnly))
            {
                itog += new FileInfo(fayl).Length;
            }

            foreach (var pod in Directory.EnumerateDirectories(put))
            {
                // Внутрь ссылки не заходим и здесь: чужие байты не наши, сколько
                // бы их ни было.
                if (new DirectoryInfo(pod).LinkTarget is null)
                {
                    itog += Razmer(pod);
                }
            }

            return itog;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    [SuppressMessage(
        "Design", "CA1031:Do not catch general exception types",
        Justification =
            "Перенос исключения через границу потока это и есть задача этого метода. " +
            "Исключение не глотается: оно перебрасывается на вызывающем потоке с " +
            "сохранённым стеком через ExceptionDispatchInfo.")]
    private static T VStaPotoke<T>(Func<T> rabota)
    {
        T rezultat = default!;
        ExceptionDispatchInfo? oshibka = null;

        var potok = new Thread(() =>
        {
            try
            {
                rezultat = rabota();
            }
            catch (Exception ex)
            {
                oshibka = ExceptionDispatchInfo.Capture(ex);
            }
        });

        potok.SetApartmentState(ApartmentState.STA);
        potok.Start();
        potok.Join();

        oshibka?.Throw();
        return rezultat;
    }

    private static class Native
    {
        public const uint FOF_SILENT = 0x0004;
        public const uint FOF_NOCONFIRMATION = 0x0010;
        public const uint FOF_NOERRORUI = 0x0400;
        public const uint FOFX_RECYCLEONDELETE = 0x00080000;
        public const uint FOFX_EARLYFAILURE = 0x00100000;

        public static readonly Guid CLSID_FileOperation =
            new("3ad05575-8857-4850-9277-11b85bdb8e09");

        public static readonly Guid IID_IShellItem =
            new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

        /// <remarks>
        /// <c>PreserveSig = false</c> обязателен и стоил падения с access
        /// violation в госте. Нативная функция возвращает HRESULT, а объект
        /// отдаёт последним параметром <c>void** ppv</c>. Без этого флага среда
        /// считает объявленный возврат настоящим возвращаемым значением и
        /// маршалит целое число HRESULT как указатель на интерфейс. С флагом
        /// HRESULT становится исключением, а хвостовой out-параметр
        /// возвращаемым значением, что и требуется.
        /// </remarks>
        [DllImport(
            "shell32.dll",
            CharSet = CharSet.Unicode,
            EntryPoint = "SHCreateItemFromParsingName",
            PreserveSig = false)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Interface)]
        public static extern object SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, in Guid riid);

        /// <summary>
        /// Only the members this code calls carry real signatures. The rest are
        /// slot placeholders: a COM vtable is positional, so every method above
        /// the ones we use has to exist, and none of them is ever called.
        /// </summary>
        [ComImport]
        [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFileOperation
        {
            void Advise();

            void Unadvise();

            void SetOperationFlags(uint dwOperationFlags);

            void SetProgressMessage();

            void SetProgressDialog();

            void SetProperties();

            void SetOwnerWindow();

            void ApplyPropertiesToItem();

            void ApplyPropertiesToItems();

            void RenameItem();

            void RenameItems();

            void MoveItem();

            void MoveItems();

            void CopyItem();

            void CopyItems();

            void DeleteItem([MarshalAs(UnmanagedType.Interface)] IShellItem psiItem, IntPtr pfopsItem);

            void DeleteItems();

            void NewItem();

            void PerformOperations();

            void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
        }

        /// <summary>
        /// Handed straight back to the shell and never called from here, so every
        /// slot is a placeholder.
        /// </summary>
        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItem
        {
            void BindToHandler();

            void GetParent();

            void GetDisplayName();

            void GetAttributes();

            void Compare();
        }
    }
}
