using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace PlayerDBSync
{
    /// <summary>
    ///  A see-through frame on top of everything that the user drags over the game and resizes, to say what the Linux
    ///  screenshots are taken of. Its inside, in pixels of the desktop, is the <see cref="CaptureArea"/> given back.
    /// </summary>
    internal sealed class CaptureBox : Window
    {
        // How close to the frame a press resizes rather than moves, in the window's units.
        private const double Grip = 14;
        private const double Frame = 3;

        private static readonly IBrush FrameBrush = new SolidColorBrush(Color.FromRgb(255, 180, 0));

        private readonly TaskCompletionSource<CaptureArea?> _result = new();
        private readonly ScreenSize _gameSize;
        private readonly TextBlock _readout = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly TextBlock _note = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, TextAlignment = TextAlignment.Center, IsVisible = false };
        private readonly Border _inside;

        /// <summary>Shows the box at <paramref name="start"/>; the task ends with where it was when the user picked <i>Use this area</i>, or null.</summary>
        public static Task<CaptureArea?> PlaceAsync(CaptureArea start, ScreenSize gameSize, double scaling)
        {
            var box = new CaptureBox(start, gameSize, scaling);
            box.Show();
            return box._result.Task;
        }

        private CaptureBox(CaptureArea start, ScreenSize gameSize, double scaling)
        {
            _gameSize = gameSize;
            Title = "PlayerDBSync capture area";
            WindowDecorations = WindowDecorations.None;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = true;
            MinWidth = 160;
            MinHeight = 120;
            // See-through where the desktop can draw that (a compositor is running); the inside stays a few percent
            // opaque so presses there still reach the box to drag it.
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(start.X, start.Y);
            Width = start.Width / scaling;
            Height = start.Height / scaling;

            var useButton = new Button { Content = "Use this area", IsDefault = true };
            useButton.Classes.Add("primary");
            useButton.Click += (_, _) => Finish(Area());
            var cancelButton = new Button { Content = "Cancel", IsCancel = true };
            cancelButton.Click += (_, _) => Finish(null);
            var gameSizeButton = new Button { Content = $"Make it {gameSize}" };
            ToolTip.SetTip(gameSizeButton, "The Game screen size picked: what Demo OCR screenshots must be");
            gameSizeButton.Click += (_, _) => SetSize(_gameSize.Width, _gameSize.Height);
            var screenButton = new Button { Content = "Fill this screen" };
            screenButton.Click += (_, _) => FillScreen();

            var panel = new StackPanel
            {
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = "Capture area", Foreground = FrameBrush, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                    _readout,
                    new TextBlock
                    {
                        Text = "Drag to move it over the game; drag the edges to resize.\nArrow keys move it a pixel (Shift: 10), Ctrl+arrows resize.\nThis box isn't in the screenshots: it closes when you pick.",
                        Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 420,
                    },
                    _note,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { gameSizeButton, screenButton, cancelButton, useButton },
                    },
                },
            };
            _inside = new Border
            {
                BorderBrush = FrameBrush,
                BorderThickness = new Thickness(Frame),
                Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                Child = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(200, 20, 24, 30)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = panel,
                },
            };
            Content = _inside;

            _inside.PointerPressed += OnPressed;
            _inside.PointerMoved += (_, e) => Cursor = CursorFor(Edge(e.GetPosition(this)));
            PositionChanged += (_, _) => ShowArea();
            SizeChanged += (_, _) => ShowArea();
            // Before the buttons see them, which would take the arrows for moving focus.
            AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
            Opened += (_, _) =>
            {
                if (ActualTransparencyLevel == WindowTransparencyLevel.None)
                {
                    // No compositor: the box can't be see-through, so it covers what it's placed over.
                    _note.Text = "Your desktop can't draw see-through windows (no compositor is running), so the box hides what's under it: line its edges up with the game's.";
                    _note.IsVisible = true;
                }
                ShowArea();
                useButton.Focus();
            };
            Closed += (_, _) => _result.TrySetResult(null);
        }

        /// <summary>The inside of the box, in pixels of the desktop.</summary>
        private CaptureArea Area() =>
            new(Position.X, Position.Y, (int)Math.Round(ClientSize.Width * RenderScaling), (int)Math.Round(ClientSize.Height * RenderScaling));

        private void ShowArea() => _readout.Text = Area().ToString().Replace("×", " × ");

        private void Finish(CaptureArea? area)
        {
            _result.TrySetResult(area);
            Close();
        }

        private void SetSize(int width, int height)
        {
            Width = width / RenderScaling;
            Height = height / RenderScaling;
        }

        private void FillScreen()
        {
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                Position = screen.Bounds.Position;
                SetSize(screen.Bounds.Width, screen.Bounds.Height);
            }
        }

        private void OnKey(object? sender, KeyEventArgs e)
        {
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            var (dx, dy) = e.Key switch
            {
                Key.Left => (-step, 0),
                Key.Right => (step, 0),
                Key.Up => (0, -step),
                Key.Down => (0, step),
                _ => (0, 0),
            };
            if ((dx, dy) == (0, 0))
            {
                return;
            }
            var area = Area();
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                SetSize(Math.Max(1, area.Width + dx), Math.Max(1, area.Height + dy));
            }
            else
            {
                Position = new PixelPoint(area.X + dx, area.Y + dy);
            }
            e.Handled = true;
        }

        private void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }
            // The window manager does the moving and resizing, so it goes as smoothly as any window's.
            if (Edge(e.GetPosition(this)) is { } edge)
            {
                BeginResizeDrag(edge, e);
            }
            else
            {
                BeginMoveDrag(e);
            }
            e.Handled = true;
        }

        /// <summary>The edge or corner <paramref name="point"/> is on, or null if it's inside.</summary>
        private WindowEdge? Edge(Point point)
        {
            var (width, height) = (ClientSize.Width, ClientSize.Height);
            var left = point.X < Grip;
            var right = point.X > width - Grip;
            var top = point.Y < Grip;
            var bottom = point.Y > height - Grip;
            return (left, right, top, bottom) switch
            {
                (true, _, true, _) => WindowEdge.NorthWest,
                (_, true, true, _) => WindowEdge.NorthEast,
                (true, _, _, true) => WindowEdge.SouthWest,
                (_, true, _, true) => WindowEdge.SouthEast,
                (true, _, _, _) => WindowEdge.West,
                (_, true, _, _) => WindowEdge.East,
                (_, _, true, _) => WindowEdge.North,
                (_, _, _, true) => WindowEdge.South,
                _ => null,
            };
        }

        private static readonly Dictionary<StandardCursorType, Cursor> Cursors = new();

        private static Cursor CursorFor(WindowEdge? edge)
        {
            var type = CursorTypeFor(edge);
            if (!Cursors.TryGetValue(type, out var cursor))
            {
                Cursors[type] = cursor = new Cursor(type);
            }
            return cursor;
        }

        private static StandardCursorType CursorTypeFor(WindowEdge? edge) => edge switch
        {
            WindowEdge.NorthWest => StandardCursorType.TopLeftCorner,
            WindowEdge.NorthEast => StandardCursorType.TopRightCorner,
            WindowEdge.SouthWest => StandardCursorType.BottomLeftCorner,
            WindowEdge.SouthEast => StandardCursorType.BottomRightCorner,
            WindowEdge.West => StandardCursorType.LeftSide,
            WindowEdge.East => StandardCursorType.RightSide,
            WindowEdge.North => StandardCursorType.TopSide,
            WindowEdge.South => StandardCursorType.BottomSide,
            _ => StandardCursorType.SizeAll,
        };
    }
}
