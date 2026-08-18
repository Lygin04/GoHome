using System.Globalization;
using System.Text;
using GoHome.Core;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

/// <summary>
/// Набор журналов, покрывающий все формы дня, которые расчёт различает: короткие и длинные
/// отлучки, окно обеда и его границы, поправки, незакрытые дни. Нужен, чтобы сравнивать
/// расчёт до и после изменений построчно, а не на глаз.
/// </summary>
internal static class RulesCorpus
{
    /// <summary>Момент расчёта. Фиксированный: иначе слепок менялся бы сам по себе.</summary>
    public static readonly DateTimeOffset Now = At(20);

    /// <summary>Журналы с именами. Имя попадает в слепок, чтобы расхождение было адресным.</summary>
    public static IEnumerable<(string Name, DayLog Log)> All()
    {
        foreach (var (name, punches, adjustments) in Shapes())
        {
            // Три состояния поля версии: его нет, оно старое, оно нынешнее.
            foreach (var (suffix, version) in new (string, int?)[] { ("без версии", null), ("версия 1", 1), ("версия 2", 2) })
            {
                var log = new DayLog
                {
                    Date = Today,
                    Punches = [.. punches],
                    RulesVersion = version,
                    Adjustments = adjustments.Length == 0 ? null : [.. adjustments],
                };

                yield return ($"{name} / {suffix}", log);
            }
        }
    }

    private static IEnumerable<(string Name, Punch[] Punches, BreakAdjustment[] Adjustments)> Shapes()
    {
        yield return ("пустой день", [], []);
        yield return ("только приход", [In(9)], []);
        yield return ("приход и уход", [In(9), Out(18)], []);

        // Короткая отлучка — чай, вопрос коллеге. В историю не попадает.
        yield return ("короткая отлучка вне окна", [In(9), BreakStart(10), BreakEnd(10, 5), Out(18)], []);
        yield return ("короткая отлучка в окне", [In(9), BreakStart(12), BreakEnd(12, 5), Out(18)], []);

        // Ровно на границах: длительность значимости и длительность догадки.
        yield return ("отлучка ровно 10 минут в окне", [In(9), BreakStart(12), BreakEnd(12, 10), Out(18)], []);
        yield return ("отлучка ровно 25 минут в окне", [In(9), BreakStart(12), BreakEnd(12, 25), Out(18)], []);
        yield return ("отлучка 24 минуты в окне", [In(9), BreakStart(12), BreakEnd(12, 24), Out(18)], []);

        yield return ("обед 45 минут", [In(9), BreakStart(13), BreakEnd(13, 45), Out(18)], []);
        yield return ("длинная отлучка до окна", [In(9), BreakStart(10), BreakEnd(11), Out(18)], []);
        yield return ("длинная отлучка после окна", [In(9), BreakStart(16), BreakEnd(17), Out(18)], []);

        // Начало внутри окна, конец за ним: догадка смотрит на начало.
        yield return ("отлучка через край окна", [In(9), BreakStart(14, 40), BreakEnd(15, 40), Out(18)], []);

        yield return ("две длинные отлучки в окне", [In(9), BreakStart(12), BreakEnd(12, 40), BreakStart(14), BreakEnd(14, 40), Out(18)], []);
        yield return ("длинная до окна и обед", [In(9), BreakStart(10), BreakEnd(11), BreakStart(13), BreakEnd(13, 40), Out(18)], []);

        yield return ("висящая отлучка", [In(9), BreakStart(17)], []);
        yield return ("отлучка оборвана уходом", [In(9), BreakStart(17), Out(17, 30)], []);
        yield return ("день открыт", [In(9)], []);
        yield return ("повторная блокировка", [In(9), BreakStart(12), BreakStart(12, 10), BreakEnd(13), Out(18)], []);
        yield return ("возврат без ухода дважды", [In(9), BreakEnd(10), BreakEnd(11), Out(18)], []);

        // Первое событие дня — всегда приход, каким бы типом ни записалось.
        yield return ("день начат разблокировкой", [BreakEnd(9), BreakStart(13), BreakEnd(13, 40), Out(18)], []);

        // Поправки человека: сильнее и правил, и догадки.
        yield return ("обед возвращён в зачёт", [In(9), BreakStart(13), BreakEnd(13, 45), Out(18)], [Paid(13)]);
        yield return ("короткая помечена обедом", [In(9), BreakStart(10), BreakEnd(10, 30), Out(18)], [Unpaid(10)]);
        yield return ("поправка мимо отлучки", [In(9), BreakStart(13), BreakEnd(13, 45), Out(18)], [Unpaid(11)]);
        yield return ("две поправки на один момент", [In(9), BreakStart(13), BreakEnd(13, 45), Out(18)], [Unpaid(13), Paid(13)]);
    }

    /// <summary>
    /// Слепок расчёта одной строкой на день. Всё, что видит человек: состояние, время,
    /// зачёт и поимённо отлучки.
    /// </summary>
    public static string Render(string name, DaySummary summary)
    {
        var text = new StringBuilder();
        text.Append(name.PadRight(38))
            .Append(" | ").Append(summary.State.ToString().PadRight(10))
            .Append(" | отработано ").Append(Exact(summary.Worked))
            .Append(" | отлучки ").Append(Exact(summary.Breaks))
            .Append(" | не в зачёт ").Append(Exact(summary.Unpaid))
            .Append(" | приход ").Append(Moment(summary.ArrivedAt))
            .Append(" | уход ").Append(Moment(summary.LeftAt))
            .Append(" | цель ").Append(Exact(summary.Goal))
            .Append(" | норма ").Append(summary.GoalReached ? "да" : "нет");

        foreach (var interval in summary.Intervals)
        {
            text.Append(" | ")
                .Append(Moment(interval.Start)).Append('–').Append(Moment(interval.End))
                .Append(' ').Append(interval.Kind)
                .Append(interval.Guessed ? " (догадка)" : string.Empty);
        }

        return text.ToString();
    }

    /// <summary>Ровно то, что посчитано, без округления до минут: расхождение должно быть видно.</summary>
    private static string Exact(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);

    private static string Moment(DateTimeOffset? value) =>
        value is { } moment ? moment.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "—";
}