using System.Xml.Linq;
using WorkClock.App;

namespace WorkClock.Tests;

public class AutostartTests
{
    private static readonly XNamespace TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static XElement Xml(string exe = @"C:\Program Files\WorkClock\WorkClock.exe", string user = @"CONTOSO\ivan") =>
        XDocument.Parse(Autostart.BuildTaskXml(exe, user)).Root!;

    private static string Setting(XElement task, string name) =>
        task.Element(TaskNs + "Settings")?.Element(TaskNs + name)?.Value ?? string.Empty;

    [Fact]
    public void Схема_задачи_разбирается()
    {
        var task = Xml();

        Assert.Equal(TaskNs + "Task", task.Name);
        Assert.Equal("1.2", task.Attribute("version")?.Value);
    }

    [Fact]
    public void Объявление_кодировки_соответствует_требованию_schtasks()
    {
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-16\"?>", Autostart.BuildTaskXml("x.exe", "user"), StringComparison.Ordinal);
    }

    [Fact]
    public void Лимит_времени_выполнения_снят()
    {
        // По умолчанию трое суток: планировщик молча убил бы процесс в середине недели.
        Assert.Equal("PT0S", Setting(Xml(), "ExecutionTimeLimit"));
    }

    [Fact]
    public void Работа_от_батареи_разрешена()
    {
        // Запрет старта от батареи по умолчанию включён.
        Assert.Equal("false", Setting(Xml(), "DisallowStartIfOnBatteries"));
        Assert.Equal("false", Setting(Xml(), "StopIfGoingOnBatteries"));
    }

    [Fact]
    public void Простой_не_останавливает_задачу()
    {
        var idle = Xml().Element(TaskNs + "Settings")?.Element(TaskNs + "IdleSettings");

        Assert.Equal("false", idle?.Element(TaskNs + "StopOnIdleEnd")?.Value);
        Assert.Equal("false", Setting(Xml(), "RunOnlyIfIdle"));
    }

    [Fact]
    public void Триггер_на_вход_в_систему_от_текущего_пользователя()
    {
        var trigger = Xml().Element(TaskNs + "Triggers")?.Element(TaskNs + "LogonTrigger");

        Assert.NotNull(trigger);
        Assert.Equal("true", trigger.Element(TaskNs + "Enabled")?.Value);
        Assert.Equal(@"CONTOSO\ivan", trigger.Element(TaskNs + "UserId")?.Value);
    }

    [Fact]
    public void Права_администратора_не_требуются()
    {
        var principal = Xml().Element(TaskNs + "Principals")?.Element(TaskNs + "Principal");

        Assert.Equal("LeastPrivilege", principal?.Element(TaskNs + "RunLevel")?.Value);
        Assert.Equal("InteractiveToken", principal?.Element(TaskNs + "LogonType")?.Value);
    }

    [Fact]
    public void Команда_указывает_на_переданный_exe()
    {
        var command = Xml()
            .Element(TaskNs + "Actions")?
            .Element(TaskNs + "Exec")?
            .Element(TaskNs + "Command")?.Value;

        Assert.Equal(@"C:\Program Files\WorkClock\WorkClock.exe", command);
    }

    [Fact]
    public void Путь_с_разметкой_экранируется()
    {
        var task = Xml(@"C:\tools\work&clock<1>.exe");

        var command = task.Element(TaskNs + "Actions")?.Element(TaskNs + "Exec")?.Element(TaskNs + "Command")?.Value;
        Assert.Equal(@"C:\tools\work&clock<1>.exe", command);
    }

    [Fact]
    public void Путь_к_своему_exe_определяется()
    {
        Assert.False(string.IsNullOrWhiteSpace(Autostart.ExecutablePath));
    }
}