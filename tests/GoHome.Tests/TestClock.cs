using GoHome.Core;

namespace GoHome.Tests;

/// <summary>
/// Хелперы для журналов. Время фиксированное и явное: тесты не трогают системные часы
/// и не спят.
/// </summary>
internal static class TestClock
{
    /// <summary>Смещение, в котором живут все тесты (MSK).</summary>
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    /// <summary>Рабочая дата, вокруг которой строятся сценарии.</summary>
    public static readonly DateOnly Today = new(2026, 8, 6);

    /// <summary>Момент в пределах <see cref="Today"/> (или соседних суток через <paramref name="dayShift"/>).</summary>
    public static DateTimeOffset At(int hour, int minute = 0, int dayShift = 0) =>
        new(Today.AddDays(dayShift).ToDateTime(new TimeOnly(hour, minute)), Offset);

    public static DayLog Log(DateOnly date, params Punch[] punches) =>
        new() { Date = date, Punches = [.. punches] };

    /// <summary>Журнал без версии правил — то есть по старым: любая блокировка вне зачёта.</summary>
    public static DayLog Log(params Punch[] punches) => Log(Today, punches);

    /// <summary>Журнал по действующим правилам: в зачёт не идёт только обед.</summary>
    public static DayLog Fresh(params Punch[] punches) =>
        new() { Date = Today, Punches = [.. punches], RulesVersion = RulesVersion.Current };

    /// <summary>Поправка к классификации отлучки, начавшейся в указанный момент.</summary>
    public static BreakAdjustment Paid(int hour, int minute = 0) =>
        new(At(hour, minute), BreakKind.Paid, "test");

    /// <inheritdoc cref="Paid"/>
    public static BreakAdjustment Unpaid(int hour, int minute = 0) =>
        new(At(hour, minute), BreakKind.Unpaid, "test");

    /// <summary>Тот же журнал с приложенными поправками.</summary>
    public static DayLog With(this DayLog log, params BreakAdjustment[] adjustments)
    {
        log.Adjustments = [.. adjustments];
        return log;
    }

    public static Punch In(int hour, int minute = 0, int dayShift = 0) =>
        new(At(hour, minute, dayShift), PunchKind.In);

    public static Punch BreakStart(int hour, int minute = 0, int dayShift = 0) =>
        new(At(hour, minute, dayShift), PunchKind.BreakStart);

    public static Punch BreakEnd(int hour, int minute = 0, int dayShift = 0) =>
        new(At(hour, minute, dayShift), PunchKind.BreakEnd);

    public static Punch Out(int hour, int minute = 0, int dayShift = 0) =>
        new(At(hour, minute, dayShift), PunchKind.Out);

    public static TimeSpan Hm(int hours, int minutes = 0) => new(hours, minutes, 0);
}