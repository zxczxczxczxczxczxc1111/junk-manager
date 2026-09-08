namespace JunkManager.Tests.Infrastructure;

/// <summary>
/// Часы, которые стоят там, где сказано. Нужны там, где проверяется имя файла
/// или отсечка по возрасту: тест, зависящий от настоящего времени, краснеет
/// однажды ночью и не воспроизводится днём.
/// </summary>
public sealed class FiksirovannoeVremya(DateTimeOffset moment) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => moment;
}
