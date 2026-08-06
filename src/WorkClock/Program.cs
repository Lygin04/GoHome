namespace WorkClock;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Тёмная тема включается здесь: в .NET 10 это одна строка,
        // подавлять экспериментальный диагностик больше не нужно.
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);
        return 0;
    }
}
