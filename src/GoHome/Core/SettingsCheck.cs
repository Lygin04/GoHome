namespace GoHome.Core;

/// <summary>Замечание к настройкам: что именно не так и почему.</summary>
/// <param name="Field">Ярлык поля — форме, чтобы подсветить нужную вкладку.</param>
/// <param name="Message">Объяснение человеку, без терминов и кодов.</param>
public sealed record SettingsProblem(string Field, string Message);

/// <summary>
/// Проверка настроек.
/// </summary>
/// <remarks>
/// Проверяются связки, а не отдельные поля: порог короткой отлучки сам по себе осмыслен
/// при любом значении, а вот больший, чем минимум для догадки, означает, что обед
/// не определится никогда — и заметить это по одному полю невозможно.
/// <para>
/// В форме недопустимое значение не даёт сохранить и объясняет причину
/// (<see cref="Validate"/>). В файле, правленом руками, оно заменяется значением
/// по умолчанию, остальные значения при этом принимаются (<see cref="Sanitize"/>).
/// </para>
/// </remarks>
public static class SettingsCheck
{
    /// <summary>Самый длинный осмысленный рабочий день.</summary>
    public static readonly TimeSpan MaxGoal = TimeSpan.FromHours(24);

    /// <summary>Самый длинный осмысленный порог отлучки.</summary>
    public static readonly TimeSpan MaxThreshold = TimeSpan.FromHours(4);

    /// <summary>Всё, что не так с этими настройками. Пустой список — сохранять можно.</summary>
    public static IReadOnlyList<SettingsProblem> Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var problems = new List<SettingsProblem>();

        foreach (var day in WeekSchedule.Days)
        {
            if (settings.Schedule[day] is { } hours && !IsSaneGoal(hours))
            {
                problems.Add(new SettingsProblem(
                    "график",
                    $"{DayName(day)}: продолжительность дня должна быть больше нуля и не больше {MaxGoal.TotalHours:0} часов."));
            }
        }

        var seen = new HashSet<DateOnly>();
        foreach (var exception in settings.Exceptions)
        {
            if (exception is null)
            {
                problems.Add(new SettingsProblem("исключения", "В списке исключений есть пустая запись."));
                continue;
            }

            if (!seen.Add(exception.Date))
            {
                problems.Add(new SettingsProblem(
                    "исключения",
                    $"Дата {exception.Date:dd.MM.yyyy} задана в исключениях дважды."));
            }

            if (exception.Hours is { } hours && !IsSaneGoal(hours))
            {
                problems.Add(new SettingsProblem(
                    "исключения",
                    $"{exception.Date:dd.MM.yyyy}: продолжительность дня должна быть больше нуля и не больше {MaxGoal.TotalHours:0} часов."));
            }
        }

        // Настройки обеда при выключенном зачёте не влияют ни на что — режется всё,
        // и запрещать сохранение из-за неиспользуемых значений незачем.
        if (settings.CountShortBreaks)
        {
            problems.AddRange(LunchProblems(settings.Lunch));
        }

