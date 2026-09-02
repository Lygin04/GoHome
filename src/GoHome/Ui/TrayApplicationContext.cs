using System.Diagnostics;
using Microsoft.Win32;
using GoHome.App;
using GoHome.Core;
using GoHome.Diagnostics;
using GoHome.Interop;
using GoHome.Storage;

namespace GoHome.Ui;

/// <summary>Приложение целиком: иконка в трее, меню и реакция на события сессии.</summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private const int BalloonTimeoutMs = 10_000;

    private readonly GoHomeService _service;
    private readonly Func<DateTimeOffset> _clock;
    private readonly NotifyIcon _notifyIcon;
    private readonly TrayRing _ring;
    private readonly System.Windows.Forms.Timer _timer;

    /// <summary>Якорь на UI-потоке: события сессии приходят на своём.</summary>
    private readonly Control _uiThread;

    private readonly ToolStripMenuItem _counterItem;
    private readonly ToolStripMenuItem _arrivedItem;
    private readonly ToolStripMenuItem _projectedItem;
    private readonly ToolStripMenuItem _lunchItem;
    private readonly ToolStripMenuItem _cancelLunchItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _autostartItem;

    private DateOnly _day;
    private DayForm? _dayForm;
    private StatsForm? _statsForm;
    private SettingsForm? _settingsForm;
    private bool _disposed;

    /// <summary>Сейчас висит подсказка про угаданный обед, и щелчок по ней означает отмену.</summary>
    private bool _pendingLunchCancel;

    public TrayApplicationContext(GoHomeService service, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(service);

        _service = service;
        _clock = clock ?? (() => DateTimeOffset.Now);

        _uiThread = new Control();
        _ = _uiThread.Handle;

        // Иконка приложения ставится сразу: между появлением значка в трее и первой
        // отрисовкой кольца пустого места быть не должно.
        _notifyIcon = new NotifyIcon { Text = "GoHome", Icon = AppIcon.ForTray };
        _ring = new TrayRing(_notifyIcon);

        _counterItem = new ToolStripMenuItem { Enabled = false };
        _arrivedItem = new ToolStripMenuItem { Enabled = false };
        _projectedItem = new ToolStripMenuItem { Enabled = false };
        _lunchItem = new ToolStripMenuItem { Enabled = false, Visible = false };
        _cancelLunchItem = new ToolStripMenuItem("Вернуть обед в зачёт") { Visible = false };
        _pauseItem = new ToolStripMenuItem("Пауза");
        _autostartItem = new ToolStripMenuItem("Запускать при входе в систему") { CheckOnClick = false };

        _cancelLunchItem.Click += (_, _) => CancelLunch();
        _pauseItem.Click += (_, _) => TogglePause();
        _autostartItem.Click += (_, _) => ToggleAutostart();

        var dayItem = new ToolStripMenuItem("Открыть день");
        dayItem.Click += (_, _) => ShowDay();

        var statsItem = new ToolStripMenuItem("Статистика…");
        statsItem.Click += (_, _) => ShowStats();

        var settingsItem = new ToolStripMenuItem("Настройки…");
        settingsItem.Click += (_, _) => ShowSettings();

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => ExitApplication();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _counterItem,
            _arrivedItem,
            _projectedItem,
            _lunchItem,
            _cancelLunchItem,
            new ToolStripSeparator(),
            _pauseItem,
            dayItem,
            statsItem,
            settingsItem,
            new ToolStripSeparator(),
            _autostartItem,
            exitItem,
        ]);
        menu.Opening += (_, _) => Guard("открытие меню", () => UpdateMenu(_clock()));

        _notifyIcon.ContextMenuStrip = menu;
        // Левый щелчок открывает день сразу — правый по-прежнему разворачивает меню.
        // Двойной щелчок ведёт туда же: окно одно, и открыть его дважды нельзя.
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowDay();
            }
        };

        // У всплывающей подсказки нет кнопок, поэтому отмена — это щелчок по ней самой.
        // Тот же пункт лежит в меню и доступен до конца дня, а не пока висит уведомление.
        _notifyIcon.BalloonTipClicked += (_, _) => OnBalloonClicked();
        _notifyIcon.BalloonTipClosed += (_, _) => _pendingLunchCancel = false;

        var now = _clock();
        _day = WorkDay.DateOf(now);

        Guard("запуск", () =>
        {
            _service.CloseStaleDays(now);
            if (!WorkstationState.IsLocked())
            {
                // Приход пишется только при разблокированном экране. После ночного обновления
                // корпоративная Windows логинится в сессию сама и блокирует её (ARSO), поэтому
                // ни факт запуска, ни событие логона приходом считать нельзя.
                _service.RecordReturn(now, "startup");
            }

            Refresh(now);
        });

        _notifyIcon.Visible = true;

        // Таймер именно UI-потоковый: NotifyIcon с чужого потока трогать нельзя.
        _timer = new System.Windows.Forms.Timer { Interval = (int)TickInterval.TotalMilliseconds };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.SessionEnding += OnSessionEnding;
        SystemEvents.DisplaySettingsChanged += OnAppearanceChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnTick() => Guard("тик таймера", () =>
    {
        var now = _clock();
        var idle = UserActivity.GetIdleTime();

        if (WorkDay.DateOf(now) != _day)
        {
            _service.HandleRollover(now, _day, idle, WorkstationState.IsLocked());
            _day = WorkDay.DateOf(now);
        }

        _service.Heartbeat(now, idle);

        var summary = Refresh(now);

        if ((_service.TryTakeStorageAlert() ?? _service.TryTakeSettingsAlert()) is { } alert)
        {
            // Подсказка, а не модальное окно: учёт продолжается, а человек чинит файл,
            // когда ему удобно. Остальные уведомления подождут следующего тика — своё
            // право показаться они ещё не забирали.
            ShowBalloon(Describe(alert), ToolTipIcon.Warning);
            return;
        }

        // Про обед сообщаем первым: он меняет счётчик, и уведомление про норму
        // должно исходить уже из поправленного времени.
        if (_service.TryTakeLunchNotification(now) is { } lunch)
        {
            ShowBalloon(
                $"Перерыв {WorkTimeFormat.Interval(lunch)} засчитан обедом и не идёт в рабочее время. "
                    + "Щёлкните здесь, если это был не обед.");

            _pendingLunchCancel = true;
            summary = Refresh(now);
        }

        // Предупреждение спрашивается раньше нормы, но само уступает ей: если пересчёт
        // перебросил счётчик через оба порога разом, право сказать остаётся у нормы.
        var warning = _service.TryTakeWarningNotification(now);

        if (_service.TryTakeGoalNotification(now))
        {
            ShowBalloon($"Норма отработана: {WorkTimeFormat.Duration(summary.Worked)}. Можно домой.");
            return;
        }

        if (warning is { } left)
        {
            ShowBalloon($"До нормы {WorkTimeFormat.Minutes(left)} — пора сворачивать дела.");
        }
    });

    /// <summary>Что сказать человеку про заупрямившийся файл, чтобы он понял, куда смотреть.</summary>
    private static string Describe(StorageAlert alert) => alert.Kind switch
    {
        StorageAlertKind.FileUnreadable =>
            $"Файл дня не разбирается, поэтому не перезаписывается: {alert.Path}. "
                + "Учёт продолжается, но день показан пустым — поправьте синтаксис JSON.",
        _ =>
            $"Не удаётся сохранить файл дня: {alert.Path}. Возможно, он занят другой программой. "
                + "Время считается дальше и запишется, как только файл освободится.",
    };

    /// <summary>Показывает подсказку. Любая чужая перебивает обеденную: щелчок по ней ничего не отменяет.</summary>
    private void ShowBalloon(string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _pendingLunchCancel = false;
        _notifyIcon.ShowBalloonTip(BalloonTimeoutMs, "GoHome", text, icon);
    }

    /// <summary>
    /// Последний рубеж вокруг обработчика. Фоновому приложению, которое должно тихо
    /// проработать весь день, аварийное окно обходится дороже потерянной минуты учёта,
    /// поэтому наружу отсюда не выходит ничего.
    /// </summary>
    private static void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ErrorLog.Default.Write($"сбой в обработчике «{what}»", ex);
        }
    }

    /// <summary>Щелчок по подсказке отменяет догадку, только если подсказка была про обед.</summary>
    private void OnBalloonClicked()
    {
        if (!_pendingLunchCancel)
        {
            return;
        }

        _pendingLunchCancel = false;
        CancelLunch();
    }

    private void CancelLunch()
    {
        var now = _clock();
        if (_service.CancelGuessedLunch(now))
        {
            Refresh(now);
        }
    }

    private DaySummary Refresh(DateTimeOffset now)
    {
        var summary = _service.Summarize(now);
        _ring.Update(summary.Progress, MoodOf(summary));
        _notifyIcon.Text = Tooltip(summary);
        return summary;
    }

    private void UpdateMenu(DateTimeOffset now)
    {
        var summary = Refresh(now);

        _counterItem.Text = summary.State == WorkState.NotStarted
            ? "День ещё не начат"
            : $"Сегодня: {WorkTimeFormat.Duration(summary.Worked)} / {WorkTimeFormat.Duration(summary.Goal)}";

        _arrivedItem.Text = $"Приход: {WorkTimeFormat.Clock(summary.ArrivedAt)}";

        _projectedItem.Text = summary.State switch
        {
            WorkState.Working when summary.GoalReached => "Норма отработана",
            WorkState.Working => $"Уход в {WorkTimeFormat.Clock(summary.ProjectedEnd)}",
            WorkState.OnBreak => "Прогноз: пауза",
            WorkState.Finished => $"Уход: {WorkTimeFormat.Clock(summary.LeftAt)}",
            _ => "Прогноз: —",
        };

        var unpaid = summary.UnpaidIntervals.ToList();
        _lunchItem.Visible = unpaid.Count > 0;
        _lunchItem.Text = unpaid.Count switch
        {
            0 => string.Empty,
            1 => $"Обед: {WorkTimeFormat.Interval(unpaid[0])}",
            _ => $"Не в зачёт: {WorkTimeFormat.Minutes(summary.Unpaid)} в {unpaid.Count} перерывах",
        };

        // Пункт живёт, пока догадка не отменена, — то есть до конца дня, а не пока висит подсказка.
        _cancelLunchItem.Visible = summary.GuessedLunch is not null;
        if (summary.GuessedLunch is { } guessed)
        {
            _cancelLunchItem.Text = $"Вернуть {WorkTimeFormat.Interval(guessed)} в зачёт";
        }

        _pauseItem.Text = summary.State == WorkState.Working ? "Пауза" : "Продолжить";
        _pauseItem.Enabled = summary.State != WorkState.NotStarted || !WorkstationState.IsLocked();
        _autostartItem.Checked = Autostart.IsEnabled();
    }

    private void TogglePause()
    {
        var now = _clock();
        if (_service.Summarize(now).State == WorkState.Working)
        {
            _service.RecordPause(now, UserActivity.GetIdleTime(), "manual");
        }
        else
        {
            _service.RecordReturn(now, "manual");
        }

        Refresh(now);
    }

    /// <summary>Открывает форму дня. Окно одно: второй щелчок поднимает уже открытое.</summary>
    private void ShowDay(DateOnly? date = null)
    {
        if (_dayForm is null || _dayForm.IsDisposed)
        {
            _dayForm = new DayForm(_service, _clock);
            _dayForm.FormClosed += (_, _) => _dayForm = null;
        }

        if (date is { } day)
        {
            _dayForm.ShowDate(day);
        }

        _dayForm.Show();
        if (_dayForm.WindowState == FormWindowState.Minimized)
        {
            _dayForm.WindowState = FormWindowState.Normal;
        }

        _dayForm.Activate();
    }

    private void ShowStats()
    {
        if (_statsForm is null || _statsForm.IsDisposed)
        {
            _statsForm = new StatsForm(_service, _clock);

            // Окно дня одно на приложение, поэтому открывает его трей, а не статистика:
            // иначе у каждого окна завелась бы своя копия формы дня.
            _statsForm.DayRequested += (_, date) => ShowDay(date);
            _statsForm.FormClosed += (_, _) => _statsForm = null;
        }

        _statsForm.Show();
        if (_statsForm.WindowState == FormWindowState.Minimized)
        {
            _statsForm.WindowState = FormWindowState.Normal;
        }

        _statsForm.Activate();
    }

    private void ShowSettings()
    {
        if (_settingsForm is null || _settingsForm.IsDisposed)
        {
            // Файл могли поправить руками с прошлого открытия — форма должна показать то,
            // что действует на самом деле.
            _service.ReloadSettings();

            _settingsForm = new SettingsForm(_service, _clock);
            _settingsForm.FormClosed += (_, _) => _settingsForm = null;

            // Изменённые предпочтения доходят до работающего приложения сразу.
            _settingsForm.Saved += (_, _) =>
            {
                Refresh(_clock());
                _statsForm?.Reload();
            };
        }

        _settingsForm.Show();
        if (_settingsForm.WindowState == FormWindowState.Minimized)
        {
            _settingsForm.WindowState = FormWindowState.Normal;
        }

        _settingsForm.Activate();
    }

    private void ToggleAutostart()
    {
        var result = Autostart.IsEnabled() ? Autostart.Disable() : Autostart.Enable();
        if (!result.Success)
        {
            ShowBalloon("Не удалось изменить автозапуск. " + result.Output, ToolTipIcon.Warning);
        }

        _autostartItem.Checked = Autostart.IsEnabled();
    }

    private void ExitApplication()
    {
        _timer.Stop();

        // Выход — последняя возможность досказать на диск накопленное за день.
        Guard("выход", () => _service.FlushHeartbeat(_clock(), UserActivity.GetIdleTime()));

        ExitThread();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) => Guard("событие сессии", () =>
    {
        // Событие приходит на своём потоке; файл дня защищён локом внутри хранилища.
        var now = _clock();

        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.SessionLogoff:
            case SessionSwitchReason.RemoteDisconnect:
            case SessionSwitchReason.ConsoleDisconnect:
                _service.RecordPause(now, UserActivity.GetIdleTime(), e.Reason.ToString());
                break;

            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.RemoteConnect:
                _service.RecordReturn(now, e.Reason.ToString());
                break;

            default:
                // SessionLogon и подключение консоли намеренно игнорируются:
                // войти в сессию Windows умеет и без человека.
                return;
        }

        PostRefresh();
    });

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => Guard("завершение сессии", () =>
    {
        // Штатное завершение системы. Запись синхронная, до диска — второго шанса не будет,
        // поэтому метки живучести сбрасываются здесь независимо от их обычного расписания.
        var now = _clock();
        var idle = UserActivity.GetIdleTime();

        _service.RecordPause(now, idle, "shutdown");
        _service.FlushHeartbeat(now, idle);
    });

    private void OnAppearanceChanged(object? sender, EventArgs e) => PostRedraw();

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Переключение светлой и тёмной темы приходит как General (WM_SETTINGCHANGE
        // с «ImmersiveColorSet»), высокая контрастность — как Accessibility.
        if (e.Category is UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle
            or UserPreferenceCategory.Accessibility)
        {
            PostRedraw();
        }
    }

    /// <summary>Сменились DPI или тема: размер и подложка кольца пересчитываются заново.</summary>
    private void PostRedraw() => Post(() =>
    {
        _ring.Invalidate();
        Refresh(_clock());
    });

    private void PostRefresh() => Post(() => Refresh(_clock()));

    private void Post(Action action)
    {
        if (_disposed || !_uiThread.IsHandleCreated)
        {
            return;
        }

        try
        {
            _uiThread.BeginInvoke(() =>
            {
                if (!_disposed)
                {
                    action();
                }
            });
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // Приложение закрывается — обновлять уже нечего.
        }
    }

    private static RingMood MoodOf(DaySummary summary) => summary switch
    {
        { GoalReached: true } => RingMood.Done,
        { State: WorkState.Working } => RingMood.Running,
        _ => RingMood.Paused,
    };

    private static string Tooltip(DaySummary summary)
    {
        var counter = $"{WorkTimeFormat.Duration(summary.Worked)} / {WorkTimeFormat.Duration(summary.Goal)}";
        if (summary.Unpaid > TimeSpan.Zero)
        {
            counter += $" (обед {WorkTimeFormat.Minutes(summary.Unpaid)})";
        }

        return summary.State switch
        {
            WorkState.NotStarted => "GoHome — день не начат",
            WorkState.OnBreak => counter + " — пауза",
            WorkState.Finished => counter + " — день закрыт",
            _ => counter,
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;

            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.SessionEnding -= OnSessionEnding;
            SystemEvents.DisplaySettingsChanged -= OnAppearanceChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

            _timer.Stop();
            _timer.Dispose();

            _notifyIcon.Visible = false;
            _ring.Dispose();
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();

            _statsForm?.Dispose();
            _settingsForm?.Dispose();
            _uiThread.Dispose();
        }

        base.Dispose(disposing);
    }
}