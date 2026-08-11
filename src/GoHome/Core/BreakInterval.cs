namespace GoHome.Core;

/// <summary>Закрытая отлучка и то, чем она оказалась после применения правил и поправок.</summary>
/// <param name="Start">Блокировка.</param>
/// <param name="End">Возврат.</param>
/// <param name="Kind">Идёт ли в зачёт.</param>
/// <param name="Guessed">
/// Обедом эту отлучку посчитала догадка, а не человек. Такую пометку можно снять в один клик,
/// и именно её показывает уведомление.
/// </param>
public sealed record BreakInterval(DateTimeOffset Start, DateTimeOffset End, BreakKind Kind, bool Guessed)
{
    /// <summary>Длительность отлучки.</summary>
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;

    /// <summary>Отлучка не идёт в рабочее время.</summary>
    public bool IsUnpaid => Kind == BreakKind.Unpaid;
}