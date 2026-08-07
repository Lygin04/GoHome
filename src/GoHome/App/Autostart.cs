using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace GoHome.App;

/// <summary>Итог операции с планировщиком.</summary>
/// <param name="Success">Команда отработала.</param>
/// <param name="Output">Что сказал schtasks — пригодится, когда не отработала.</param>
public readonly record struct AutostartResult(bool Success, string Output);

/// <summary>
/// Автозапуск через планировщик задач.
/// </summary>
/// <remarks>
/// Не через <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>: неподписанный exe,
/// который прописывает сам себя в автозагрузку, — типовая сигнатура для корпоративного EDR.
/// Задача ставится от текущего пользователя, права администратора не нужны.
/// </remarks>
public static class Autostart
{
    /// <summary>
    /// Имя задачи в планировщике. То же имя зашито в <c>uninstall.cmd</c> как запасной путь,
    /// когда exe уже удалён, — менять его можно только вместе со скриптом.
    /// </summary>
    public const string TaskName = "GoHome";

    private static readonly XNamespace TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Путь к своему exe. Под single-file публикацией это именно apphost, а не dll.</summary>
    public static string ExecutablePath => Environment.ProcessPath ?? Application.ExecutablePath;

    /// <summary>Учётная запись, от которой ставится задача.</summary>
    public static string CurrentUserId =>
        System.Security.Principal.WindowsIdentity.GetCurrent().Name;

    /// <summary>Задача зарегистрирована.</summary>
    public static bool IsEnabled() => Run("/Query", "/TN", TaskName).Success;

    /// <summary>Регистрирует задачу на вход в систему.</summary>
    public static AutostartResult Enable() => Enable(ExecutablePath);

    /// <inheritdoc cref="Enable()"/>
    public static AutostartResult Enable(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var file = Path.Combine(Path.GetTempPath(), $"gohome-task-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks принимает только UTF-16: файл в UTF-8 он объявит некорректным XML.
            File.WriteAllText(file, BuildTaskXml(executablePath, CurrentUserId), Encoding.Unicode);
            return Run("/Create", "/TN", TaskName, "/XML", file, "/F");
        }
        catch (IOException ex)
        {
            return new AutostartResult(false, ex.Message);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Временный файл переживёт: его уберёт уборка каталога temp.
            }
        }
    }

    /// <summary>Снимает задачу.</summary>
    public static AutostartResult Disable() => Run("/Delete", "/TN", TaskName, "/F");

    /// <summary>
    /// Описание задачи для планировщика.
    /// </summary>
    /// <remarks>
    /// Два умолчания здесь смертельны и потому заданы явно:
    /// <list type="bullet">
    /// <item><c>ExecutionTimeLimit</c> по умолчанию трое суток — планировщик молча убьёт
    /// процесс в середине недели, поэтому <c>PT0S</c>, то есть без ограничения;</item>
    /// <item><c>DisallowStartIfOnBatteries</c> по умолчанию включён — на ноутбуке без розетки
    /// приложение просто не стартовало бы.</item>
    /// </list>
    /// </remarks>
    public static string BuildTaskXml(string executablePath, string userId)
    {
        var task = new XElement(
            TaskNs + "Task",
            new XAttribute("version", "1.2"),
            new XElement(
                TaskNs + "RegistrationInfo",
                new XElement(TaskNs + "Author", userId),
                new XElement(TaskNs + "Description", "Учёт отработанного времени с паузой по блокировке экрана"),
                new XElement(TaskNs + "URI", "\\" + TaskName)),
            new XElement(
                TaskNs + "Principals",
                new XElement(
                    TaskNs + "Principal",
                    new XAttribute("id", "Author"),
                    new XElement(TaskNs + "UserId", userId),
                    new XElement(TaskNs + "LogonType", "InteractiveToken"),
                    new XElement(TaskNs + "RunLevel", "LeastPrivilege"))),
            new XElement(
                TaskNs + "Settings",
                new XElement(TaskNs + "DisallowStartIfOnBatteries", "false"),
                new XElement(TaskNs + "StopIfGoingOnBatteries", "false"),
                new XElement(TaskNs + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(TaskNs + "StartWhenAvailable", "true"),
                new XElement(TaskNs + "RunOnlyIfNetworkAvailable", "false"),
                new XElement(TaskNs + "AllowStartOnDemand", "true"),
                new XElement(TaskNs + "AllowHardTerminate", "true"),
                new XElement(TaskNs + "Enabled", "true"),
                new XElement(TaskNs + "Hidden", "false"),
                new XElement(TaskNs + "RunOnlyIfIdle", "false"),
                new XElement(TaskNs + "WakeToRun", "false"),
                new XElement(
                    TaskNs + "IdleSettings",
                    new XElement(TaskNs + "StopOnIdleEnd", "false"),
                    new XElement(TaskNs + "RestartOnIdle", "false")),
                new XElement(TaskNs + "ExecutionTimeLimit", "PT0S"),
                new XElement(TaskNs + "Priority", "7")),
            new XElement(
                TaskNs + "Triggers",
                new XElement(
                    TaskNs + "LogonTrigger",
                    new XElement(TaskNs + "Enabled", "true"),
                    new XElement(TaskNs + "UserId", userId),
                    // Пусть рабочий стол успеет подняться.
                    new XElement(TaskNs + "Delay", "PT15S"))),
            new XElement(
                TaskNs + "Actions",
                new XAttribute("Context", "Author"),
                new XElement(
                    TaskNs + "Exec",
                    new XElement(TaskNs + "Command", executablePath))));

        var document = new XDocument(new XDeclaration("1.0", "UTF-16", null), task);
        return document.Declaration + Environment.NewLine + document;
    }

    private static AutostartResult Run(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new AutostartResult(false, "не удалось запустить schtasks");
            }

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (!process.WaitForExit((int)CommandTimeout.TotalMilliseconds))
            {
                return new AutostartResult(false, "schtasks не ответил");
            }

            return new AutostartResult(process.ExitCode == 0, output.Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new AutostartResult(false, ex.Message);
        }
    }
}