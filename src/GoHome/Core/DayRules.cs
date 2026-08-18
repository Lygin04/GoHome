using System.Text.Json.Serialization;

namespace GoHome.Core;

/// <summary>
/// Правила, по которым считается один день, и его цель. Снимок этих значений ложится
/// в файл дня при создании и дальше не меняется.
/// </summary>
/// <remarks>
/// <para>
/// Так сделано потому, что изменение правил задним числом меняет смысл уже накопленных
/// дней: смена обеденного окна в два часа дня означает, что засчитанный утром обед
/// перестаёт быть обедом, и день оказывается посчитан двумя способами.
/// </para>
/// <para>
/// Раньше ту же задачу решала версия правил — одно число на весь набор. Снимок
/// покрывает её целиком: день по старым правилам — это день, в снимке которого
/// <see cref="CountShortBreaks"/> выключен. Файлы с полем версии читаются как прежде,
/// см. <see cref="Of"/>.
/// </para>
/// </remarks>
public sealed record DayRules
{
    /// <summary>Правила и цель по умолчанию: восьмичасовой день, короткие отлучки в зачёт.</summary>
    public static readonly DayRules Default = new();

    /// <summary>
    /// Цель на день. <c>null</c> — нерабочий день, и это не то же самое, что ноль часов:
    /// нулевая цель формально достигнута в момент запуска.
    /// </summary>
    /// <remarks>Пишется всегда, даже пустым: иначе нерабочий день не отличить от файла без поля.</remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public TimeSpan? Goal { get; init; } = WorkTimeCalculator.DefaultGoal;

    /// <summary>
    /// Короткие отлучки идут в зачёт, а из рабочего времени вычитается только обед.
    /// Выключено — счёт останавливает любая блокировка экрана, независимо от длительности,
    /// и тогда <see cref="Lunch"/> ни на что не влияет: режется всё.
    /// </summary>
    public bool CountShortBreaks { get; init; } = true;

    /// <summary>Когда отлучка может оказаться обедом.</summary>
    public LunchRules Lunch { get; init; } = LunchRules.Default;

    /// <summary>Нерабочий день.</summary>
    [JsonIgnore]
    public bool IsDayOff => Goal is null;

    /// <summary>Цель как длительность: у нерабочего дня она нулевая.</summary>
    [JsonIgnore]
    public TimeSpan GoalOrZero => Goal ?? TimeSpan.Zero;

    /// <summary>
    /// Правила этого дня: снимок из файла, иначе миграция со старого поля версии,
    /// иначе нынешние настройки.
    /// </summary>
    /// <param name="log">Журнал дня.</param>
    /// <param name="current">Что действует сейчас — для дней, которые ещё не начинались.</param>
    public static DayRules Of(DayLog log, DayRules? current = null)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (log.Rules is { } snapshot)
        {
            return snapshot;
        }

        var now = current ?? Default;
        if (log.RulesVersion is { } version)
        {
            return FromVersion(version, now);
        }

        // Ни снимка, ни версии. Либо день ещё не начинался — тогда действуют нынешние
        // настройки, — либо это файл, созданный до того, как правила вообще стали
        // записываться, и считать его надо по самым старым.
        return log.Punches.Count == 0 ? now : FromVersion(RulesVersion.BreaksAreUnpaid, now);
    }

    /// <summary>
    /// Правила дня, помеченного старым полем версии. Цели в таких файлах не было — она
    /// бралась из настроек, поэтому берётся оттуда и сейчас. Обеденных настроек не было тоже.
    /// </summary>
    private static DayRules FromVersion(int version, DayRules current) => new()
    {
        Goal = current.Goal,
        CountShortBreaks = RulesVersion.OnlyLunchUnpaid(version),
        Lunch = LunchRules.Default,
    };
}