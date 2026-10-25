using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PlayerDBSync
{
    /// <summary>
    ///  The PlayerDB site's look (its public/style.css): dark only, cards a shade lighter than the page with a 1px
    ///  border and rounded corners, muted secondary text and a blue accent.
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Background = ColorTranslator.FromHtml("#14171c"); // --bg
        public static readonly Color Surface = ColorTranslator.FromHtml("#1c2027");    // --surface
        public static readonly Color Text = ColorTranslator.FromHtml("#e6e8ec");       // --text
        public static readonly Color Muted = ColorTranslator.FromHtml("#9aa1ad");      // --muted
        public static readonly Color Border = ColorTranslator.FromHtml("#2d333d");     // --border
        public static readonly Color Accent = ColorTranslator.FromHtml("#79a6f2");     // --accent
        public static readonly Color Hover = ColorTranslator.FromHtml("#232935");      // --row-hover
        public static readonly Color Good = ColorTranslator.FromHtml("#5cc98a");       // --fresh
        public static readonly Color Bad = ColorTranslator.FromHtml("#f06a5f");        // --stale

        // Cards, and the buttons and inputs on them.
        public const int CardRadius = 8;
        public const int ControlRadius = 6;

        public static readonly Font Brand = new("Segoe UI", 13f, FontStyle.Bold);
        public static readonly Font Heading = new("Segoe UI Semibold", 10.5f);
        public static readonly Font Strong = new("Segoe UI Semibold", 9f);
        public static readonly Font Mono = new("Consolas", 9f);

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Fills (and outlines, if <paramref name="border"/> isn't empty) a rounded rectangle just inside <paramref name="bounds"/>.</summary>
        public static void DrawRounded(Graphics g, Rectangle bounds, float radius, Color fill, Color border)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1, bounds.Height - 1), radius);
            if (fill.A > 0)
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }
            if (border.A > 0)
            {
                using var pen = new Pen(border);
                g.DrawPath(pen, path);
            }
        }

        /// <summary><paramref name="a"/> moved <paramref name="amount"/> (0 to 1) of the way to <paramref name="b"/>.</summary>
        public static Color Mix(Color a, Color b, float amount) => Color.FromArgb(
            (int)(a.A + (b.A - a.A) * amount),
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount));

        /// <summary>The colour a control's parent paints behind it, for drawing outside its rounded corners.</summary>
        public static Color BackOf(Control control) => control.Parent?.BackColor ?? Background;

        /// <summary>
        ///  Paints what shows behind <paramref name="control"/> (outside its rounded corners): the backdrop on a page,
        ///  otherwise its parent's colour.
        /// </summary>
        public static void PaintBehind(Control control, Graphics g)
        {
            if (control.Parent is BackdropPanel { Backdrop: { } backdrop } && backdrop.PaintBehind(control, g))
            {
                return;
            }
            g.Clear(BackOf(control));
        }

        /// <summary>
        ///  The window's edge on Windows 11: rounded corners and a 1px border in the theme's colour, as the site's cards.
        ///  Dark mode also darkens its system menu (Alt+Space).
        /// </summary>
        public static void StyleWindow(Form form) => WhenCreated(form, handle =>
        {
            int on = 1;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
            int round = DwmwcpRound;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref round, sizeof(int));
            int border = ColorTranslator.ToWin32(Border);
            DwmSetWindowAttribute(handle, DwmwaBorderColor, ref border, sizeof(int));
        });

        /// <summary>Dark scroll bars, e.g. on a multi-line text box.</summary>
        public static void DarkScrollBars(Control control) =>
            WhenCreated(control, handle => SetWindowTheme(handle, "DarkMode_Explorer", null));

        // Older Windows ignores what it doesn't know, so failures are left alone.
        private static void WhenCreated(Control control, Action<IntPtr> apply)
        {
            if (control.IsHandleCreated)
            {
                apply(control.Handle);
            }
            control.HandleCreated += (_, _) => apply(control.Handle);
        }

        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaBorderColor = 34;
        private const int DwmwcpRound = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);
    }
}
