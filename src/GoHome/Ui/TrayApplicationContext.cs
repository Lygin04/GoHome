using System.Diagnostics;
using Microsoft.Win32;
using GoHome.App;
using GoHome.Core;
using GoHome.Interop;

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
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _autostartItem;

    private DateOnly _day;
    private HistoryForm? _historyForm;
    private bool _disposed;

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
        _pauseItem = new ToolStripMenuItem("Пауза");
        _autostartItem = new ToolStripMenuItem("Запускать при входе в систему") { CheckOnClick = false };

        _pauseItem.Click += (_, _) => TogglePause();
        _autostartItem.Click += (_, _) => ToggleAutostart();

        var journalItem = new ToolStripMenuItem("Открыть журнал дня");
        journalItem.Click += (_, _) => OpenDayJournal();

        var historyItem = new ToolStripMenuItem("История за неделю…");
        historyItem.Click += (_, _) => ShowHistory();

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => ExitApplication();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _counterItem,
            _arrivedItem,
            _projectedItem,
            new ToolStripSeparator(),
            _pauseItem,
            journalItem,
            historyItem,
            new ToolStripSeparator(),
            _autostartItem,
            exitItem,
        ]);
        menu.Opening += (_, _) => UpdateMenu(_clock());

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowHistory();

        var now = _clock();
        _day = WorkDay.DateOf(now);

        _service.CloseStaleDays(now);
        if (!WorkstationState.IsLocked())
        {
            // Приход пишется только при разблокированном экране. После ночного обновления
            // корпоративная Windows логинится в сессию сама и блокирует её (ARSO), поэтому
            // ни факт запуска, ни событие логона приходом считать нельзя.
            _service.RecordReturn(now, "startup");
        }

        Refresh(now);
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

    private void OnTick()
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

        if (_service.TryTakeGoalNotification(now))
        {
            _notifyIcon.ShowBalloonTip(
                BalloonTimeoutMs,
                "GoHome",
                $"Норма отработана: {WorkTimeFormat.Duration(summary.Worked)}. Можно домой.",
                ToolTipIcon.Info);
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

    private void OpenDayJournal()
    {
        var now = _clock();
        var target = _service.DayFileExists(now) ? _service.DayFilePath(now) : _service.DataRoot;

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // Для .json может не быть сопоставленной программы — открываем каталог.
            TryOpen(_service.DataRoot);
        }
    }

    private static void TryOpen(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // Показывать нечего — молча пропускаем.
        }
    }

    private void ShowHistory()
    {
        if (_historyForm is null || _historyForm.IsDisposed)
        {
            _historyForm = new HistoryForm(_service, _clock);
            _historyForm.FormClosed += (_, _) => _historyForm = null;
        }

        _historyForm.Show();
        if (_historyForm.WindowState == FormWindowState.Minimized)
        {
            _historyForm.WindowState = FormWindowState.Normal;
        }

        _historyForm.Activate();
    }

    private void ToggleAutostart()
    {
        var result = Autostart.IsEnabled() ? Autostart.Disable() : Autostart.Enable();
        if (!result.Success)
        {
            _notifyIcon.ShowBalloonTip(
                BalloonTimeoutMs,
                "GoHome",
                "Не удалось изменить автозапуск. " + result.Output,
                ToolTipIcon.Warning);
        }

        _autostartItem.Checked = Autostart.IsEnabled();
    }

    private void ExitApplication()
    {
        _timer.Stop();
        ExitThread();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
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
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        // Штатное завершение системы. Запись синхронная, до диска — второго шанса не будет.
        _service.RecordPause(_clock(), UserActivity.GetIdleTime(), "shutdown");
    }

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

            _historyForm?.Dispose();
            _uiThread.Dispose();
        }

        base.Dispose(disposing);
    }
}