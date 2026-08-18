namespace GoHome.Core;

/// <summary>
/// Устаревшая версия правил учёта — поле <c>rulesVersion</c> в файлах дней,
/// созданных до появления снимка настроек.
/// </summary>
/// <remarks>
/// Механизма как такового больше нет: правила дня описывает <see cref="DayRules"/>,
/// и версия целиком в него укладывается — день по старым правилам это день,
/// в снимке которого выключен <see cref="DayRules.CountShortBreaks"/>. Здесь остались
/// только числа, нужные, чтобы прочитать накопленные файлы ровно как раньше;
/// новым дням версия не проставляется.
/// </remarks>
public static class RulesVersion
{
    /// <summary>Любая блокировка экрана останавливала счётчик. Файл без поля версии — отсюда.</summary>
    public const int BreaksAreUnpaid = 1;

    /// <summary>Из рабочего времени вычитается только обед, остальные отлучки идут в зачёт.</summary>
    public const int OnlyLunchIsUnpaid = 2;

    /// <summary>По этим правилам в зачёт не идёт только обед.</summary>
    public static bool OnlyLunchUnpaid(int version) => version >= OnlyLunchIsUnpaid;
}