using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace PlayerDBSync
{
    /// <summary>Captures the game window and saves pictures of it as WebP, for raid screenshots and Demo OCR alike.</summary>
    internal static class GameCapture
    {
        private const string GameProcessName = "archeage";

        /// <summary>
        ///  Captures the client area of the archeage.exe window. When the game is in front this copies what is on
        ///  screen; when it's behind other windows it asks the window to draw itself, which most games support.
        /// </summary>
        public static (Bitmap? Image, string? Error) Capture()
        {
            var processes = Process.GetProcessesByName(GameProcessName);
            if (processes.Length == 0)
            {
                return (null, "archeage.exe isn't running");
            }

            // Work in physical pixels so the window size is right on scaled (125%, 150%…) displays.
            var oldDpiContext = SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
            try
            {
                var hwnd = processes.Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);
                if (hwnd == IntPtr.Zero)
                {
                    return (null, "archeage.exe has no window yet");
                }
                if (IsIconic(hwnd))
                {
                    return (null, "the game window is minimised");
                }
                if (!GetClientRect(hwnd, out var rect) || rect.Right <= 0 || rect.Bottom <= 0)
                {
                    return (null, "couldn't read the game window size");
                }
                var origin = new NativePoint();
                ClientToScreen(hwnd, ref origin);

                var size = new Size(rect.Right, rect.Bottom);
                var image = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
                using (var g = Graphics.FromImage(image))
                {
                    if (GetForegroundWindow() == hwnd)
                    {
                        g.CopyFromScreen(origin.X, origin.Y, 0, 0, size);
                    }
                    else
                    {
                        var hdc = g.GetHdc();
                        bool ok;
                        try
                        {
                            ok = PrintWindow(hwnd, hdc, PwClientOnly | PwRenderFullContent);
                        }
                        finally
                        {
                            g.ReleaseHdc(hdc);
                        }
                        if (!ok)
                        {
                            image.Dispose();
                            return (null, "the game window couldn't be captured");
                        }
                    }
                }
                // Neither capture takes the screen in exclusive mode, so a game in exclusive full screen
                // comes out black; don't save that.
                if (IsBlack(image))
                {
                    image.Dispose();
                    return (null, "the capture came out black (is the game in exclusive full screen? Use windowed or borderless)");
                }
                return (image, null);
            }
            finally
            {
                if (oldDpiContext != IntPtr.Zero)
                {
                    SetThreadDpiAwarenessContext(oldDpiContext);
                }
                foreach (var p in processes)
                {
                    p.Dispose();
                }
            }
        }

        // Every 8th pixel of every 8th row is checked: enough to find any picture, without reading all of it.
        private const int BlackCheckStep = 8;

        /// <summary>True if every sampled pixel of <paramref name="image"/> is black.</summary>
        private static bool IsBlack(Bitmap image)
        {
            var bits = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                var row = new int[image.Width];
                for (int y = 0; y < image.Height; y += BlackCheckStep)
                {
                    Marshal.Copy(bits.Scan0 + y * bits.Stride, row, 0, row.Length);
                    for (int x = 0; x < row.Length; x += BlackCheckStep)
                    {
                        // The alpha byte is undefined; only the colour counts.
                        if ((row[x] & 0xFFFFFF) != 0)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
            finally
            {
                image.UnlockBits(bits);
            }
        }

        /// <summary>Saves a captured <paramref name="image"/> to <paramref name="path"/> as the WebP <paramref name="encode"/> makes of it.</summary>
        public static void SaveWebp(Bitmap image, string path, Func<SKPixmap, SKData?> encode)
        {
            var bits = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            SKData? webp;
            try
            {
                // 32bppRgb is BGRA in memory; GDI leaves the alpha byte undefined, so treat it as opaque.
                var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                using var pixmap = new SKPixmap(info, bits.Scan0, bits.Stride);
                webp = encode(pixmap);
            }
            finally
            {
                image.UnlockBits(bits);
            }
            WriteWebp(webp, path);
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

        private const uint PwClientOnly = 0x1;
        private const uint PwRenderFullContent = 0x2;
        private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = -4;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X, Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    }
}
