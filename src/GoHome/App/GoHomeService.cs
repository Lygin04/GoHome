using GoHome.Core;
using GoHome.Storage;

namespace GoHome.App;

/// <summary>
/// Решает, когда и какую отметку писать. Состояние нигде не копится: всё, что нужно,
/// каждый раз пересчитывается из журнала.
/// </summary>
public sealed class GoHomeService
{
    /// <summary>
    /// Простой, начиная с которого метка паузы сдвигается назад. При ручном Win+L
    /// простой близок к нулю, а при автолоке по доменной политике эти минуты
    /// иначе попали бы в рабочее время.
    /// </summary>
    public static readonly TimeSpan AutoLockGrace = TimeSpan.FromMinutes(1);

    /// <summary>Насколько свежей должна быть активность, чтобы считать человека присутствующим.</summary>
    private static readonly TimeSpan PresenceWindow = TimeSpan.FromMinutes(2);

    /// <summary>Сколько дней назад имеет смысл искать незакрытые дни на старте.</summary>
    private const int StaleDayLookback = 14;

    private readonly DayLogStore _store;

    public GoHomeService(DayLogStore store, TimeSpan? goal = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        Goal = goal ?? WorkTimeCalculator.DefaultGoal;
    }

    /// <summary>Норма рабочего дня.</summary>
    public TimeSpan Goal { get; }

    /// <summary>Каталог с журналами.</summary>
    public string DataRoot => _store.Root;

    /// <summary>Расчёт текущего дня.</summary>
    public DaySummary Summarize(DateTimeOffset now) =>
        WorkTimeCalculator.Compute(_store.Load(WorkDay.DateOf(now)), now, Goal);

    /// <summary>Путь к файлу дня, которому принадлежит момент.</summary>
    public string DayFilePath(DateTimeOffset now) => _store.PathFor(WorkDay.DateOf(now));

    /// <summary>Есть ли файл журнала за этот день.</summary>
    public bool DayFileExists(DateTimeOffset now) => File.Exists(DayFilePath(now));

    /// <summary>Сводка за последние дни, свежие сверху.</summary>
    public IReadOnlyList<DaySummary> History(DateTimeOffset now, int days)
    {
        var today = WorkDay.DateOf(now);
        var logs = _store.LoadRange(today.AddDays(-(days - 1)), today);
        return HistoryCalculator.Recent(logs, now, days, Goal);
    }

    /// <summary>
    /// Отметка возврата: разблокировка, подключение RDP, ручное продолжение, старт приложения
    /// при незаблокированном экране. Первое событие дня расчёт сам трактует как приход.
    /// </summary>
    public bool RecordReturn(DateTimeOffset now, string source) =>
        _store.TryUpdate(WorkDay.DateOf(now), log =>
        {
            if (WorkTimeCalculator.Compute(log, now, Goal).State == WorkState.Working)
            {
                // Время уже идёт — дубль только замусорит журнал.
                return false;
            }

            log.Punches.Add(new Punch(NotBeforeLastPunch(log, now).ToWholeSecond(), PunchKind.BreakEnd, source));
            return true;
        });

    /// <summary>
    /// Отметка паузы: блокировка, логофф, отключение RDP, завершение системы, ручная пауза.
    /// Метка сдвигается назад на фактический простой — иначе минуты до автолока
    /// по доменной политике попадут в рабочее время.
    /// </summary>
    public bool RecordPause(DateTimeOffset now, TimeSpan idle, string source) =>
        _store.TryUpdate(WorkDay.DateOf(now), log =>
        {
            var summary = WorkTimeCalculator.Compute(log, now, Goal);
            if (summary.State != WorkState.Working)
            {
                return false;
            }

            var at = idle >= AutoLockGrace ? now - idle : now;
            if (at > now)
            {
                at = now;
            }

            log.Punches.Add(new Punch(NotBeforeLastPunch(log, at).ToWholeSecond(), PunchKind.BreakStart, source));
            return true;
        });

