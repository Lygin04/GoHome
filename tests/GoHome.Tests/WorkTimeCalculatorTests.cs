using GoHome.Core;
using static GoHome.Tests.TestClock;

namespace GoHome.Tests;

public class WorkTimeCalculatorTests
{
    [Fact]
    public void Открытый_интервал_считается_до_текущего_момента()
    {
        var log = Log(In(9));

        var summary = WorkTimeCalculator.Compute(log, At(13));

        Assert.Equal(Hm(4), summary.Worked);
        Assert.Equal(WorkState.Working, summary.State);
        Assert.Equal(At(9), summary.ArrivedAt);
    }

    [Fact]
    public void Перерыв_вычитается_из_отработанного()
    {
        var log = Log(In(9), BreakStart(13), BreakEnd(14));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(Hm(8), summary.Worked);
        Assert.Equal(Hm(1), summary.Breaks);
        Assert.Equal(WorkState.Working, summary.State);
    }

    [Fact]
    public void Пустой_журнал_даёт_ноль()
    {
        var summary = WorkTimeCalculator.Compute(Log(), At(13));

        Assert.Equal(TimeSpan.Zero, summary.Worked);
        Assert.Equal(WorkState.NotStarted, summary.State);
        Assert.Null(summary.ArrivedAt);
        Assert.Null(summary.ProjectedEnd);
        Assert.Equal(0d, summary.Progress, 6);
    }

    [Fact]
    public void Пауза_без_парного_возврата_не_уводит_счётчик_в_минус()
    {
        // Блокировка после ухода домой: интервал уже закрыт, отметка — шум.
        var log = Log(In(9), Out(17), BreakStart(18));

        var summary = WorkTimeCalculator.Compute(log, At(20));

        Assert.Equal(Hm(8), summary.Worked);
        Assert.True(summary.Worked >= TimeSpan.Zero);
        Assert.Equal(At(17), summary.LeftAt);
    }

    [Fact]
    public void Повторная_блокировка_во_время_перерыва_игнорируется()
    {
        var log = Log(In(9), BreakStart(13), BreakStart(13, 30), BreakEnd(14));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(Hm(8), summary.Worked);
    }

    [Fact]
    public void Возврат_без_парной_паузы_игнорируется()
    {
        var log = Log(In(9), BreakEnd(10), BreakEnd(11));

        var summary = WorkTimeCalculator.Compute(log, At(13));

        Assert.Equal(Hm(4), summary.Worked);
    }

    [Fact]
    public void Первое_событие_дня_трактуется_как_приход()
    {
        // Утренняя разблокировка приходит как BreakEnd — журнал сырой, смысл придаёт расчёт.
        var log = Log(BreakEnd(9), BreakStart(13), BreakEnd(14));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(At(9), summary.ArrivedAt);
        Assert.Equal(Hm(8), summary.Worked);
    }

    [Fact]
    public void Висящая_пауза_вчерашним_днём_трактуется_как_уход()
    {
        // Человек нажал Win+L и пошёл домой. Время не должно капать до «сейчас».
        var yesterday = Today.AddDays(-1);
        var log = Log(
            yesterday,
            new Punch(At(9, 0, -1), PunchKind.In),
            new Punch(At(18, 0, -1), PunchKind.BreakStart));

        var summary = WorkTimeCalculator.Compute(log, At(13));

        Assert.Equal(Hm(9), summary.Worked);
        Assert.Equal(WorkState.Finished, summary.State);
        Assert.Equal(At(18, 0, -1), summary.LeftAt);
    }

    [Fact]
    public void Висящая_пауза_сегодняшним_днём_остаётся_паузой()
    {
        // Человек на обеде.
        var log = Log(In(9), BreakStart(13));

        var summary = WorkTimeCalculator.Compute(log, At(13, 40));

        Assert.Equal(Hm(4), summary.Worked);
        Assert.Equal(WorkState.OnBreak, summary.State);
        Assert.Null(summary.LeftAt);
    }

    [Fact]
    public void Отметки_не_по_порядку_дают_верный_результат()
    {
        // Человек дописал забытую отметку руками в конец файла.
        var log = Log(BreakEnd(14), In(9), BreakStart(13));

        var summary = WorkTimeCalculator.Compute(log, At(18));

        Assert.Equal(Hm(8), summary.Worked);
        Assert.Equal(At(9), summary.ArrivedAt);
    }

    [Fact]
    public void Вечерняя_работа_после_полуночи_остаётся_в_том_же_дне()
    {
        var log = Log(In(22), Out(1, 0, 1));

        var summary = WorkTimeCalculator.Compute(log, At(2, 0, 1));

        Assert.Equal(WorkDay.DateOf(At(22)), WorkDay.DateOf(At(1, 0, 1)));
        Assert.Equal(Hm(3), summary.Worked);
        Assert.Equal(WorkState.Finished, summary.State);
    }

