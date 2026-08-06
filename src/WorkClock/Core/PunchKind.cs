namespace WorkClock.Core;

/// <summary>
/// Тип отметки в журнале. Журнал сырой: смысл отметке придаёт расчёт, а не запись.
/// </summary>
public enum PunchKind
{
    /// <summary>Приход.</summary>
    In,

    /// <summary>Начало перерыва: блокировка экрана, логофф, отключение RDP, ручная пауза.</summary>
    BreakStart,

    /// <summary>Возврат: разблокировка экрана, подключение RDP, ручное продолжение.</summary>
    BreakEnd,

    /// <summary>Уход.</summary>
    Out,
}