        return problems;
    }

    /// <summary>Что не так с обеденными правилами.</summary>
    private static IEnumerable<SettingsProblem> LunchProblems(LunchRules lunch)
    {
        if (!IsSaneThreshold(lunch.Minimum))
        {
            yield return new SettingsProblem(
                "учёт времени",
                $"Порог короткой отлучки должен быть больше нуля и не больше {MaxThreshold.TotalHours:0} часов.");
        }

        if (!IsSaneThreshold(lunch.GuessMinimum))
        {
            yield return new SettingsProblem(
                "учёт времени",
                $"Минимальная длительность обеда должна быть больше нуля и не больше {MaxThreshold.TotalHours:0} часов.");
        }

        if (IsSaneThreshold(lunch.Minimum) && IsSaneThreshold(lunch.GuessMinimum) && lunch.Minimum > lunch.GuessMinimum)
        {
            yield return new SettingsProblem(
                "учёт времени",
                "Порог короткой отлучки больше минимальной длительности обеда — обед не определится никогда. "
                    + "Сделайте порог меньше или равным.");
        }

        if (lunch.WindowEnd <= lunch.WindowStart)
        {
            yield return new SettingsProblem(
                "учёт времени",
                "Конец обеденного окна должен быть позже начала.");
        }
        else if (IsSaneThreshold(lunch.GuessMinimum) && WindowWidth(lunch) < lunch.GuessMinimum)
        {
            yield return new SettingsProblem(
                "учёт времени",
                $"Обеденное окно короче минимальной длительности обеда ({WorkTimeFormat.Minutes(lunch.GuessMinimum)}) — "
                    + "обед не определится никогда.");
        }
    }

    /// <summary>
    /// Приводит настройки к пригодному виду, заменяя недопустимое значениями по умолчанию.
    /// </summary>
    /// <param name="settings">Что прочиталось из файла.</param>
    /// <param name="replaced">Что именно пришлось заменить — для журнала ошибок.</param>
    /// <returns>Настройки, которые точно можно использовать.</returns>
    public static AppSettings Sanitize(AppSettings settings, out IReadOnlyList<string> replaced)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var notes = new List<string>();
        var schedule = settings.Schedule ?? WeekSchedule.Default;

        foreach (var day in WeekSchedule.Days)
        {
            if (schedule[day] is { } hours && !IsSaneGoal(hours))
            {
                notes.Add($"график, {DayName(day)}: {hours} заменено на {WorkTimeCalculator.DefaultGoal}");
                schedule = schedule.With(day, WorkTimeCalculator.DefaultGoal);
            }
        }

        var exceptions = new List<DateException>();
        var seen = new HashSet<DateOnly>();
        foreach (var exception in settings.Exceptions ?? [])
        {
            if (exception is null)
            {
                notes.Add("исключения: пустая запись отброшена");
                continue;
            }

            if (!seen.Add(exception.Date))
            {
                notes.Add($"исключения: повтор даты {exception.Date:dd.MM.yyyy} отброшен");
                continue;
            }

            if (exception.Hours is { } hours && !IsSaneGoal(hours))
            {
                notes.Add($"исключения, {exception.Date:dd.MM.yyyy}: {hours} заменено на нерабочий день");
                exceptions.Add(exception with { Hours = null });
                continue;
            }

            exceptions.Add(exception);
        }

        exceptions.Sort((left, right) => left.Date.CompareTo(right.Date));

        var lunch = SanitizeLunch(settings.Lunch, notes);

        replaced = notes;
        return settings with
        {
            Schedule = schedule,
            Exceptions = exceptions,
            Lunch = lunch,
        };
    }

    private static LunchRules SanitizeLunch(LunchRules? lunch, List<string> notes)
    {
        var fallback = LunchRules.Default;
        if (lunch is null)
        {
            return fallback;
        }

        var minimum = lunch.Minimum;
        var guess = lunch.GuessMinimum;
        var start = lunch.WindowStart;
        var end = lunch.WindowEnd;

        if (!IsSaneThreshold(minimum))
        {
            notes.Add($"учёт времени: порог короткой отлучки {minimum} заменён на {fallback.Minimum}");
            minimum = fallback.Minimum;
        }

        if (!IsSaneThreshold(guess))
        {
            notes.Add($"учёт времени: минимум обеда {guess} заменён на {fallback.GuessMinimum}");
            guess = fallback.GuessMinimum;
        }

        // Порог больше минимума обеда означает, что обед не определится никогда. Какое
        // из двух значений человек хотел изменить — неизвестно, поэтому оба идут в default.
        if (minimum > guess)
        {
            notes.Add($"учёт времени: порог отлучки {minimum} больше минимума обеда {guess}, оба заменены на исходные");
            minimum = fallback.Minimum;
            guess = fallback.GuessMinimum;
        }

        if (end <= start || (end - start) < guess)
        {
            notes.Add($"учёт времени: обеденное окно {start}–{end} не вмещает обед и заменено на исходное");
            start = fallback.WindowStart;
            end = fallback.WindowEnd;
        }

        return new LunchRules(start, end, minimum, guess);
    }

    private static TimeSpan WindowWidth(LunchRules lunch) => lunch.WindowEnd - lunch.WindowStart;

    private static bool IsSaneGoal(TimeSpan value) => value > TimeSpan.Zero && value <= MaxGoal;

    private static bool IsSaneThreshold(TimeSpan value) => value > TimeSpan.Zero && value <= MaxThreshold;

    /// <summary>Название дня недели с большой буквы — для сообщений человеку.</summary>
    public static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "понедельник",
        DayOfWeek.Tuesday => "вторник",
        DayOfWeek.Wednesday => "среда",
        DayOfWeek.Thursday => "четверг",
        DayOfWeek.Friday => "пятница",
        DayOfWeek.Saturday => "суббота",
        _ => "воскресенье",
    };
}