namespace GoHome.Storage;

/// <summary>О чём стоит сказать человеку, чтобы он смог вмешаться.</summary>
public enum StorageAlertKind
{
    /// <summary>Файл дня не разбирается — скорее всего опечатка после ручной правки.</summary>
    FileUnreadable,

    /// <summary>Запись не удаётся несколько раз подряд: файл чем-то занят.</summary>
    WriteFailing,
}

/// <summary>
/// Повод показать подсказку: что случилось и с каким файлом. Забирается ровно один раз,
/// чтобы человек не получал одно и то же сообщение на каждой минуте.
/// </summary>
public sealed record StorageAlert(StorageAlertKind Kind, string Path);