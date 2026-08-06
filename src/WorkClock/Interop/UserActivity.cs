namespace WorkClock.Interop;

/// <summary>
/// Сколько человек не трогал клавиатуру и мышь. Нужно в двух местах: чтобы сдвинуть
/// назад метку паузы при автолоке по доменной политике и чтобы корректно закрыть день,
/// если машина умерла с открытым интервалом.
/// </summary>
public static class UserActivity
{
    /// <summary>Время простоя пользователя.</summary>
    public static TimeSpan GetIdleTime()
    {
        var info = new NativeMethods.LastInputInfo
        {
            CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LastInputInfo>(),
        };

        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        return IdleFromTicks(unchecked((uint)NativeMethods.GetTickCount64()), info.DwTime);
    }

    /// <summary>
    /// Разница двух 32-битных счётчиков тиков.
    /// </summary>
    /// <remarks>
    /// <c>GetLastInputInfo</c> отдаёт 32-битный счётчик, который переполняется через 49 суток
    /// аптайма. Поэтому 64-битный <c>GetTickCount64</c> обрезается до тех же 32 бит:
    /// беззнаковое вычитание переносит переполнение само, а вычитание в 64 битах дало бы
    /// после переполнения простой длиной в полтора месяца.
    /// </remarks>
    public static TimeSpan IdleFromTicks(uint nowTicks, uint lastInputTicks) =>
        TimeSpan.FromMilliseconds(unchecked(nowTicks - lastInputTicks));
}