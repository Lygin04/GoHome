namespace WorkClock.Ui;

/// <summary>Настроение кольца — оно же цвет.</summary>
public enum RingMood
{
    /// <summary>Время идёт.</summary>
    Running,

    /// <summary>Пауза.</summary>
    Paused,

    /// <summary>Норма отработана.</summary>
    Done,
}

/// <summary>
/// Всё, что влияет на картинку в трее. Сравнение двух таких структур отвечает
/// на вопрос «перерисовывать ли»: лишняя перерисовка — это лишний хендл GDI.
/// </summary>
/// <param name="Size">Сторона иконки в пикселях.</param>
/// <param name="Steps">Заполнение кольца в шагах <see cref="TotalSteps"/>.</param>
/// <param name="Mood">Цвет кольца.</param>
/// <param name="DarkBackground">Панель задач тёмная.</param>
public readonly record struct RingVisual(int Size, int Steps, RingMood Mood, bool DarkBackground)
{
    /// <summary>
    /// Дробление кольца. Полградуса: мельче на 16 пикселях всё равно не видно,
    /// а лишние перерисовки текут хендлами.
    /// </summary>
    public const int TotalSteps = 720;

    /// <summary>Строит описание картинки, огрубляя прогресс до различимого шага.</summary>
    public static RingVisual From(int size, double progress, RingMood mood, bool darkBackground)
    {
        var clamped = double.IsNaN(progress) ? 0d : Math.Clamp(progress, 0d, 1d);
        return new RingVisual(size, (int)Math.Round(clamped * TotalSteps), mood, darkBackground);
    }

    /// <summary>Доля заполнения от 0 до 1.</summary>
    public double Progress => (double)Steps / TotalSteps;
}