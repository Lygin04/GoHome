using System.Globalization;
using System.Text;

namespace GoHome.Core;

/// <summary>
/// Выгрузка периода в CSV.
/// </summary>
/// <remarks>
/// Формат подогнан под Excel с русскими региональными настройками: разделитель полей —
/// точка с запятой, дробная часть — через запятую. Файл с запятыми между полями такой
/// Excel сваливает в один столбец, и выгрузка становится бесполезной — а править её
/// руками после каждой выгрузки никто не станет.
/// <para>
/// Длительности выгружаются дважды: как <c>7:45</c> — читать глазами, и как <c>7,75</c> —
/// складывать формулой. Одного из двух всегда не хватает.
/// </para>
/// </remarks>
public static class CsvExport
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly string[] Header =
    [
        "Дата",
        "День недели",
        "Приход",
        "Уход",
        "Отработано",
        "Отработано, ч",
        "Цель",
        "Цель, ч",
        "Баланс",
        "Баланс, ч",
        "Перерывы",
        "Не в зачёт",
    ];

    /// <summary>Имя файла по умолчанию. Даты в обратном порядке — чтобы выгрузки сортировались.</summary>
    public static string FileName(PeriodRange range) =>
        $"GoHome-{range.Start:yyyy-MM-dd}—{range.End:yyyy-MM-dd}.csv";

    /// <summary>Строит содержимое файла. Записывать его нужно в UTF-8 с BOM — иначе Excel испортит кириллицу.</summary>
    public static string Build(PeriodStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        var text = new StringBuilder();
        text.AppendLine(string.Join(';', Header));

        foreach (var day in stats.Days)
        {
            var started = day.State != WorkState.NotStarted;

            text.AppendLine(string.Join(';', [
                Quote(day.Date.ToString("dd.MM.yyyy", Russian)),
                Quote(SettingsCheck.DayName(day.Date.DayOfWeek)),
                Quote(started ? WorkTimeFormat.Clock(day.ArrivedAt) : string.Empty),
                Quote(day.LeftAt is not null ? WorkTimeFormat.Clock(day.LeftAt) : string.Empty),
                Quote(started ? WorkTimeFormat.Duration(day.Worked) : string.Empty),
                Hours(started ? day.Worked : null),
                Quote(day.IsDayOff ? "нерабочий" : WorkTimeFormat.Duration(day.Goal)),
                Hours(day.IsDayOff ? null : day.Goal),
                Quote(started ? WorkTimeFormat.SignedDuration(day.Balance) : string.Empty),
                Hours(started ? day.Balance : null),
                Quote(started ? WorkTimeFormat.Duration(day.Breaks) : string.Empty),
                Quote(started ? WorkTimeFormat.Duration(day.Unpaid) : string.Empty),
            ]));
        }

        return text.ToString();
    }

    /// <summary>Часы десятичной дробью — для формул.</summary>
    private static string Hours(TimeSpan? value) =>
        value is { } duration ? duration.TotalHours.ToString("0.00", Russian) : string.Empty;

    /// <summary>
    /// Экранирование по RFC 4180: поле с разделителем, кавычкой или переводом строки
    /// берётся в кавычки, а кавычка внутри удваивается.
    /// </summary>
    private static string Quote(string value) =>
        value.Contains(';', StringComparison.Ordinal)
        || value.Contains('"', StringComparison.Ordinal)
        || value.Contains('\n', StringComparison.Ordinal)
            ? '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : value;
}
