using SkiaSharp;

namespace PlayerDBSync
{
    /// <summary>Takes a picture of the game window. How depends on the system: see <see cref="WindowsGameCapture"/> and <see cref="LinuxScreenCapture"/>.</summary>
    internal interface IGameCapture
    {
        /// <summary>The game's client area, or why it couldn't be taken (the game isn't running, is minimised…). The caller disposes the image.</summary>
        (SKBitmap? Image, string? Error) Capture();
    }

    /// <summary>What every way of capturing the game window shares, and the saving of its pictures as WebP, for raid screenshots and Demo OCR alike.</summary>
    internal static class GameCapture
    {
        /// <summary>The game's process name, without ".exe".</summary>
        public const string GameProcessName = "archeage";

        // Every 8th pixel of every 8th row is checked: enough to find any picture, without reading all of it.
        private const int BlackCheckStep = 8;

        /// <summary>
        ///  True if every sampled pixel of <paramref name="image"/> is black. Neither way of capturing takes the screen
        ///  in exclusive mode, so a game in exclusive full screen comes out black; that isn't worth saving.
        /// </summary>
        public static bool IsBlack(SKBitmap image)
        {
            if (image.BytesPerPixel != 4)
            {
                return false;
            }
            var pixels = image.GetPixelSpan();
            var stride = image.RowBytes;
            for (int y = 0; y < image.Height; y += BlackCheckStep)
            {
                for (int x = 0; x < image.Width; x += BlackCheckStep)
                {
                    int i = y * stride + x * 4;
                    // The alpha byte is undefined; only the colour counts.
                    if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>Saves a captured <paramref name="image"/> to <paramref name="path"/> as the WebP <paramref name="encode"/> makes of it.</summary>
        public static void SaveWebp(SKBitmap image, string path, Func<SKPixmap, SKData?> encode)
        {
            using var pixmap = image.PeekPixels();
            WriteWebp(encode(pixmap), path);
        }

        /// <summary>Writes (and disposes) an encoded <paramref name="webp"/>; null means the encoding failed.</summary>
        public static void WriteWebp(SKData? webp, string path)
        {
            using (webp)
            {
                if (webp == null)
                {
                    throw new InvalidOperationException("WebP encoding failed");
                }
                FileHelper.WriteAtomic(path, tmp =>
                {
                    using var fs = File.Create(tmp);
                    webp.SaveTo(fs);
                });
            }
        }
    }
}
