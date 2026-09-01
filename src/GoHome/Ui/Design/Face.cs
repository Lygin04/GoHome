namespace GoHome.Ui.Design;

/// <summary>
/// Как выглядит нарисованный элемент в одном состоянии.
/// </summary>
/// <remarks>
/// Прозрачный <paramref name="Back"/> или <paramref name="Line"/> означает «не рисовать вовсе»,
/// а не «нарисовать прозрачным»: в дизайне у опасной кнопки в покое нет ни подложки, ни рамки,
/// и рисовать её прозрачной кистью — значит стирать то, что под ней.
/// </remarks>
/// <param name="Back">Подложка.</param>
/// <param name="Line">Рамка.</param>
/// <param name="Ink">Текст и значки.</param>
internal readonly record struct Face(Color Back, Color Line, Color Ink)
{
    /// <summary>Подложку рисовать надо.</summary>
    public bool HasBack => Back.A != 0;

    /// <summary>Рамку рисовать надо.</summary>
    public bool HasLine => Line.A != 0;
}

/// <summary>Состояние элемента под курсором и мышью.</summary>
internal enum Mood
{
    /// <summary>Покой.</summary>
    Rest,

    /// <summary>Курсор над элементом.</summary>
    Hover,

    /// <summary>Кнопка мыши нажата.</summary>
    Pressed,

    /// <summary>Элемент недоступен.</summary>
    Disabled,
}

/// <summary>
/// Четыре состояния одного вида кнопки.
/// </summary>
/// <remarks>
/// Виды различаются не оттенком, а тем, <i>что</i> меняется между состояниями: у обычной —
/// рамка, у основной — заливка целиком, у опасной в покое нет ни того ни другого. Поэтому
/// это четыре независимых <see cref="Face"/>, а не один с вычисляемыми поправками.
/// </remarks>
internal readonly record struct ButtonStyle(Face Rest, Face Hover, Face Pressed, Face Disabled)
{
    /// <summary>Вид под текущее состояние.</summary>
    public Face this[Mood mood] => mood switch
    {
        Mood.Hover => Hover,
        Mood.Pressed => Pressed,
        Mood.Disabled => Disabled,
        _ => Rest,
    };
}

/// <summary>
/// Состояния поля ввода. Их четыре, но это не те же четыре, что у кнопки: поле не бывает
/// «нажатым», зато бывает ошибочным и сфокусированным.
/// </summary>
/// <param name="Rest">Покой.</param>
/// <param name="Focus">Фокус ввода.</param>
/// <param name="Error">Введено недопустимое.</param>
/// <param name="Disabled">Недоступно.</param>
internal readonly record struct FieldStyle(Face Rest, Face Focus, Face Error, Face Disabled);