    /// <summary>
    /// Метки живучести: время тика и время последней активности пользователя.
    /// Вторая нужна, чтобы закрыть день по-человечески, если машина умрёт с открытым интервалом.
    /// </summary>
    public void Heartbeat(DateTimeOffset now, TimeSpan idle) =>
        _store.TryUpdate(WorkDay.DateOf(now), log =>
        {
            if (log.Punches.Count == 0)
            {
                // День ещё не начинался — не плодим пустые файлы.
                return false;
            }

            var activeAt = now - (idle > TimeSpan.Zero ? idle : TimeSpan.Zero);
            log.LastHeartbeat = now.ToWholeSecond();
            if (log.LastUserActivity is not { } known || activeAt > known)
            {
                log.LastUserActivity = activeAt.ToWholeSecond();
            }

            return true;
        });

    /// <summary>
    /// Дописывает уход в прошлые дни, оставшиеся с открытым интервалом.
    /// Уход ставится по последней активности пользователя — по времени ребута
    /// или «сейчас» ночная перезагрузка записала бы уход в 03:00.
    /// </summary>
    public int CloseStaleDays(DateTimeOffset now)
    {
        var today = WorkDay.DateOf(now);
        var closed = 0;

        foreach (var date in _store.ListDates())
        {
            if (date >= today || date < today.AddDays(-StaleDayLookback))
            {
                continue;
            }

            var wasClosed = _store.TryUpdate(date, log =>
            {
                if (LastPunch(log) is not { } last || last.Kind is PunchKind.Out or PunchKind.BreakStart)
                {
                    // Уход уже есть, либо день закончился блокировкой — расчёт трактует её как уход.
                    return false;
                }

                var summary = WorkTimeCalculator.Compute(log, now, Goal);
                if (summary.LeftAt is not { } leftAt)
                {
                    return false;
                }

                log.Punches.Add(new Punch(leftAt.ToWholeSecond(), PunchKind.Out, "recovered"));
                return true;
            });

            if (wasClosed)
            {
                closed++;
            }
        }

        return closed;
    }

    /// <summary>
    /// Переход через границу рабочего дня. Если человек работает в четыре утра,
    /// интервал переносится в новый день; во всех остальных случаях старый день
    /// просто закрывается по последней активности.
    /// </summary>
    public void HandleRollover(DateTimeOffset now, DateOnly previousDate, TimeSpan idle, bool workstationLocked)
    {
        var today = WorkDay.DateOf(now);
        if (today == previousDate)
        {
            return;
        }

        var boundary = WorkDay.StartOf(today, now.Offset);
        var humanPresent = !workstationLocked && idle < PresenceWindow;

        if (humanPresent && previousDate < today)
        {
            var atBoundary = WorkTimeCalculator.Compute(_store.Load(previousDate), boundary.AddTicks(-1), Goal);
            if (atBoundary.State == WorkState.Working)
            {
                _store.TryUpdate(previousDate, log =>
                {
                    log.Punches.Add(new Punch(boundary, PunchKind.Out, "day-rollover"));
                    return true;
                });

                _store.TryUpdate(today, log =>
                {
                    log.Punches.Add(new Punch(boundary, PunchKind.In, "day-rollover"));
                    return true;
                });
            }
        }

        CloseStaleDays(now);
    }

    /// <summary>
    /// Забирает право показать уведомление о норме. Флаг живёт в файле дня,
    /// поэтому уведомление приходит ровно один раз за день даже после перезапуска.
    /// </summary>
    public bool TryTakeGoalNotification(DateTimeOffset now) =>
        _store.TryUpdate(WorkDay.DateOf(now), log =>
        {
            if (log.GoalNotified)
            {
                return false;
            }

            var summary = WorkTimeCalculator.Compute(log, now, Goal);
            if (summary.State == WorkState.NotStarted || !summary.GoalReached)
            {
                return false;
            }

            log.GoalNotified = true;
            return true;
        });

    private static Punch? LastPunch(DayLog log) =>
        log.Punches.Count == 0 ? null : log.Punches.MaxBy(punch => punch.At);

    /// <summary>Отметка не может оказаться раньше уже записанных — иначе журнал станет непонятным.</summary>
    private static DateTimeOffset NotBeforeLastPunch(DayLog log, DateTimeOffset at) =>
        LastPunch(log) is { } last && at < last.At ? last.At : at;
}