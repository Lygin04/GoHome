using GoHome.App;
using GoHome.Core;
using GoHome.Storage;

namespace GoHome.Tests;

/// <summary>
/// Приложение над временным каталогом. Собирается целиком, потому что многое проверяется
/// именно на стыке: снимок правил ставится службой, а читается расчётом.
/// </summary>
internal static class TestApp
{
    /// <summary>Служба над каталогом. Настройки по умолчанию, если не сказано иное.</summary>
    public static GoHomeService Service(string root, AppSettings? settings = null) =>
        new(new DayLogStore(root), Settings(root, settings));

    /// <summary>Хранилище настроек в том же каталоге. Файл заводится, только если он нужен.</summary>
    public static SettingsStore Settings(string root, AppSettings? settings = null)
    {
        var store = new SettingsStore(Path.Combine(root, "settings.json"))
        {
            WriteBackoff = [],
            ReadBackoff = [],
        };

        if (settings is not null)
        {
            store.Save(settings);
        }

        return store;
    }
}