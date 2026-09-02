namespace GoHome.Core;

/// <summary>
/// То, что убрало из журнала удаление отлучки.
/// </summary>
/// <remarks>
/// Держится в памяти сессии ровно для того, чтобы вернуть эти же отметки обратно.
/// Отмена удаления — это откат конкретной операции, а не создание отлучки из воздуха:
/// вернуть можно только то, что сам же и убрал.
/// </remarks>
/// <param name="Opening">Отметка ухода на перерыв.</param>
/// <param name="Closing">Отметка возвращения.</param>
/// <param name="Adjustment">Поправка зачёта, если она была.</param>
public sealed record RemovedBreak(Punch Opening, Punch Closing, BreakAdjustment? Adjustment);
