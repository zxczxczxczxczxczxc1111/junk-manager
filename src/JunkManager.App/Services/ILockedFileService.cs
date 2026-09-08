namespace JunkManager.App.Services;

/// <summary>
/// What one attempt to free a locked file ended with.
/// </summary>
/// <param name="Ok">
/// True только когда действие ВЫПОЛНЕНО. «Попросили и получили отказ» это false:
/// продукт, который считает неуслышанную просьбу успехом, отправит человека
/// нажимать «повторить удаление» в пустоту.
/// </param>
/// <param name="Note">
/// Что сказать человеку. Заполнено ВСЕГДА, включая успех: строка «готово» без
/// подробностей на экране, где только что был отказ, читается как ещё один
/// отказ.
/// </param>
internal sealed record LockedFileActionResult(bool Ok, string Note);

/// <summary>
/// The three ways out of "file is in use", and nothing else.
/// </summary>
/// <remarks>
/// Интерфейс, а не статические вызовы, потому что за двумя из трёх стоят
/// необратимые действия над ЧУЖИМИ процессами человека. Проверка модели экрана
/// обязана уметь нажать все три кнопки, не закрывая при этом ничего на машине,
/// где её запустили.
/// </remarks>
internal interface ILockedFileService
{
    /// <summary>
    /// Просит держателей закрыться штатно. Данные человека не теряются.
    /// </summary>
    Task<LockedFileActionResult> PoprositZakrytsyaAsync(string put, CancellationToken ct);

    /// <summary>
    /// Откладывает удаление до следующей загрузки. Требует прав администратора.
    /// </summary>
    Task<LockedFileActionResult> OtlozhitNaZagruzkuAsync(string put, CancellationToken ct);

    /// <summary>
    /// Завершает держателей принудительно. Несохранённое пропадает.
    /// </summary>
    Task<LockedFileActionResult> ZavershitAsync(string put, CancellationToken ct);
}
