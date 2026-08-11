namespace GoHome.Core;

/// <summary>
/// Версия правил учёта, по которым считается день.
/// </summary>
/// <remarks>
/// Накопленные дни не пересчитываются: версия проставляется при создании дня и дальше
/// не меняется. Если обновление приедет в середине дня, он досчитается по тем правилам,
/// по которым начался — иначе первая половина дня окажется посчитана иначе, чем вторая.
/// </remarks>
public static class RulesVersion
{
    /// <summary>Любая блокировка экрана останавливала счётчик. Файл без поля версии — отсюда.</summary>
    public const int BreaksAreUnpaid = 1;

    /// <summary>Из рабочего времени вычитается только обед, остальные отлучки идут в зачёт.</summary>
    public const int OnlyLunchIsUnpaid = 2;

    /// <summary>Версия, которой помечаются новые дни.</summary>
    public const int Current = OnlyLunchIsUnpaid;

    /// <summary>Версия правил этого дня. Отсутствующее поле означает самые старые правила.</summary>
    public static int Of(DayLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return log.RulesVersion is { } version && version > BreaksAreUnpaid ? version : BreaksAreUnpaid;
    }

    /// <summary>По этим правилам в зачёт не идёт только обед.</summary>
    public static bool OnlyLunchUnpaid(int version) => version >= OnlyLunchIsUnpaid;
}