    [Fact]
    public void Ровно_восемь_часов_достигают_порога_уведомления()
    {
        var log = Log(In(9), Out(17));

        var summary = WorkTimeCalculator.Compute(log, At(17));

        Assert.Equal(Hm(8), summary.Worked);
        Assert.True(summary.GoalReached);
        Assert.Equal(TimeSpan.Zero, summary.Remaining);
        Assert.Equal(1d, summary.Progress, 6);
    }

    [Fact]
    public void Без_минуты_восемь_часов_порога_не_достигают()
    {
        var log = Log(In(9));

        var summary = WorkTimeCalculator.Compute(log, At(16, 59));

        Assert.False(summary.GoalReached);
        Assert.Equal(Hm(0, 1), summary.Remaining);
    }

    [Fact]
    public void Прогноз_окончания_во_время_паузы_не_определён()
    {
        var log = Log(In(9), BreakStart(13));

        var summary = WorkTimeCalculator.Compute(log, At(13, 40));

        Assert.Null(summary.ProjectedEnd);
    }

    [Fact]
    public void Прогноз_окончания_во_время_работы_учитывает_остаток()
    {
        var log = Log(In(9), BreakStart(13), BreakEnd(14));

        var summary = WorkTimeCalculator.Compute(log, At(15));

        Assert.Equal(At(18), summary.ProjectedEnd);
    }

    [Fact]
    public void Незакрытый_прошлый_день_закрывается_по_последней_активности()
    {
        // Ночная перезагрузка: heartbeat дотикал до 03:00, но человека не было с 19:20.
        var yesterday = Today.AddDays(-1);
        var log = Log(yesterday, new Punch(At(9, 0, -1), PunchKind.In));
        log.LastUserActivity = At(19, 20, -1);
        log.LastHeartbeat = At(3, 0);

        var summary = WorkTimeCalculator.Compute(log, At(13));

        Assert.Equal(Hm(10, 20), summary.Worked);
        Assert.Equal(At(19, 20, -1), summary.LeftAt);
        Assert.Equal(WorkState.Finished, summary.State);
    }

    [Fact]
    public void Незакрытый_прошлый_день_без_следов_активности_не_капает_до_сейчас()
    {
        var yesterday = Today.AddDays(-1);
        var log = Log(yesterday, new Punch(At(9, 0, -1), PunchKind.In));

        var summary = WorkTimeCalculator.Compute(log, At(13));

        Assert.Equal(TimeSpan.Zero, summary.Worked);
        Assert.Equal(WorkState.Finished, summary.State);
    }

    [Fact]
    public void Активность_за_границей_рабочего_дня_обрезается()
    {
        var yesterday = Today.AddDays(-1);
        var log = Log(yesterday, new Punch(At(9, 0, -1), PunchKind.In));
        log.LastUserActivity = At(12, 0); // машина не спала сутки и наврала

        var summary = WorkTimeCalculator.Compute(log, At(13));

        Assert.Equal(WorkDay.EndOf(yesterday, Offset), summary.LeftAt);
        Assert.Equal(Hm(19), summary.Worked);
    }

    [Fact]
    public void Возврат_после_ручного_ухода_снова_запускает_счётчик()
    {
        var log = Log(In(9), Out(13), BreakEnd(14));

        var summary = WorkTimeCalculator.Compute(log, At(15));

        Assert.Equal(Hm(5), summary.Worked);
        Assert.Equal(WorkState.Working, summary.State);
        Assert.Null(summary.LeftAt);
    }

    [Fact]
    public void Переработка_даёт_положительный_баланс_и_полный_прогресс()
    {
        var log = Log(In(9));

        var summary = WorkTimeCalculator.Compute(log, At(19));

        Assert.Equal(Hm(2), summary.Balance);
        Assert.Equal(1d, summary.Progress, 6);
        Assert.True(summary.GoalReached);
    }

    [Fact]
    public void Норма_берётся_из_правил_дня()
    {
        var log = Log(In(9));

        var summary = WorkTimeCalculator.Compute(log, At(15), Goal(Hm(6)));

        Assert.True(summary.GoalReached);
        Assert.Equal(Hm(6), summary.Goal);
    }

    [Fact]
    public void При_совпадении_меток_решает_порядок_в_файле()
    {
        // Заблокировал и тут же разблокировал — перерыва не было.
        var lockedThenBack = WorkTimeCalculator.Compute(Log(In(9), BreakStart(13), BreakEnd(13)), At(14));

        Assert.Equal(Hm(5), lockedThenBack.Worked);
        Assert.Equal(WorkState.Working, lockedThenBack.State);

        // Разблокировал и тут же заблокировал — человек ушёл.
        var backThenLocked = WorkTimeCalculator.Compute(Log(In(9), BreakEnd(13), BreakStart(13)), At(14));

        Assert.Equal(Hm(4), backThenLocked.Worked);
        Assert.Equal(WorkState.OnBreak, backThenLocked.State);
    }
}