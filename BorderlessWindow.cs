using System.Runtime.InteropServices;

namespace PlayerDBSync
{
    /// <summary>
    ///  What a window without Windows' frame (<see cref="FormBorderStyle.None"/>) needs to still move and resize:
    ///  dragging its header moves it, and its edges resize it.
    /// </summary>
    internal static class BorderlessWindow
    {
        private const int WmNcHitTest = 0x84;
        private const int WmNcLButtonDown = 0xA1;
        private const int HtTransparent = -1;
        private const int HtCaption = 2;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;

        // How far in from the window's edge it can be grabbed to resize, at 96 dpi.
        private const int ResizeBorder = 6;

        /// <summary>
        ///  Dragging any of <paramref name="handles"/> moves <paramref name="form"/>, as its title bar would (snapping
        ///  to the screen's edges included). The drag only starts once the mouse has moved a little, so a click on one
        ///  is still a click.
        /// </summary>
        public static void AddDragHandles(Form form, params Control[] handles)
        {
            Point? pressedAt = null;
            foreach (var handle in handles)
            {
                handle.MouseDown += (_, e) => pressedAt = e.Button == MouseButtons.Left ? Cursor.Position : null;
                handle.MouseUp += (_, _) => pressedAt = null;
                handle.MouseMove += (_, e) =>
                {
                    if (pressedAt is not { } from || e.Button != MouseButtons.Left)
                    {
                        return;
                    }
                    var drag = SystemInformation.DragSize;
                    if (Math.Abs(Cursor.Position.X - from.X) < drag.Width && Math.Abs(Cursor.Position.Y - from.Y) < drag.Height)
                    {
                        return;
                    }
                    pressedAt = null;
                    // Hand the drag to Windows as if the title bar had been pressed.
                    ReleaseCapture();
                    SendMessage(form.Handle, WmNcLButtonDown, HtCaption, 0);
                };
            }
        }

        /// <summary>
        ///  In <paramref name="form"/>'s WndProc: answers a hit test on its edge with the edge, so Windows resizes it
        ///  from there. False for any other message, or away from the edge.
        /// </summary>
        public static bool HitTestEdge(Form form, ref Message m)
        {
            var edge = m.Msg == WmNcHitTest ? EdgeAt(form, PointOf(m)) : 0;
            if (edge == 0)
            {
                return false;
            }
            m.Result = edge;
            return true;
        }

        /// <summary>
        ///  In the WndProc of a control that reaches the window's edge (the header, the pages): leaves a hit test on
        ///  the edge to the window (<see cref="HitTestEdge"/>). False for any other message, or away from the edge.
        /// </summary>
        public static bool PassEdgeToWindow(Control control, ref Message m)
        {
            if (m.Msg != WmNcHitTest || control.FindForm() is not { } form || EdgeAt(form, PointOf(m)) == 0)
            {
                return false;
            }
            m.Result = HtTransparent;
            return true;
        }

        /// <summary>The resize edge or corner of <paramref name="form"/> at <paramref name="screen"/>; 0 if none.</summary>
        private static int EdgeAt(Form form, Point screen)
        {
            if (form.WindowState != FormWindowState.Normal)
            {
                return 0;
            }
            var border = ResizeBorder * form.DeviceDpi / 96;
            var bounds = form.Bounds;
            bool left = screen.X < bounds.Left + border;
            bool right = screen.X >= bounds.Right - border;
            bool top = screen.Y < bounds.Top + border;
            bool bottom = screen.Y >= bounds.Bottom - border;
            return (top, bottom, left, right) switch
            {
                (true, _, true, _) => HtTopLeft,
                (true, _, _, true) => HtTopRight,
                (_, true, true, _) => HtBottomLeft,
                (_, true, _, true) => HtBottomRight,
                (true, _, _, _) => HtTop,
                (_, true, _, _) => HtBottom,
                (_, _, true, _) => HtLeft,
                (_, _, _, true) => HtRight,
                _ => 0,
            };
        }

        // A hit test's point, in screen coordinates; each half is signed, as screens left of or above the main one are negative.
        private static Point PointOf(Message m) => new((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF));

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    }
}
