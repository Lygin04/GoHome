using GoHome.Interop;

namespace GoHome.Ui.Design;

/// <summary>
/// Что у окна под курсором, когда рамку рисует приложение.
/// </summary>
/// <remarks>
/// Вынесено из окна отдельно, потому что здесь одна ошибка на всю сложную часть: если
/// сторона ответит раньше угла, окно перестанет тянуться по диагонали. Заметить это на глаз
/// трудно — окно ведь тянется, просто не за угол, — а проверить чистую геометрию легко.
/// </remarks>
internal static class FrameHitTest
{
    /// <summary>Что под точкой.</summary>
    /// <param name="client">Точка в координатах клиентской области, она же — всё окно.</param>
    /// <param name="size">Размер клиентской области.</param>
    /// <param name="captionHeight">Высота полосы заголовка.</param>
    /// <param name="border">Полоса растяжения вдоль стороны.</param>
    /// <param name="corner">Сторона квадрата растяжения в углу — шире полосы стороны.</param>
    /// <param name="sizable">Окно вообще можно тянуть. Развёрнутое и с жёсткой рамкой — нельзя.</param>
    public static nint At(Point client, Size size, int captionHeight, int border, int corner, bool sizable)
    {
        if (sizable)
        {
            var left = client.X < corner;
            var right = client.X >= size.Width - corner;
            var top = client.Y < corner;
            var bottom = client.Y >= size.Height - corner;

            // Углы — раньше сторон. Про этот порядок забывают чаще всего.
            if (top && left)
            {
                return NativeMethods.HtTopLeft;
            }

            if (top && right)
            {
                return NativeMethods.HtTopRight;
            }

            if (bottom && left)
            {
                return NativeMethods.HtBottomLeft;
            }

            if (bottom && right)
            {
                return NativeMethods.HtBottomRight;
            }

            if (client.Y < border)
            {
                return NativeMethods.HtTop;
            }

            if (client.Y >= size.Height - border)
            {
                return NativeMethods.HtBottom;
            }

            if (client.X < border)
            {
                return NativeMethods.HtLeft;
            }

            if (client.X >= size.Width - border)
            {
                return NativeMethods.HtRight;
            }
        }

        // Перетаскивается вся полоса заголовка. До её кнопок здесь не доходит: там отвечает
        // сама полоса, и отвечает она только ниже полосы растяжения верхнего края —
        // иначе за верх окна нельзя было бы потянуть в том углу, где стоит закрытие.
        return client.Y < captionHeight ? NativeMethods.HtCaption : NativeMethods.HtClient;
    }
}
