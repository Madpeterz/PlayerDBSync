using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SkiaSharp;

namespace PlayerDBSync
{
    /// <summary>
    ///  The site's background (its partials/backdrop.blade.php and site.js): one of its pictures (Backgrounds\*.webp),
    ///  picked at random, covering the window under a dark veil and fixed behind the pages
    ///  (<see cref="BackdropPanel"/>). Every <see cref="ChangeEvery"/> a different one fades in over the last.
    /// </summary>
    internal sealed class Backdrop : IDisposable
    {
        public static readonly TimeSpan ChangeEvery = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(2);

        // The site's --veil: the page colour, nearly opaque, so the picture only just shows.
        private const float VeilOpacity = 0.92f;

        private readonly Control _host;
        private readonly string[] _pictures;
        private readonly System.Windows.Forms.Timer _changeTimer = new() { Interval = (int)ChangeEvery.TotalMilliseconds };
        private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = 33 };
        private readonly Random _random = new();

        // The picture showing, and the one fading in over it (null when none is), each with its copy scaled to cover
        // the window at its current size.
        private string? _currentName;
        private Bitmap? _current;
        private Bitmap? _currentScaled;
        private Bitmap? _next;
        private Bitmap? _nextScaled;
        private DateTime _fadeStart;
        private float _fade;
        private bool _changing;

        // All of it composed at the window's size, which BackdropPanel and what's on it paint from.
        private Bitmap? _frame;

        /// <summary>Raised when the picture has changed (or moved on in its fade), so what shows it repaints.</summary>
        public event Action? Changed;

