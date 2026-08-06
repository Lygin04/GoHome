namespace WorkClock.Core;

/// <summary>Состояние дня, вычисленное из журнала.</summary>
public enum WorkState
{
    /// <summary>Отметок нет: человек ещё не разблокировал экран.</summary>
    NotStarted,

    /// <summary>Интервал открыт, время идёт.</summary>
    Working,

    /// <summary>Висящий перерыв в текущем дне — человек на обеде.</summary>
    OnBreak,

    /// <summary>День закрыт.</summary>
    Finished,
}
