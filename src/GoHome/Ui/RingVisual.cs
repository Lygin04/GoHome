namespace GoHome.Ui;

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

/// <summary>Набор цветов кольца — по оформлению Windows.</summary>
public enum RingPalette
{
    /// <summary>Тёмная панель задач.</summary>
    Dark,

    /// <summary>Светлая панель задач.</summary>
    Light,

    /// <summary>Высокая контрастность: цвета берутся из системной темы, а не свои.</summary>
    HighContrast,
}

/// <summary>
/// Всё, что влияет на картинку в трее. Сравнение двух таких структур отвечает
/// на вопрос «перерисовывать ли»: лишняя перерисовка — это лишний хендл GDI.
/// </summary>
/// <param name="Size">Сторона иконки в пикселях.</param>
/// <param name="Steps">Заполнение кольца в шагах <see cref="TotalSteps"/>.</param>
/// <param name="Mood">Цвет кольца.</param>
/// <param name="Palette">Оформление Windows.</param>
/// <remarks>
/// В режиме высокой контрастности цвета приходят из системной темы, и смена темы
/// внутри режима на это описание не влияет. Такую смену ловит
/// <see cref="TrayRing.Invalidate"/> по событию системы.
/// </remarks>
public readonly record struct RingVisual(int Size, int Steps, RingMood Mood, RingPalette Palette)
{
    /// <summary>
    /// Дробление кольца. Полградуса: мельче на 16 пикселях всё равно не видно,
    /// а лишние перерисовки текут хендлами.
    /// </summary>
    public const int TotalSteps = 720;

    /// <summary>Строит описание картинки, огрубляя прогресс до различимого шага.</summary>
    public static RingVisual From(int size, double progress, RingMood mood, RingPalette palette)
    {
        var clamped = double.IsNaN(progress) ? 0d : Math.Clamp(progress, 0d, 1d);
        return new RingVisual(size, (int)Math.Round(clamped * TotalSteps), mood, palette);
    }

    /// <summary>Доля заполнения от 0 до 1.</summary>
    public double Progress => (double)Steps / TotalSteps;
}