        public Backdrop(Control host)
        {
            _host = host;
            _pictures = typeof(Backdrop).Assembly.GetManifestResourceNames()
                .Where(n => n.Contains(".Backgrounds.", StringComparison.Ordinal) && n.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (_pictures.Length > 0)
            {
                _currentName = _pictures[_random.Next(_pictures.Length)];
                _current = Decode(_currentName);
            }
            Compose();

            _host.SizeChanged += (_, _) =>
            {
                Rescale();
                Compose();
                Changed?.Invoke();
            };
            _changeTimer.Tick += (_, _) => ChangeNow();
            _fadeTimer.Tick += (_, _) => FadeStep();
            _changeTimer.Start();
        }

        /// <summary>
        ///  Fades in a different picture now (the timer starts over). Ignored while one is still loading or fading in.
        /// </summary>
        public void ChangeNow()
        {
            var choices = _pictures.Where(p => p != _currentName).ToArray();
            if (_changing || choices.Length == 0)
            {
                return;
            }
            _changing = true;
            _changeTimer.Stop();
            _changeTimer.Start();

            // Decoded off the UI thread, then handed back to it: the fade's timer only ticks on the thread that owns the window.
            var name = choices[_random.Next(choices.Length)];
            Task.Run(() => Decode(name)).ContinueWith(decoded =>
            {
                try
                {
                    _host.BeginInvoke(() => StartFade(name, decoded));
                }
                catch (InvalidOperationException)
                {
                    // The window has closed meanwhile.
                    if (decoded.IsCompletedSuccessfully)
                    {
                        decoded.Result?.Dispose();
                    }
                }
            }, TaskScheduler.Default);
        }

        private void StartFade(string name, Task<Bitmap?> decoded)
        {
            // A picture that won't load: keep the one showing.
            var next = decoded.IsCompletedSuccessfully ? decoded.Result : null;
            if (next == null || _host.IsDisposed)
            {
                next?.Dispose();
                _changing = false;
                return;
            }

            _currentName = name;
            _next = next;
            _nextScaled = Cover(_next);
            _fade = 0;
            _fadeStart = DateTime.Now;
            // With Windows' animations off (its version of the site's reduced motion), it simply changes.
            if (SystemInformation.UIEffectsEnabled)
            {
                _fadeTimer.Start();
            }
            else
            {
                FinishFade();
            }
        }

        /// <summary>
        ///  Paints the part of the backdrop behind <paramref name="control"/>. False if there's nothing to paint
        ///  (no picture, or the window is minimised).
        /// </summary>
        public bool PaintBehind(Control control, Graphics g)
        {
            if (_frame == null)
            {
                return false;
            }
            var at = _host.PointToClient(control.PointToScreen(Point.Empty));
            g.DrawImage(_frame, new Rectangle(Point.Empty, control.Size), new Rectangle(at, control.Size), GraphicsUnit.Pixel);
            return true;
        }

        private void FadeStep()
        {
            _fade = (float)Math.Min(1, (DateTime.Now - _fadeStart) / FadeTime);
            if (_fade >= 1)
            {
                FinishFade();
                return;
            }
            Compose();
            Changed?.Invoke();
        }

        private void FinishFade()
        {
            _fadeTimer.Stop();
            _current?.Dispose();
            _currentScaled?.Dispose();
            _current = _next;
            _currentScaled = _nextScaled;
            _next = null;
            _nextScaled = null;
            _changing = false;
            Compose();
            Changed?.Invoke();
        }

        private void Rescale()
        {
            _currentScaled?.Dispose();
            _currentScaled = _current == null ? null : Cover(_current);
            _nextScaled?.Dispose();
            _nextScaled = _next == null ? null : Cover(_next);
        }

        /// <summary>The picture, then the one fading in over it, then the veil, at the window's size.</summary>
        private void Compose()
        {
            _frame?.Dispose();
            _frame = null;
            var size = _host.ClientSize;
            if (_current == null || size.Width <= 0 || size.Height <= 0)
            {
                return;
            }
            _currentScaled ??= Cover(_current);

            _frame = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(_frame);
            g.DrawImageUnscaled(_currentScaled!, 0, 0);
            if (_nextScaled != null && _fade > 0)
            {
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(new ColorMatrix { Matrix33 = _fade });
                g.DrawImage(_nextScaled, new Rectangle(Point.Empty, size), 0, 0, size.Width, size.Height, GraphicsUnit.Pixel, attributes);
            }
            using var veil = new SolidBrush(Color.FromArgb((int)(VeilOpacity * 255), Theme.Background));
            g.FillRectangle(veil, 0, 0, size.Width, size.Height);
        }

        /// <summary>
        ///  <paramref name="picture"/> scaled to cover the window, centred and cut to fit (the site's
        ///  <c>center / cover</c>); null while the window has no size.
        /// </summary>
        private Bitmap? Cover(Bitmap picture)
        {
            var size = _host.ClientSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return null;
            }
            var scale = Math.Max((float)size.Width / picture.Width, (float)size.Height / picture.Height);
            var width = picture.Width * scale;
            var height = picture.Height * scale;

            var scaled = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(scaled);
            // Barely visible under the veil, so quick (it's redone as the window is resized) beats sharp.
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(picture, (size.Width - width) / 2, (size.Height - height) / 2, width, height);
            return scaled;
        }

        /// <summary>An embedded WebP picture, decoded with SkiaSharp (GDI+ can't read WebP).</summary>
        private static Bitmap? Decode(string resourceName)
        {
            using var stream = typeof(Backdrop).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return null;
            }
            using var decoded = SKBitmap.Decode(stream);
            if (decoded == null)
            {
                return null;
            }
            using var pixmap = decoded.PeekPixels();
            var bitmap = new Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppPArgb);
            var bits = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            bool read = false;
            try
            {
                // 32bppPArgb is premultiplied BGRA in memory.
                var info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                read = pixmap.ReadPixels(info, bits.Scan0, bits.Stride);
            }
            finally
            {
                bitmap.UnlockBits(bits);
                if (!read)
                {
                    bitmap.Dispose();
                }
            }
            return read ? bitmap : null;
        }

        public void Dispose()
        {
            _changeTimer.Dispose();
            _fadeTimer.Dispose();
            _current?.Dispose();
            _currentScaled?.Dispose();
            _next?.Dispose();
            _nextScaled?.Dispose();
            _frame?.Dispose();
        }
    }
}
