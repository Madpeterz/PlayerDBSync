using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PlayerDBSync
{
    /// <summary>Draws a menu (the tray icon's) in the theme: the surface colour with a border, items lit on hover.</summary>
    internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        public ThemedMenuRenderer() : base(new Colours())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Theme.Border);
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
        }

        private sealed class Colours : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Surface;
            public override Color ImageMarginGradientBegin => Theme.Surface;
            public override Color ImageMarginGradientMiddle => Theme.Surface;
            public override Color ImageMarginGradientEnd => Theme.Surface;
            public override Color MenuBorder => Theme.Border;
            public override Color MenuItemBorder => Theme.Hover;
            public override Color MenuItemSelected => Theme.Hover;
            public override Color MenuItemSelectedGradientBegin => Theme.Hover;
            public override Color MenuItemSelectedGradientEnd => Theme.Hover;
            public override Color SeparatorDark => Theme.Border;
            public override Color SeparatorLight => Theme.Border;
        }
    }

    /// <summary>The site's .card: a rounded panel in the surface colour with a 1px border.</summary>
    internal sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Theme.PaintBehind(this, e.Graphics);
            Theme.DrawRounded(e.Graphics, ClientRectangle, Theme.CardRadius, BackColor, Theme.Border);
        }
    }

    /// <summary>
    ///  A page: see-through to the window's <see cref="PlayerDBSync.Backdrop"/>, which stays put as the page scrolls
    ///  (the site's fixed background).
    /// </summary>
    internal sealed class BackdropPanel : Panel
    {
        public BackdropPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Backdrop? Backdrop { get; set; }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Backdrop?.PaintBehind(this, e.Graphics) != true)
            {
                e.Graphics.Clear(BackColor);
            }
        }

        // Scrolling moves what was drawn along with the page; the backdrop must stay where it is.
        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate(true);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate(true);
        }

        // It reaches the window's edges, which resize the window.
        protected override void WndProc(ref Message m)
        {
            if (!BorderlessWindow.PassEdgeToWindow(this, ref m))
            {
                base.WndProc(ref m);
            }
        }
    }

    /// <summary>The site's header (.top): the surface colour with a border along the bottom.</summary>
    internal sealed class HeaderBar : Panel
    {
        public HeaderBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }

        // It reaches the window's edges, which resize the window.
        protected override void WndProc(ref Message m)
        {
            if (!BorderlessWindow.PassEdgeToWindow(this, ref m))
            {
                base.WndProc(ref m);
            }
        }
    }

    internal enum WindowGlyph
    {
        Minimize,
        Close,

        /// <summary>An up arrow in the site's green: a newer release is out.</summary>
        Update,
    }

    /// <summary>
    ///  A window button in the header (the window has no title bar of its own), drawn as the site's icon buttons:
    ///  a muted glyph, lit on hover; red for closing, green for updating.
    /// </summary>
    internal sealed class WindowButton : Control
    {
        private WindowGlyph _glyph;
        private bool _hover;
        private bool _pressed;

        public WindowButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        [DefaultValue(WindowGlyph.Minimize)]
        public WindowGlyph Glyph
        {
            get => _glyph;
            set
            {
                _glyph = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.PaintBehind(this, g);
            float scale = DeviceDpi / 96f;
            float cx = Width / 2f;
            float cy = Height / 2f;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (_glyph == WindowGlyph.Update)
            {
                PaintUpdateOrb(g, scale, cx, cy);
                return;
            }

            var close = _glyph == WindowGlyph.Close;
            if (_hover || _pressed)
            {
                var fill = close ? Theme.Bad : Theme.Hover;
                Theme.DrawRounded(g, ClientRectangle, Theme.ControlRadius, _pressed ? Theme.Mix(fill, Color.White, 0.1f) : fill, Color.Empty);
            }

            var colour = _hover || _pressed ? (close ? Color.White : Theme.Text) : Theme.Muted;
            float half = 5 * scale;
            using var pen = new Pen(colour, 1.5f * scale);
            if (close)
            {
                g.DrawLine(pen, cx - half, cy - half, cx + half, cy + half);
                g.DrawLine(pen, cx - half, cy + half, cx + half, cy - half);
            }
            else
            {
                g.DrawLine(pen, cx - half, cy, cx + half, cy);
            }
        }

        /// <summary>A green orb, lit from its top left and brighter on hover, with a dark up arrow in it.</summary>
        private void PaintUpdateOrb(Graphics g, float scale, float cx, float cy)
        {
            float r = 10 * scale;
            var orb = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
            var green = _pressed ? Theme.Mix(Theme.Good, Color.White, 0.3f) : _hover ? Theme.Mix(Theme.Good, Color.White, 0.15f) : Theme.Good;
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddEllipse(orb);
                using var shading = new System.Drawing.Drawing2D.PathGradientBrush(path)
                {
                    CenterPoint = new PointF(cx - r * 0.35f, cy - r * 0.35f),
                    CenterColor = Theme.Mix(green, Color.White, 0.35f),
                    SurroundColors = [Theme.Mix(green, Color.Black, 0.15f)],
                };
                g.FillPath(shading, path);
            }

            float half = 5 * scale;
            float head = half * 0.8f;
            using var pen = new Pen(Theme.Background, 2f * scale)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
            };
            g.DrawLine(pen, cx, cy + half, cx, cy - half);
            g.DrawLines(pen, [new PointF(cx - head, cy - half + head), new PointF(cx, cy - half), new PointF(cx + head, cy - half + head)]);
        }
    }

    /// <summary>
    ///  A page link in the header, as the site's nav: muted, or in the text colour and underlined in the accent
    ///  while its page is showing.
    /// </summary>
    internal sealed class NavTab : Control
    {
        private bool _active;
        private bool _hover;

        public NavTab()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        [DefaultValue(false)]
        public bool Active
        {
            get => _active;
            set
            {
                _active = value;
                Invalidate();
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var text = TextRenderer.MeasureText(Text, Font);
            return new Size(text.Width + 16, text.Height + 12);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.PaintBehind(this, g);
            var text = TextRenderer.MeasureText(Text, Font);
            var textBounds = new Rectangle((Width - text.Width) / 2, (Height - text.Height) / 2, text.Width, text.Height);
            TextRenderer.DrawText(g, Text, Font, textBounds, _active || _hover ? Theme.Text : Theme.Muted,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            if (_active)
            {
                using var accent = new SolidBrush(Theme.Accent);
                g.FillRectangle(accent, textBounds.Left, textBounds.Bottom + 4, textBounds.Width, 2);
            }
            if (Focused && ShowFocusCues)
            {
                Theme.DrawRounded(g, Rectangle.Inflate(textBounds, 6, 4), 4, Color.Empty, Theme.Muted);
            }
        }
    }

    internal enum ButtonKind
    {
        /// <summary>The site's plain buttons: the surface colour, a border and text.</summary>
        Secondary,

        /// <summary>The site's action buttons: filled with the accent.</summary>
        Primary,

        /// <summary>Something that can't be taken back: red text and border, filling red on hover.</summary>
        Danger,
    }

    /// <summary>A button drawn as the site's: rounded, flat and in the theme's colours (see <see cref="ButtonKind"/>).</summary>
    internal sealed class ThemedButton : Button
    {
        private ButtonKind _kind;
        private bool _hover;
        private bool _pressed;

        public ThemedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
        }

        [DefaultValue(ButtonKind.Secondary)]
        public ButtonKind Kind
        {
            get => _kind;
            set
            {
                _kind = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            // What's behind it, also mixed into the disabled colours below (near enough, on the backdrop).
            var back = Theme.BackOf(this);
            Theme.PaintBehind(this, g);

            var hot = Enabled && (_hover || _pressed);
            var (fill, border, text) = _kind switch
            {
                ButtonKind.Primary => (hot ? Theme.Mix(Theme.Accent, Color.White, _pressed ? 0.2f : 0.1f) : Theme.Accent, Color.Empty, Color.White),
                ButtonKind.Danger => (hot ? Theme.Bad : Color.Empty, Theme.Bad, hot ? Color.White : Theme.Bad),
                _ => (hot ? Theme.Hover : Theme.Surface, hot ? Theme.Muted : Theme.Border, Theme.Text),
            };
            if (!Enabled)
            {
                // As the site's disabled buttons: half see-through.
                fill = fill.A == 0 ? fill : Theme.Mix(fill, back, 0.5f);
                border = border.A == 0 ? border : Theme.Mix(border, back, 0.5f);
                text = Theme.Mix(text, back, 0.5f);
            }

            Theme.DrawRounded(g, ClientRectangle, Theme.ControlRadius, fill, border);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
            {
                var ring = Rectangle.Inflate(ClientRectangle, -3, -3);
                Theme.DrawRounded(g, ring, Theme.ControlRadius - 2, Color.Empty, Theme.Mix(text, fill.A == 0 ? back : fill, 0.4f));
            }
        }
    }

    /// <summary>A check box drawn as the theme's: a rounded box, filled with the accent and ticked when checked.</summary>
    internal sealed class ThemedCheckBox : CheckBox
    {
        private const int Gap = 8;
        private bool _hover;

        public ThemedCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        private int BoxSize => Font.Height;

        public override Size GetPreferredSize(Size proposedSize)
        {
            var text = TextRenderer.MeasureText(Text, Font);
            return new Size(BoxSize + Gap + text.Width + 2, Math.Max(BoxSize, text.Height) + 4);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.PaintBehind(this, g);

            var box = new Rectangle(0, (Height - BoxSize) / 2, BoxSize, BoxSize);
            if (Checked)
            {
                Theme.DrawRounded(g, box, 4, Theme.Accent, Color.Empty);
                // A tick, in proportion to the box.
                using var pen = new Pen(Color.White, Math.Max(2f, BoxSize / 8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                float s = BoxSize;
                g.DrawLines(pen, new[]
                {
                    new PointF(box.Left + s * 0.25f, box.Top + s * 0.52f),
                    new PointF(box.Left + s * 0.43f, box.Top + s * 0.70f),
                    new PointF(box.Left + s * 0.76f, box.Top + s * 0.32f),
                });
            }
            else
            {
                Theme.DrawRounded(g, box, 4, Theme.Background, _hover && Enabled ? Theme.Muted : Theme.Border);
            }

            var textBounds = new Rectangle(box.Right + Gap, 0, Width - box.Right - Gap, Height);
            TextRenderer.DrawText(g, Text, Font, textBounds, Enabled ? ForeColor : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues)
            {
                Theme.DrawRounded(g, Rectangle.Inflate(box, 2, 2), 5, Color.Empty, Theme.Muted);
            }
        }
    }

    /// <summary>
    ///  A text box framed as the site's inputs: the page colour, a 1px rounded border, in the accent while it has the
    ///  focus. It holds one borderless <see cref="TextBox"/>, which it lays out and colours.
    /// </summary>
    internal sealed class InputFrame : Panel
    {
        private const int Inset = 8;

        public InputFrame()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
            Cursor = Cursors.IBeam;
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control is TextBox box)
            {
                box.BorderStyle = BorderStyle.None;
                box.BackColor = BackColor;
                box.ForeColor = Theme.Text;
            }
            e.Control!.GotFocus += (_, _) => Invalidate();
            e.Control.LostFocus += (_, _) => Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            foreach (Control child in Controls)
            {
                child.SetBounds(Inset, (Height - child.Height) / 2, Width - Inset * 2, child.Height);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Controls.Cast<Control>().FirstOrDefault()?.Focus();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Theme.PaintBehind(this, e.Graphics);
            Theme.DrawRounded(e.Graphics, ClientRectangle, Theme.ControlRadius, BackColor, ContainsFocus ? Theme.Accent : Theme.Border);
        }
    }

    /// <summary>
    ///  A schedule row's bar, drawn as the site's bars (.side-split): a thin rounded track with the fill over it.
    ///  While <see cref="Running"/>, a stretch of fill sweeps along it instead.
    /// </summary>
    internal sealed class ScheduleBar : Control
    {
        private const int TrackHeight = 8;
        private const float SweepWidth = 0.3f;

        private readonly System.Windows.Forms.Timer _sweep = new() { Interval = 30 };
        private float _fraction;
        private bool _running;
        private float _sweepAt;
        private Color _fill = Theme.Accent;

        public ScheduleBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            _sweep.Tick += (_, _) =>
            {
                _sweepAt = (_sweepAt + 0.02f) % (1 + SweepWidth);
                Invalidate();
            };
        }

        /// <summary>How full it is, 0 to 1.</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float Fraction
        {
            get => _fraction;
            set
            {
                value = Math.Clamp(value, 0, 1);
                // Repainted only when the fill's end moves to another pixel.
                bool moved = (int)(value * Width) != (int)(_fraction * Width);
                _fraction = value;
                if (moved)
                {
                    Invalidate();
                }
            }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Running
        {
            get => _running;
            set
            {
                if (_running == value)
                {
                    return;
                }
                _running = value;
                _sweepAt = 0;
                _sweep.Enabled = value;
                Invalidate();
            }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color FillColor
        {
            get => _fill;
            set
            {
                if (_fill != value)
                {
                    _fill = value;
                    Invalidate();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sweep.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.PaintBehind(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var track = new RectangleF(0, (Height - TrackHeight) / 2f, Width, TrackHeight);
            using var path = Theme.RoundedRect(track, TrackHeight / 2f);
            using (var trackBrush = new SolidBrush(Theme.Border))
            {
                g.FillPath(trackBrush, path);
            }

            var fill = _running
                ? new RectangleF((_sweepAt - SweepWidth) * Width, track.Top, SweepWidth * Width, track.Height)
                : new RectangleF(0, track.Top, _fraction * Width, track.Height);
            if (fill.Width > 0)
            {
                g.SetClip(path);
                using var fillBrush = new SolidBrush(_fill);
                g.FillRectangle(fillBrush, fill);
                g.ResetClip();
            }
        }
    }
}
