using GoHome.App;
using GoHome.Storage;
using GoHome.Ui;

namespace GoHome;

internal static class Program
{
    /// <summary>Локальный для сессии: в терминальной среде у каждого пользователя свой экземпляр.</summary>
    private const string SingleInstanceMutex = @"Local\GoHome.SingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            // Ключи для скриптов установки: окон не показываем, ответ — в коде возврата.
            switch (args[0].ToLowerInvariant())
            {
                case "--install":
                    return Autostart.Enable().Success ? 0 : 1;
                case "--uninstall":
                    return Autostart.Disable().Success ? 0 : 1;
            }
        }

        using var singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutex, out var isFirstInstance);
        if (!isFirstInstance)
        {
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);

        using var context = new TrayApplicationContext(new GoHomeService(new DayLogStore()));
        Application.Run(context);
        return 0;
    }
}