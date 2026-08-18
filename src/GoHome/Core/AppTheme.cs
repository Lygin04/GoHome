namespace GoHome.Core;

/// <summary>
/// Оформление окон — настроек и истории.
/// </summary>
/// <remarks>
/// На кольцо в трее не влияет никогда. Кольцо живёт на панели задач и обязано следовать
/// её оформлению: принудительно тёмное кольцо на светлой панели снова станет невидимым.
/// Цвета кольца берутся из <c>SystemTheme.IsDarkTaskbar</c> и этой настройки не видят.
/// </remarks>
public enum AppTheme
{
    /// <summary>Как в системе.</summary>
    System,

    /// <summary>Всегда светлое.</summary>
    Light,

    /// <summary>Всегда тёмное.</summary>
    Dark,
}