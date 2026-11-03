using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace PlayerDBSync
{
    /// <summary>
    ///  The site's background (its partials/backdrop.blade.php and site.js): one of its pictures picked at random,
    ///  covering the window under a dark veil, and every <see cref="ChangeEvery"/> a different one fading in over the last.
    ///  The veil is the window's own colour, so the pictures are shown at the opacity it lets through.
    /// </summary>
    internal sealed class Backdrop
    {
        public static readonly TimeSpan ChangeEvery = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(2);

        // The site's --veil is 92% opaque, so the picture only just shows.
        private const double PictureOpacity = 0.08;

        private static readonly Uri Folder = new("avares://PlayerDBSync.Avalonia/Assets/Backgrounds/");

        private readonly Image[] _layers;
        private readonly Uri[] _pictures;
        private readonly DispatcherTimer _timer = new() { Interval = ChangeEvery };
        private int _front = -1;
        private Uri? _current;

        public Backdrop(Image first, Image second)
        {
            _layers = [first, second];
            foreach (var layer in _layers)
            {
                layer.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = FadeTime }];
            }
            _pictures = AssetLoader.GetAssets(Folder, null)
                .Where(u => u.AbsolutePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                .Order()
                .ToArray();
            _timer.Tick += (_, _) => ChangeNow();
            ChangeNow();
            _timer.Start();
        }

        /// <summary>Fades in a different picture now (the timer starts over).</summary>
        public void ChangeNow()
        {
            var choices = _pictures.Where(p => p != _current).ToArray();
            if (choices.Length == 0)
            {
                return;
            }
            Bitmap picture;
            var next = choices[Random.Shared.Next(choices.Length)];
            try
            {
                using var stream = AssetLoader.Open(next);
                picture = new Bitmap(stream);
            }
            catch (Exception)
            {
                // A picture that won't load: keep the one showing.
                return;
            }

            _timer.Stop();
            _timer.Start();
            _current = next;
            var back = _front < 0 ? 0 : 1 - _front;
            _layers[back].Source = picture;
            _layers[back].Opacity = PictureOpacity;
            if (_front >= 0)
            {
                _layers[_front].Opacity = 0;
            }
            _front = back;
        }
    }
}
