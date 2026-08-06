namespace WorkClock.Core;

/// <summary>
/// Одна отметка журнала. Время абсолютное, со смещением: тики таймера не накапливаются,
/// поэтому сон и гибернация счётчик не ломают.
/// </summary>
/// <param name="At">Момент события.</param>
/// <param name="Kind">Тип отметки.</param>
/// <param name="Source">Что породило отметку — для разбора журнала руками.</param>
public sealed record Punch(DateTimeOffset At, PunchKind Kind, string? Source = null);