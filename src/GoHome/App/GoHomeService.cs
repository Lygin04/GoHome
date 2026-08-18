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

    /// <summary>
    /// Как часто метки живучести доходят до диска. Заметно реже тика: раз в минуту
    /// переписывать файл дня целиком — это около пятисот перезаписей за рабочий день,
    /// и каждая даёт антивирусу, индексатору и открытому проводнику повод вмешаться.
    /// Точность восстановления после внезапного выключения падает на единицы минут,
    /// и это дешевле, чем сбой записи.
    /// </summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);

    /// <summary>Сколько дней назад имеет смысл искать незакрытые дни на старте.</summary>
    private const int StaleDayLookback = 14;

    private readonly DayLogStore _store;

    /// <summary>Когда метки живучести в последний раз доходили до файла.</summary>
    private DateTimeOffset? _heartbeatAt;

    public GoHomeService(DayLogStore store, TimeSpan? goal = null, LunchRules? lunch = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        Goal = goal ?? WorkTimeCalculator.DefaultGoal;
        Lunch = lunch ?? LunchRules.Default;
    }

    /// <summary>Норма рабочего дня.</summary>
    public TimeSpan Goal { get; }

    /// <summary>Когда отлучка может оказаться обедом.</summary>
    public LunchRules Lunch { get; }

    /// <summary>Каталог с журналами.</summary>
    public string DataRoot => _store.Root;

    /// <summary>Расчёт текущего дня.</summary>
    public DaySummary Summarize(DateTimeOffset now) => Compute(_store.Load(WorkDay.DateOf(now)), now);

    private DaySummary Compute(DayLog log, DateTimeOffset now) =>
        WorkTimeCalculator.Compute(log, now, Goal, Lunch);

    /// <summary>Путь к файлу дня, которому принадлежит момент.</summary>
    public string DayFilePath(DateTimeOffset now) => _store.PathFor(WorkDay.DateOf(now));

    /// <summary>Есть ли файл журнала за этот день.</summary>
    public bool DayFileExists(DateTimeOffset now) => File.Exists(DayFilePath(now));

    /// <summary>Сводка за последние дни, свежие сверху.</summary>
    public IReadOnlyList<DaySummary> History(DateTimeOffset now, int days)
    {
        var today = WorkDay.DateOf(now);
        var logs = _store.LoadRange(today.AddDays(-(days - 1)), today);
        return HistoryCalculator.Recent(logs, now, days, Goal, Lunch);
    }

    /// <summary>
    /// Переклассифицирует отлучку: пометить обедом или вернуть в зачёт. Поправка ложится
    /// отдельным списком, отметки журнала не трогаются.
    /// </summary>
    /// <returns><c>true</c>, если поправка записана.</returns>
    public bool Reclassify(DateOnly date, DateTimeOffset breakAt, BreakKind kind, string source) =>
        _store.TryUpdate(date, log =>
        {
            if (log.Punches.Count == 0)
            {
                // В дне без отметок поправлять нечего, а файл заводить незачем.
                return false;
            }

            log.Adjustments ??= [];
            log.Adjustments.RemoveAll(a => a is null || a.BreakAt.UtcTicks == breakAt.UtcTicks);
            log.Adjustments.Add(new BreakAdjustment(breakAt.ToWholeSecond(), kind, source));
            return true;
        });

    /// <summary>
    /// Снимает пометку с отлучки, которую обедом сочла догадка, и возвращает догадку
    /// в исходное состояние: следующая подходящая отлучка снова становится кандидатом.
    /// </summary>
    /// <returns><c>true</c>, если было что отменять.</returns>
    public bool CancelGuessedLunch(DateTimeOffset now)
    {
        var date = WorkDay.DateOf(now);
        return _store.TryUpdate(date, log =>
        {
            if (Compute(log, now).GuessedLunch is not { } guessed)
            {
                return false;
            }

            log.Adjustments ??= [];
            log.Adjustments.RemoveAll(a => a is null || a.BreakAt.UtcTicks == guessed.Start.UtcTicks);
            log.Adjustments.Add(new BreakAdjustment(guessed.Start, BreakKind.Paid, "cancelled"));
            log.LunchNotifiedAt = null;
            return true;
        });
    }

    /// <summary>
    /// Отметка возврата: разблокировка, подключение RDP, ручное продолжение, старт приложения
    /// при незаблокированном экране. Первое событие дня расчёт сам трактует как приход.
    /// </summary>
    public bool RecordReturn(DateTimeOffset now, string source)
    {
        var recorded = _store.TryUpdate(WorkDay.DateOf(now), log =>
        {
            if (Compute(log, now).State == WorkState.Working)
            {
                // Время уже идёт — дубль только замусорит журнал.
                return false;
            }

            Append(log, new Punch(NotBeforeLastPunch(log, now).ToWholeSecond(), PunchKind.BreakEnd, source));
            // Человек здесь прямо сейчас — заодно и метки живучести свежие.
            Stamp(log, now, now);
            return true;
        });

        if (recorded)
        {
            _heartbeatAt = now;
        }

        return recorded;
    }

    /// <summary>
    /// Отметка паузы: блокировка, логофф, отключение RDP, завершение системы, ручная пауза.
    /// Метка сдвигается назад на фактический простой — иначе минуты до автолока
    /// по доменной политике попадут в рабочее время.
    /// </summary>
    public bool RecordPause(DateTimeOffset now, TimeSpan idle, string source)
    {
        var recorded = _store.TryUpdate(WorkDay.DateOf(now), log =>
        {
            if (Compute(log, now).State != WorkState.Working)
            {
                return false;
            }

            var at = idle >= AutoLockGrace ? now - idle : now;
            if (at > now)
            {
                at = now;
            }

            Append(log, new Punch(NotBeforeLastPunch(log, at).ToWholeSecond(), PunchKind.BreakStart, source));
            Stamp(log, now, ActiveAt(now, idle));
            return true;
        });

        if (recorded)
        {
            _heartbeatAt = now;
        }

        // Пауза — точка, после которой машина может и не проснуться: дни, зависшие
        // в памяти после неудачных записей, досылаются именно здесь.
        _store.Flush();
        return recorded;
    }

    /// <summary>
    /// Метки живучести: время тика и время последней активности пользователя.
    /// Вторая нужна, чтобы закрыть день по-человечески, если машина умрёт с открытым интервалом.
    /// Доходят до файла не каждый тик, а раз в <see cref="HeartbeatInterval"/>.
    /// </summary>
    public void Heartbeat(DateTimeOffset now, TimeSpan idle) => Heartbeat(now, idle, force: false);

    /// <summary>
    /// Метки живучести немедленно, вместе со всем, что зависло после неудачных записей.
    /// Зовётся там, где второго шанса не будет: завершение сессии и выход.
    /// </summary>
    public void FlushHeartbeat(DateTimeOffset now, TimeSpan idle) => Heartbeat(now, idle, force: true);

    private void Heartbeat(DateTimeOffset now, TimeSpan idle, bool force)
    {
        // Часы могли уехать назад (перевод времени, синхронизация) — тогда пишем сразу,
        // иначе метки замерли бы до тех пор, пока «сейчас» снова не догонит прошлое.
        var due = force
            || _heartbeatAt is not { } last
            || now - last >= HeartbeatInterval
            || now < last;

        if (!due)
        {
            return;
        }

        var stamped = _store.TryUpdate(WorkDay.DateOf(now), log =>
        {
            if (log.Punches.Count == 0)
            {
                // День ещё не начинался — не плодим пустые файлы.
                return false;
            }

            Stamp(log, now, ActiveAt(now, idle));
            return true;
        });

        if (stamped)
        {
            _heartbeatAt = now;
        }

        _store.Flush();
    }

    /// <summary>Забирает право один раз сказать человеку о проблеме с файлами.</summary>
    public StorageAlert? TryTakeStorageAlert() => _store.TryTakeAlert();

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

                if (Compute(log, now).LeftAt is not { } leftAt)
                {
                    return false;
                }

                Append(log, new Punch(leftAt.ToWholeSecond(), PunchKind.Out, "recovered"));
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
            var atBoundary = Compute(_store.Load(previousDate), boundary.AddTicks(-1));
            if (atBoundary.State == WorkState.Working)
            {
                _store.TryUpdate(previousDate, log =>
                {
                    Append(log, new Punch(boundary, PunchKind.Out, "day-rollover"));
                    return true;
                });

                _store.TryUpdate(today, log =>
                {
                    Append(log, new Punch(boundary, PunchKind.In, "day-rollover"));
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
    /// <remarks>
    /// Флаг не односторонний: пометка отлучки обедом уменьшает отработанное, и счётчик
    /// может уйти ниже нормы уже после уведомления. Тогда флаг снимается, и при повторном
    /// достижении нормы уведомление приходит заново.
    /// </remarks>
    public bool TryTakeGoalNotification(DateTimeOffset now)
    {
        var notify = false;

        _store.TryUpdate(WorkDay.DateOf(now), log =>
        {
            var summary = Compute(log, now);
            if (summary.State == WorkState.NotStarted)
            {
                return false;
            }

            if (!summary.GoalReached)
            {
                if (!log.GoalNotified)
                {
                    return false;
                }

                log.GoalNotified = false;
                return true;
            }

            if (log.GoalNotified)
            {
                return false;
            }

            log.GoalNotified = true;
            notify = true;
            return true;
        });

        return notify;
    }

    /// <summary>
    /// Забирает право сообщить об угаданном обеде. Отметка о показе привязана к началу
    /// интервала: если догадку отменили и она перешла на следующую отлучку, сообщить нужно снова.
    /// </summary>
    /// <returns>Интервал, о котором надо сказать, либо <c>null</c>.</returns>
    public BreakInterval? TryTakeLunchNotification(DateTimeOffset now)
    {
        BreakInterval? announce = null;

        _store.TryUpdate(WorkDay.DateOf(now), log =>
        {
            var guessed = Compute(log, now).GuessedLunch;
            if (guessed is null)
            {
                if (log.LunchNotifiedAt is null)
                {
                    return false;
                }

                log.LunchNotifiedAt = null;
                return true;
            }

            if (log.LunchNotifiedAt is { } shown && shown.UtcTicks == guessed.Start.UtcTicks)
            {
                return false;
            }

            log.LunchNotifiedAt = guessed.Start;
            announce = guessed;
            return true;
        });

        return announce;
    }

    /// <summary>
    /// Дописывает отметку. Версия правил проставляется при создании дня и дальше не меняется:
    /// обновление посреди дня не должно посчитать его половины по-разному.
    /// </summary>
    private static void Append(DayLog log, Punch punch)
    {
        if (log.Punches.Count == 0)
        {
            log.RulesVersion ??= Core.RulesVersion.Current;
        }

        log.Punches.Add(punch);
    }

    /// <summary>
    /// Проставляет метки живучести. Известная активность назад не уезжает: минуты простоя
    /// перед автоблокировкой не должны стереть более свежее уже записанное значение.
    /// </summary>
    private static void Stamp(DayLog log, DateTimeOffset now, DateTimeOffset activeAt)
    {
        log.LastHeartbeat = now.ToWholeSecond();
        if (log.LastUserActivity is not { } known || activeAt > known)
        {
            log.LastUserActivity = activeAt.ToWholeSecond();
        }
    }

    private static DateTimeOffset ActiveAt(DateTimeOffset now, TimeSpan idle) =>
        now - (idle > TimeSpan.Zero ? idle : TimeSpan.Zero);

    private static Punch? LastPunch(DayLog log) =>
        log.Punches.Count == 0 ? null : log.Punches.MaxBy(punch => punch.At);

    /// <summary>Отметка не может оказаться раньше уже записанных — иначе журнал станет непонятным.</summary>
    private static DateTimeOffset NotBeforeLastPunch(DayLog log, DateTimeOffset at) =>
        LastPunch(log) is { } last && at < last.At ? last.At : at;
}