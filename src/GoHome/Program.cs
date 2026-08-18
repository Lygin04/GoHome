using GoHome.App;
using GoHome.Diagnostics;
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
        CatchEverything();

        using var context = new TrayApplicationContext(new GoHomeService(new DayLogStore()));
        Application.Run(context);
        return 0;
    }

    /// <summary>
    /// Последний рубеж: необработанное исключение не показывает модальное окно и не
    /// останавливает учёт, а уходит в журнал ошибок. Основное средство — не это, а то,
    /// что операции с файлами не выпускают исключений наружу; сюда доходит лишь то,
    /// чего никто не предвидел.
    /// </summary>
    private static void CatchEverything()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, e) =>
            ErrorLog.Default.Write("необработанное исключение UI-потока", e.Exception);

        // Из фонового потока приложение уже не спасти — но причина должна остаться записанной.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ErrorLog.Default.Write("необработанное исключение вне UI-потока", e.ExceptionObject as Exception);
    }
}