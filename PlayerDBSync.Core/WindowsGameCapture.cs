using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SkiaSharp;

namespace PlayerDBSync
{
    /// <summary>
    ///  Captures the client area of the archeage.exe window with the Windows API. When the game is in front this copies
    ///  what is on screen; when it's behind other windows it asks the window to draw itself, which most games support.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsGameCapture(string processName = GameCapture.GameProcessName) : IGameCapture
    {
        public (SKBitmap? Image, string? Error) Capture()
        {
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
            {
                return (null, $"{processName}.exe isn't running");
            }

            // Work in physical pixels so the window size is right on scaled (125%, 150%…) displays.
            var oldDpiContext = SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
            try
            {
                var hwnd = processes.Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);
                if (hwnd == IntPtr.Zero)
                {
                    return (null, $"{processName}.exe has no window yet");
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

                var image = Copy(hwnd, origin, rect.Right, rect.Bottom);
                if (image == null)
                {
                    return (null, "the game window couldn't be captured");
                }
                if (GameCapture.IsBlack(image))
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

        /// <summary>The window's client area as a picture: copied off the screen if the window is in front, else drawn by the window itself.</summary>
        private static SKBitmap? Copy(IntPtr hwnd, NativePoint origin, int width, int height)
        {
            var screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero)
            {
                return null;
            }
            var memory = CreateCompatibleDC(screen);
            var header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height, // negative: rows run top to bottom
                Planes = 1,
                BitCount = 32,
            };
            var dib = CreateDIBSection(screen, ref header, 0, out var bits, IntPtr.Zero, 0);
            try
            {
                if (memory == IntPtr.Zero || dib == IntPtr.Zero || bits == IntPtr.Zero)
                {
                    return null;
                }
                var previous = SelectObject(memory, dib);
                bool ok;
                if (GetForegroundWindow() == hwnd)
                {
                    ok = BitBlt(memory, 0, 0, width, height, screen, origin.X, origin.Y, SrcCopy | CaptureBlt);
                }
                else
                {
                    ok = PrintWindow(hwnd, memory, PwClientOnly | PwRenderFullContent);
                }
                SelectObject(memory, previous);
                if (!ok)
                {
                    return null;
                }

                // 32 bits a pixel, BGRA in memory; GDI leaves the alpha byte undefined, so it's treated as opaque.
                var image = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
                var pixels = new byte[checked(width * height * 4)];
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                pixels.CopyTo(image.GetPixelSpan());
                return image;
            }
            finally
            {
                if (dib != IntPtr.Zero)
                {
                    DeleteObject(dib);
                }
                if (memory != IntPtr.Zero)
                {
                    DeleteDC(memory);
                }
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        private const uint PwClientOnly = 0x1;
        private const uint PwRenderFullContent = 0x2;
        private const uint SrcCopy = 0x00CC0020;
        private const uint CaptureBlt = 0x40000000;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public int Size;
            public int Width;
            public int Height;
            public short Planes;
            public short BitCount;
            public int Compression;
            public int SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public int ColorsUsed;
            public int ColorsImportant;
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

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int width, int height, IntPtr hdcSrc, int srcX, int srcY, uint rop);
    }
}
